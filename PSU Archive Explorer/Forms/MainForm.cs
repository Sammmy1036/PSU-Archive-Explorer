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
        ContainerFile loadedContainer;
        PsuFile currentRight;
        MainSettings settings;
        private HexEditForm currentFileHexForm;
        private byte[] pendingAdxReplacementBytes;
        public string gameDirectory
        {
            get { return Properties.Settings.Default.GameDirectory ?? ""; }
            set
            {
                Properties.Settings.Default.GameDirectory = value ?? "";
                Properties.Settings.Default.Save();
            }
        }
        public bool batchPngExport = true;
        public bool batchWavExport = true;
        public bool batchDat2WavExport = true;
        public bool batchRecursive = true;
        public bool batchExportSubArchiveFiles = false;
        public bool compressNMLL = false;
        public bool compressTMLL = false;
        public bool exportMetaData = true;

        private class FileTreeNodeTag
        {
            public ContainerFile OwnerContainer { get; set; }
            public string FileName { get; set; }
            public string FullPath { get; set; }
        }

        public MainForm()
        {
            InitializeComponent();
            treeView1.HideSelection = false;
            searchStatusLabel.ForeColor = System.Drawing.Color.FromArgb(64, 64, 64);

            Text += System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            setAFSEnabled(false);

            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            this.MinimumSize = new Size(900, 600);

            string indexPath = Path.Combine(
                Path.GetDirectoryName(Application.ExecutablePath),
                "psu_file_index.gz");
            FileIndex.LoadFromFile(indexPath);

            // Probe for the optional string index file. Only its existence is
            // checked here — the file itself can be hundreds of MB and is
            // lazy-loaded on first use via the search mode toggle. If the
            // file isn't present, the toggle stays hidden and nothing about
            // search behavior changes.
            EnableStringSearchToggleIfAvailable();

            // Wire up dead-space click handlers so the "Search files..." placeholder
            // is restored when the user clicks anywhere off the search box.
            WireSearchFocusDrop();

            // The hex viewer needs a selected file to work on. Start disabled
            // and let the Idle handler keep it in sync from then on. Using
            // Application.Idle is a standard WinForms pattern for command-state
            // updates: it fires whenever the message pump is idle, so the
            // button enabled state automatically tracks `currentRight` no
            // matter where in the code that field gets assigned.
            viewInHexButton.Enabled = false;
            Application.Idle += UpdateViewInHexButtonState;

            // Show the archive overview panel once an archive finishes loading
            // and no specific node is selected. Same Idle pattern, same reason:
            // we don't have to plumb a callback through every load path.
            Application.Idle += MaybeShowArchiveOverviewPanel;

            // Show welcome screen on first launch
            // It is torn down by HideWelcomeScreen() as soon as a file loads
            this.Shown += (s, e) => ShowWelcomeScreen();
        }

        /// <summary>
        /// Sync the View Current File in Hex button's enabled state with whether
        /// there's actually a file selected to view. Wired to Application.Idle
        /// in the constructor so it runs automatically; no need to call this
        /// from every site that touches <see cref="currentRight"/>.
        ///
        /// ADX audio and SFD video files are excluded — the hex editor isn't
        /// useful for these (they have their own dedicated preview panels),
        /// and offering it just invites confusion. Match the same filename
        /// check used by treeView1_AfterSelect so the button enables and the
        /// preview routing agree on what is what.
        /// </summary>
        private void UpdateViewInHexButtonState(object sender, EventArgs e)
        {
            // Cheap to call repeatedly Enabled is a property setter that
            // no-ops if the value isn't actually changing.
            viewInHexButton.Enabled = currentRight != null && !IsHexUnviewable(currentRight);
        }

        // Cache for the IsHexUnviewable content sniff. The Idle handler can
        // fire hundreds of times per second, and the content check calls
        // ToRaw() which can be non-trivial for large files. We only need to
        // re-evaluate when the selected file actually changes, so key the
        // cache on reference identity of currentRight.
        private PsuFile lastUnviewableCheckTarget;
        private bool lastUnviewableCheckResult;

        /// <summary>
        /// Returns true for file types we don't want to expose in the hex
        /// editor. Currently: ADX audio and SFD video. Uses two layers:
        ///
        ///   1) Filename match (.adx / .sfd) — fast, covers normal archive entries.
        ///   2) Content sniff against known magic headers — covers the "fake
        ///      archive" case where a hashed container holds a single ADX (or
        ///      SFD) file with no extension, just the hash for a name. The
        ///      filename check misses those; reading the first few bytes
        ///      catches them regardless of what they're named.
        ///
        /// Results are cached per reference identity of <paramref name="file"/>
        /// so the Idle handler stays cheap on repeated calls.
        /// </summary>
        private bool IsHexUnviewable(PsuFile file)
        {
            if (file == null) return false;

            // Reference-identity cache hit. currentRight gets reassigned each
            // time the user selects a different node, so a reference match
            // means "same file as last time we asked".
            if (ReferenceEquals(file, lastUnviewableCheckTarget))
                return lastUnviewableCheckResult;

            bool result = IsHexUnviewableCore(file);

            lastUnviewableCheckTarget = file;
            lastUnviewableCheckResult = result;
            return result;
        }

        /// <summary>
        /// The actual decision logic for <see cref="IsHexUnviewable"/>.
        /// Separated so the caching wrapper stays tiny and the rules stay
        /// in one obvious place if we ever need to add more file types.
        /// </summary>
        private static bool IsHexUnviewableCore(PsuFile file)
        {
            // Filename based check of files inside an archive
            string name = file.filename;
            if (!string.IsNullOrEmpty(name))
            {
                if (name.EndsWith(".adx", StringComparison.OrdinalIgnoreCase)) return true;
                if (name.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase)) return true;
            }

            // Checks second for fake container that holds a single adx file
            byte[] bytes;
            try
            {
                bytes = file.ToRaw();
            }
            catch
            {
                return false; // can't sniff it falls back to "viewable"
            }
            if (bytes == null || bytes.Length < 4) return false;

            // ADX magic: 0x80 0x00 at the start. This is the
            // CRI ADX header marker used everywhere in the codebase's
            // existing ADX validation paths.
            if (bytes[0] == 0x80 && bytes[1] == 0x00) return true;

            // SFD magic: MPEG program stream pack header 0x00 0x00 0x01 0xBA.
            // SFD is Sofdec, which is a CRI MPEG variant and the same pack header.
            if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0xBA)
                return true;

            return false;
        }

        /// <summary>
        /// Clears all controls from the right panel, disposing them first so any
        /// IDisposable resources are released
        /// Always use this instead of calling splitContainer1.Panel2.Controls.Clear() directly
        /// </summary>
        private void ClearRightPanel()
        {
            // If the welcome screen is up, tear it down
            if (welcomeVisible) HideWelcomeScreen();

            var toDispose = splitContainer1.Panel2.Controls.Cast<Control>().ToList();
            splitContainer1.Panel2.Controls.Clear();
            foreach (var c in toDispose)
            {
                c.Dispose();
            }
        }
    }
}