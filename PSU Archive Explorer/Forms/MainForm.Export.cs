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
        private void exportBlob_Click(object sender, EventArgs e)
        {
            if (loadedContainer is NblLoader)
            {
                CommonOpenFileDialog goodOpenFileDialog = new CommonOpenFileDialog();
                goodOpenFileDialog.IsFolderPicker = true;
                if (goodOpenFileDialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    ((NblLoader)loadedContainer).exportDataBlob(goodOpenFileDialog.FileName);
                }
            }

        }

        private async void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (loadedContainer == null)
            {
                TreeNode node = treeView1.SelectedNode;
                if (node != null &&
                    node.Tag is FileTreeNodeTag tag &&
                    tag.OwnerContainer == null &&
                    tag.FileName != null &&
                    tag.FileName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tag.FullPath))
                {
                    byte[] bytesToSave = pendingAdxReplacementBytes;
                    if (bytesToSave == null)
                    {
                        try { bytesToSave = File.ReadAllBytes(tag.FullPath); }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Could not read the original file:\n{ex.Message}",
                                "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                    string hashName = Path.GetFileNameWithoutExtension(tag.FullPath);
                    saveFileDialog1.FileName = hashName;
                    saveFileDialog1.Filter = "All files (*.*)|*.*";
                    if (saveFileDialog1.ShowDialog() != DialogResult.OK) return;
                    string destPath = saveFileDialog1.FileName;
                    if (destPath.EndsWith(".adx", StringComparison.OrdinalIgnoreCase))
                        destPath = destPath.Substring(0, destPath.Length - 4);
                    try { File.WriteAllBytes(destPath, bytesToSave); }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not save file:\n{ex.Message}",
                            "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    MessageBox.Show(
                        $"Saved to:\n{destPath}\n\n" +
                        $"(The .adx extension was stripped so the filename matches the hash.)",
                        "Save Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    pendingAdxReplacementBytes = null;
                    return;
                }
            }

            if (loadedContainer != null)
            {
                saveFileDialog1.FileName = fileDialog.FileName;
                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    string destPath = saveFileDialog1.FileName;
                    var containerToSave = loadedContainer;

                    // Lock down anything that could mutate the model mid-serialize.
                    // The form itself stays enabled — user can drag the window,
                    // scroll the tree, click around to view things.
                    arbitraryFileContextMenuStrip.Enabled = false;
                    nblChunkContextMenuStrip.Enabled = false;
                    splitContainer1.Panel2.Enabled = false;

                    // Indeterminate progress bar — PRS compression doesn't report progress,
                    // so we use the bouncing/marquee style to show "still working".
                    var prevStyle = actionProgressBar.Style;
                    actionProgressBar.Style = ProgressBarStyle.Marquee;
                    actionProgressBar.MarqueeAnimationSpeed = 30;
                    string prevStatus = progressStatusLabel.Text;
                    progressStatusLabel.Text = "Saving... (compressing archive, please wait)";

                    try
                    {
                        byte[] savedContainer = await Task.Run(() => containerToSave.ToRaw());
                        await Task.Run(() => File.WriteAllBytes(destPath, savedContainer));

                        this.Text = "PSU Archive Explorer " + Path.GetFileName(destPath);
                        fileDialog.FileName = destPath;
                        progressStatusLabel.Text = $"Saved {Path.GetFileName(destPath)} ({savedContainer.Length:N0} bytes)";

                        // Clear the message after 4 seconds, but only if it hasn't been replaced by something else
                        string savedText = progressStatusLabel.Text;
                        _ = Task.Delay(4000).ContinueWith(_ =>
                        {
                            if (progressStatusLabel.Text == savedText)
                                progressStatusLabel.Text = "Progress:";
                        }, TaskScheduler.FromCurrentSynchronizationContext());
                    }
                    catch (ScriptValidationException exc)
                    {
                        string joinedErrors = String.Join("\r\n", exc.ScriptValidationErrors.Select(error =>
                            error.LineNumber != -1
                                ? $"{error.FunctionName}, line {error.LineNumber}: {error.Description}"
                                : $"{error.FunctionName}: {error.Description}"));
                        MessageBox.Show($"Could not save archive. \r\nFile \"{exc.FileName}\" failed to validate for the following reasons: \r\n{joinedErrors}",
                            "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        progressStatusLabel.Text = prevStatus;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Save failed:\n{ex.Message}",
                            "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        progressStatusLabel.Text = prevStatus;
                    }
                    finally
                    {
                        actionProgressBar.Style = prevStyle;
                        actionProgressBar.MarqueeAnimationSpeed = 0;
                        actionProgressBar.Value = 0;
                        arbitraryFileContextMenuStrip.Enabled = true;
                        nblChunkContextMenuStrip.Enabled = true;
                        splitContainer1.Panel2.Enabled = true;
                    }
                }
            }
        }

        private void exportSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            exportSelected();
        }

        private void extractFileTreeContextItem_Click(object sender, EventArgs e)
        {
            exportSelected();
        }

        private void exportAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //It is a sin to use the standard folder dialog
            CommonOpenFileDialog goodOpenFileDialog = new CommonOpenFileDialog();
            goodOpenFileDialog.IsFolderPicker = true;

            if (goodOpenFileDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                exportAll(treeView1.Nodes, goodOpenFileDialog.FileName);
            }
        }

        private void exportAll(TreeNodeCollection treeNodes, string folderName)
        {
            Directory.CreateDirectory(folderName);
            foreach (TreeNode node in treeNodes)
            {
                exportNode(node, folderName);
            }
        }

        private void exportSelected()
        {
            SaveFileDialog exportFileDialog = new SaveFileDialog();

            if (currentRight != null)
            {
                string suggestedName = currentRight.filename;

                if (treeView1.SelectedNode?.Tag is FileTreeNodeTag tag && !string.IsNullOrEmpty(tag.FileName))
                {
                    suggestedName = tag.FileName;
                }

                exportFileDialog.FileName = suggestedName;

                // Special handling for ADX files (works for both standalone hashed ADX and normal .adx)
                bool isAdxFile = suggestedName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);
                // Special handling for DAT sound files (xobxDDNS / xobxKPTD)
                bool isDatFile = suggestedName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

                if (currentRight is ITextureFile)
                {
                    exportFileDialog.FileName = exportFileDialog.FileName.Replace(".xvr", ".png");
                    exportFileDialog.Filter = "Portable Network Graphics (*.png)|*.png|Xbox PowerVR Texture (*.xvr)|*.xvr";
                }
                else if (currentRight is TextFile)
                {
                    exportFileDialog.FileName = exportFileDialog.FileName.Replace(".bin", ".txt");
                    exportFileDialog.Filter = "Text (*.txt)|*.txt|Binary File (*.bin)|*.bin";
                }
                else if (isAdxFile)
                {
                    if (batchWavExport)
                    {
                        // Default to .wav when the ADX to WAV setting is on. User can still
                        // switch back to .adx via the filter dropdown.
                        exportFileDialog.FileName = Path.ChangeExtension(exportFileDialog.FileName, ".wav");
                        exportFileDialog.Filter = "WAV Audio (*.wav)|*.wav|ADX Audio (*.adx)|*.adx|All Files (*.*)|*.*";
                    }
                    else
                    {
                        exportFileDialog.FileName = Path.ChangeExtension(exportFileDialog.FileName, ".adx");
                        exportFileDialog.Filter = "ADX Audio (*.adx)|*.adx|All Files (*.*)|*.*";
                    }
                }
                else if (isDatFile)
                {
                    // Only suggest .wav if the in memory bytes look like a real sound DAT.
                    // Non sound .dat files fall through to raw extraction.
                    bool datIsSound = currentRight is UnpointeredFile unpointedCheck
                                      && DatConverter.IsSoundDat(unpointedCheck.theData);

                    if (batchDat2WavExport && datIsSound)
                    {
                        exportFileDialog.FileName = Path.ChangeExtension(exportFileDialog.FileName, ".wav");
                        exportFileDialog.Filter = "WAV Audio (*.wav)|*.wav|DAT File (*.dat)|*.dat|All Files (*.*)|*.*";
                    }
                    else
                    {
                        exportFileDialog.FileName = Path.ChangeExtension(exportFileDialog.FileName, ".dat");
                        exportFileDialog.Filter = "DAT File (*.dat)|*.dat|All Files (*.*)|*.*";
                    }
                }

                if (exportFileDialog.ShowDialog() == DialogResult.OK)
                {
                    if (currentRight is ITextureFile &&
                        Path.GetExtension(exportFileDialog.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        ((ITextureFile)currentRight).mipMaps[0].Save(exportFileDialog.FileName);
                    }
                    else if (currentRight is TextFile &&
                             Path.GetExtension(exportFileDialog.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ((TextFile)currentRight).saveToTextFile(exportFileDialog.OpenFile());
                    }
                    else if (isAdxFile)
                    {
                        string chosenExt = Path.GetExtension(exportFileDialog.FileName);
                        bool saveAsWav = chosenExt.Equals(".wav", StringComparison.OrdinalIgnoreCase);

                        if (saveAsWav && currentRight is UnpointeredFile unpointed)
                        {
                            try
                            {
                                // theData holds the raw ADX bytes exactly as loaded no
                                // metadata wrapper, which is what AdxDecoder expects.
                                byte[] adxBytes = unpointed.theData;
                                byte[] wavBytes = AdxDecoder.DecodeToWav(adxBytes);
                                File.WriteAllBytes(exportFileDialog.FileName, wavBytes);
                            }
                            catch (Exception ex)
                            {
                                // AdxDecoder rejects any non-PSU-standard ADX variant.
                                // Offer the user a raw .adx fallback rather than silently failing.
                                DialogResult fallback = MessageBox.Show(
                                    $"ADX → WAV conversion failed:\n\n{ex.Message}\n\n" +
                                    "Would you like to save the raw .adx file instead?",
                                    "ADX Decode Failed",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning);

                                if (fallback == DialogResult.Yes)
                                {
                                    string adxPath = Path.ChangeExtension(exportFileDialog.FileName, ".adx");
                                    bool originalExportMetaData = exportMetaData;
                                    exportMetaData = false;
                                    extractFile(currentRight, adxPath);
                                    exportMetaData = originalExportMetaData;
                                }
                            }
                        }
                        else
                        {
                            // User picked .adx or any non .wav) from the filter dropdown,
                            // or currentRight isn't an UnpointeredFile for some reason
                            // write raw via the existing extract path.
                            bool originalExportMetaData = exportMetaData;
                            exportMetaData = false;
                            extractFile(currentRight, exportFileDialog.FileName);
                            exportMetaData = originalExportMetaData;
                        }
                    }
                    else if (isDatFile)
                    {
                        string chosenExt = Path.GetExtension(exportFileDialog.FileName);
                        bool saveAsWav = chosenExt.Equals(".wav", StringComparison.OrdinalIgnoreCase);

                        if (saveAsWav && currentRight is UnpointeredFile unpointedDat)
                        {
                            try
                            {
                                byte[] wavBytes = DatConverter.DecodeToWav(unpointedDat.theData);
                                File.WriteAllBytes(exportFileDialog.FileName, wavBytes);
                            }
                            catch (Exception ex)
                            {
                                // Either a non-sound .dat or a corrupt sound .dat offer
                                // the user a raw .dat fallback rather than silently failing.
                                DialogResult fallback = MessageBox.Show(
                                    $"DAT → WAV conversion failed:\n\n{ex.Message}\n\n" +
                                    "Would you like to save the raw .dat file instead?",
                                    "DAT Decode Failed",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning);

                                if (fallback == DialogResult.Yes)
                                {
                                    string datPath = Path.ChangeExtension(exportFileDialog.FileName, ".dat");
                                    bool originalExportMetaData = exportMetaData;
                                    exportMetaData = false;
                                    extractFile(currentRight, datPath);
                                    exportMetaData = originalExportMetaData;
                                }
                            }
                        }
                        else
                        {
                            // If user picked .dat or any non .wav from the filter dropdown
                            // write raw via the existing extract path.
                            bool originalExportMetaData = exportMetaData;
                            exportMetaData = false;
                            extractFile(currentRight, exportFileDialog.FileName);
                            exportMetaData = originalExportMetaData;
                        }
                    }
                    else
                    {
                        extractFile(currentRight, exportFileDialog.FileName);
                    }
                }
            }
        }

        private void exportNode(TreeNode node, string fileDirectory)
        {
            if (!(node.Tag is FileTreeNodeTag tag))
                return;

            string originalFilename = tag.FileName;

            // ---- Standalone file case (fake container: OwnerContainer is null,
            // bytes live on disk at tag.FullPath). Used for single-file opens like
            // a raw .adx. There's nothing to recurse into, so we handle it here
            // and return.
            if (tag.OwnerContainer == null)
            {
                if (string.IsNullOrEmpty(tag.FullPath) || !File.Exists(tag.FullPath))
                    return;

                bool isAdx = originalFilename != null &&
                             originalFilename.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);
                bool isDat = originalFilename != null &&
                             originalFilename.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

                if (isAdx && batchWavExport)
                {
                    string wavName = Path.ChangeExtension(originalFilename, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);

                    try
                    {
                        byte[] adxBytes = File.ReadAllBytes(tag.FullPath);
                        byte[] wavBytes = AdxDecoder.DecodeToWav(adxBytes);
                        File.WriteAllBytes(wavPath, wavBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"ADX->WAV conversion failed for {originalFilename}: {ex.Message}. " +
                            "Writing raw .adx instead.");
                        string adxPath = Path.Combine(fileDirectory, originalFilename);
                        File.Copy(tag.FullPath, adxPath, overwrite: true);
                    }
                }
                else if (isDat && batchDat2WavExport && DatConverter.IsSoundDat(tag.FullPath))
                {
                    string wavName = Path.ChangeExtension(originalFilename, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);

                    try
                    {
                        byte[] datBytes = File.ReadAllBytes(tag.FullPath);
                        byte[] wavBytes = DatConverter.DecodeToWav(datBytes);
                        File.WriteAllBytes(wavPath, wavBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"DAT->WAV conversion failed for {originalFilename}: {ex.Message}. " +
                            "Writing raw .dat instead.");
                        string datPath = Path.Combine(fileDirectory, originalFilename);
                        File.Copy(tag.FullPath, datPath, overwrite: true);
                    }
                }
                else
                {
                    // Not an audio file (or conversion setting off, or non-sound .dat)
                    // just copy the file through.
                    string destPath = Path.Combine(fileDirectory, originalFilename);
                    try { File.Copy(tag.FullPath, destPath, overwrite: true); }
                    catch (Exception ex) { Console.WriteLine($"Copy failed for {originalFilename}: {ex.Message}"); }
                }

                return;
            }

            // ---- Normal container case ----
            ContainerFile parent = tag.OwnerContainer;
            int fileIndex = node.Index;
            List<string> parentFilenames = parent.getFilenames();
            PsuFile file = parent.getFileParsed(fileIndex);

            //NBLs only have "NML(B/L)" or "TML(B/L)" chunks as children.
            if (!(parent is NblLoader))
            {
                if (file is ITextureFile && batchPngExport)
                {
                    string filename = Path.Combine(fileDirectory, Path.GetFileName(originalFilename + ".png"));
                    ((ITextureFile)file).mipMaps[0].Save(filename);
                }
                else if (batchWavExport
                         && originalFilename != null
                         && originalFilename.EndsWith(".adx", StringComparison.OrdinalIgnoreCase)
                         && file is UnpointeredFile adxFile)
                {
                    string uniqueName = getUniqueFilename(originalFilename, fileIndex, parentFilenames);
                    string wavName = Path.ChangeExtension(uniqueName, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);

                    try
                    {
                        byte[] wavBytes = AdxDecoder.DecodeToWav(adxFile.theData);
                        File.WriteAllBytes(wavPath, wavBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"ADX->WAV conversion failed for {originalFilename}: {ex.Message}. " +
                            "Writing raw .adx instead.");
                        string adxPath = Path.Combine(fileDirectory, uniqueName);
                        extractFile(file, adxPath);
                    }
                }
                else if (batchDat2WavExport
                         && originalFilename != null
                         && originalFilename.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                         && file is UnpointeredFile datUnpointed
                         && DatConverter.IsSoundDat(datUnpointed.theData))
                {
                    string uniqueName = getUniqueFilename(originalFilename, fileIndex, parentFilenames);
                    string wavName = Path.ChangeExtension(uniqueName, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);

                    try
                    {
                        byte[] wavBytes = DatConverter.DecodeToWav(datUnpointed.theData);
                        File.WriteAllBytes(wavPath, wavBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"DAT->WAV conversion failed for {originalFilename}: {ex.Message}. " +
                            "Writing raw .dat instead.");
                        string datPath = Path.Combine(fileDirectory, uniqueName);
                        extractFile(file, datPath);
                    }
                }
                else
                {
                    if (batchExportSubArchiveFiles || !(file is ContainerFile))
                    {
                        string filename = Path.Combine(fileDirectory, getUniqueFilename(originalFilename, fileIndex, parentFilenames));
                        extractFile(file, filename);
                    }
                }
            }

            if (file is ContainerFile)
            {
                string newFolder = fileDirectory + @"\" + getUniqueFilename(originalFilename, fileIndex, parentFilenames) + "_ext";
                exportAll(node.Nodes, newFolder);
            }
            else
            {
                foreach (TreeNode nodeChild in node.Nodes)
                {
                    exportNode(nodeChild, fileDirectory);
                }
            }
        }

        private string getUniqueFilename(string originalFilename, int fileIndex, List<string> parentFilenames)
        {
            string usedFilename;
            if (parentFilenames.Count(filename => filename == originalFilename) > 1)
            {
                usedFilename = Path.GetFileName(originalFilename) + "_" + (fileIndex - parentFilenames.FindIndex(name => name == originalFilename)) + Path.GetExtension(originalFilename);
            }
            else
            {
                usedFilename = originalFilename;
            }
            return usedFilename;
        }

        private void extractFile(PsuFile psuFile, string filename)
        {
            RawFile file = psuFile.ToRawFile(0);
            byte[] bytes = file.WriteToBytes(exportMetaData);
            try
            {
                File.WriteAllBytes(filename, bytes);
            }
            catch
            {
                MessageBox.Show("Unable to extract " + filename + ". The file may be in use or otherwise inaccessible. Skipping.");
            }
        }

        private void extractAllInFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog goodOpenFileDialog = new CommonOpenFileDialog();
            goodOpenFileDialog.IsFolderPicker = true;

            if (goodOpenFileDialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            string rootFolder = goodOpenFileDialog.FileName;

            string[] fileNames = batchRecursive
                ? Directory.GetFiles(rootFolder, "*.*", SearchOption.AllDirectories)
                : Directory.GetFiles(rootFolder);

            actionProgressBar.Value = 0;
            actionProgressBar.Maximum = fileNames.Length;
            progressStatusLabel.Text = $"Progress: 0/{fileNames.Length} Files. Please wait, this can take time.";

            menuStrip1.Enabled = false;

            var worker = new System.ComponentModel.BackgroundWorker
            {
                WorkerReportsProgress = true
            };

            worker.DoWork += (s, args) =>
            {
                var files = (string[])args.Argument;
                for (int i = 0; i < files.Length; i++)
                {
                    string fileName = files[i];
                    string newFolder = Path.GetDirectoryName(fileName);

                    try
                    {
                        extractPSUArchive(fileName, newFolder);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to extract {fileName}: {ex.Message}");
                    }

                    worker.ReportProgress(i + 1, fileName);
                }
            };

            worker.ProgressChanged += (s, args) =>
            {
                actionProgressBar.Value = args.ProgressPercentage;
                progressStatusLabel.Text =
                    $"Progress: {args.ProgressPercentage}/{actionProgressBar.Maximum} Files. Please wait, this can take time.";
            };

            worker.RunWorkerCompleted += (s, args) =>
            {
                actionProgressBar.Value = 0;
                progressStatusLabel.Text = args.Error != null
                    ? "Progress: Failed — " + args.Error.Message
                    : "Progress: Done!";
                menuStrip1.Enabled = true;
                worker.Dispose();
            };

            worker.RunWorkerAsync(fileNames);
        }
    }
}