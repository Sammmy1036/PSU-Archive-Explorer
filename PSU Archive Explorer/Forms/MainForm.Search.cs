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
        private const string SearchPlaceholderStrings = "Search strings...";
        private bool searchBoxHasRealText = false;

        // The placeholder text varies by mode. CurrentPlaceholder gives the
        // text we should display when nothing is typed; IsPlaceholder returns
        // true if a given string matches EITHER placeholder, so existing
        // checks that just need "is this a placeholder?" don't have to know
        // which mode is active.
        private string CurrentPlaceholder
        {
            get { return IsStringSearchMode() ? SearchPlaceholderStrings : SearchPlaceholder; }
        }

        private static bool IsPlaceholder(string text)
        {
            return string.Equals(text, SearchPlaceholder, StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, SearchPlaceholderStrings, StringComparison.OrdinalIgnoreCase);
        }

        // String search mode state. searchModeCombo is a designer-defined
        // ComboBox sitting to the right of the search box, hidden by default.
        // EnableStringSearchToggleIfAvailable reveals it when the optional
        // psu_string_index.gz file is detected next to the EXE.
        //
        // The "Strings" mode prompts the user before loading (the index is
        // huge — tens of seconds — so we don't load it without consent). On
        // confirmation, the index is loaded on a background thread with
        // progress reported through actionProgressBar; while loading,
        // stringIndexLoading is true and the search box is disabled. Once
        // loaded, subsequent toggles between Files/Strings are instant.
        private bool stringIndexLoading = false;
        private bool stringIndexLoadAttempted = false;

        private string GetStringIndexPath()
        {
            // Look for any of the three supported index formats next to
            // the EXE and return the first one we find. StringIndex.cs
            // auto-detects format from the path; we just have to point
            // at the right thing.
            //
            //   1. psu_string_index.idx/  (binary OR chunked — both live
            //      inside this folder, distinguished by what's inside it)
            //   2. psu_string_index.gz    (single gzipped JSON file)
            //
            // Returns the folder path or .gz path of whichever exists,
            // or the default folder path if none exist (so callers that
            // pass this to IndexFolderExists still get a definite "no").
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            string folderPath = Path.Combine(exeDir, StringIndex.IndexFolderName);
            string gzPath = Path.Combine(exeDir, "psu_string_index.gz");

            if (StringIndex.IndexFolderExists(folderPath))
                return folderPath;
            if (StringIndex.IndexFolderExists(gzPath))
                return gzPath;
            return folderPath; // fall-through; nothing exists
        }

        /// <summary>
        /// Called once from the constructor. If psu_string_index.gz exists
        /// next to the EXE, the search-mode combo becomes visible AND the
        /// search box is resized to leave room for it. If the file isn't
        /// present, the combo stays hidden and the search box keeps its full
        /// designer width — feature is invisible and zero-cost.
        ///
        /// The runtime resize is needed because both controls anchor to the
        /// right edge of the panel: leaving them at their designer sizes
        /// causes the combo to overlap the search box. Subtracting the
        /// combo's width plus a small gap from the searchBox at runtime is
        /// simpler than fighting WinForms anchor layout in the designer.
        /// </summary>
        private void EnableStringSearchToggleIfAvailable()
        {
            try
            {
                string path = GetStringIndexPath();
                if (StringIndex.IndexFolderExists(path))
                {
                    const int gap = 4;
                    // Shrink searchBox to leave room for the combo.
                    int newWidth = searchBox.Width - searchModeCombo.Width - gap;
                    if (newWidth > 60) // sanity: don't collapse it
                        searchBox.Width = newWidth;

                    // Reposition the combo to sit immediately to the right of
                    // the resized searchBox. Both are anchored Top|Right so
                    // they stay together as the panel resizes.
                    searchModeCombo.Left = searchBox.Right + gap;
                    searchModeCombo.Top = searchBox.Top;
                    searchModeCombo.Visible = true;
                }
                // If the folder isn't present, do nothing. String search is an
                // optional dev-oriented feature; users who don't have the
                // index folder shouldn't see any UI hint that it exists.
            }
            catch
            {
                // Probe failed for some reason (path resolution, permissions,
                // etc.). String search is optional; fail silently and leave
                // the combo hidden rather than annoy the user on every
                // launch with an error about a feature they don't use.
            }
        }

        private bool IsStringSearchMode()
        {
            return searchModeCombo.Visible && searchModeCombo.SelectedIndex == 1;
        }

        /// <summary>
        /// Handler for the search-mode combo. Switching to Strings the first
        /// time prompts for confirmation and kicks off a background load;
        /// switching back to Files snaps back instantly. Switching to Strings
        /// again after the index is already loaded is also instant.
        /// </summary>
        private void searchModeCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Switching to Files mode: clear any string-search UI state.
            if (!IsStringSearchMode())
            {
                searchBox.Enabled = true;
                if (!searchBoxHasRealText)
                {
                    searchBox.Text = SearchPlaceholder;
                    searchBox.ForeColor = System.Drawing.Color.Gray;
                }
                searchStatusLabel.Text = "";
                // Re-run the current query under the new mode so results
                // refresh immediately.
                RunSearch(searchBoxHasRealText ? searchBox.Text : "");
                return;
            }

            // Switching to Strings mode.
            // If the index is already loaded, just refresh the results.
            if (StringIndex.IsLoaded)
            {
                if (!searchBoxHasRealText)
                {
                    searchBox.Text = SearchPlaceholderStrings;
                    searchBox.ForeColor = System.Drawing.Color.Gray;
                }
                RunSearch(searchBoxHasRealText ? searchBox.Text : "");
                return;
            }

            // Already loading on a background thread — ignore re-entry.
            if (stringIndexLoading)
            {
                return;
            }

            // First time switching to Strings. Confirm the wait.
            var choice = MessageBox.Show(
                "Loading the PSU string index can take a while, " +
                "depending on your storage speed.\n\n" +
                "Once loaded, you can search for in game text \n" +
                "Results will show which hash file contains a match.\n\n" +
                "Would you like to proceed?",
                "Load String Index",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (choice != DialogResult.Yes)
            {
                // User declined — snap back to Files mode.
                searchModeCombo.SelectedIndex = 0;
                return;
            }

            BeginLoadStringIndex();
        }

        /// <summary>
        /// Loads the string index on a background thread, reporting progress
        /// through actionProgressBar / progressStatusLabel. The search box
        /// is disabled during the load so a user can't type a query into a
        /// not-yet-ready index. On completion (success or failure) UI state
        /// is restored on the form thread.
        /// </summary>
        private void BeginLoadStringIndex()
        {
            stringIndexLoading = true;
            stringIndexLoadAttempted = true;

            // Snapshot prior progress UI state so we can restore it cleanly
            // afterward. The progress bar isn't currently used elsewhere in
            // the app, but capturing the state is cheap insurance against
            // future conflicts (e.g. if archive loading starts using it).
            int priorProgressValue = actionProgressBar.Value;
            string priorProgressText = progressStatusLabel.Text;

            actionProgressBar.Minimum = 0;
            actionProgressBar.Maximum = 100;
            actionProgressBar.Value = 0;
            progressStatusLabel.Text = "Progress: Loading string index...";
            searchBox.Enabled = false;
            searchModeCombo.Enabled = false;

            string path = GetStringIndexPath();

            // Run the load on a background thread. StringIndex.LoadFromFolder
            // reads meta and all token shards eagerly; string shards are
            // loaded lazily during searches. The token-shard loop is the
            // bulk of the work and reports progress shard-by-shard.
            System.Threading.Tasks.Task.Run(() =>
            {
                bool ok = false;
                Exception failure = null;
                try
                {
                    ok = StringIndex.LoadFromFolder(path, (frac, label) =>
                    {
                        int pct = (int)Math.Round(frac * 100);
                        if (pct < 0) pct = 0;
                        if (pct > 100) pct = 100;
                        try
                        {
                            this.BeginInvoke((Action)(() =>
                            {
                                if (!this.IsDisposed)
                                {
                                    actionProgressBar.Value = pct;
                                    progressStatusLabel.Text =
                                        $"Progress: {label} ({pct}%)";
                                }
                            }));
                        }
                        catch
                        {
                            // The form may be closing while a progress
                            // callback fires; swallow rather than crash.
                        }
                    });
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                try
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        if (this.IsDisposed) return;

                        stringIndexLoading = false;
                        searchBox.Enabled = true;
                        searchModeCombo.Enabled = true;

                        // Restore the progress UI to its prior state. We
                        // don't leave the bar full or empty — we leave it
                        // exactly as we found it, so the rest of the app
                        // can use it freely after this.
                        actionProgressBar.Value = priorProgressValue;
                        progressStatusLabel.Text = priorProgressText;

                        if (ok)
                        {
                            // Re-paint the placeholder for string mode and
                            // run any pending query.
                            if (!searchBoxHasRealText)
                            {
                                searchBox.Text = SearchPlaceholderStrings;
                                searchBox.ForeColor = System.Drawing.Color.Gray;
                            }

                            int fileCount = StringIndex.TotalFileCount;
                            searchStatusLabel.Text =
                                $"String index loaded ({fileCount:N0} files).";
                            RunSearch(searchBoxHasRealText ? searchBox.Text : "");
                        }
                        else
                        {
                            // Load failed — flip back to Files mode and tell
                            // the user. Include the full exception chain so a
                            // diagnosis is possible: wrappers like IOException
                            // and InvalidDataException carry the inner
                            // underlying error which is usually the actual
                            // cause (e.g. OutOfMemoryException, malformed
                            // JSON token, truncated gzip stream).
                            string msg;
                            if (failure != null)
                            {
                                var sb = new System.Text.StringBuilder();
                                sb.AppendLine("The string index could not be loaded:");
                                sb.AppendLine();
                                var ex = failure;
                                int depth = 0;
                                while (ex != null && depth < 5)
                                {
                                    sb.AppendLine($"[{ex.GetType().Name}] {ex.Message}");
                                    ex = ex.InnerException;
                                    depth++;
                                }
                                msg = sb.ToString();
                            }
                            else
                            {
                                msg = "The string index could not be loaded. " +
                                      "The file may be corrupt or an unsupported version.";
                            }
                            MessageBox.Show(msg, "Load Failed",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            searchModeCombo.SelectedIndex = 0;
                        }
                    }));
                }
                catch
                {
                    // Form closed mid-load — nothing useful to do.
                }
            });
        }

        // Case-insensitive compare to whether the box currently shows the
        // placeholder. We compare like this because the Designer-set initial
        // text ("Search Files...") was historically inconsistent with the
        // SearchPlaceholder constant's casing ("Search files..."), so a strict
        // == comparison failed on first launch and broke all the placeholder
        // logic. The boolean check on searchBoxHasRealText is the source of
        // truth; this is just the visual check.
        private bool ShowingPlaceholder()
        {
            return !searchBoxHasRealText && IsPlaceholder(searchBox.Text);
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
                searchBox.Text = CurrentPlaceholder;
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
            if (!searchBox.Focused && IsPlaceholder(searchBox.Text))
                return;

            searchBoxHasRealText = !string.IsNullOrEmpty(searchBox.Text)
                                   && !IsPlaceholder(searchBox.Text);

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
            // String search mode: route to the string index instead of the
            // file index. If the user switched modes mid-typing, the
            // displayed placeholder is "Search strings..." — strip it the
            // same way the file path does to detect "no real query".
            if (IsStringSearchMode())
            {
                if (!StringIndex.IsLoaded)
                {
                    // Shouldn't happen — the toggle handler gates this — but
                    // be defensive in case the load failed silently.
                    searchStatusLabel.Text = "String index not loaded.";
                    searchResults.Visible = false;
                    treeView1.Visible = !welcomeVisible;
                    return;
                }

                if (IsPlaceholder(query) || string.IsNullOrWhiteSpace(query))
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

                var stringHits = StringIndex.Search(query, 5000);

                searchResults.BeginUpdate();
                searchResults.Items.Clear();
                foreach (var hit in stringHits)
                {
                    // For string-search results FriendlyName holds the
                    // preview of the matched text; show that as the primary
                    // column with the hash as the secondary. The tooltip
                    // shows the full inner path so the user can see exactly
                    // which file inside the hash matched.
                    string primary = hit.FriendlyName ?? hit.FileName;
                    var item = new ListViewItem(primary);
                    item.SubItems.Add(hit.Archive);
                    item.ToolTipText = $"{hit.FileName}\n{hit.InnerPath}";
                    item.Tag = hit;
                    searchResults.Items.Add(item);
                }
                searchResults.EndUpdate();

                treeView1.Visible = false;
                searchResults.Visible = true;
                searchResults.ShowItemToolTips = true;

                searchStatusLabel.Text = stringHits.Count >= 5000
                    ? "Showing first 5000 matches"
                    : $"{stringHits.Count} match{(stringHits.Count == 1 ? "" : "es")}";
                return;
            }

            // ---- File search mode (original behavior) ----
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
            searchBox.Text = CurrentPlaceholder;
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