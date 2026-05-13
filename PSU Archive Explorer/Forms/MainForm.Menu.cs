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

                bool isNblFile = fileName.EndsWith(".nbl", StringComparison.OrdinalIgnoreCase);
                bool isSfdVideo = fileName.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase);
                bool isAdxFile = fileName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);
                bool isDatFile = fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

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
                               "• Right click the file and Extract Selected to save it.\n" +
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
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK && saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                string folderToOpen = folderBrowserDialog1.SelectedPath;
                string fileToSave = saveFileDialog1.FileName;
                AfsLoader.createFromDirectory(folderToOpen, fileToSave);
            }
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
            if (loadedContainer is NblLoader && loadedContainer.getFilenames().Count > 0)
            {
                if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                {
                    //getFilenames is relatively expensive.
                    NblChunk nmllChunk = (NblChunk)loadedContainer.getFileParsed(0);
                    var nmllFilenames = nmllChunk.getFilenames();
                    foreach (string filename in nmllFilenames)
                    {
                        if (filename.Contains("itemWeaponParam") && nmllChunk.getFileParsed(filename) is WeaponParamFile weaponParamFile)
                        {
                            MemoryStream memStream = new MemoryStream();
                            weaponParamFile.saveTextFile(memStream);
                            File.WriteAllBytes(folderBrowserDialog1.SelectedPath + "\\" + filename + ".txt", memStream.ToArray());
                        }
                    }
                }
            }
        }

        private void importAllWeaponsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (loadedContainer is NblLoader && loadedContainer.getFilenames().Count > 0)
            {
                if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                {
                    var files = Directory.GetFiles(folderBrowserDialog1.SelectedPath);
                    //getFilenames is relatively expensive.
                    var nmllFilenames = ((NblChunk)loadedContainer.getFileParsed(0)).getFilenames();
                    foreach (string filename in files)
                    {
                        if (filename.Contains("itemWeaponParam"))
                        {
                            //try replacing .txt with nothing (e.g itemWeaponParam_01DKSword.xnr.txt)
                            if (!tryImportWeaponTextFile((NblLoader)loadedContainer, nmllFilenames, filename, Path.GetFileName(filename).Replace(".txt", "")))
                            {
                                //try replacing .txt with .xnr (e.g itemWeaponParam_01DKSword.txt) -- parser doesn't do this, but other people may.
                                if (!tryImportWeaponTextFile((NblLoader)loadedContainer, nmllFilenames, filename, Path.GetFileName(filename).Replace(".txt", ".xnr")))
                                {

                                }
                            }
                        }
                    }
                }
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
                    "Open New Session",
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