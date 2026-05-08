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
        /// </summary>
        private void UpdateViewInHexButtonState(object sender, EventArgs e)
        {
            // Cheap to call repeatedly Enabled is a property setter that
            // no-ops if the value isn't actually changing.
            viewInHexButton.Enabled = currentRight != null;
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