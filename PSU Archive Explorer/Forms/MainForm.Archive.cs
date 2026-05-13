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
        private bool openPSUArchive(string fileName, TreeNodeCollection treeNodeCollection)
        {
            bool isValidArchive = false;
            byte[] formatName = new byte[4];

            treeView1.BeginUpdate();
            try
            {
                using (Stream stream = File.Open(fileName, FileMode.Open))
                {
                    int headerBytesRead = stream.Read(formatName, 0, 4);
                    if (headerBytesRead < 4)
                    {
                        return false;
                    }

                    string identifier = Encoding.ASCII.GetString(formatName, 0, 4);
                    if (identifier == "NMLL" || identifier == "NMLB")
                    {
                        setAFSEnabled(false);
                        treeNodeCollection.Clear();
                        loadedContainer = new NblLoader(stream);
                        ClearRightPanel();
                        addChildFiles(treeNodeCollection, loadedContainer);
                        compressNMLL = loadedContainer.Compressed;
                        compressTMLL = loadedContainer.getFilenames().Count > 1 && ((NblChunk)loadedContainer.getFileParsed(1)).Compressed;
                        isValidArchive = true;
                    }
                    else if (identifier == "AFS\0")
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
                    else if (BitConverter.ToInt16(formatName, 0) == 0x50AF)
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