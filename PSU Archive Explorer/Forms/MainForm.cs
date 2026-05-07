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

            // Show welcome screen on first launch
            // It is torn down by HideWelcomeScreen() as soon as a file loads
            this.Shown += (s, e) => ShowWelcomeScreen();
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