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
        // ====================== File Index Search ======================

        private System.Windows.Forms.Timer searchDebounceTimer;
        private const string SearchPlaceholder = "Search files...";
        private bool searchBoxHasRealText = false;

        private void searchBox_Enter(object sender, EventArgs e)
        {
            if (!searchBoxHasRealText)
            {
                searchBox.Text = "";
                searchBox.ForeColor = System.Drawing.SystemColors.WindowText;
            }
        }

        private void searchBox_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(searchBox.Text))
            {
                searchBoxHasRealText = false;
                searchBox.ForeColor = System.Drawing.Color.Gray;
                searchBox.Text = SearchPlaceholder;
            }
        }

        private void searchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void searchBox_TextChanged(object sender, EventArgs e)
        {
            if (!searchBox.Focused && searchBox.Text == SearchPlaceholder) return;

            searchBoxHasRealText = !string.IsNullOrEmpty(searchBox.Text)
                                   && searchBox.Text != SearchPlaceholder;

            if (searchDebounceTimer == null)
            {
                searchDebounceTimer = new System.Windows.Forms.Timer();
                searchDebounceTimer.Interval = 250;
                searchDebounceTimer.Tick += (s, args) =>
                {
                    searchDebounceTimer.Stop();
                    RunSearch(searchBoxHasRealText ? searchBox.Text : "");
                };
            }

            searchDebounceTimer.Stop();
            searchDebounceTimer.Start();
        }

        private void RunSearch(string query)
        {
            if (!FileIndex.IsLoaded)
            {
                searchStatusLabel.Text = "PSU File Index was not detected. Please place psu_file_index.gz next to the .exe.";
                searchResults.Visible = false;
                treeView1.Visible = !welcomeVisible;
                return;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                searchResults.Visible = false;
                treeView1.Visible = !welcomeVisible;
                searchStatusLabel.Text = "";
                return;
            }

            if (searchResults.Columns.Count == 0)
            {
                searchResults.Columns.Add("Filename", 140);
                searchResults.Columns.Add("Archive", 120);
            }

            var hits = FileIndex.Search(query, 10000);

            searchResults.BeginUpdate();
            searchResults.Items.Clear();
            foreach (var hit in hits)
            {
                // For hashed ADX entries column 1 uses a the adx mapping names
                // instead of the 32hex.adx hash file names
                string displayName = GetSearchResultDisplayName(hit);

                var item = new ListViewItem(displayName);
                item.SubItems.Add(hit.Archive);

                // Tooltip shows the InnerPath as before, but if we substituted
                // a friendly name we also show the real filename so the user
                // can see which hash they're actually about to open.
                item.ToolTipText = !ReferenceEquals(displayName, hit.FileName)
                    ? $"{hit.FileName}\n{hit.InnerPath}"
                    : hit.InnerPath;

                item.Tag = hit;
                searchResults.Items.Add(item);
            }
            searchResults.EndUpdate();

            treeView1.Visible = false;
            searchResults.Visible = true;
            searchResults.ShowItemToolTips = true;

            searchStatusLabel.Text = hits.Count >= 5000
                ? "Showing first 5000 matches"
                : $"{hits.Count} match{(hits.Count == 1 ? "" : "es")}";
        }

        private void searchResults_DoubleClick(object sender, EventArgs e)
        {
            if (searchResults.SelectedItems.Count == 0) return;
            var hit = searchResults.SelectedItems[0].Tag as FileIndex.SearchResult;
            if (hit == null) return;

            if (string.IsNullOrEmpty(gameDirectory))
            {
                var choice = MessageBox.Show(
                    "To open this file, PSU Archive Explorer needs to know where your " +
                    "game is installed (the folder containing online.exe and the 'data' folder).\n\n" +
                    "Would you like to select it now?",
                    "Game Directory Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (choice != DialogResult.Yes) return;

                if (!PromptForGameDirectory()) return;
            }

            string hashPath = Path.Combine(gameDirectory, "data", hit.Archive);

            if (!File.Exists(hashPath))
            {
                MessageBox.Show(
                    $"Couldn't find this hash in your game directory:\n{hashPath}\n\n" +
                    $"Filename: {hit.FileName}\n" +
                    $"Archive:  {hit.Archive}\n" +
                    $"Path:     {hit.InnerPath}",
                    "Not found in game directory",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            searchBoxHasRealText = false;
            searchBox.Text = SearchPlaceholder;
            searchBox.ForeColor = System.Drawing.Color.Gray;
            searchResults.Items.Clear();
            searchStatusLabel.Text = "";
            searchResults.Visible = false;
            treeView1.Visible = true;

            ClearRightPanel();
            pendingAdxReplacementBytes = null;
            bool success = openPSUArchive(hashPath, treeView1.Nodes);
            if (!success)
            {
                TryOpenAsAdx(hashPath);
                success = true;
            }

            if (success)
            {
                fileDialog.FileName = hashPath;
                Text = "PSU Archive Explorer " +
                       System.Reflection.Assembly.GetExecutingAssembly().GetName().Version +
                       " — " + hit.Archive;
            }
        }
        private bool PromptForGameDirectory()
        {
            using (var dlg = new CommonOpenFileDialog())
            {
                dlg.IsFolderPicker = true;
                dlg.Title = "Select your PSU game folder (contains online.exe and the 'DATA' folder)";

                if (dlg.ShowDialog() != CommonFileDialogResult.Ok) return false;

                string selected = dlg.FileName;
                string dataFolder = Path.Combine(selected, "data");

                if (!Directory.Exists(dataFolder))
                {
                    var result = MessageBox.Show(
                        $"No 'data' subfolder found in:\n{selected}\n\nUse this folder anyway?",
                        "Game Directory",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (result != DialogResult.Yes) return false;
                }

                gameDirectory = selected;
                return true;
            }
        }

        /// <summary>
        /// Returns the display name we want to show in the search results
        /// "Filename" column. For a hashed ADX whose hash exists in
        /// <see cref="AdxHashMap"/>, this is the friendly name from the map
        /// For everything else, this is just the original FileName.
        /// Returns the original hit.FileName instance (by reference) when no
        /// substitution happened, so callers can detect substitution with
        /// ReferenceEquals.
        /// </summary>
        private static string GetSearchResultDisplayName(FileIndex.SearchResult hit)
        {
            if (hit?.FileName == null) return hit?.FileName;

            // Only ADX entries can have friendly names. Skip other extensions
            // to avoid an unnecessary dictionary lookup on every row.
            if (!hit.FileName.EndsWith(".adx", System.StringComparison.OrdinalIgnoreCase))
                return hit.FileName;

            string baseName = System.IO.Path.GetFileNameWithoutExtension(hit.FileName);
            if (baseName.Length != 32) return hit.FileName;

            if (TryGetAdxFriendlyName(baseName, out string friendly)
                && !string.IsNullOrEmpty(friendly))
            {
                return friendly;
            }

            return hit.FileName;
        }
    }
}