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
        private void setQuest_Click(object sender, EventArgs e)
        {
            if (loadedContainer is AfsLoader)
            {
                if (importDialog.ShowDialog() == DialogResult.OK)
                {
                    ((AfsLoader)loadedContainer).setQuest(importDialog.OpenFile());
                }
            }
        }

        private void setZone_Click_1(object sender, EventArgs e)
        {
            if (loadedContainer is AfsLoader)
            {
                if (importDialog.ShowDialog() == DialogResult.OK)
                {
                    ((AfsLoader)loadedContainer).setZone((int)zoneUD.Value, importDialog.OpenFile());
                }
            }
        }

        private void addZone_Click_1(object sender, EventArgs e)
        {
            if (loadedContainer is AfsLoader)
            {
                if (importDialog.ShowDialog() == DialogResult.OK)
                {
                    ((AfsLoader)loadedContainer).addZone((int)zoneUD.Value, importDialog.OpenFile());
                }
            }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            ClearRightPanel();

            if (!(e.Node.Tag is FileTreeNodeTag tag))
                return;

            // Standalone .adx on disk route to ADX preview
            if (tag.OwnerContainer == null && tag.FileName?.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) == true)
            {
                string pathToUse = tag.FullPath ?? fileDialog.FileName;
                if (File.Exists(pathToUse))
                    LoadAdxIntoRightPanel(pathToUse, e.Node);
                return;
            }

            // Standalone .dat on disk if it's a sound DAT, route to DAT preview.
            if (tag.OwnerContainer == null && tag.FileName?.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) == true)
            {
                string pathToUse = tag.FullPath ?? fileDialog.FileName;
                if (File.Exists(pathToUse) && DatConverter.IsSoundDat(pathToUse))
                {
                    LoadDatSoundIntoRightPanel(pathToUse, e.Node);
                    return;
                }
            }

            // Standalone .sfd on disk route to SFD preview.
            if (tag.OwnerContainer == null && tag.FileName?.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase) == true)
            {
                string pathToUse = tag.FullPath ?? fileDialog.FileName;
                if (File.Exists(pathToUse))
                {
                    long size = 0;
                    try { size = new FileInfo(pathToUse).Length; } catch { }
                    if (size > 0 && size <= MAX_SFD_PREVIEW_SIZE)
                    {
                        LoadSfdIntoRightPanelFromFile(pathToUse, e.Node);
                        return;
                    }
                    // Too big fall through to the large file warning panel below.
                }
            }

            if (tag.OwnerContainer != null)
            {
                ContainerFile parent = tag.OwnerContainer;
                int index = e.Node.Index;

                string fileName = tag.FileName ?? "Unknown";

                // Filename-based type detection. The .nbl check is supplemented
                // by a content check further down — AfsLoader and MiniAfsLoader
                // store filenames in 32-byte fixed-width slots, so any source
                // filename longer than 32 chars is truncated on disk and the
                // ".nbl" suffix is the first thing to go. We can't recover the
                // missing characters, but we can ask the parser whether the
                // entry's content is actually an NBL and treat it as one.
                bool isNblFile = fileName.EndsWith(".nbl", StringComparison.OrdinalIgnoreCase);
                bool isSfdVideo = fileName.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase);
                bool isAdxFile = fileName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);
                bool isDatFile = fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

                // Content fallback for the NBL case. Both AfsLoader and
                // MiniAfsLoader eagerly parse their children during load
                // (populateFile / the corresponding loop), so getFileParsed
                // here is a property access, not a re-parse — no measurable
                // cost. Guarded with try/catch because exotic entries may not
                // parse cleanly and we don't want to crash the tree select.
                if (!isNblFile)
                {
                    try
                    {
                        if (parent.getFileParsed(index) is NblLoader)
                        {
                            isNblFile = true;
                        }
                    }
                    catch
                    {
                        // Fall through — leave isNblFile false and let the
                        // normal generic-file path handle this entry.
                    }
                }

                if (isDatFile)
                {
                    LoadArchiveDatAsync(parent, index, fileName);
                    return;
                }

                bool shouldSkipSizeCheck = isNblFile ||
                                           fileName.IndexOf("NMLL chunk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           fileName.IndexOf("TMLL chunk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           isAdxFile;

                if (shouldSkipSizeCheck)
                {
                    // Selecting a chunk node previously left the right panel
                    // blank, since there's no viewer for the chunk itself.
                    // Show a short explanation of what each chunk is so the
                    // user has some context. Match the wording style of the
                    // ADX info panel.
                    bool isNmllChunk = fileName.IndexOf("NMLL chunk", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isTmllChunk = fileName.IndexOf("TMLL chunk", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isNmllChunk)
                    {
                        ShowChunkInfoPanel(
                            "NMLL chunk",
                            "The NMLL chunk holds the structured game data inside an NBL container.\r\n" +
                            "Things like models, parameter tables, animations, scripts, etc.\r\n" +
                            "Each entry in the chunk is a separately parseable file with its own format.\r\n\r\n" +
                            "Expand this node to browse its contents.");
                        return;
                    }

                    if (isTmllChunk)
                    {
                        ShowChunkInfoPanel(
                            "TMLL chunk",
                            "The TMLL chunk holds texture data for an NBL container.\r\n" +
                            "Things like texture files used by models and UI stored in the NMLL chunk.\r\n" +
                            "Archives without textures will only contain an NMLL.\r\n\r\n" +
                            "Expand this node to browse its contents.");
                        return;
                    }

                    if (isNblFile)
                    {
                        // Title: generic "NBL Archive" label rather than the
                        // file's name, since the body text below is a general
                        // description of what an NBL is. Matches the style of
                        // the NMLL/TMLL chunk info panels above.
                        ShowChunkInfoPanel(
                            "NBL Archive",
                            "NBL Archive (Nu2 Binary Library) is a container holding the structured data.\r\n" +
                            "This data is packed into an NMLL chunk and an optional TMLL chunk.\r\n" +
                            "NBLs are typically nested inside larger AFS containers with many sibling NBLs\r\n\r\n" +
                            "Expand this node to browse its chunks.");
                        return;
                    }

                    setRightPanel(parent.getFileParsed(index));
                    return;
                }

                const long MAX_SAFE_SIZE = 500 * 1024 * 1024; // 500 MB (generic preview cap)

                long fileSize = 0;
                string displayName = fileName;

                try
                {
                    RawFile raw = parent.getFileRaw(index);
                    if (raw != null)
                    {
                        displayName = raw.filename ?? fileName;

                        if (raw.fileContents != null && raw.fileContents.Length > 0)
                        {
                            fileSize = raw.fileContents.LongLength;
                        }
                        else
                        {
                            byte[] data = raw.WriteToBytes(false);
                            fileSize = data?.LongLength ?? 0;
                        }
                    }
                }
                catch
                {
                    fileSize = long.MaxValue;
                }

                // Archive-embedded .sfd route to the in-panel video preview
                // if it's under the SFD ceiling, otherwise drop to warning.
                if (isSfdVideo && fileSize <= MAX_SFD_PREVIEW_SIZE)
                {
                    LoadSfdIntoRightPanel(parent, index, displayName, e.Node);
                    return;
                }

                bool isLargeOrVideo = (fileSize > MAX_SAFE_SIZE) || isSfdVideo;

                if (isLargeOrVideo)
                {
                    try { currentRight = parent.getFileParsed(index); } catch (Exception ex) { Console.WriteLine("getFileParsed failed: " + ex); }

                    var warningPanel = new Panel
                    {
                        Dock = DockStyle.Fill,
                        BackColor = Color.FromArgb(229, 229, 229)
                    };

                    var lbl = new Label
                    {
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = new Font("Segoe UI", 10.5f),
                        Text = $"Preview unavailable due to file size.\n\n" +
                               "• Right click the file and Export Selected to save it.\n" +
                               "• .sfd videos can be opened with VLC Media Player.\n\n" +
                               $"File name: {displayName}"
                    };

                    warningPanel.Controls.Add(lbl);
                    splitContainer1.Panel2.Controls.Add(warningPanel);
                    return;
                }

                setRightPanel(parent.getFileParsed(index));
            }
        }

        private void addFile_Click(object sender, EventArgs e)
        {
            if (loadedContainer is AfsLoader)
            {
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    ((AfsLoader)loadedContainer).addFile(fileDialog.SafeFileName, fileDialog.OpenFile());
                }
            }
        }

        private void createAFSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Step 1: ask for the folder whose contents will be packed into
            // the AFS, using the modern Vista-style folder picker.
            if (!PromptForFolder("Select the folder whose contents will be packed into the AFS", out string folderToOpen))
                return;

            // Step 2: ask where to save the resulting AFS file.
            // SaveFileDialog already renders in the modern style on Vista+
            // (its AutoUpgradeEnabled default is true), so nothing to do here.
            saveFileDialog1.Title = "Save the new AFS file as";
            if (saveFileDialog1.ShowDialog() != DialogResult.OK)
                return;

            AfsLoader.createFromDirectory(folderToOpen, saveFileDialog1.FileName);
        }

        private void replaceFileTreeContextItem_Click(object sender, EventArgs e)
        {
            TreeNode node = treeView1.SelectedNode;
            if (node == null || !(node.Tag is FileTreeNodeTag tag))
                return;

            if (tag.OwnerContainer == null &&
                tag.FileName != null &&
                tag.FileName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(tag.FullPath) &&
                File.Exists(tag.FullPath))
            {
                OpenFileDialog adxReplaceDialog = new OpenFileDialog
                {
                    Filter = "ADX audio files (*.adx)|*.adx|All files (*.*)|*.*",
                    Title = "Select an ADX file to replace the hashed file with"
                };

                if (adxReplaceDialog.ShowDialog() != DialogResult.OK)
                    return;

                string sourcePath = adxReplaceDialog.FileName;

                if (!IsValidAdxFile(sourcePath))
                {
                    MessageBox.Show(
                        "The selected file is not a valid ADX audio file.\n\n" +
                        "Please pick a file with a proper ADX header.",
                        "Invalid ADX File",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                try
                {
                    pendingAdxReplacementBytes = File.ReadAllBytes(sourcePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not read the selected ADX file:\n{ex.Message}",
                        "Read Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string filename = Path.GetFileName(tag.FullPath);
                byte[] header = new byte[4];
                if (pendingAdxReplacementBytes.Length >= 4)
                {
                    Array.Copy(pendingAdxReplacementBytes, 0, header, 0, 4);
                }
                currentRight = new UnpointeredFile(filename, pendingAdxReplacementBytes, header);

                MessageBox.Show(
                    $"'{Path.GetFileName(sourcePath)}' loaded!\n\n" +
                    $"Click File → Save As to save to hash.\n\n" +
                    $"(The .adx extension will be stripped and match the hash name.)",
                    "ADX Replacement",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ContainerFile owningFile = tag.OwnerContainer;
            OpenFileDialog replaceDialog = new OpenFileDialog();
            replaceDialog.FileName = tag.FileName;
            if (replaceDialog.ShowDialog() == DialogResult.OK)
            {
                string pickedFilename = Path.GetFileName(replaceDialog.FileName);
                RawFile file = new RawFile(replaceDialog.OpenFile(), pickedFilename);

                // RawFile's constructor parses the stream and may pull a
                // filename out of an embedded metadata header (the same header
                // we WRITE during extraction when exportMetaData is true).
                // That means: if the user is feeding back a file that was
                // previously extracted from a PSU archive — even one with a
                // totally different on-disk name now — `file.filename` will
                // end up as whatever the metadata header says, NOT what the
                // user picked. That's how the tree node ends up displaying
                // an unrelated filename like "charamake_back02.xnt" after the
                // user picked "ep02m01.sfd" from disk.
                //
                // Always honor the user's choice instead. If they picked
                // "ep02m01.sfd", that's the name we use, full stop.
                if (!string.Equals(file.filename, pickedFilename, StringComparison.Ordinal))
                {
                    file.filename = pickedFilename;
                }

                if (owningFile is FilenameAwareContainerFile awareContainerFile)
                {
                    string filename = file.filename;
                    if (filename != tag.FileName && !awareContainerFile.ValidateFilename(filename))
                    {
                        FileRenameForm rename = new FileRenameForm(filename);
                        while (!awareContainerFile.ValidateFilename(filename))
                        {
                            if (rename.ShowDialog() == DialogResult.OK)
                            {
                                filename = rename.FileName;
                            }
                            else
                            {
                                return;
                            }
                        }
                    }
                    if (filename != file.filename)
                    {
                        file.filename = filename;
                    }
                }
                owningFile.replaceFile(node.Index, file);
                node.Text = file.filename;
                tag.FileName = file.filename;
                PsuFile parsedFile = owningFile.getFileParsed(node.Index);
                if (parsedFile is ContainerFile)
                {
                    node.Nodes.Clear();
                    addChildFiles(node.Nodes, (ContainerFile)parsedFile);
                }
                var sel = treeView1.SelectedNode;
                treeView1.SelectedNode = null;
                treeView1.SelectedNode = sel;
            }
        }

        private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            ((TreeView)sender).SelectedNode = e.Node;
        }

        private void disableScriptParsingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            disableScriptParsingToolStripMenuItem.Checked = !disableScriptParsingToolStripMenuItem.Checked;
            PsuFiles.parseScripts = !disableScriptParsingToolStripMenuItem.Checked;
        }

        private void exportAllWeaponsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Need *something* loaded with files in it. We support two cases:
            //   1) The loaded container is itself an NBL  → look at its NMLL chunk directly.
            //   2) The loaded container holds NBLs inside (e.g. an AFS hash file)
            //      → walk each child NBL and export from each one's NMLL chunk
            //        into its own subfolder, so re-import can target the right NBL later.
            if (loadedContainer == null || loadedContainer.getFilenames().Count == 0)
            {
                MessageBox.Show(
                    "No archive is currently loaded.\r\n\r\n" +
                    "Weapon export reads itemWeaponParam_*.xnr files from the NMLL chunk " +
                    "of an NBL archive. Open an NBL — or an AFS containing NBLs — first.",
                    "Nothing to Export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!PromptForFolder("Select the folder to export weapon param text files into", out string exportFolder))
                return;

            int exportedCount = 0;          // total weapon files written
            int nblsWithWeapons = 0;        // distinct NBLs that contributed at least one
            var errors = new List<string>(); // per-NBL load errors, surfaced at the end

            if (loadedContainer is NblLoader topNbl)
            {
                // Case 1: a single NBL is loaded directly.
                int wrote = ExportWeaponsFromNbl(topNbl, exportFolder, errors);
                exportedCount += wrote;
                if (wrote > 0) nblsWithWeapons++;
            }
            else
            {
                // Case 2: a container of containers (AFS-style). Walk children,
                // and for each one that turns out to be an NBL, export its
                // weapons into a subfolder named after the child entry so
                // results from different NBLs don't collide.
                var childNames = loadedContainer.getFilenames();
                for (int i = 0; i < childNames.Count; i++)
                {
                    string childName = childNames[i];
                    NblLoader childNbl = null;
                    try
                    {
                        childNbl = loadedContainer.getFileParsed(i) as NblLoader;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{childName}: failed to parse ({ex.Message})");
                        continue;
                    }
                    if (childNbl == null)
                        continue; // not an NBL, skip silently

                    // Use the child entry name (sans .nbl) as the subfolder.
                    // Sanitize defensively — archive filenames are normally
                    // well-behaved but we don't want a stray character to
                    // blow up Path.Combine on someone.
                    string subfolderName = SanitizeFolderName(
                        Path.GetFileNameWithoutExtension(childName));
                    string nblFolder = Path.Combine(exportFolder, subfolderName);

                    // Only create the subfolder if this NBL actually has
                    // weapons — no empty folders left lying around for NBLs
                    // that don't contain any weapon params.
                    int wrote = ExportWeaponsFromNbl(childNbl, nblFolder, errors,
                        ensureFolderExists: true);
                    exportedCount += wrote;
                    if (wrote > 0) nblsWithWeapons++;
                }
            }

            // Report outcome. Previously the operation completed silently
            // which made the menu item look broken when nothing matched.
            if (exportedCount == 0)
            {
                string body =
                    "No weapon parameter files were found.\r\n\r\n" +
                    "Weapon export looks for files named 'itemWeaponParam_*.xnr' inside " +
                    "the NMLL chunk of any NBL archive in the loaded container.";
                if (errors.Count > 0)
                {
                    body += "\r\n\r\nSome children couldn't be inspected:\r\n  " +
                            string.Join("\r\n  ", errors.Take(5)) +
                            (errors.Count > 5 ? $"\r\n  ... ({errors.Count - 5} more)" : "");
                }
                MessageBox.Show(body, "No Weapons Found",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                string summary =
                    $"Exported {exportedCount} weapon parameter file{(exportedCount == 1 ? "" : "s")} " +
                    $"from {nblsWithWeapons} NBL archive{(nblsWithWeapons == 1 ? "" : "s")} to:\r\n\r\n{exportFolder}";
                if (errors.Count > 0)
                {
                    summary += $"\r\n\r\n{errors.Count} child{(errors.Count == 1 ? "" : "ren")} couldn't be inspected " +
                               "(likely not NBL archives or corrupted entries).";
                }
                MessageBox.Show(summary, "Export Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// Export every itemWeaponParam_*.xnr from <paramref name="nbl"/>'s NMLL
        /// chunk into <paramref name="destFolder"/> as .txt files. Returns the
        /// number of files written. Errors are appended to <paramref name="errors"/>
        /// rather than thrown so a single bad NBL doesn't abort a multi-NBL export.
        /// </summary>
        /// <param name="ensureFolderExists">
        /// When true (used for per-NBL subfolders), the folder is created lazily
        /// on the first matching file. Avoids creating empty subfolders for
        /// NBLs that contain no weapons.
        /// </param>
        private int ExportWeaponsFromNbl(NblLoader nbl, string destFolder,
            List<string> errors, bool ensureFolderExists = false)
        {
            int wrote = 0;
            NblChunk nmllChunk;
            try
            {
                nmllChunk = (NblChunk)nbl.getFileParsed(0);
            }
            catch (Exception ex)
            {
                errors.Add($"{nbl.filename ?? "(unnamed NBL)"}: couldn't read NMLL chunk ({ex.Message})");
                return 0;
            }

            var nmllFilenames = nmllChunk.getFilenames();
            foreach (string filename in nmllFilenames)
            {
                if (!filename.Contains("itemWeaponParam"))
                    continue;
                if (!(nmllChunk.getFileParsed(filename) is WeaponParamFile weaponParamFile))
                    continue;

                if (ensureFolderExists && wrote == 0)
                {
                    try { Directory.CreateDirectory(destFolder); }
                    catch (Exception ex)
                    {
                        errors.Add($"{nbl.filename ?? "(unnamed NBL)"}: couldn't create folder '{destFolder}' ({ex.Message})");
                        return 0;
                    }
                }

                MemoryStream memStream = new MemoryStream();
                weaponParamFile.saveTextFile(memStream);
                File.WriteAllBytes(Path.Combine(destFolder, filename + ".txt"), memStream.ToArray());
                wrote++;
            }
            return wrote;
        }

        /// <summary>
        /// Replace any character that's invalid in a Windows folder name with '_'.
        /// Archive filenames are normally safe but we don't want a stray ':' or
        /// '*' (or an empty string) to crash Path.Combine.
        /// </summary>
        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "_";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                    chars[i] = '_';
            }
            return new string(chars);
        }

        private void importAllWeaponsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Mirrors the export contract:
            //   - If a single NBL is loaded, the folder is treated as a flat
            //     set of .txt files (back-compat with the original behavior).
            //   - If an AFS-style container is loaded, each subfolder in the
            //     picked folder corresponds to a child NBL by name. We match
            //     subfolder name → child entry name (with .nbl) and import
            //     the .txt files inside each subfolder into that NBL.
            if (loadedContainer == null || loadedContainer.getFilenames().Count == 0)
            {
                MessageBox.Show(
                    "No archive is currently loaded.\r\n\r\n" +
                    "Weapon import applies edits from text files back to itemWeaponParam_*.xnr " +
                    "entries in the NMLL chunk of an NBL archive. Open the target NBL (or AFS " +
                    "containing NBLs) first, then run import.",
                    "Nothing to Import Into",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!PromptForFolder("Select the folder containing weapon param text files to import", out string importFolder))
                return;

            int importedCount = 0;
            int nblsTouched = 0;
            var skippedFiles = new List<string>();
            var errors = new List<string>();

            if (loadedContainer is NblLoader topNbl)
            {
                // Case 1: a single NBL is loaded. Use the flat-folder layout
                // for back-compat: every weapon .txt sits directly inside the
                // chosen folder, no subfolders.
                int wrote = ImportWeaponsIntoNbl(topNbl, importFolder, skippedFiles, errors);
                importedCount += wrote;
                if (wrote > 0) nblsTouched++;
            }
            else
            {
                // Case 2: AFS-style container. Each direct subfolder corresponds
                // by name to a child NBL. Build a quick lookup of child NBL
                // entry names (lowercased, sans .nbl) so subfolder names like
                // "ob_009_mm_0" match the child entry "ob_009_mm_0.nbl".
                var childNames = loadedContainer.getFilenames();
                var childNblIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < childNames.Count; i++)
                {
                    string keyWithExt = childNames[i];
                    string keyNoExt = Path.GetFileNameWithoutExtension(keyWithExt);
                    // Register both with and without extension so either form works.
                    if (!childNblIndex.ContainsKey(keyWithExt))
                        childNblIndex[keyWithExt] = i;
                    if (!childNblIndex.ContainsKey(keyNoExt))
                        childNblIndex[keyNoExt] = i;
                }

                string[] subfolders;
                try
                {
                    subfolders = Directory.GetDirectories(importFolder);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Couldn't read the selected folder:\r\n\r\n{ex.Message}",
                        "Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (subfolders.Length == 0)
                {
                    MessageBox.Show(
                        "The selected folder doesn't contain any subfolders.\r\n\r\n" +
                        "When an AFS (or similar multi-NBL container) is loaded, weapon import " +
                        "expects one subfolder per target NBL — the same layout 'Export All " +
                        "Weapons' produces. Each subfolder name should match an NBL entry name " +
                        "in the archive (e.g. 'ob_009_mm_0' for 'ob_009_mm_0.nbl').",
                        "No Subfolders Found",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                foreach (string subfolder in subfolders)
                {
                    string subfolderName = Path.GetFileName(subfolder);
                    if (!childNblIndex.TryGetValue(subfolderName, out int childIdx))
                    {
                        errors.Add($"'{subfolderName}': no matching NBL in the loaded archive");
                        continue;
                    }

                    NblLoader targetNbl;
                    try
                    {
                        targetNbl = loadedContainer.getFileParsed(childIdx) as NblLoader;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"'{subfolderName}': failed to load target NBL ({ex.Message})");
                        continue;
                    }
                    if (targetNbl == null)
                    {
                        errors.Add($"'{subfolderName}': matching entry isn't an NBL");
                        continue;
                    }

                    int wrote = ImportWeaponsIntoNbl(targetNbl, subfolder, skippedFiles, errors);
                    importedCount += wrote;
                    if (wrote > 0) nblsTouched++;
                }
            }

            // Outcome summary. Three meaningful cases.
            if (importedCount == 0 && skippedFiles.Count == 0 && errors.Count == 0)
            {
                MessageBox.Show(
                    "No weapon parameter text files were found to import.\r\n\r\n" +
                    "Import looks for files whose names contain 'itemWeaponParam' " +
                    "(typically produced by 'Export All Weapons'). None were found, so " +
                    "nothing was imported.",
                    "No Files to Import",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (importedCount == 0)
            {
                var sb = new StringBuilder();
                sb.Append("No weapon parameter files were imported.\r\n\r\n");
                sb.Append("Check that text filenames correspond to existing " +
                          "itemWeaponParam_*.xnr entries");
                if (loadedContainer is NblLoader)
                {
                    sb.Append(" in the loaded NBL.");
                }
                else
                {
                    sb.Append(", and that subfolder names match NBL entry names in the " +
                              "loaded archive (e.g. 'ob_009_mm_0' for 'ob_009_mm_0.nbl').");
                }
                AppendDiagnostics(sb, skippedFiles, errors);
                MessageBox.Show(sb.ToString(), "No Matches Found",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                var sb = new StringBuilder();
                sb.Append($"Imported {importedCount} weapon parameter file{(importedCount == 1 ? "" : "s")} ");
                sb.Append($"across {nblsTouched} NBL archive{(nblsTouched == 1 ? "" : "s")}.\r\n\r\n");
                sb.Append("Remember to save the archive to persist these changes.");
                AppendDiagnostics(sb, skippedFiles, errors);
                MessageBox.Show(sb.ToString(), "Import Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// Import every itemWeaponParam_*.txt file in <paramref name="sourceFolder"/>
        /// into the NMLL chunk of <paramref name="nbl"/>. Returns the number of
        /// files successfully imported. Files that don't match an entry are
        /// appended to <paramref name="skippedFiles"/>; errors go to <paramref name="errors"/>.
        /// </summary>
        private int ImportWeaponsIntoNbl(NblLoader nbl, string sourceFolder,
            List<string> skippedFiles, List<string> errors)
        {
            int wrote = 0;
            string[] files;
            try
            {
                files = Directory.GetFiles(sourceFolder);
            }
            catch (Exception ex)
            {
                errors.Add($"'{Path.GetFileName(sourceFolder)}': couldn't read folder ({ex.Message})");
                return 0;
            }

            List<string> nmllFilenames;
            try
            {
                nmllFilenames = ((NblChunk)nbl.getFileParsed(0)).getFilenames();
            }
            catch (Exception ex)
            {
                errors.Add($"'{nbl.filename ?? Path.GetFileName(sourceFolder)}': couldn't read NMLL chunk ({ex.Message})");
                return 0;
            }

            foreach (string filename in files)
            {
                if (!filename.Contains("itemWeaponParam"))
                    continue;

                //try replacing .txt with nothing (e.g itemWeaponParam_01DKSword.xnr.txt)
                if (tryImportWeaponTextFile(nbl, nmllFilenames, filename,
                    Path.GetFileName(filename).Replace(".txt", "")))
                {
                    wrote++;
                }
                //try replacing .txt with .xnr (e.g itemWeaponParam_01DKSword.txt) -- parser doesn't do this, but other people may.
                else if (tryImportWeaponTextFile(nbl, nmllFilenames, filename,
                    Path.GetFileName(filename).Replace(".txt", ".xnr")))
                {
                    wrote++;
                }
                else
                {
                    skippedFiles.Add(Path.GetFileName(filename));
                }
            }
            return wrote;
        }

        /// <summary>
        /// Tack the skipped-file list and any error messages onto a result
        /// summary. Caps each section at 5 lines so a runaway count doesn't
        /// produce a MessageBox you can't read.
        /// </summary>
        private static void AppendDiagnostics(StringBuilder sb,
            List<string> skippedFiles, List<string> errors)
        {
            if (skippedFiles.Count > 0)
            {
                sb.Append($"\r\n\r\n{skippedFiles.Count} file{(skippedFiles.Count == 1 ? " was" : "s were")} skipped " +
                          "(no matching entry in the target NBL):\r\n  ");
                sb.Append(string.Join("\r\n  ", skippedFiles.Take(5)));
                if (skippedFiles.Count > 5)
                    sb.Append($"\r\n  ... ({skippedFiles.Count - 5} more)");
            }
            if (errors.Count > 0)
            {
                sb.Append($"\r\n\r\n{errors.Count} problem{(errors.Count == 1 ? "" : "s")} encountered:\r\n  ");
                sb.Append(string.Join("\r\n  ", errors.Take(5)));
                if (errors.Count > 5)
                    sb.Append($"\r\n  ... ({errors.Count - 5} more)");
            }
        }

        private bool tryImportWeaponTextFile(NblLoader nbl, List<string> nmllFilenames, string filepath, string attemptFilename)
        {
            if (nmllFilenames.Contains(attemptFilename) && (nbl.chunks[0].getFileParsed(attemptFilename) is WeaponParamFile))
            {
                WeaponParamFile paramFile = (WeaponParamFile)nbl.chunks[0].getFileParsed(attemptFilename);
                using (FileStream inStream = new FileStream(filepath, FileMode.Open))
                {
                    paramFile.loadTextFile(inStream);
                }
                return true;
            }
            return false;
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Launches a new, independent instance of PSU Archive Explorer so the user
        /// can have multiple sessions open at once (e.g. compare two archives side
        /// by side). The new process is fully independent and the current session
        /// is unaffected if it is closed.
        /// </summary>
        private void openNewSessionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(exePath) || !System.IO.File.Exists(exePath))
                {
                    // Fallback for unusual deployment scenarios (single-file publish, etc.)
                    exePath = Application.ExecutablePath;
                }

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty,
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not start a new session:\n" + ex.Message,
                    "New Session",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void insertNMLLFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (loadedContainer is NblLoader && treeView1.SelectedNode != null && treeView1.SelectedNode.Level == 1)
            {
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    using (Stream inStream = fileDialog.OpenFile())
                    {
                        ((ContainerFile)loadedContainer.getFileParsed(0)).addFile(treeView1.SelectedNode.Index, new RawFile(inStream, Path.GetFileName(fileDialog.FileName)));
                    }
                    treeView1.Nodes.Clear();
                    addChildFiles(treeView1.Nodes, loadedContainer);
                }
            }
        }

        private void decryptNMLBToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                byte[] fileContents = File.ReadAllBytes(fileDialog.FileName);

                MemoryStream fileStream = new MemoryStream(fileContents);
                fileStream.Seek(3, SeekOrigin.Begin);
                byte endian = (byte)fileStream.ReadByte();
                fileStream.Seek(0, SeekOrigin.Begin);
                BinaryReader fileLoader;
                bool bigEndian = false;
                if (endian == 0x42)
                {
                    fileLoader = new BigEndianBinaryReader(fileStream);
                    bigEndian = true;
                }
                else
                    fileLoader = new BinaryReader(fileStream);

                string formatName = new String(fileLoader.ReadChars(4));
                ushort fileVersion = fileLoader.ReadUInt16();
                ushort chunkFilenameLength = fileLoader.ReadUInt16();
                uint headerSize = fileLoader.ReadUInt32();
                uint nmllCount = fileLoader.ReadUInt32();
                uint uncompressedSize = fileLoader.ReadUInt32();
                uint compressedSize = fileLoader.ReadUInt32();
                uint pointerLength = fileLoader.ReadUInt32() / 4;
                uint blowfishKey = fileLoader.ReadUInt32();
                uint tmllHeaderSize = fileLoader.ReadUInt32();
                uint tmllDataSizeUncomp = fileLoader.ReadUInt32();
                uint tmllDataSizeComp = fileLoader.ReadUInt32();
                uint tmllCount = fileLoader.ReadUInt32();
                uint tmllHeaderLoc = 0;

                uint pointerLoc = 0;

                uint size = compressedSize == 0 ? uncompressedSize : compressedSize;

                uint nmllDataLoc = (uint)((headerSize + 0x7FF) & 0xFFFFF800);
                pointerLoc = (uint)(nmllDataLoc + size + 0x7FF) & 0xFFFFF800;
                if (tmllCount > 0)
                    tmllHeaderLoc = (pointerLoc + pointerLength * 4 + 0x7FF) & 0xFFFFF800;

                BlewFish fish = new BlewFish(blowfishKey, bigEndian);

                for (int i = 0; i < nmllCount; i++)
                {
                    int headerLoc = 0x40 + i * 0x60;
                    byte[] toDecrypt = new byte[0x30];
                    Array.Copy(fileContents, headerLoc, toDecrypt, 0, 0x30);
                    toDecrypt = fish.decryptBlock(toDecrypt);
                    Array.Copy(toDecrypt, 0, fileContents, headerLoc, 0x30);
                }

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < tmllCount; i++)
                {
                    uint headerLoc = (uint)(tmllHeaderLoc + 0x30 + i * 0x60);
                    byte[] toDecrypt = new byte[0x30];
                    Array.Copy(fileContents, headerLoc, toDecrypt, 0, 0x30);
                    toDecrypt = fish.decryptBlock(toDecrypt);
                    Array.Copy(toDecrypt, 0, fileContents, headerLoc, 0x30);

                    sb.Append(Encoding.ASCII.GetString(toDecrypt, 0, 0x20).Split('\0')[0] + "\t");
                }

                fileStream.Seek(nmllDataLoc, SeekOrigin.Begin);
                byte[] encryptedNmll = fileLoader.ReadBytes((int)size);
                byte[] decryptedNmll = fish.decryptBlock(encryptedNmll);
                byte[] decompressedNmll = compressedSize != 0 ? PrsCompDecomp.Decompress(decryptedNmll, uncompressedSize) : decryptedNmll;

                File.WriteAllText(fileDialog.FileName + ".tml.list", sb.ToString());
                File.WriteAllBytes(fileDialog.FileName + ".decrypt", fileContents);
                File.WriteAllBytes(fileDialog.FileName + ".encryptNmll", encryptedNmll);
                File.WriteAllBytes(fileDialog.FileName + ".decryptNmll", decryptedNmll);
                File.WriteAllBytes(fileDialog.FileName + ".decompressNmll", decompressedNmll);
            }
        }

        private void decryptNMLLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                byte[] fileContents = File.ReadAllBytes(fileDialog.FileName);

                MemoryStream fileStream = new MemoryStream(fileContents);
                BinaryReader fileLoader = new BinaryReader(fileStream);

                string formatName = new String(fileLoader.ReadChars(4));
                ushort fileVersion = fileLoader.ReadUInt16();
                ushort chunkFilenameLength = fileLoader.ReadUInt16();
                uint headerSize = fileLoader.ReadUInt32();
                uint nmllCount = fileLoader.ReadUInt32();
                uint uncompressedSize = fileLoader.ReadUInt32();
                uint compressedSize = fileLoader.ReadUInt32();
                uint pointerLength = fileLoader.ReadUInt32() / 4;
                uint blowfishKey = fileLoader.ReadUInt32();
                uint tmllHeaderSize = fileLoader.ReadUInt32();
                uint tmllDataSizeUncomp = fileLoader.ReadUInt32();
                uint tmllDataSizeComp = fileLoader.ReadUInt32();
                uint tmllCount = fileLoader.ReadUInt32();
                uint tmllHeaderLoc = 0;

                uint pointerLoc = 0;

                uint size = compressedSize == 0 ? uncompressedSize : compressedSize;

                uint nmllDataLoc = (uint)((headerSize + 0x7FF) & 0xFFFFF800);
                pointerLoc = (uint)(nmllDataLoc + size + 0x7FF) & 0xFFFFF800;
                if (tmllCount > 0)
                    tmllHeaderLoc = (pointerLoc + pointerLength * 4 + 0x7FF) & 0xFFFFF800;

                BlewFish fish = new BlewFish(blowfishKey);

                for (int i = 0; i < nmllCount; i++)
                {
                    int headerLoc = 0x40 + i * 0x60;
                    byte[] toDecrypt = new byte[0x30];
                    Array.Copy(fileContents, headerLoc, toDecrypt, 0, 0x30);
                    toDecrypt = fish.decryptBlock(toDecrypt);
                    Array.Copy(toDecrypt, 0, fileContents, headerLoc, 0x30);
                }
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < tmllCount; i++)
                {
                    uint headerLoc = (uint)(tmllHeaderLoc + 0x30 + i * 0x60);
                    byte[] toDecrypt = new byte[0x30];
                    Array.Copy(fileContents, headerLoc, toDecrypt, 0, 0x30);
                    toDecrypt = fish.decryptBlock(toDecrypt);
                    Array.Copy(toDecrypt, 0, fileContents, headerLoc, 0x30);

                    sb.Append(Encoding.ASCII.GetString(toDecrypt, 0, 0x20).Split('\0')[0] + "\t");
                    sb.Append(BitConverter.ToUInt16(fileContents, (int)(headerLoc + 0x4C)) + "\t");
                    sb.Append(BitConverter.ToUInt16(fileContents, (int)(headerLoc + 0x4E)) + "\n");
                }
                File.WriteAllText(fileDialog.FileName + ".tml.list", sb.ToString());
                File.WriteAllBytes(fileDialog.FileName + ".decrypt", fileContents);
            }
        }

        private void copyHashToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopySelectedSearchResultField(hit => hit.Archive);
        }

        private void copyFilenameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopySelectedSearchResultField(hit => hit.FileName);
        }

        private void copyPathToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopySelectedSearchResultField(hit => hit.InnerPath);
        }

        private void CopySelectedSearchResultField(Func<FileIndex.SearchResult, string> selector)
        {
            if (searchResults.SelectedItems.Count == 0) return;
            var hit = searchResults.SelectedItems[0].Tag as FileIndex.SearchResult;
            if (hit == null) return;

            string value = selector(hit);
            if (string.IsNullOrEmpty(value)) return;

            try
            {
                Clipboard.SetText(value);
                searchStatusLabel.Text = $"Copied: {value}";
            }
            catch
            {
                // Clipboard occasionally fails not worth crashing over
            }
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (settings == null || settings.IsDisposed)
            {
                settings = new MainSettings(this);
            }
            settings.Show();
            settings.BringToFront();
        }

        private void viewInHexButton_Click(object sender, EventArgs e)
        {
            if (currentRight != null)
            {
                PointeredFile pointeredFile = null;
                byte[] file = currentRight.ToRaw();
                if (currentRight.calculatedPointers != null)
                {
                    if (currentRight is PointeredFile)
                    {
                        pointeredFile = (PointeredFile)currentRight;
                    }
                    else
                    {
                        //For now, Big Endian files don't really need to be considered here since they'd be a PointeredFile already. Possibly add in further support if added elsewhere later
                        pointeredFile = new PointeredFile(currentRight.filename, file, currentRight.header, currentRight.calculatedPointers, 0, false);
                    }
                    pointeredFile.ToRaw();
                }

                string headingText = $"Selected File: {currentRight.filename}";

                if (currentFileHexForm != null)
                {
                    currentFileHexForm.Close();
                }
                if (pointeredFile != null)
                {
                    currentFileHexForm = new HexEditForm(file, headingText, true,
                        pointeredFile);
                }
                else
                {
                    currentFileHexForm = new HexEditForm(file, headingText, true,
                        null);
                }

                currentFileHexForm.Show();
            }
        }

        private void compressChunkToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            var node = treeView1.SelectedNode;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                ContainerFile parent = tag.OwnerContainer;
                ((NblChunk)parent.getFileParsed(treeView1.SelectedNode.Index)).Compressed = compressChunkToolStripMenuItem.Checked;
                if (compressChunkToolStripMenuItem.Checked)
                {
                    treeView1.SelectedNode.ForeColor = Color.Green;
                }
                else
                {
                    treeView1.SelectedNode.ForeColor = Color.Black;
                }
                if (node.Parent != null)
                {
                    if (parent.Compressed)
                    {
                        node.Parent.ForeColor = Color.Green;
                    }
                    else
                    {
                        node.Parent.ForeColor = Color.Black;
                    }
                }
            }
        }

        public void setNmllCompressOverride(NblLoader.CompressionOverride settings)
        {
            NblLoader.NmllCompressionOverride = settings;
        }

        public void setTmllCompressOverride(NblLoader.CompressionOverride settings)
        {
            NblLoader.TmllCompressionOverride = settings;
        }

        private void nblChunkContextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            FileTreeNodeTag tag = treeView1.SelectedNode.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                ContainerFile parent = tag.OwnerContainer;
                compressChunkToolStripMenuItem.Checked = ((NblChunk)parent.getFileParsed(treeView1.SelectedNode.Index)).Compressed;
            }
        }

        AnimationNameHashDialog dialog;

        private void calculateAnimationNameHashToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dialog == null || dialog.IsDisposed)
            {
                dialog = new AnimationNameHashDialog();
            }
            if (currentRight != null && !(currentRight is NblChunk))
            {
                dialog.SetFileName(currentRight.filename);
            }
            if (!dialog.Visible)
            {
                dialog.Show();
            }
        }

        private void addFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var node = treeView1.SelectedNode;
            if (node == null) return;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                ContainerFile parent = tag.OwnerContainer;
                OpenFileDialog replaceDialog = new OpenFileDialog();

                if (replaceDialog.ShowDialog() == DialogResult.OK)
                {
                    NblChunk chunk = parent.getFileParsed(node.Index) as NblChunk;
                    if (chunk == null) return;

                    RawFile file = new RawFile(replaceDialog.OpenFile(), Path.GetFileName(replaceDialog.FileName));
                    string filename = file.filename;
                    if (!chunk.ValidateFilename(filename))
                    {
                        while (!chunk.ValidateFilename(filename))
                        {
                            using (FileRenameForm rename = new FileRenameForm(filename))
                            {
                                if (rename.ShowDialog() == DialogResult.OK)
                                {
                                    filename = rename.FileName;
                                }
                                else
                                {
                                    return;
                                }
                            }
                        }
                    }
                    if (filename != file.filename)
                    {
                        file.filename = filename;
                    }

                    chunk.addFile(file);

                    TreeNode newNode = new TreeNode(file.filename);
                    FileTreeNodeTag newTag = new FileTreeNodeTag();
                    newTag.OwnerContainer = chunk;
                    newTag.FileName = file.filename;
                    newNode.Tag = newTag;
                    newNode.ContextMenuStrip = arbitraryFileContextMenuStrip;
                    node.Nodes.Add(newNode);

                    if (file.fileheader == "NMLL" || file.fileheader == "NMLB")
                    {
                        addChildFiles(newNode.Nodes, (ContainerFile)chunk.getFileParsed(newNode.Index));
                    }
                    treeView1.SelectedNode = newNode;
                }
            }
        }

        private void deleteFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var node = treeView1.SelectedNode;
            if (node == null) return;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag == null) return;
            bool isAdxFile = tag.FileName?.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) == true;
            if (isAdxFile && !(tag.OwnerContainer is NblChunk))
            {
                MessageBox.Show(
                    "Cannot delete the ADX file.\n\n" +
                    "The game requires this data to be present. You can replace " +
                    "this with another ADX file, but it cannot be removed from " +
                    "the hashed container.",
                    "PSU Archive Explorer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            bool isSfdFile = tag.FileName?.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase) == true;
            if (isSfdFile && !(tag.OwnerContainer is NblChunk))
            {
                MessageBox.Show(
                    "Cannot delete the SFD file.\n\n" +
                    "The game requires this data to be present. You can replace " +
                    "this with another SFD file, but it cannot be removed from " +
                    "the hashed container.",
                    "PSU Archive Explorer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (tag.OwnerContainer is NblChunk chunk)
            {
                chunk.removeFile(node.Index);
                node.Remove();
            }
        }

        private void renameFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var node = treeView1.SelectedNode;
            if (node == null) return;
            node.BeginEdit();
        }

        private void treeView1_BeforeLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            var node = e.Node;
            if (node == null) return;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                e.CancelEdit = true;
            }
        }

        private void treeView1_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            var node = e.Node;
            if (node == null) return;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                e.CancelEdit = true;
            }
            else if (tag != null && e.Label != null)
            {
                if (!(tag.OwnerContainer is FilenameAwareContainerFile facf) || facf.ValidateFilename(e.Label))
                {
                    tag.OwnerContainer.renameFile(node.Index, e.Label);
                }
                else
                {
                    e.CancelEdit = true;
                }
            }
        }

        private void splitContainer1_Panel2_Paint(object sender, PaintEventArgs e)
        {

        }

        /// <summary>
        /// Renders an informational panel in the right side of the form,
        /// styled to match the ADX-detected info panel (centered text on a
        /// light grey background). Used when the user selects a tree node
        /// that's meaningful in the hierarchy but doesn't have its own
        /// viewer (e.g. NMLL/TMLL chunks).
        /// The title is bold; the body uses regular weight. Stacked vertically
        /// in the centre of the panel via TableLayoutPanel so they stay
        /// centred as the form resizes.
        /// </summary>
        private void ShowChunkInfoPanel(string title, string body)
        {
            ClearRightPanel();
            BuildCenteredInfoPanel(title, body);
        }

        // Tracks whether the archive overview is currently the thing being
        // shown in the right panel. Lets MaybeShowArchiveOverviewPanel skip
        // its work most idle ticks, and lets us know when the user has
        // navigated away from it (so we don't re-show it on every idle).
        private bool isShowingArchiveOverview = false;

        /// <summary>
        /// Called every Application.Idle tick. Shows the archive overview
        /// panel iff: we have a loaded archive, the tree has nodes, no node
        /// is selected, the welcome screen is gone, and the right panel is
        /// empty. This naturally fires once after a fresh archive load (when
        /// ClearRightPanel has just run) and never re-fires until the user
        /// clicks somewhere and comes back to a clean state, which is exactly
        /// the behavior we want.
        /// </summary>
        private void MaybeShowArchiveOverviewPanel(object sender, EventArgs e)
        {
            if (welcomeVisible) return;
            if (loadedContainer == null) return;
            if (treeView1.Nodes.Count == 0) return;
            if (treeView1.SelectedNode != null) return;
            if (splitContainer1.Panel2.Controls.Count > 0) return;

            try
            {
                ShowArchiveOverviewPanel();
            }
            catch (Exception ex)
            {
                // The overview is purely informational. If something throws
                // while building it, just swallow it — better an empty grey
                // panel than a crash on every idle tick.
                Console.WriteLine("Failed to build archive overview: " + ex);
            }
        }

        /// <summary>
        /// Renders an "archive overview" info panel in the right side of the
        /// form, summarising the currently-loaded archive: filename, type, and
        /// chunk/file counts. Shown automatically after a fresh archive load
        /// (when no node is selected) so the user has context instead of an
        /// empty grey panel.
        /// </summary>
        private void ShowArchiveOverviewPanel()
        {
            ClearRightPanel();

            string archiveFileName = string.IsNullOrEmpty(fileDialog.FileName)
                ? "(unknown)"
                : Path.GetFileName(fileDialog.FileName);

            string archiveType;
            string countsLine;

            if (loadedContainer is NblLoader nbl)
            {
                archiveType = "NBL Archive";
                countsLine = BuildNblCountsLine(nbl);
            }
            else if (loadedContainer is AfsLoader)
            {
                archiveType = "AFS Container";
                int total = SafeCountTopLevel();
                countsLine = total + " entr" + (total == 1 ? "y" : "ies");
            }
            else
            {
                archiveType = loadedContainer.GetType().Name;
                int total = SafeCountTopLevel();
                countsLine = total + " entr" + (total == 1 ? "y" : "ies");
            }

            string body = archiveType + "\r\n"
                        + countsLine + "\r\n\r\n"
                        + "Click any item in the tree on the left to view it.";

            BuildCenteredInfoPanel(archiveFileName, body);
            isShowingArchiveOverview = true;
        }

        /// <summary>
        /// Builds the per-chunk counts line for an NBL archive, e.g.
        /// "NMLL chunk: 47 files · TMLL chunk: 12 files". Falls back to a
        /// generic count if introspecting the chunks fails for any reason.
        /// </summary>
        private string BuildNblCountsLine(NblLoader nbl)
        {
            try
            {
                var topNames = nbl.getFilenames();
                var parts = new List<string>();

                for (int i = 0; i < topNames.Count; i++)
                {
                    string chunkLabel = topNames[i];
                    int innerCount = 0;
                    try
                    {
                        if (nbl.getFileParsed(i) is ContainerFile inner)
                        {
                            innerCount = inner.getFilenames().Count;
                        }
                    }
                    catch
                    {
                        innerCount = -1; // signal "couldn't count"
                    }

                    parts.Add(innerCount >= 0
                        ? chunkLabel + ": " + innerCount + " file" + (innerCount == 1 ? "" : "s")
                        : chunkLabel);
                }

                return string.Join("  ·  ", parts);
            }
            catch
            {
                int total = SafeCountTopLevel();
                return total + " chunk" + (total == 1 ? "" : "s");
            }
        }

        /// <summary>
        /// Returns the number of top-level entries in the loaded container,
        /// or 0 on any error. Used by the overview panel's fallback paths.
        /// </summary>
        private int SafeCountTopLevel()
        {
            try { return loadedContainer?.getFilenames()?.Count ?? 0; }
            catch { return 0; }
        }

        /// <summary>
        /// Shared layout used by ShowChunkInfoPanel and ShowArchiveOverviewPanel:
        /// bold title, regular-weight body, both centred horizontally and
        /// vertically on a light-grey background. Mirrors the ADX info panel's
        /// look (minus the audio player).
        /// </summary>
        private void BuildCenteredInfoPanel(string title, string body)
        {
            var infoPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(229, 229, 229),
            };

            // 4-row layout: top spacer, title, body, bottom spacer. The two
            // 50%-height spacers keep the title+body block vertically centred
            // as the form resizes, while the AutoSize rows in the middle let
            // the labels grow to fit their text.
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent,
                Padding = new Padding(10),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            var titleLabel = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 28,
            };

            var bodyLabel = new Label
            {
                Text = body,
                Font = new Font("Segoe UI", 10.5f),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 180,
            };

            layout.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, 0);
            layout.Controls.Add(titleLabel, 0, 1);
            layout.Controls.Add(bodyLabel, 0, 2);
            layout.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, 3);

            infoPanel.Controls.Add(layout);
            splitContainer1.Panel2.Controls.Add(infoPanel);
        }
    }
}