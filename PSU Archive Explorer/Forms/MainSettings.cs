using psu_archive_explorer;
using PSULib.FileClasses.Archives;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace psu_archive_explorer
{
    public partial class MainSettings : Form
    {
        public MainForm mainForm;
        public MainSettings(MainForm form)
        {
            mainForm = form;
            InitializeComponent();
            ExportPNGCheckBox.Checked = mainForm.batchPngExport;
            ExportWAVCheckBox.Checked = mainForm.batchWavExport;
            ExportDAT2WAVCheckBox.Checked = mainForm.batchDat2WavExport;
            exportMetaDataCheckBox.Checked = mainForm.exportMetaData;
            BatchExportSubContainersCheckBox.Checked = mainForm.batchExportSubArchiveFiles;
            BatchExportSubDirectoriesCheckBox.Checked = mainForm.batchRecursive;
            gameDirectoryTextBox.Text = mainForm.gameDirectory ?? "";
            gameDirectoryTextBox.SelectionStart = 0;
            gameDirectoryTextBox.SelectionLength = 0;

            this.StartPosition = FormStartPosition.Manual;
            this.MinimumSize = new Size(248, 470);
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            switch (NblLoader.NmllCompressionOverride)
            {
                case NblLoader.CompressionOverride.ForceCompress: alwaysCompressNmllRadioButton.Checked = true; break;
                case NblLoader.CompressionOverride.ForceDecompress: alwaysDecompressNmllRadioButton.Checked = true; break;
                default: useOriginalNmllCompressionRadioButton.Checked = true; break;
            }

            switch (NblLoader.TmllCompressionOverride)
            {
                case NblLoader.CompressionOverride.ForceCompress: alwaysCompressTmllRadioButton.Checked = true; break;
                case NblLoader.CompressionOverride.ForceDecompress: alwaysDecompressTmllRadioButton.Checked = true; break;
                default: useOriginalTmllCompressionRadioButton.Checked = true; break;
            }
        }
        private void ExportPNGCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            mainForm.batchPngExport = ExportPNGCheckBox.Checked;
        }

        private void exportMetaDataCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            mainForm.exportMetaData = exportMetaDataCheckBox.Checked;
        }

        private void BatchExportSubContainersCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            mainForm.batchExportSubArchiveFiles = BatchExportSubContainersCheckBox.Checked;
        }

        private void BatchExportSubDirectories_CheckedChanged(object sender, EventArgs e)
        {
            mainForm.batchRecursive = BatchExportSubDirectoriesCheckBox.Checked;
        }

        private void nmllChunkOverrideOptions_CheckedChanged(object sender, EventArgs e)
        {
            NblLoader.CompressionOverride compressionOverride = NblLoader.CompressionOverride.UseFileSetting;
            if (alwaysCompressNmllRadioButton.Checked)
            {
                compressionOverride = NblLoader.CompressionOverride.ForceCompress;
            }
            else if (alwaysDecompressNmllRadioButton.Checked)
            {
                compressionOverride = NblLoader.CompressionOverride.ForceDecompress;
            }
            mainForm.setNmllCompressOverride(compressionOverride);
        }

        private void tmllChunkOverrideOptions_CheckedChanged(object sender, EventArgs e)
        {
            NblLoader.CompressionOverride compressionOverride = NblLoader.CompressionOverride.UseFileSetting;
            if (alwaysCompressTmllRadioButton.Checked)
            {
                compressionOverride = NblLoader.CompressionOverride.ForceCompress;
            }
            else if (alwaysDecompressTmllRadioButton.Checked)
            {
                compressionOverride = NblLoader.CompressionOverride.ForceDecompress;
            }
            mainForm.setTmllCompressOverride(compressionOverride);
        }

        private void ExportWAVCheckBox_CheckedChanged_1(object sender, EventArgs e)
        {
            mainForm.batchWavExport = ExportWAVCheckBox.Checked;
        }

        private void ExportDAT2WAVCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            mainForm.batchDat2WavExport = ExportDAT2WAVCheckBox.Checked;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            gameDirectoryTextBox.Text = mainForm.gameDirectory ?? "";
            gameDirectoryTextBox.SelectionStart = 0;
            gameDirectoryTextBox.SelectionLength = 0;
        }

        private void gameDirectoryBrowseButton_Click(object sender, EventArgs e)
        {
            using (var dlg = new CommonOpenFileDialog())
            {
                dlg.IsFolderPicker = true;
                dlg.Title = "Select your PSU game folder (contains online.exe and the 'data' folder)";
                if (!string.IsNullOrEmpty(mainForm.gameDirectory))
                {
                    dlg.InitialDirectory = mainForm.gameDirectory;
                }

                if (dlg.ShowDialog() != CommonFileDialogResult.Ok) return;

                string selected = dlg.FileName;

                // Smart adjustment: if the user selected the 'data' subfolder
                // instead of the game root, walk up one level automatically.
                // CommonOpenFileDialog can return paths with or without trailing
                // separators so we check both the raw string and DirectoryInfo.
                try
                {
                    string trimmed = selected.TrimEnd(
                        System.IO.Path.DirectorySeparatorChar,
                        System.IO.Path.AltDirectorySeparatorChar);

                    string lastName = System.IO.Path.GetFileName(trimmed);
                    if (string.IsNullOrEmpty(lastName))
                    {
                        // Edge case: root drive selected, GetFileName returns ""
                        var di2 = new DirectoryInfo(trimmed);
                        lastName = di2.Name;
                    }

                    if (string.Equals(lastName, "data", StringComparison.OrdinalIgnoreCase))
                    {
                        string parent = System.IO.Path.GetDirectoryName(trimmed);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            selected = parent;
                            MessageBox.Show(
                                $"It looks like you selected the 'data' subfolder.\n\n" +
                                $"Automatically adjusted to the game root:\n{selected}",
                                "Game Directory Auto-Adjusted",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                    }
                }
                catch { /* leave selected as-is if detection fails */ }

                string dataFolder = Path.Combine(selected, "data");

                if (!Directory.Exists(dataFolder))
                {
                    var result = MessageBox.Show(
                        $"No 'data' subfolder found in:\n{selected}\n\nUse this folder anyway?",
                        "Game Directory",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (result != DialogResult.Yes) return;
                }

                mainForm.gameDirectory = selected;
                gameDirectoryTextBox.Text = selected;
                gameDirectoryTextBox.SelectionStart = 0;
                gameDirectoryTextBox.SelectionLength = 0;
            }
        }
    }
}