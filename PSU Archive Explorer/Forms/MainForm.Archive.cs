using Microsoft.WindowsAPICodePack.Dialogs;
using psu_archive_explorer.FileViewers;
using psu_archive_explorer.Forms;
using psu_archive_explorer.Forms.FileViewers;
using psu_archive_explorer.Forms.FileViewers.Enemies;
using psu_archive_explorer;
using PSULib;
using PSULib.FileClasses.Archives;
using PSULib.FileClasses.Bosses;
using PSULib.FileClasses.Characters;
using PSULib.FileClasses.Enemies;
using PSULib.FileClasses.General;
using PSULib.FileClasses.General.Scripts;
using PSULib.FileClasses.Items;
using PSULib.FileClasses.Maps;
using PSULib.FileClasses.Missions;
using PSULib.FileClasses.Models;
using PSULib.FileClasses.Textures;
using PSULib.Support;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace psu_archive_explorer
{
    public partial class MainForm : Form
    {
        private class NodeBlueprint
        {
            public string Text;
            public string FileName;
            public ContainerFile OwnerContainer;
            public Color ForeColor = Color.Empty;
            public bool UseNblContextMenu;
            public List<NodeBlueprint> Children = new List<NodeBlueprint>();
        }

        private List<NodeBlueprint> BuildNodeBlueprints(ContainerFile toRead)
        {
            var result = new List<NodeBlueprint>();
            List<string> filenames = toRead.getFilenames();

            for (int i = 0; i < filenames.Count; i++)
            {
                string filename = filenames[i];
                var bp = new NodeBlueprint
                {
                    FileName = filename,
                    Text = filename,
                    OwnerContainer = toRead,
                    UseNblContextMenu = toRead is NblLoader,
                };

                if (toRead is AfsLoader || toRead is NblLoader || toRead is MiniAfsLoader)
                {
                    PsuFile child = toRead.getFileParsed(i);

                    if (child is NblLoader
                        && !filename.EndsWith(".nbl", StringComparison.OrdinalIgnoreCase))
                    {
                        bp.Text = StripPartialNblSuffix(filename) + ".nbl";
                    }

                    if (child is ContainerFile childContainer)
                    {
                        if (childContainer.Compressed)
                            bp.ForeColor = Color.Green;

                        if (toRead is NblLoader)
                        {
                            if (childContainer is NblChunk nblChunk &&
                                (nblChunk.chunkID == "NMLL" ||
                                 nblChunk.chunkID == "TMLL" ||
                                 filename.EndsWith(".nbl", StringComparison.OrdinalIgnoreCase)))
                            {
                                bp.Children = BuildNodeBlueprints(childContainer);
                            }
                        }
                        else
                        {
                            bp.Children = BuildNodeBlueprints(childContainer);
                        }
                    }
                }
                else
                {
                    RawFile raw = toRead.getFileRaw(i);
                    if (filename.EndsWith(".nbl") ||
                        raw.fileheader == "NMLL" ||
                        raw.fileheader == "TMLL")
                    {
                        ContainerFile parsed = (ContainerFile)toRead.getFileParsed(i);
                        bp.Children = BuildNodeBlueprints(parsed);
                        if (parsed.Compressed)
                            bp.ForeColor = Color.Green;
                    }
                }

                result.Add(bp);
            }

            return result;
        }

        /// <summary>
        /// Creates TreeNode objects from a pre-built NodeBlueprint list.
        /// Must be called on the UI thread, but does no getFileParsed calls
        /// so it completes almost instantly even for thousands of nodes.
        /// </summary>
        private void ApplyNodeBlueprints(TreeNodeCollection target,
                                          List<NodeBlueprint> blueprints)
        {
            foreach (var bp in blueprints)
            {
                var node = new TreeNode(bp.Text)
                {
                    Tag = new FileTreeNodeTag
                    {
                        OwnerContainer = bp.OwnerContainer,
                        FileName = bp.FileName,
                    },
                    ContextMenuStrip = bp.UseNblContextMenu
                        ? nblChunkContextMenuStrip
                        : arbitraryFileContextMenuStrip,
                };
 
                if (bp.ForeColor != Color.Empty)
                    node.ForeColor = bp.ForeColor;
 
                if (bp.Children.Count > 0)
                    ApplyNodeBlueprints(node.Nodes, bp.Children);
 
                target.Add(node);
            }
        }
 
        private bool openPSUArchive(string fileName, TreeNodeCollection treeNodeCollection)
        {
            bool isValidArchive = false;
            byte[] formatName = new byte[4];
            long fileSize = 0;
 
            // Probe — read magic bytes and file size before touching any UI.
            using (Stream probeStream = File.Open(fileName, FileMode.Open))
            {
                int read = probeStream.Read(formatName, 0, 4);
                if (read < 4) return false;
                fileSize = probeStream.Length;
            }
 
            string identifier = Encoding.ASCII.GetString(formatName, 0, 4);
            short shortId = BitConverter.ToInt16(formatName, 0);
 
            bool isNmll = identifier == "NMLL" || identifier == "NMLB";
            bool isAfs  = identifier == "AFS\0";
            bool isMini = shortId == 0x50AF;
 
            if (!isNmll && !isAfs && !isMini)
                return false;
 
            // Files under 100 MB always load fast enough to do synchronously
            // with no noticeable freeze. Large files (e.g. clothing texture
            // archives with thousands of NBL children) use a background thread
            // so the UI stays responsive during the parse.
            const long ASYNC_THRESHOLD = 100L * 1024 * 1024; // 100 MB
 
            if (fileSize < ASYNC_THRESHOLD)
            {
                // ---- Synchronous path (original behaviour, unchanged) ----
                treeView1.BeginUpdate();
                try
                {
                    using (Stream stream = File.Open(fileName, FileMode.Open))
                    {
                        if (isNmll)
                        {
                            setAFSEnabled(false);
                            treeNodeCollection.Clear();
                            loadedContainer = new NblLoader(stream);
                            ClearRightPanel();
                            addChildFiles(treeNodeCollection, loadedContainer);
                            compressNMLL = loadedContainer.Compressed;
                            compressTMLL = loadedContainer.getFilenames().Count > 1
                                && ((NblChunk)loadedContainer.getFileParsed(1)).Compressed;
                            isValidArchive = true;
                        }
                        else if (isAfs)
                        {
                            setAFSEnabled(true);
                            treeNodeCollection.Clear();
                            loadedContainer = new AfsLoader(stream);
                            ClearRightPanel();
                            addChildFiles(treeNodeCollection, loadedContainer);
                            isValidArchive = true;
 
                            // If the AFS is purely audio/video (every entry is .adx
                            // or .sfd), the toolbar's edit operations (Set Quest /
                            // Add File / Set Zone / Add Zone / Zone selector) don't
                            // apply — those are meaningful only against a real game
                            // AFS containing zones and quest data. Downgrade the
                            // enabled state we just set so the user doesn't see
                            // clickable buttons that would corrupt the file.
                            //
                            // Filename-only check (no content sniffing) because
                            // large audio AFS containers can have hundreds of
                            // entries and we don't want to pay a per-entry byte
                            // read on archive open. Hash-named ADX/SFD entries
                            // without an extension would slip through, but real
                            // AFS files in this game use proper filenames inside.
                            if (IsAllAdxOrSfdAfs(loadedContainer))
                            {
                                setAFSEnabled(false);
                            }
                        }
                        else if (isMini)
                        {
                            setAFSEnabled(false);
                            treeNodeCollection.Clear();
                            loadedContainer = new MiniAfsLoader(stream);
                            ClearRightPanel();
                            addChildFiles(treeNodeCollection, loadedContainer);
                            isValidArchive = true;
                        }
                    }
                }
                finally
                {
                    treeView1.EndUpdate();
                }
 
                return isValidArchive;
            }
 
            // ---- Async path for large files ----
            // Clear the tree immediately so old contents don't ghost,
            // then show a loading panel before kicking off the background parse.
            treeView1.BeginUpdate();
            treeNodeCollection.Clear();
            treeView1.EndUpdate();
 
            ClearRightPanel();
            BuildCenteredInfoPanel(
                "Loading...",
                "Parsing " + Path.GetFileName(fileName) + "\u2026\n\n" +
                "Large archives may take a few seconds.");
 
            // Disable interactive controls while loading so the user can't
            // trigger another open or click tree nodes mid-parse.
            treeView1.Enabled = false;
            menuStrip1.Enabled = false;

            _ = Task.Run(() =>
            {
                ContainerFile container = null;
                bool afsEnabled = false;
                bool nmllCompressed = false;
                bool tmllCompressed = false;
                List<NodeBlueprint> blueprints = null;
                Exception parseError = null;

                try
                {
                    using (Stream stream = File.Open(fileName, FileMode.Open))
                    {
                        if (isNmll)
                        {
                            var nbl = new NblLoader(stream);
                            nmllCompressed = nbl.Compressed;
                            tmllCompressed = nbl.getFilenames().Count > 1
                                && ((NblChunk)nbl.getFileParsed(1)).Compressed;
                            container = nbl;
                            afsEnabled = false;
                        }
                        else if (isAfs)
                        {
                            var afs = new AfsLoader(stream);
                            afsEnabled = !IsAllAdxOrSfdAfs(afs);
                            container = afs;
                        }
                        else if (isMini)
                        {
                            container = new MiniAfsLoader(stream);
                            afsEnabled = false;
                        }
                    }

                    // Build blueprints on the background thread now that getFileRaw
                    // is no longer called on NblLoader (which throws). All getFileParsed
                    // calls here are on already-loaded in-memory data so they are
                    // thread-safe reads with no disk I/O.
                    blueprints = BuildNodeBlueprints(container);
                }
                catch (Exception ex)
                {
                    parseError = ex;
                }

                this.Invoke((Action)(() =>
                {
                    treeView1.Enabled = true;
                    menuStrip1.Enabled = true;

                    if (parseError != null)
                    {
                        ClearRightPanel();
                        BuildCenteredInfoPanel("Could not open archive", parseError.Message);
                        return;
                    }

                    loadedContainer = container;
                    setAFSEnabled(afsEnabled);

                    if (isNmll)
                    {
                        compressNMLL = nmllCompressed;
                        compressTMLL = tmllCompressed;
                    }

                    // ApplyNodeBlueprints only creates TreeNode objects from
                    // pre-built data — no getFileParsed calls — so this is fast
                    // and the UI thread is only blocked for a moment.
                    treeView1.BeginUpdate();
                    ApplyNodeBlueprints(treeNodeCollection, blueprints);
                    treeView1.EndUpdate();

                    ClearRightPanel();
                    ResetContainerSearchIfActive();
                    UpdateContainerModeVisibility(true);
                }));
            });

            // Return true immediately — format was recognised and async load
            // has started. The caller's ResetContainerSearchIfActive() and
            // UpdateContainerModeVisibility() will also run here on the still-
            // empty tree, which is harmless since they run again inside Invoke
            // above once the load actually completes.
            return true;
        }
 
        private void setAFSEnabled(bool isActive)
        {
            zoneUD.Enabled = isActive;
            addZoneButton.Enabled = isActive;
            setZoneButton.Enabled = isActive;
            addFileButton.Enabled = isActive;
            setQuestButton.Enabled = isActive;
        }

        /// <summary>
        /// Returns true iff every entry in the given container has an .adx or
        /// .sfd filename extension. Used to detect AFS containers that are
        /// pure audio/video packs (no zones, no quests, no files for the AFS
        /// toolbar to operate on).
        ///
        /// Empty containers return false — an empty AFS could legitimately be
        /// the destination of an "Add File" / "Add Zone" operation, so we
        /// want the toolbar to stay enabled for those. The "all audio/video"
        /// determination requires at least one entry to actually be all of.
        ///
        /// Filename-only check on purpose: content sniffing every entry of a
        /// large audio AFS on load would add noticeable latency, and real
        /// game AFS files in this codebase use proper filenames for their
        /// entries — the hash-named-without-extension case is a single-file
        /// fake-archive scenario, handled separately in OpenSingleFileAsAdx.
        /// </summary>
        private static bool IsAllAdxOrSfdAfs(ContainerFile container)
        {
            if (container == null) return false;

            List<string> names;
            try { names = container.getFilenames(); }
            catch { return false; }
            if (names == null || names.Count == 0) return false;

            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) return false;
                bool isAdx = name.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);
                bool isSfd = name.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase);
                if (!isAdx && !isSfd) return false;
            }
            return true;
        }

        /// <summary>
        /// If <paramref name="filename"/> ends with a partial fragment of
        /// ".nbl" (i.e. ".n" or ".nb" — case-insensitive), strip that fragment
        /// and return the remainder; otherwise return the input unchanged.
        ///
        /// Used by the truncated-filename visual recovery in addChildFiles:
        /// when the 32-byte filename slot in an AFS/MiniAFS truncates mid
        /// extension, the on-disk name keeps the leading dot and as many
        /// characters of "nbl" as fit. Stripping that partial before we append
        /// the full ".nbl" gives us a clean display name instead of garbage
        /// like "foo.nb.nbl" or "bar.n.nbl".
        ///
        /// Note: we only treat ".n" / ".nb" as partials. A bare "." at the end
        /// could just as legitimately mean "filename ended with a period and
        /// then everything else got cut" — we'd risk eating a real character
        /// that happens to be a dot, so we leave it alone. The exact-match
        /// ".nbl" case is handled by the caller's outer EndsWith check, so
        /// this helper only ever sees non-".nbl"-ending inputs.
        /// </summary>
        private static string StripPartialNblSuffix(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return filename;

            if (filename.EndsWith(".nb", StringComparison.OrdinalIgnoreCase))
                return filename.Substring(0, filename.Length - ".nb".Length);
            if (filename.EndsWith(".n", StringComparison.OrdinalIgnoreCase))
                return filename.Substring(0, filename.Length - ".n".Length);

            return filename;
        }

        /// <summary>
        /// Adds a container file's children to a given node collection.
        /// </summary>
        /// <param name="currNode">node collection</param>
        /// <param name="toRead">container file</param>
        private void addChildFiles(TreeNodeCollection currNode, ContainerFile toRead)
        {
            List<string> filenames = toRead.getFilenames();
            for (int i = 0; i < filenames.Count; i++)
            {
                string filename = filenames[i];
                TreeNode temp = new TreeNode(filename);
                if (toRead is NblLoader)
                {
                    temp.ContextMenuStrip = nblChunkContextMenuStrip;
                }
                else
                {
                    temp.ContextMenuStrip = arbitraryFileContextMenuStrip;
                }

                if (toRead is AfsLoader || toRead is NblLoader || toRead is MiniAfsLoader)
                {
                    PsuFile child = toRead.getFileParsed(i);
                    if (child != null && child is ContainerFile)
                    {
                        addChildFiles(temp.Nodes, (ContainerFile)child);
                        if (((ContainerFile)child).Compressed)
                        {
                            temp.ForeColor = Color.Green;
                        }
                    }

                    // Visual recovery for truncated filenames. Both AfsLoader and
                    // MiniAfsLoader store filenames in 32-byte fixed-width slots,
                    // so any source filename longer than 32 chars gets clipped on
                    // disk — e.g. "xf_PlyMotActDat_05_DK_DOUBLESABE_M.nbl" comes
                    // out as "xf_PlyMotActDat_05_DK_DOUBLESABE" with the .nbl
                    // extension chopped off. The on-disk data is genuinely
                    // missing, so we can't recover the original full name, but
                    // when the entry's CONTENT parses as an NblLoader we can at
                    // least restore the ".nbl" suffix on the displayed label so
                    // the user can tell what kind of file it is at a glance.
                    //
                    // Truncation can happen mid-extension too. A 33-char
                    // original name leaves ".nb" at the end after the 32-byte
                    // clip; a 34-char one leaves ".n". Naively appending ".nbl"
                    // in those cases produces garbage like ".nb.nbl" or
                    // ".n.nbl", so strip any trailing partial of ".nbl" first.
                    //
                    // Important: this only changes temp.Text (the visible label).
                    // tag.FileName below stays as the truthful on-disk string so
                    // save-back paths don't try to write a longer-than-32-byte
                    // name into the fixed slot and corrupt the file.
                    if (child is NblLoader
                        && !filename.EndsWith(".nbl", StringComparison.OrdinalIgnoreCase))
                    {
                        temp.Text = StripPartialNblSuffix(filename) + ".nbl";
                    }
                }
                else //NBL chunk as parent
                {
                    //For an NBL chunk, only read parsed children if they're containers.
                    //This is sort of a mediocre variety of lazy loading...
                    RawFile raw = toRead.getFileRaw(i);
                    if (filename.EndsWith(".nbl") || raw.fileheader == "NMLL" || raw.fileheader == "TMLL")
                    {
                        ContainerFile parsed = (ContainerFile)toRead.getFileParsed(i);
                        addChildFiles(temp.Nodes, parsed);
                        if (parsed.Compressed)
                        {
                            temp.ForeColor = Color.Green;
                        }
                    }
                }
                temp.Tag = new FileTreeNodeTag { OwnerContainer = toRead, FileName = filename };
                currNode.Add(temp);
            }
        }

        private void extractPSUArchive(string fileName, string outDirectory)
        {
            string baseName = Path.GetFileName(fileName);
            string finalDirectory = Path.Combine(outDirectory, baseName + "_ext");
            byte[] formatName = new byte[4];

            bool handled = false;
            using (Stream stream = File.Open(fileName, FileMode.Open))
            {
                int headerBytesRead = stream.Read(formatName, 0, 4);
                if (headerBytesRead < 4)
                {
                }
                else
                {
                    string identifier = Encoding.ASCII.GetString(formatName, 0, 4);
                    short shortId = BitConverter.ToInt16(formatName, 0);

                    if (identifier == "NMLL" || identifier == "NMLB")
                    {
                        loadedContainer = new NblLoader(stream);
                        exportChildFiles(loadedContainer, finalDirectory);
                        handled = true;
                    }
                    else if (identifier == "AFS\0")
                    {
                        loadedContainer = new AfsLoader(stream);
                        exportChildFiles(loadedContainer, finalDirectory);
                        handled = true;
                    }
                    else if (shortId == 0x50AF)
                    {
                        loadedContainer = new MiniAfsLoader(stream);
                        exportChildFiles(loadedContainer, finalDirectory);
                        handled = true;
                    }
                }
            }

            if (!handled)
            {
                // Standalone ADX on disk — either a hashed filename (32 hex chars)
                // or a regular *.adx file. Validate by header, then either convert
                // to WAV or copy the raw bytes depending on the batchWavExport setting.
                bool isHashedAdx = IsHashedAdxFilename(baseName) && IsValidAdxFile(fileName);
                bool isPlainAdx = baseName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) && IsValidAdxFile(fileName);

                if (isHashedAdx || isPlainAdx)
                {
                    try
                    {
                        Directory.CreateDirectory(finalDirectory);

                        // Hashed files get .adx appended (matches the single-file Extract All
                        // behavior in exportNode); plain .adx files keep their name as-is.
                        string outBase = isHashedAdx ? baseName + ".adx" : baseName;

                        if (batchWavExport)
                        {
                            string wavName = Path.ChangeExtension(outBase, ".wav");
                            string wavPath = Path.Combine(finalDirectory, wavName);

                            try
                            {
                                byte[] adxBytes = File.ReadAllBytes(fileName);
                                byte[] wavBytes = AdxDecoder.DecodeToWav(adxBytes);
                                File.WriteAllBytes(wavPath, wavBytes);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(
                                    $"ADX->WAV conversion failed for {baseName}: {ex.Message}. " +
                                    "Writing raw .adx instead.");
                                string adxPath = Path.Combine(finalDirectory, outBase);
                                File.Copy(fileName, adxPath, overwrite: true);
                            }
                        }
                        else
                        {
                            string destFile = Path.Combine(finalDirectory, outBase);
                            File.Copy(fileName, destFile, overwrite: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Unable to process ADX " + baseName + ": " + ex.Message);
                    }
                }
                else if (baseName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                {
                    // Standalone DAT on disk. Signature check decides whether it's a sound
                    // DAT (convert) or a non-sound DAT (copy raw). Setting off → always raw.
                    try
                    {
                        Directory.CreateDirectory(finalDirectory);

                        if (batchDat2WavExport && DatConverter.IsSoundDat(fileName))
                        {
                            string wavName = Path.ChangeExtension(baseName, ".wav");
                            string wavPath = Path.Combine(finalDirectory, wavName);

                            try
                            {
                                byte[] datBytes = File.ReadAllBytes(fileName);
                                byte[] wavBytes = DatConverter.DecodeToWav(datBytes);
                                File.WriteAllBytes(wavPath, wavBytes);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(
                                    $"DAT->WAV conversion failed for {baseName}: {ex.Message}. " +
                                    "Writing raw .dat instead.");
                                string datPath = Path.Combine(finalDirectory, baseName);
                                File.Copy(fileName, datPath, overwrite: true);
                            }
                        }
                        else
                        {
                            // Non-sound .dat, or setting off — copy raw.
                            string destFile = Path.Combine(finalDirectory, baseName);
                            File.Copy(fileName, destFile, overwrite: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Unable to process DAT " + baseName + ": " + ex.Message);
                    }
                }
            }
        }

        private void exportChildFiles(ContainerFile toRead, string outDirectory)
        {
            Directory.CreateDirectory(outDirectory);
            List<string> filenames = toRead.getFilenames();
            List<string> writtenFiles = new List<string>();

            for (int i = 0; i < filenames.Count; i++)
            {
                bool isArchive = false;
                string filename = filenames[i];

                bool isKnownRawType =
                    filename.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase) ||
                    filename.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);

                if (!isKnownRawType)
                {
                    if (toRead is AfsLoader || toRead is NblLoader || toRead is MiniAfsLoader)
                    {
                        PsuFile child = toRead.getFileParsed(i);
                        if (child != null && child is ContainerFile)
                        {
                            isArchive = true;
                            if (filename == "NMLL chunk" || filename == "TMLL chunk")
                                exportChildFiles((ContainerFile)child, outDirectory);
                            else
                                exportChildFiles((ContainerFile)child, Path.Combine(outDirectory, filename + "_ext"));
                        }
                    }
                    else
                    {
                        RawFile raw = toRead.getFileRaw(i);
                        if (filename.EndsWith(".nbl") || raw.fileheader == "NMLL" || raw.fileheader == "TMLL")
                        {
                            isArchive = true;
                            exportChildFiles((ContainerFile)toRead.getFileParsed(i), outDirectory);
                        }
                    }
                }

                try
                {
                    if (isArchive)
                    {
                        if (batchExportSubArchiveFiles)
                            extractFile(toRead.getFileParsed(i), Path.Combine(outDirectory, filename));
                        continue;
                    }
                    else if (filename.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase))
                    {
                        if (toRead is AfsLoader || toRead is MiniAfsLoader)
                            filename = CheckForDupeFilenames(writtenFiles, filename);

                        RawFile sfdRaw = toRead.getFileRaw(i);
                        if (sfdRaw?.fileContents != null)
                        {
                            File.WriteAllBytes(Path.Combine(outDirectory, filename), sfdRaw.fileContents);
                            writtenFiles.Add(filename);
                        }
                    }
                    else if (filename.EndsWith(".adx", StringComparison.OrdinalIgnoreCase))
                    {
                        if (toRead is AfsLoader || toRead is MiniAfsLoader)
                            filename = CheckForDupeFilenames(writtenFiles, filename);

                        RawFile adxRaw = toRead.getFileRaw(i);
                        if (adxRaw?.fileContents != null)
                        {
                            if (batchWavExport)
                            {
                                // Try ADX → WAV. On any failure (non-PSU variant,
                                // corrupt data, etc.) fall back to writing the raw
                                // .adx so batch extraction never loses a file.
                                string wavName = Path.ChangeExtension(filename, ".wav");

                                // Re-check dupes against the .wav name — rare, but
                                // AFS containers can have repeated filenames.
                                if (toRead is AfsLoader || toRead is MiniAfsLoader)
                                    wavName = CheckForDupeFilenames(writtenFiles, wavName);

                                try
                                {
                                    byte[] wavBytes = AdxDecoder.DecodeToWav(adxRaw.fileContents);
                                    File.WriteAllBytes(Path.Combine(outDirectory, wavName), wavBytes);
                                    writtenFiles.Add(wavName);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(
                                        $"ADX→WAV conversion failed for {filename}: {ex.Message}. " +
                                        "Writing raw .adx instead.");
                                    File.WriteAllBytes(Path.Combine(outDirectory, filename), adxRaw.fileContents);
                                    writtenFiles.Add(filename);
                                }
                            }
                            else
                            {
                                File.WriteAllBytes(Path.Combine(outDirectory, filename), adxRaw.fileContents);
                                writtenFiles.Add(filename);
                            }
                        }
                    }
                    else if (filename.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                    {
                        if (toRead is AfsLoader || toRead is MiniAfsLoader)
                            filename = CheckForDupeFilenames(writtenFiles, filename);

                        RawFile datRaw = toRead.getFileRaw(i);
                        if (datRaw?.fileContents != null)
                        {
                            // Only attempt conversion if: setting is on AND bytes actually
                            // look like a sound DAT. Non-sound .dat files (and any other
                            // case) write raw — no spam, no wasted conversion attempts.
                            if (batchDat2WavExport && DatConverter.IsSoundDat(datRaw.fileContents))
                            {
                                string wavName = Path.ChangeExtension(filename, ".wav");

                                if (toRead is AfsLoader || toRead is MiniAfsLoader)
                                    wavName = CheckForDupeFilenames(writtenFiles, wavName);

                                try
                                {
                                    byte[] wavBytes = DatConverter.DecodeToWav(datRaw.fileContents);
                                    File.WriteAllBytes(Path.Combine(outDirectory, wavName), wavBytes);
                                    writtenFiles.Add(wavName);
                                }
                                catch (Exception ex)
                                {
                                    // Genuine failure on a file that did have the signature —
                                    // log it and fall back to raw.
                                    Console.WriteLine(
                                        $"DAT→WAV conversion failed for {filename}: {ex.Message}. " +
                                        "Writing raw .dat instead.");
                                    File.WriteAllBytes(Path.Combine(outDirectory, filename), datRaw.fileContents);
                                    writtenFiles.Add(filename);
                                }
                            }
                            else
                            {
                                // Non-sound .dat, or setting off — raw extract.
                                File.WriteAllBytes(Path.Combine(outDirectory, filename), datRaw.fileContents);
                                writtenFiles.Add(filename);
                            }
                        }
                    }
                    else if (filename.Contains(".xvr") && batchPngExport)
                    {
                        if (toRead is AfsLoader || toRead is MiniAfsLoader)
                            filename = CheckForDupeFilenames(writtenFiles, filename);
                        filename = filename.Replace(".xvr", ".png");
                        ((ITextureFile)toRead.getFileParsed(i)).mipMaps[0].Save(Path.Combine(outDirectory, filename));
                    }
                    else
                    {
                        if (toRead is AfsLoader || toRead is MiniAfsLoader)
                            filename = CheckForDupeFilenames(writtenFiles, filename);
                        File.WriteAllBytes(Path.Combine(outDirectory, filename), toRead.getFileRaw(i).WriteToBytes(exportMetaData));
                    }
                }
                catch
                {
                    Console.WriteLine("Unable to extract " + filename + ". The file may be in use, inaccessible, or incompatible. Skipping.");
                }
            }
        }

        private static string CheckForDupeFilenames(List<string> writtenFiles, string filename)
        {
            if (writtenFiles.Contains(filename))
            {
                string nameOnly = Path.GetFileNameWithoutExtension(filename);
                string ext = Path.GetExtension(filename);
                int j = 0;
                string candidate;
                do
                {
                    candidate = nameOnly + $"_{j}" + ext;
                    j++;
                }
                while (writtenFiles.Contains(candidate));
                filename = candidate;
                writtenFiles.Add(filename);
            }
            else
            {
                writtenFiles.Add(filename);
            }

            return filename;
        }

        private void setRightPanel(PsuFile toRead)
        {
            ClearRightPanel();
            currentRight = null;
            currentRight = toRead;
            UserControl toAdd = new UserControl();

            if (toRead is ITextureFile texFile)
            {
                toAdd = new TextureViewer(texFile);
            }
            else if (toRead is PointeredFile pointeredFile)
            {
                toAdd = new PointeredFileViewer(pointeredFile);
            }
            else if (toRead is ActDataFile actDataFile)
            {
                toAdd = new ActDataFileViewer(actDataFile);
            }
            else if (toRead is EnemySoundEffectFile seDataFile)
            {
                toAdd = new EnemySoundEffectFileViewer(seDataFile);
            }
            else if (toRead is ListFile listFile)
            {
                toAdd = new ListFileViewer(listFile);
            }
            else if (toRead is XntFile xntFile)
            {
                toAdd = new XntFileViewer(xntFile);
            }
            else if (toRead is XnjFile xnjFile)
            {
                toAdd = new XnjFileViewer(xnjFile);
            }
            else if (toRead is XnaFile xnaFile)
            {
                toAdd = new XnaFileViewer(xnaFile);
            }
            else if (toRead is XncpFile xncpFile)
            {
                toAdd = new XncpFileViewer(xncpFile);
            }
            else if (toRead is XnrFile xnrFile)
            {
                toAdd = new XnrFileViewer(xnrFile);
            }
            else if (toRead is XncfFile xncfFile)
            {
                toAdd = new XncfFileViewer(xncfFile);
            }
            else if (toRead is NomFile nomFile)
            {
                toAdd = new NomFileViewer(nomFile);
            }
            else if (toRead is EnemyLayoutFile enemyLayoutFile)
            {
                toAdd = new EnemyLayoutViewer(enemyLayoutFile);
            }
            else if (toRead is ItemTechParamFile itemTechParamFile)
            {
                toAdd = new ItemTechParamViewer(itemTechParamFile);
            }
            else if (toRead is ItemSkillParamFile itemSkillParamFile)
            {
                toAdd = new ItemSkillParamViewer(itemSkillParamFile);
            }
            else if (toRead is ItemBulletParamFile itemBulletParamFile)
            {
                toAdd = new ItemBulletParamViewer(itemBulletParamFile);
            }
            else if (toRead is RmagBulletParamFile rmagBulletParamFile)
            {
                toAdd = new RmagBulletViewer(rmagBulletParamFile);
            }
            else if (toRead is TextFile textFile)
            {
                toAdd = new TextViewer(textFile);
            }
            else if (toRead is ScriptFile scriptFile)
            {
                toAdd = new ScriptFileViewer(scriptFile);
            }
            else if (toRead is EnemyLevelParamFile enemyLevelParamFile)
            {
                toAdd = new EnemyStatEditor(enemyLevelParamFile);
            }
            else if (toRead is WeaponListFile weaponListFile)
            {
                toAdd = new WeaponListEditor(weaponListFile);
            }
            else if (toRead is PartsInfoFile partsInfoFile)
            {
                toAdd = new PartsInfoViewer(partsInfoFile);
            }
            else if (toRead is ItemPriceFile itemPriceFile)
            {
                toAdd = new ItemPriceViewer(itemPriceFile);
            }
            else if (toRead is EnemyDropFile enemyDropFile)
            {
                toAdd = new EnemyDropViewer(enemyDropFile);
            }
            else if (toRead is SetFile setFile)
            {
                toAdd = new SetFileViewer(setFile);
            }
            else if (toRead is ThinkDragonFile thinkDragonFile)
            {
                toAdd = new ThinkDragonViewer(thinkDragonFile);
            }
            else if (toRead is WeaponParamFile weaponParamFile)
            {
                toAdd = new WeaponParamViewer(weaponParamFile);
            }
            else if (toRead is ItemSuitParamFile itemSuitParamFile)
            {
                toAdd = new ClothingFileViewer(itemSuitParamFile);
            }
            else if (toRead is ItemUnitParamFile itemUnitParamFile)
            {
                toAdd = new UnitParamViewer(itemUnitParamFile);
            }
            else if (toRead is ItemCommonInfoFile itemCommonInfoFile)
            {
                toAdd = new ItemCommonInfoViewer(itemCommonInfoFile);
            }
            else if (toRead is QuestListFile questListFile)
            {
                toAdd = new QuestListViewer(questListFile);
            }
            else if (toRead is ObjectParticleInfoFile objectParticleInfoFile)
            {
                toAdd = new ObjectParticleInfoFileViewer(objectParticleInfoFile);
            }
            else if (toRead is ObjectParamFile objParamFile)
            {
                toAdd = new ObjParamViewer(objParamFile);
            }
            else if (toRead is EnemyParamFile enemyParamFile)
            {
                toAdd = new EnemyParamFileViewer(enemyParamFile);
            }
            else if (toRead is AtkDatFile atkDatFile)
            {
                toAdd = new AtkDatFileViewer(atkDatFile);
            }
            else if (toRead is DamageDataFile damageDataFile)
            {
                toAdd = new DamageDataFileViewer(damageDataFile);
            }
            else if (toRead is EnemyMotTblFile enemyMotTblFile)
            {
                toAdd = new EnemyMotTblFileViewer(enemyMotTblFile);
            }
            else if (toRead is LndCommonFile lndCommonFile)
            {
                toAdd = new LndCommonEditor(lndCommonFile);
            }
            else if (toRead is RipcFile ripcFile)
            {
                toAdd = new RipcFileViewer(ripcFile);
            }
            else if (toRead is UnpointeredFile unpointeredFile)
            {
                // ADX interception — if this UnpointeredFile is an archive-embedded
                // .adx, show the AdxPreviewPanel (audio preview) instead of the
                // raw/hex viewer. Standalone .adx on disk is handled earlier in
                // treeView1_AfterSelect via LoadAdxIntoRightPanel; this branch
                // covers the case where an ADX lives inside a real container.
                bool isAdx = unpointeredFile.filename?.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) == true;

                // Sound DAT interception — if this UnpointeredFile is a .dat that
                // passes the xobxDDNS / xobxKPTD signature check, show the audio
                // preview panel instead of the raw/hex viewer.
                bool isDat = unpointeredFile.filename?.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) == true;

                if (isAdx && unpointeredFile.theData != null)
                {
                    // Mirror the filename-hash lookup from LoadAdxIntoRightPanel so
                    // the info panel shows the mapped sound title when available.
                    string hashKey = Path.GetFileNameWithoutExtension(unpointeredFile.filename ?? "").TrimStart('-');
                    string mappedTitle = null;
                    if (hashKey.Length == 32
                        && hashKey.All(c => "0123456789abcdefABCDEF".Contains(c)))
                    {
                        AdxHashMap.TryGetValue(hashKey.ToLowerInvariant(), out mappedTitle);
                    }

                    string infoText =
                        "ADX audio file detected.\n\n" +
                        "If you wish to replace this file, convert a .wav to .adx.\n" +
                        "Replace one of the .adx files in the container with a valid .adx file\n" +
                        "and save your hashed file.\n\n" +
                        $"File name: {unpointeredFile.filename}";

                    if (mappedTitle != null)
                    {
                        infoText += $"\n\nADX Mapping: {mappedTitle}";
                    }

                    toAdd = new AdxPreviewPanel(unpointeredFile.theData, infoText, mappedTitle ?? unpointeredFile.filename);
                }
                else if (isDat
                    && unpointeredFile.theData != null
                    && DatConverter.IsSoundDat(unpointeredFile.theData))
                {
                    string infoText =
                        "DAT sound file detected (xobxDDNS / xobxKPTD).\n\n" +
                        "This is a raw PCM sound container used by PSU.\n" +
                        "You can preview playback below, or use Extract Selected\n" +
                        "to save it as either the raw .dat or a converted .wav.\n\n" +
                        $"File name: {unpointeredFile.filename}";

                    toAdd = new DatPreviewPanel(unpointeredFile.theData, infoText);
                }
                else
                {
                    toAdd = new UnpointeredFileViewer(unpointeredFile);
                }
            }
            splitContainer1.Panel2.Controls.Add(toAdd);
            toAdd.Dock = DockStyle.Fill;
        }
    }
}