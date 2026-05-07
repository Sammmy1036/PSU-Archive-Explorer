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
        // ====================== Official ADX Header Check ======================
        private bool IsValidAdxFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath) || new FileInfo(filePath).Length < 16)
                    return false;

                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] header = new byte[16];
                    int bytesRead = fs.Read(header, 0, 16);
                    if (bytesRead < 8) return false;

                    if (header[0] != 0x80 || header[1] != 0x00) return false;

                    ushort headerSize = (ushort)((header[2] << 8) | header[3]);
                    if (headerSize < 8 || headerSize > 4096) return false;

                    if (header[4] != 0x03 && header[4] != 0x04) return false;
                    if (header[5] != 0x12) return false;           // most PSU ADX use 0x12
                    byte channels = header[7];
                    if (channels == 0 || channels > 8) return false;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        // ====================== Open File Handler ======================
        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                string fileName = fileDialog.FileName;
                this.Text = "PSU Archive Explorer " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + " " + Path.GetFileName(fileName);
                ClearRightPanel();
                pendingAdxReplacementBytes = null;

                bool success = openPSUArchive(fileName, treeView1.Nodes);

                if (!success)
                {
                    TryOpenAsAdx(fileName);
                }
            }
        }

        // ====================== Smart ADX Detection & Open ======================
        private void TryOpenAsAdx(string fileName)
        {
            if (!IsValidAdxFile(fileName))
            {
                ShowAdxSuggestion(fileName);
                return;
            }

            //  Hashed ADX files
            if (!fileName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase))
            {
                if (IsHashedAdxFilename(fileName))
                {
                    // Does NOT rename hashed files, hashes with a singular ADX file get the extension
                    OpenSingleFileAsAdx(fileName);
                    return;
                }

                // Normal ADX files get auto renamed appending the .adx
                string newPath = Path.ChangeExtension(fileName, ".adx");

                try
                {
                    if (File.Exists(newPath))
                    {
                        DialogResult dr = MessageBox.Show(
                            $"A file named '{Path.GetFileName(newPath)}' already exists.\n\nOverwrite it?",
                            "ADX Rename Conflict",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);

                        if (dr != DialogResult.Yes)
                        {
                            OpenSingleFileAsAdx(fileName);
                            return;
                        }

                        File.Delete(newPath);
                    }

                    File.Move(fileName, newPath);
                    fileName = newPath;

                    MessageBox.Show(
                        $"Valid ADX audio file detected!\n\nRenamed → {Path.GetFileName(fileName)}\n\nAdded to tree view.",
                        "ADX Auto-Renamed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not auto-rename:\n{ex.Message}\n\nOpening anyway.",
                        "Rename Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            OpenSingleFileAsAdx(fileName);
        }

        private bool IsHashedAdxFilename(string fileName)
        {
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (baseName.Length != 32)
                return false;

            return baseName.All(c => "0123456789abcdefABCDEF".Contains(c));
        }

        // ====================== Open ADX as tree node ======================
        private void OpenSingleFileAsAdx(string filePath)
        {
            treeView1.BeginUpdate();
            try
            {
                treeView1.Nodes.Clear();
                ClearRightPanel();

                loadedContainer = null;   // Since there are no real containers for ADX files

                string fileNameOnDisk = Path.GetFileName(filePath);

                // Hashed ADX files will append .adx to the tree view only
                string displayName = fileNameOnDisk;

                string cleaned = fileNameOnDisk.TrimStart('-');   // removes any leading dashes the container might add
                if (cleaned.Length == 32 && cleaned.All(c => "0123456789abcdefABCDEF".Contains(c)))
                {
                    displayName = cleaned.ToLowerInvariant() + ".adx";
                }

                TreeNode adxNode = new TreeNode(displayName);
                adxNode.Tag = new FileTreeNodeTag
                {
                    OwnerContainer = null,
                    FileName = displayName,           // Extraction will now save as hash.adx
                    FullPath = filePath
                };

                adxNode.ContextMenuStrip = arbitraryFileContextMenuStrip;

                treeView1.Nodes.Add(adxNode);
                treeView1.SelectedNode = adxNode;

                LoadAdxIntoRightPanel(filePath, adxNode);
            }
            finally
            {
                treeView1.EndUpdate();
            }
        }

        // ====================== Display ADX Info + Preview in right panel ======================
        private void LoadAdxIntoRightPanel(string filePath, TreeNode node)
        {
            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                string filename = Path.GetFileName(filePath);

                if (currentFileHexForm != null && !currentFileHexForm.IsDisposed)
                    currentFileHexForm.Close();

                // Clearing controls disposes the previous AdxPreviewPanel (if any),
                // which in turn stops playback and releases NAudio resources.
                ClearRightPanel();

                // Derive the hash (sans .adx) from the filename, if applicable,
                // and look up the original sound title in the ADX hash map.
                string hashKey = Path.GetFileNameWithoutExtension(filename).TrimStart('-');
                string mappedTitle = null;
                if (hashKey.Length == 32
                    && hashKey.All(c => "0123456789abcdefABCDEF".Contains(c)))
                {
                    AdxHashMap.TryGetValue(hashKey.ToLowerInvariant(), out mappedTitle);
                }

                string infoText =
                    "ADX audio file detected.\n\n" +
                    "If you wish to replace this file, convert a .wav to .adx and rename it\n" +
                    "to the hash name without the .adx extension, or replace the .adx in\n" +
                    "this software with your new .adx sound file and save the hash.\n\n" +
                    $"File name: {filename}";

                if (mappedTitle != null)
                {
                    infoText += $"\n\nADX Mapping: {mappedTitle}";
                }

                var previewPanel = new AdxPreviewPanel(filePath, infoText, mappedTitle ?? filename);
                splitContainer1.Panel2.Controls.Add(previewPanel);

                byte[] header = new byte[4];
                if (data.Length >= 4)
                {
                    Array.Copy(data, 0, header, 0, 4);
                }
                currentRight = new UnpointeredFile(filename, data, header);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load ADX file:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                currentRight = null;
            }
        }

        // ====================== ADX Suggestion Dialog ======================
        private void ShowAdxSuggestion(string fileName)
        {
            string message = "Could not load this file as a PSU archive.\n\n" +
                             "Would you like to open the containing folder?";

            DialogResult result = MessageBox.Show(message,
                "Unknown File Format",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + fileName + "\"");
                }
                catch (Exception ex) { Console.WriteLine("Failed to open Explorer: " + ex); }
            }
        }
    }
}