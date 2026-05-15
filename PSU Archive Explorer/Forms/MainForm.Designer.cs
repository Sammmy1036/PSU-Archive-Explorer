namespace psu_archive_explorer
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.fileDialog = new System.Windows.Forms.OpenFileDialog();
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.standardToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openNewSessionToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exportSelectedFileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exportAllToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.saveAsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.settingsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.extractAllInFolderToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.listAllObjparamsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.listAllMonsterLayoutsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.calculateAnimationNameHashToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exportBlobToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.createAFSToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.disableScriptParsingToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exportAllWeaponsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.importAllWeaponsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.insertNMLLFileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.decryptNMLBToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.decryptNMLLToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.textureCatalogueToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.catalogueEnemyparamToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.extractConvertToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.catalogueToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.helpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.debugSubMenuToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fileMenuSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.helpMenuSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.saveFileDialog1 = new System.Windows.Forms.SaveFileDialog();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.searchResults = new System.Windows.Forms.ListView();
            this.searchResultContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.copyHashToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.copyFilenameToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.copyPathToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.searchBox = new System.Windows.Forms.TextBox();
            this.searchStatusLabel = new System.Windows.Forms.Label();
            this.importDialog = new System.Windows.Forms.OpenFileDialog();
            this.setQuestButton = new System.Windows.Forms.Button();
            this.setZoneButton = new System.Windows.Forms.Button();
            this.addZoneButton = new System.Windows.Forms.Button();
            this.addFileButton = new System.Windows.Forms.Button();
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this.arbitraryFileContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.extractSelectedTreeContextItem = new System.Windows.Forms.ToolStripMenuItem();
            this.replaceFileTreeContextItem = new System.Windows.Forms.ToolStripMenuItem();
            this.renameFileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.deleteFileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.actionProgressBar = new System.Windows.Forms.ProgressBar();
            this.progressStatusLabel = new System.Windows.Forms.Label();
            this.viewInHexButton = new System.Windows.Forms.Button();
            this.zoneUD = new System.Windows.Forms.NumericUpDown();
            this.AFSQuestItemsLabel = new System.Windows.Forms.Label();
            this.zoneLabel = new System.Windows.Forms.Label();
            this.afsNblFileContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.nblChunkContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.compressChunkToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.addFileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.searchResultContextMenu.SuspendLayout();
            this.arbitraryFileContextMenuStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.zoneUD)).BeginInit();
            this.nblChunkContextMenuStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.BackColor = System.Drawing.SystemColors.MenuBar;
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.standardToolStripMenuItem,
            this.extractConvertToolStripMenuItem,
            this.catalogueToolStripMenuItem,
            this.toolsToolStripMenuItem,
            this.helpToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.RenderMode = System.Windows.Forms.ToolStripRenderMode.System;
            this.menuStrip1.Size = new System.Drawing.Size(858, 24);
            this.menuStrip1.TabIndex = 12;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // standardToolStripMenuItem
            // 
            this.standardToolStripMenuItem.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.standardToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openToolStripMenuItem,
            this.openNewSessionToolStripMenuItem,
            this.saveAsToolStripMenuItem,
            this.fileMenuSeparator1,
            this.exitToolStripMenuItem});
            this.standardToolStripMenuItem.ImageTransparentColor = System.Drawing.Color.White;
            this.standardToolStripMenuItem.Name = "standardToolStripMenuItem";
            this.standardToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
            this.standardToolStripMenuItem.Text = "File";
            // 
            // openToolStripMenuItem
            // 
            this.openToolStripMenuItem.Name = "openToolStripMenuItem";
            this.openToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O)));
            this.openToolStripMenuItem.Size = new System.Drawing.Size(163, 22);
            this.openToolStripMenuItem.Text = "Open";
            this.openToolStripMenuItem.Click += new System.EventHandler(this.openToolStripMenuItem_Click);
            // 
            // openNewSessionToolStripMenuItem
            // 
            this.openNewSessionToolStripMenuItem.Name = "openNewSessionToolStripMenuItem";
            this.openNewSessionToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.N)));
            this.openNewSessionToolStripMenuItem.Size = new System.Drawing.Size(163, 22);
            this.openNewSessionToolStripMenuItem.Text = "New Session";
            this.openNewSessionToolStripMenuItem.Click += new System.EventHandler(this.openNewSessionToolStripMenuItem_Click);
            // 
            // exportSelectedFileToolStripMenuItem
            // 
            this.exportSelectedFileToolStripMenuItem.Name = "exportSelectedFileToolStripMenuItem";
            this.exportSelectedFileToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.E)));
            this.exportSelectedFileToolStripMenuItem.Size = new System.Drawing.Size(163, 22);
            this.exportSelectedFileToolStripMenuItem.Text = "Export Selected";
            this.exportSelectedFileToolStripMenuItem.Click += new System.EventHandler(this.exportSelectedToolStripMenuItem_Click);
            // 
            // exportAllToolStripMenuItem
            // 
            this.exportAllToolStripMenuItem.Name = "exportAllToolStripMenuItem";
            this.exportAllToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.A)));
            this.exportAllToolStripMenuItem.Size = new System.Drawing.Size(163, 22);
            this.exportAllToolStripMenuItem.Text = "Export All";
            this.exportAllToolStripMenuItem.Click += new System.EventHandler(this.exportAllToolStripMenuItem_Click);
            // 
            // saveAsToolStripMenuItem
            // 
            this.saveAsToolStripMenuItem.Name = "saveAsToolStripMenuItem";
            this.saveAsToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S)));
            this.saveAsToolStripMenuItem.Size = new System.Drawing.Size(163, 22);
            this.saveAsToolStripMenuItem.Text = "Save As...";
            this.saveAsToolStripMenuItem.Click += new System.EventHandler(this.saveAsToolStripMenuItem_Click);
            // 
            // settingsToolStripMenuItem
            // 
            this.settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            this.settingsToolStripMenuItem.Size = new System.Drawing.Size(163, 22);
            this.settingsToolStripMenuItem.Text = "Settings";
            this.settingsToolStripMenuItem.Click += new System.EventHandler(this.settingsToolStripMenuItem_Click);
            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(163, 22);
            this.exitToolStripMenuItem.Text = "Exit";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.exitToolStripMenuItem_Click);
            // 
            // extractAllInFolderToolStripMenuItem
            // 
            this.extractAllInFolderToolStripMenuItem.Name = "extractAllInFolderToolStripMenuItem";
            this.extractAllInFolderToolStripMenuItem.Size = new System.Drawing.Size(195, 22);
            this.extractAllInFolderToolStripMenuItem.Text = "Export All In Folder";
            this.extractAllInFolderToolStripMenuItem.Click += new System.EventHandler(this.extractAllInFolderToolStripMenuItem_Click);
            // 
            // listAllObjparamsToolStripMenuItem
            // 
            this.listAllObjparamsToolStripMenuItem.Name = "listAllObjparamsToolStripMenuItem";
            this.listAllObjparamsToolStripMenuItem.Size = new System.Drawing.Size(195, 22);
            this.listAllObjparamsToolStripMenuItem.Text = "List All Objparams";
            this.listAllObjparamsToolStripMenuItem.Click += new System.EventHandler(this.listAllObjparamsToolStripMenuItem_Click);
            // 
            // listAllMonsterLayoutsToolStripMenuItem
            // 
            this.listAllMonsterLayoutsToolStripMenuItem.Name = "listAllMonsterLayoutsToolStripMenuItem";
            this.listAllMonsterLayoutsToolStripMenuItem.Size = new System.Drawing.Size(195, 22);
            this.listAllMonsterLayoutsToolStripMenuItem.Text = "List All Monster Layouts";
            this.listAllMonsterLayoutsToolStripMenuItem.Click += new System.EventHandler(this.listAllMonsterLayoutsToolStripMenuItem_Click);
            // 
            // toolsToolStripMenuItem
            // 
            this.toolsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.createAFSToolStripMenuItem,
            this.insertNMLLFileToolStripMenuItem,
            this.decryptNMLBToolStripMenuItem,
            this.decryptNMLLToolStripMenuItem,
            this.calculateAnimationNameHashToolStripMenuItem});
            this.toolsToolStripMenuItem.Name = "toolsToolStripMenuItem";
            this.toolsToolStripMenuItem.Size = new System.Drawing.Size(47, 20);
            this.toolsToolStripMenuItem.Text = "Tools";
            // 
            // calculateAnimationNameHashToolStripMenuItem
            // 
            this.calculateAnimationNameHashToolStripMenuItem.Name = "calculateAnimationNameHashToolStripMenuItem";
            this.calculateAnimationNameHashToolStripMenuItem.Size = new System.Drawing.Size(247, 22);
            this.calculateAnimationNameHashToolStripMenuItem.Text = "Calculate Animation Name Hash";
            this.calculateAnimationNameHashToolStripMenuItem.Click += new System.EventHandler(this.calculateAnimationNameHashToolStripMenuItem_Click);
            // 
            // extractConvertToolStripMenuItem
            // 
            this.extractConvertToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.exportSelectedFileToolStripMenuItem,
            this.exportAllToolStripMenuItem,
            this.extractAllInFolderToolStripMenuItem,
            this.exportBlobToolStripMenuItem,
            this.exportAllWeaponsToolStripMenuItem,
            this.importAllWeaponsToolStripMenuItem});
            this.extractConvertToolStripMenuItem.Name = "extractConvertToolStripMenuItem";
            this.extractConvertToolStripMenuItem.Size = new System.Drawing.Size(116, 20);
            this.extractConvertToolStripMenuItem.Text = "Export";
            // 
            // catalogueToolStripMenuItem
            // 
            this.catalogueToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.textureCatalogueToolStripMenuItem,
            this.catalogueEnemyparamToolStripMenuItem,
            this.listAllObjparamsToolStripMenuItem,
            this.listAllMonsterLayoutsToolStripMenuItem});
            this.catalogueToolStripMenuItem.Name = "catalogueToolStripMenuItem";
            this.catalogueToolStripMenuItem.Size = new System.Drawing.Size(75, 20);
            this.catalogueToolStripMenuItem.Text = "Catalogue";
            // 
            // fileMenuSeparator1
            // 
            this.fileMenuSeparator1.Name = "fileMenuSeparator1";
            this.fileMenuSeparator1.Size = new System.Drawing.Size(160, 6);
            // 
            // helpToolStripMenuItem
            // 
            this.helpToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.debugSubMenuToolStripMenuItem,
            this.helpMenuSeparator1,
            this.settingsToolStripMenuItem});
            this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            this.helpToolStripMenuItem.Size = new System.Drawing.Size(44, 20);
            this.helpToolStripMenuItem.Text = "Help";
            // 
            // debugSubMenuToolStripMenuItem
            // 
            this.debugSubMenuToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.disableScriptParsingToolStripMenuItem});
            this.debugSubMenuToolStripMenuItem.Name = "debugSubMenuToolStripMenuItem";
            this.debugSubMenuToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.debugSubMenuToolStripMenuItem.Text = "Debug";
            // 
            // helpMenuSeparator1
            // 
            this.helpMenuSeparator1.Name = "helpMenuSeparator1";
            this.helpMenuSeparator1.Size = new System.Drawing.Size(177, 6);
            // 
            // exportBlobToolStripMenuItem
            // 
            this.exportBlobToolStripMenuItem.Name = "exportBlobToolStripMenuItem";
            this.exportBlobToolStripMenuItem.Size = new System.Drawing.Size(201, 22);
            this.exportBlobToolStripMenuItem.Text = "Export Blob";
            this.exportBlobToolStripMenuItem.Click += new System.EventHandler(this.exportBlob_Click);
            // 
            // createAFSToolStripMenuItem
            // 
            this.createAFSToolStripMenuItem.Name = "createAFSToolStripMenuItem";
            this.createAFSToolStripMenuItem.Size = new System.Drawing.Size(201, 22);
            this.createAFSToolStripMenuItem.Text = "Create AFS";
            this.createAFSToolStripMenuItem.Click += new System.EventHandler(this.createAFSToolStripMenuItem_Click);
            // 
            // disableScriptParsingToolStripMenuItem
            // 
            this.disableScriptParsingToolStripMenuItem.Name = "disableScriptParsingToolStripMenuItem";
            this.disableScriptParsingToolStripMenuItem.Size = new System.Drawing.Size(201, 22);
            this.disableScriptParsingToolStripMenuItem.Text = "Disable Script Parsing";
            this.disableScriptParsingToolStripMenuItem.Click += new System.EventHandler(this.disableScriptParsingToolStripMenuItem_Click);
            // 
            // exportAllWeaponsToolStripMenuItem
            // 
            this.exportAllWeaponsToolStripMenuItem.Name = "exportAllWeaponsToolStripMenuItem";
            this.exportAllWeaponsToolStripMenuItem.Size = new System.Drawing.Size(201, 22);
            this.exportAllWeaponsToolStripMenuItem.Text = "Export All Weapons";
            this.exportAllWeaponsToolStripMenuItem.Click += new System.EventHandler(this.exportAllWeaponsToolStripMenuItem_Click);
            // 
            // importAllWeaponsToolStripMenuItem
            // 
            this.importAllWeaponsToolStripMenuItem.Name = "importAllWeaponsToolStripMenuItem";
            this.importAllWeaponsToolStripMenuItem.Size = new System.Drawing.Size(201, 22);
            this.importAllWeaponsToolStripMenuItem.Text = "Import All Weapons";
            this.importAllWeaponsToolStripMenuItem.Click += new System.EventHandler(this.importAllWeaponsToolStripMenuItem_Click);
            // 
            // insertNMLLFileToolStripMenuItem
            // 
            this.insertNMLLFileToolStripMenuItem.Name = "insertNMLLFileToolStripMenuItem";
            this.insertNMLLFileToolStripMenuItem.Size = new System.Drawing.Size(201, 22);
            this.insertNMLLFileToolStripMenuItem.Text = "Insert NMLL File";
            this.insertNMLLFileToolStripMenuItem.Click += new System.EventHandler(this.insertNMLLFileToolStripMenuItem_Click);
            // 
            // decryptNMLBToolStripMenuItem
            // 
            this.decryptNMLBToolStripMenuItem.Name = "decryptNMLBToolStripMenuItem";
            this.decryptNMLBToolStripMenuItem.Size = new System.Drawing.Size(201, 22);
            this.decryptNMLBToolStripMenuItem.Text = "Decrypt NMLB";
            this.decryptNMLBToolStripMenuItem.Click += new System.EventHandler(this.decryptNMLBToolStripMenuItem_Click);
            // 
            // decryptNMLLToolStripMenuItem
            // 
            this.decryptNMLLToolStripMenuItem.Name = "decryptNMLLToolStripMenuItem";
            this.decryptNMLLToolStripMenuItem.Size = new System.Drawing.Size(201, 22);
            this.decryptNMLLToolStripMenuItem.Text = "Decrypt NMLL";
            this.decryptNMLLToolStripMenuItem.Click += new System.EventHandler(this.decryptNMLLToolStripMenuItem_Click);
            // 
            // textureCatalogueToolStripMenuItem
            // 
            this.textureCatalogueToolStripMenuItem.Name = "textureCatalogueToolStripMenuItem";
            this.textureCatalogueToolStripMenuItem.Size = new System.Drawing.Size(201, 22);
            this.textureCatalogueToolStripMenuItem.Text = "Texture Catalogue";
            this.textureCatalogueToolStripMenuItem.Click += new System.EventHandler(this.textureCatalogueToolStripMenuItem_Click);
            // 
            // catalogueEnemyparamToolStripMenuItem
            // 
            this.catalogueEnemyparamToolStripMenuItem.Name = "catalogueEnemyparamToolStripMenuItem";
            this.catalogueEnemyparamToolStripMenuItem.Size = new System.Drawing.Size(201, 22);
            this.catalogueEnemyparamToolStripMenuItem.Text = "Catalogue Enemyparam";
            this.catalogueEnemyparamToolStripMenuItem.Click += new System.EventHandler(this.catalogueEnemyparamToolStripMenuItem_Click);
            // 
            // splitContainer1
            // 
            this.splitContainer1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.splitContainer1.Location = new System.Drawing.Point(12, 78);
            this.splitContainer1.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.treeView1);
            this.splitContainer1.Panel1.Controls.Add(this.searchResults);
            this.splitContainer1.Panel1.Controls.Add(this.searchBox);
            this.splitContainer1.Panel1.Controls.Add(this.searchStatusLabel);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.BackColor = System.Drawing.SystemColors.Control;
            this.splitContainer1.Panel2.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.splitContainer1.Panel2.Paint += new System.Windows.Forms.PaintEventHandler(this.splitContainer1_Panel2_Paint);
            this.splitContainer1.Size = new System.Drawing.Size(833, 458);
            this.splitContainer1.SplitterDistance = 278;
            this.splitContainer1.TabIndex = 14;
            // 
            // treeView1
            // 
            this.treeView1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.treeView1.BackColor = System.Drawing.SystemColors.Window;
            this.treeView1.HideSelection = false;
            this.treeView1.LabelEdit = true;
            this.treeView1.Location = new System.Drawing.Point(0, 26);
            this.treeView1.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(272, 432);
            this.treeView1.TabIndex = 0;
            this.treeView1.BeforeLabelEdit += new System.Windows.Forms.NodeLabelEditEventHandler(this.treeView1_BeforeLabelEdit);
            this.treeView1.AfterLabelEdit += new System.Windows.Forms.NodeLabelEditEventHandler(this.treeView1_AfterLabelEdit);
            this.treeView1.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterSelect);
            this.treeView1.NodeMouseClick += new System.Windows.Forms.TreeNodeMouseClickEventHandler(this.treeView1_NodeMouseClick);
            // 
            // searchResults
            // 
            this.searchResults.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.searchResults.ContextMenuStrip = this.searchResultContextMenu;
            this.searchResults.FullRowSelect = true;
            this.searchResults.HideSelection = false;
            this.searchResults.Location = new System.Drawing.Point(0, 26);
            this.searchResults.Name = "searchResults";
            this.searchResults.ShowItemToolTips = true;
            this.searchResults.Size = new System.Drawing.Size(272, 414);
            this.searchResults.TabIndex = 32;
            this.searchResults.UseCompatibleStateImageBehavior = false;
            this.searchResults.View = System.Windows.Forms.View.Details;
            this.searchResults.Visible = false;
            this.searchResults.DoubleClick += new System.EventHandler(this.searchResults_DoubleClick);
            // 
            // searchResultContextMenu
            // 
            this.searchResultContextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.copyHashToolStripMenuItem,
            this.copyFilenameToolStripMenuItem,
            this.copyPathToolStripMenuItem});
            this.searchResultContextMenu.Name = "searchResultContextMenu";
            this.searchResultContextMenu.Size = new System.Drawing.Size(180, 70);
            // 
            // copyHashToolStripMenuItem
            // 
            this.copyHashToolStripMenuItem.Name = "copyHashToolStripMenuItem";
            this.copyHashToolStripMenuItem.Size = new System.Drawing.Size(179, 22);
            this.copyHashToolStripMenuItem.Text = "Copy Hash Filename";
            this.copyHashToolStripMenuItem.Click += new System.EventHandler(this.copyHashToolStripMenuItem_Click);
            // 
            // copyFilenameToolStripMenuItem
            // 
            this.copyFilenameToolStripMenuItem.Name = "copyFilenameToolStripMenuItem";
            this.copyFilenameToolStripMenuItem.Size = new System.Drawing.Size(179, 22);
            this.copyFilenameToolStripMenuItem.Text = "Copy Filename";
            this.copyFilenameToolStripMenuItem.Click += new System.EventHandler(this.copyFilenameToolStripMenuItem_Click);
            // 
            // copyPathToolStripMenuItem
            // 
            this.copyPathToolStripMenuItem.Name = "copyPathToolStripMenuItem";
            this.copyPathToolStripMenuItem.Size = new System.Drawing.Size(179, 22);
            this.copyPathToolStripMenuItem.Text = "Copy Inner Path";
            this.copyPathToolStripMenuItem.Click += new System.EventHandler(this.copyPathToolStripMenuItem_Click);
            // 
            // searchBox
            // 
            this.searchBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.searchBox.BackColor = System.Drawing.SystemColors.Window;
            this.searchBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.searchBox.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.searchBox.ForeColor = System.Drawing.Color.Gray;
            this.searchBox.Location = new System.Drawing.Point(0, 0);
            this.searchBox.Name = "searchBox";
            this.searchBox.Size = new System.Drawing.Size(272, 23);
            this.searchBox.TabIndex = 30;
            this.searchBox.Text = "Search Files...";
            this.searchBox.TextChanged += new System.EventHandler(this.searchBox_TextChanged);
            this.searchBox.Enter += new System.EventHandler(this.searchBox_Enter);
            this.searchBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.searchBox_KeyDown);
            this.searchBox.Leave += new System.EventHandler(this.searchBox_Leave);
            // 
            // searchStatusLabel
            // 
            this.searchStatusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.searchStatusLabel.Location = new System.Drawing.Point(0, 443);
            this.searchStatusLabel.Name = "searchStatusLabel";
            this.searchStatusLabel.Size = new System.Drawing.Size(272, 15);
            this.searchStatusLabel.TabIndex = 31;
            // 
            // importDialog
            // 
            this.importDialog.FileName = "openFileDialog2";
            // 
            // setQuestButton
            // 
            this.setQuestButton.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.setQuestButton.Location = new System.Drawing.Point(93, 25);
            this.setQuestButton.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.setQuestButton.Name = "setQuestButton";
            this.setQuestButton.Size = new System.Drawing.Size(75, 22);
            this.setQuestButton.TabIndex = 16;
            this.setQuestButton.Text = "Set Quest";
            this.setQuestButton.UseVisualStyleBackColor = true;
            this.setQuestButton.Click += new System.EventHandler(this.setQuest_Click);
            // 
            // setZoneButton
            // 
            this.setZoneButton.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.setZoneButton.Location = new System.Drawing.Point(93, 48);
            this.setZoneButton.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.setZoneButton.Name = "setZoneButton";
            this.setZoneButton.Size = new System.Drawing.Size(75, 22);
            this.setZoneButton.TabIndex = 17;
            this.setZoneButton.Text = "Set Zone";
            this.setZoneButton.UseVisualStyleBackColor = true;
            this.setZoneButton.Click += new System.EventHandler(this.setZone_Click_1);
            // 
            // addZoneButton
            // 
            this.addZoneButton.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.addZoneButton.Location = new System.Drawing.Point(174, 48);
            this.addZoneButton.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.addZoneButton.Name = "addZoneButton";
            this.addZoneButton.Size = new System.Drawing.Size(75, 22);
            this.addZoneButton.TabIndex = 18;
            this.addZoneButton.Text = "Add Zone";
            this.addZoneButton.UseVisualStyleBackColor = true;
            this.addZoneButton.Click += new System.EventHandler(this.addZone_Click_1);
            // 
            // addFileButton
            // 
            this.addFileButton.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.addFileButton.Location = new System.Drawing.Point(174, 25);
            this.addFileButton.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.addFileButton.Name = "addFileButton";
            this.addFileButton.Size = new System.Drawing.Size(75, 22);
            this.addFileButton.TabIndex = 21;
            this.addFileButton.Text = "Add File";
            this.addFileButton.UseVisualStyleBackColor = true;
            this.addFileButton.Click += new System.EventHandler(this.addFile_Click);
            // 
            // arbitraryFileContextMenuStrip
            // 
            this.arbitraryFileContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.extractSelectedTreeContextItem,
            this.replaceFileTreeContextItem,
            this.renameFileToolStripMenuItem,
            this.deleteFileToolStripMenuItem});
            this.arbitraryFileContextMenuStrip.Name = "treeViewContextMenu";
            this.arbitraryFileContextMenuStrip.ShowImageMargin = false;
            this.arbitraryFileContextMenuStrip.Size = new System.Drawing.Size(114, 92);
            // 
            // extractSelectedTreeContextItem
            // 
            this.extractSelectedTreeContextItem.Name = "extractSelectedTreeContextItem";
            this.extractSelectedTreeContextItem.Size = new System.Drawing.Size(113, 22);
            this.extractSelectedTreeContextItem.Text = "Extract File";
            this.extractSelectedTreeContextItem.Click += new System.EventHandler(this.extractFileTreeContextItem_Click);
            // 
            // replaceFileTreeContextItem
            // 
            this.replaceFileTreeContextItem.Name = "replaceFileTreeContextItem";
            this.replaceFileTreeContextItem.Size = new System.Drawing.Size(113, 22);
            this.replaceFileTreeContextItem.Text = "Replace File";
            this.replaceFileTreeContextItem.Click += new System.EventHandler(this.replaceFileTreeContextItem_Click);
            // 
            // renameFileToolStripMenuItem
            // 
            this.renameFileToolStripMenuItem.Name = "renameFileToolStripMenuItem";
            this.renameFileToolStripMenuItem.Size = new System.Drawing.Size(113, 22);
            this.renameFileToolStripMenuItem.Text = "Rename File";
            this.renameFileToolStripMenuItem.Click += new System.EventHandler(this.renameFileToolStripMenuItem_Click);
            // 
            // deleteFileToolStripMenuItem
            // 
            this.deleteFileToolStripMenuItem.Name = "deleteFileToolStripMenuItem";
            this.deleteFileToolStripMenuItem.Size = new System.Drawing.Size(113, 22);
            this.deleteFileToolStripMenuItem.Text = "Delete File";
            this.deleteFileToolStripMenuItem.Click += new System.EventHandler(this.deleteFileToolStripMenuItem_Click);
            // 
            // actionProgressBar
            // 
            this.actionProgressBar.ForeColor = System.Drawing.SystemColors.ButtonShadow;
            this.actionProgressBar.Location = new System.Drawing.Point(303, 57);
            this.actionProgressBar.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.actionProgressBar.Name = "actionProgressBar";
            this.actionProgressBar.Size = new System.Drawing.Size(289, 17);
            this.actionProgressBar.TabIndex = 22;
            // 
            // progressStatusLabel
            // 
            this.progressStatusLabel.AutoSize = true;
            this.progressStatusLabel.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.progressStatusLabel.Location = new System.Drawing.Point(300, 34);
            this.progressStatusLabel.Name = "progressStatusLabel";
            this.progressStatusLabel.Size = new System.Drawing.Size(54, 13);
            this.progressStatusLabel.TabIndex = 23;
            this.progressStatusLabel.Text = "Progress: ";
            // 
            // viewInHexButton
            // 
            // Anchored Top|Right so the button stays pinned to the right edge
            // of the form as the window is resized. Without this it defaults
            // to Top|Left, which locks it to a fixed X coordinate (718, the
            // designer position) and leaves a growing gap on the right when
            // the form is wider than the designer's 858 px ClientSize.
            this.viewInHexButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.viewInHexButton.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.viewInHexButton.Location = new System.Drawing.Point(718, 52);
            this.viewInHexButton.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.viewInHexButton.Name = "viewInHexButton";
            this.viewInHexButton.Size = new System.Drawing.Size(127, 22);
            this.viewInHexButton.TabIndex = 24;
            this.viewInHexButton.Text = "View Current File in Hex";
            this.viewInHexButton.UseVisualStyleBackColor = true;
            this.viewInHexButton.Click += new System.EventHandler(this.viewInHexButton_Click);
            // 
            // zoneUD
            // 
            this.zoneUD.Location = new System.Drawing.Point(50, 50);
            this.zoneUD.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.zoneUD.Name = "zoneUD";
            this.zoneUD.Size = new System.Drawing.Size(37, 20);
            this.zoneUD.TabIndex = 19;
            // 
            // AFSQuestItemsLabel
            // 
            this.AFSQuestItemsLabel.AutoSize = true;
            this.AFSQuestItemsLabel.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.AFSQuestItemsLabel.Location = new System.Drawing.Point(5, 30);
            this.AFSQuestItemsLabel.Name = "AFSQuestItemsLabel";
            this.AFSQuestItemsLabel.Size = new System.Drawing.Size(85, 13);
            this.AFSQuestItemsLabel.TabIndex = 25;
            this.AFSQuestItemsLabel.Text = "AFS/quest items";
            // 
            // zoneLabel
            // 
            this.zoneLabel.AutoSize = true;
            this.zoneLabel.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.zoneLabel.Location = new System.Drawing.Point(12, 52);
            this.zoneLabel.Name = "zoneLabel";
            this.zoneLabel.Size = new System.Drawing.Size(35, 13);
            this.zoneLabel.TabIndex = 26;
            this.zoneLabel.Text = "Zone:";
            // 
            // afsNblFileContextMenuStrip
            // 
            this.afsNblFileContextMenuStrip.Name = "afsNblFileContextMenuStrip";
            this.afsNblFileContextMenuStrip.Size = new System.Drawing.Size(61, 4);
            // 
            // nblChunkContextMenuStrip
            // 
            this.nblChunkContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.compressChunkToolStripMenuItem,
            this.addFileToolStripMenuItem});
            this.nblChunkContextMenuStrip.Name = "nblChunkContextMenuStrip";
            this.nblChunkContextMenuStrip.Size = new System.Drawing.Size(166, 48);
            this.nblChunkContextMenuStrip.Opening += new System.ComponentModel.CancelEventHandler(this.nblChunkContextMenuStrip_Opening);
            // 
            // compressChunkToolStripMenuItem
            // 
            this.compressChunkToolStripMenuItem.CheckOnClick = true;
            this.compressChunkToolStripMenuItem.Name = "compressChunkToolStripMenuItem";
            this.compressChunkToolStripMenuItem.Size = new System.Drawing.Size(165, 22);
            this.compressChunkToolStripMenuItem.Text = "Compress Chunk";
            this.compressChunkToolStripMenuItem.CheckedChanged += new System.EventHandler(this.compressChunkToolStripMenuItem_CheckedChanged);
            // 
            // addFileToolStripMenuItem
            // 
            this.addFileToolStripMenuItem.Name = "addFileToolStripMenuItem";
            this.addFileToolStripMenuItem.Size = new System.Drawing.Size(165, 22);
            this.addFileToolStripMenuItem.Text = "Add File";
            this.addFileToolStripMenuItem.Click += new System.EventHandler(this.addFileToolStripMenuItem_Click);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.ClientSize = new System.Drawing.Size(858, 548);
            this.Controls.Add(this.zoneLabel);
            this.Controls.Add(this.AFSQuestItemsLabel);
            this.Controls.Add(this.viewInHexButton);
            this.Controls.Add(this.progressStatusLabel);
            this.Controls.Add(this.actionProgressBar);
            this.Controls.Add(this.addFileButton);
            this.Controls.Add(this.zoneUD);
            this.Controls.Add(this.addZoneButton);
            this.Controls.Add(this.setZoneButton);
            this.Controls.Add(this.setQuestButton);
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.menuStrip1);
            this.DoubleBuffered = true;
            this.ForeColor = System.Drawing.SystemColors.ButtonFace;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MainMenuStrip = this.menuStrip1;
            this.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.Name = "MainForm";
            this.Text = "PSU Archive Explorer ";
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.searchResultContextMenu.ResumeLayout(false);
            this.arbitraryFileContextMenuStrip.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.zoneUD)).EndInit();
            this.nblChunkContextMenuStrip.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.OpenFileDialog fileDialog;
        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem standardToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem openToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem openNewSessionToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem saveAsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exportBlobToolStripMenuItem;
        private System.Windows.Forms.SaveFileDialog saveFileDialog1;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.OpenFileDialog importDialog;
        private System.Windows.Forms.Button setQuestButton;
        private System.Windows.Forms.Button setZoneButton;
        private System.Windows.Forms.Button addZoneButton;
        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.Button addFileButton;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
        private System.Windows.Forms.ToolStripMenuItem createAFSToolStripMenuItem;
        private System.Windows.Forms.ContextMenuStrip arbitraryFileContextMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem replaceFileTreeContextItem;
        private System.Windows.Forms.ToolStripMenuItem extractSelectedTreeContextItem;
        private System.Windows.Forms.ToolStripMenuItem disableScriptParsingToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exportAllWeaponsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem importAllWeaponsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem insertNMLLFileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem decryptNMLBToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem decryptNMLLToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem textureCatalogueToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exportSelectedFileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exportAllToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem settingsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem extractAllInFolderToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem extractSelectedFileToolStripMenuItem;
        private System.Windows.Forms.ProgressBar actionProgressBar;
        private System.Windows.Forms.Label progressStatusLabel;
        private System.Windows.Forms.Button viewInHexButton;
        private System.Windows.Forms.NumericUpDown zoneUD;
        private System.Windows.Forms.Label AFSQuestItemsLabel;
        private System.Windows.Forms.Label zoneLabel;
        private System.Windows.Forms.ContextMenuStrip afsNblFileContextMenuStrip;
        private System.Windows.Forms.ContextMenuStrip nblChunkContextMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem compressChunkToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem listAllObjparamsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem listAllMonsterLayoutsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem catalogueEnemyparamToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem toolsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem calculateAnimationNameHashToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem renameFileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem deleteFileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem addFileToolStripMenuItem;
        private System.Windows.Forms.TextBox searchBox;
        private System.Windows.Forms.ListView searchResults;
        private System.Windows.Forms.Label searchStatusLabel;
        private System.Windows.Forms.ContextMenuStrip searchResultContextMenu;
        private System.Windows.Forms.ToolStripMenuItem copyHashToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem copyFilenameToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem copyPathToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem extractConvertToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem catalogueToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem debugSubMenuToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator fileMenuSeparator1;
        private System.Windows.Forms.ToolStripSeparator helpMenuSeparator1;
    }
}