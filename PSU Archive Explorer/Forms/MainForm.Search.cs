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

        // Case-insensitive compare to whether the box currently shows the
        // placeholder. We compare like this because the Designer-set initial
        // text ("Search Files...") was historically inconsistent with the
        // SearchPlaceholder constant's casing ("Search files..."), so a strict
        // == comparison failed on first launch and broke all the placeholder
        // logic. The boolean check on searchBoxHasRealText is the source of
        // truth; this is just the visual check.
        private bool ShowingPlaceholder()
        {
            return !searchBoxHasRealText
                && string.Equals(searchBox.Text, SearchPlaceholder,
                                 System.StringComparison.OrdinalIgnoreCase);
        }

        private void searchBox_Enter(object sender, EventArgs e)
        {
            // The placeholder text itself is cleared on the user's first real
            // keystroke (see searchBox_KeyDown), not on focus, so that
            // programmatic / incidental focus changes — e.g. focus being
            // restored to the search box after a cancelled File ▸ Open
            // dialog, after dismissing a MessageBox, Alt+Tabbing back into
            // the app, the form's default tab order kicking in on launch,
            // etc. — don't wipe the "Search files..." hint before the user
            // has actually decided to search.
            //
            // WinForms also auto-selects all of a TextBox's text when it
            // gains focus via tab / programmatic focus, which paints the
            // placeholder in highlight blue and visually misleads the user.
            // Hook Application.Idle once to collapse the selection AFTER all
            // pending focus / selection messages have been processed by Win32.
            if (ShowingPlaceholder())
            {
                EventHandler idleHandler = null;
                idleHandler = (s, args) =>
                {
                    Application.Idle -= idleHandler;
                    if (searchBox != null
                        && !searchBox.IsDisposed
                        && searchBox.Focused
                        && ShowingPlaceholder())
                    {
                        searchBox.SelectionStart = searchBox.TextLength;
                        searchBox.SelectionLength = 0;
                    }
                };
                Application.Idle += idleHandler;
            }
        }

        // Belt-and-braces: if anything still manages to select the
        // placeholder text, clicking into the box will also collapse the
        // selection. MouseUp fires after the textbox processes the click
        // and any select-all-on-focus, so we win the race here.
        private void searchBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (ShowingPlaceholder() && searchBox.SelectionLength > 0)
            {
                searchBox.SelectionStart = searchBox.TextLength;
                searchBox.SelectionLength = 0;
            }
        }

        // Clicking into the search box is an unambiguous user signal to start
        // searching, so clear the placeholder right away rather than waiting
        // for the first keystroke. (searchBox_Enter deliberately does NOT
        // clear it — see the comment there — because Enter also fires on
        // incidental focus changes like Alt+Tab back into the app, focus
        // being restored after a cancelled dialog, default tab order on
        // launch, etc., and clearing on those would wipe the hint before the
        // user has decided to search. A real mouse click is different: it's
        // always the user actively pointing at the box.)
        //
        // Left-button only, to match how every other Windows textbox treats
        // "click to interact": a right-click should open a context menu, not
        // erase the hint.
        private void searchBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && ShowingPlaceholder())
            {
                searchBox.Text = "";
                searchBox.ForeColor = System.Drawing.SystemColors.WindowText;
                // searchBoxHasRealText is updated by searchBox_TextChanged
                // when the user actually types something; clearing to ""
                // here intentionally leaves it false so searchBox_Leave can
                // restore the placeholder if they click away without typing.
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
                return;
            }

            // First real keystroke while the placeholder is showing: clear it
            // and switch the text color so the user's input is visible.
            // We ignore pure modifier presses (Shift / Ctrl / Alt on their own)
            // so e.g. tapping Shift in preparation to type doesn't blank the
            // placeholder without producing any visible character.
            if (ShowingPlaceholder()
                && e.KeyCode != Keys.ShiftKey
                && e.KeyCode != Keys.ControlKey
                && e.KeyCode != Keys.Menu)
            {
                searchBox.Text = "";
                searchBox.ForeColor = System.Drawing.SystemColors.WindowText;
            }
        }

        private void searchBox_TextChanged(object sender, EventArgs e)
        {
            if (!searchBox.Focused
                && string.Equals(searchBox.Text, SearchPlaceholder, System.StringComparison.OrdinalIgnoreCase))
                return;

            searchBoxHasRealText = !string.IsNullOrEmpty(searchBox.Text)
                                   && !string.Equals(searchBox.Text, SearchPlaceholder, System.StringComparison.OrdinalIgnoreCase);

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
        /// Modern Vista-style folder picker. Use this instead of the legacy
        /// FolderBrowserDialog (the tiny tree-view one) so users get the
        /// full Explorer chrome — sidebar, breadcrumbs, search box, drive
        /// list, the works.
        /// </summary>
        /// <param name="title">Title text shown at the top of the dialog.
        /// Should describe what the user is picking, e.g. "Select folder
        /// containing weapon param text files".</param>
        /// <param name="selectedPath">On success, the absolute path the user
        /// chose. Null on cancel.</param>
        /// <returns>True if the user picked a folder, false if they cancelled.</returns>
        private bool PromptForFolder(string title, out string selectedPath)
        {
            using (var dlg = new CommonOpenFileDialog())
            {
                dlg.IsFolderPicker = true;
                dlg.Title = title;
                dlg.EnsurePathExists = true;
                dlg.AllowNonFileSystemItems = false;

                // If we know where the game lives, pre-select that as the
                // starting location it's almost certainly where the user
                // wants to be browsing for archive folders.
                if (!string.IsNullOrEmpty(gameDirectory) && Directory.Exists(gameDirectory))
                {
                    dlg.InitialDirectory = gameDirectory;
                }

                if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    selectedPath = dlg.FileName;
                    return true;
                }

                selectedPath = null;
                return false;
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

        // ====================== Search box focus drop ======================
        //
        // The search box uses a manual "placeholder" pattern: when it loses
        // focus and is empty, searchBox_Leave restores the "Search files..."
        // grey placeholder text. That only fires when focus moves to another
        // FOCUSABLE control.
        //
        // Clicking on dead space (the welcome screen labels, empty split panel
        // areas, the form background, etc.) does not move focus, so the
        // placeholder never comes back until the user clicks an actual button
        // or other focusable control.
        //
        // To fix this, we wire MouseDown on every plausible dead-space surface
        // to drop focus via this.ActiveControl = null. That triggers the
        // search box's Leave event and restores the placeholder immediately.

        /// <summary>
        /// Call this once during form construction (after InitializeComponent).
        /// Wires up MouseDown handlers on all the static dead-space controls.
        /// Welcome-screen controls are wired up inside ShowWelcomeScreen instead,
        /// because they are created and destroyed dynamically.
        /// </summary>
        private void WireSearchFocusDrop()
        {
            // Normalize the placeholder text on startup. The Designer historically
            // had this with different casing ("Search Files...") than our constant
            // ("Search files..."), which broke every == comparison and let the
            // OS-level select-all on focus paint the placeholder in highlight
            // blue. Force them into sync so casing drift in the Designer can't
            // cause that class of bug again.
            if (string.Equals(searchBox.Text, SearchPlaceholder, System.StringComparison.OrdinalIgnoreCase))
            {
                searchBox.Text = SearchPlaceholder;
                searchBox.ForeColor = System.Drawing.Color.Gray;
                searchBoxHasRealText = false;
            }

            // The form itself (clicks on the bare form background).
            this.MouseDown += DeadSpace_MouseDown;

            // Both panels of the split container. These cover most of the
            // window real estate and are the most common click targets when
            // the user is "clicking off" the search box.
            splitContainer1.Panel1.MouseDown += DeadSpace_MouseDown;
            splitContainer1.Panel2.MouseDown += DeadSpace_MouseDown;

            // Collapse any auto-selected placeholder text when the user
            // clicks into the search box. See searchBox_MouseUp for why this
            // is needed in addition to the Application.Idle hook in
            // searchBox_Enter.
            searchBox.MouseUp += searchBox_MouseUp;

            // Clear the placeholder on a real mouse click — see
            // searchBox_MouseDown for why this is wired here rather than in
            // searchBox_Enter (focus events fire on incidental focus changes
            // like Alt+Tab and cancelled dialogs, which shouldn't wipe the
            // hint; an actual click should).
            searchBox.MouseDown += searchBox_MouseDown;

            // The tree view itself doesn't need this it already takes focus on
            // click. Same with searchResults, the buttons, etc.
        }

        /// <summary>
        /// Generic MouseDown handler used by every dead-space surface. Drops
        /// focus to nothing, which causes the search box's Leave event to fire
        /// and the "Search files..." placeholder to be restored.
        /// Cheap to call repeatedly there's no penalty if focus is already
        /// elsewhere.
        /// </summary>
        private void DeadSpace_MouseDown(object sender, MouseEventArgs e)
        {
            // Only drop focus if it's currently in the search box; otherwise
            // we'd be stealing focus away from whatever else the user might
            // be interacting with (e.g. typing in a future text field).
            if (searchBox != null && searchBox.Focused)
            {
                this.ActiveControl = null;
            }
        }
    }
}