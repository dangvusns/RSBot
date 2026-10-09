namespace RSBot.PacketAnalyzer.Views
{
    partial class Main
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
            components = new System.ComponentModel.Container();
            timerRefresh = new System.Windows.Forms.Timer(components);
            panelTop = new SDUI.Controls.Panel();
            flowTop = new System.Windows.Forms.FlowLayoutPanel();
            checkCapture = new SDUI.Controls.CheckBox();
            btnRecord = new SDUI.Controls.Button();
            checkAutoScroll = new SDUI.Controls.CheckBox();
            btnClear = new SDUI.Controls.Button();
            lblSearch = new SDUI.Controls.Label();
            txtSearch = new System.Windows.Forms.TextBox();
            btnOpenFolder = new SDUI.Controls.Button();
            lblStatus = new SDUI.Controls.Label();
            panelMarker = new SDUI.Controls.Panel();
            flowMarker = new System.Windows.Forms.FlowLayoutPanel();
            lblMarker = new SDUI.Controls.Label();
            txtMarker = new System.Windows.Forms.TextBox();
            btnAddMarker = new SDUI.Controls.Button();
            tabSide = new SDUI.Controls.TabControl();
            tabFilters = new System.Windows.Forms.TabPage();
            flowFilters = new System.Windows.Forms.FlowLayoutPanel();
            lblFilterScope = new SDUI.Controls.Label();
            comboFilterScope = new SDUI.Controls.ComboBox();
            radioExclude = new SDUI.Controls.Radio();
            radioInclude = new SDUI.Controls.Radio();
            checkShowClient = new SDUI.Controls.CheckBox();
            checkShowServer = new SDUI.Controls.CheckBox();
            checkShowBot = new SDUI.Controls.CheckBox();
            flowOpcodeRow = new System.Windows.Forms.FlowLayoutPanel();
            txtOpcode = new System.Windows.Forms.TextBox();
            btnAddOpcode = new SDUI.Controls.Button();
            btnRemoveOpcode = new SDUI.Controls.Button();
            flowPresetRow = new System.Windows.Forms.FlowLayoutPanel();
            btnNoisy = new SDUI.Controls.Button();
            btnClearOpcodes = new SDUI.Controls.Button();
            listOpcodes = new System.Windows.Forms.ListBox();
            tabInject = new System.Windows.Forms.TabPage();
            flowInject = new System.Windows.Forms.FlowLayoutPanel();
            lblInjectOpcode = new SDUI.Controls.Label();
            txtInjectOpcode = new System.Windows.Forms.TextBox();
            lblInjectData = new SDUI.Controls.Label();
            txtInjectData = new System.Windows.Forms.TextBox();
            lblInjectTo = new SDUI.Controls.Label();
            comboInjectTo = new SDUI.Controls.ComboBox();
            checkInjectEncrypted = new SDUI.Controls.CheckBox();
            checkInjectMassive = new SDUI.Controls.CheckBox();
            btnInject = new SDUI.Controls.Button();
            lblInjectHint = new SDUI.Controls.Label();
            tabRecording = new System.Windows.Forms.TabPage();
            flowRecording = new System.Windows.Forms.FlowLayoutPanel();
            lblMaxFile = new SDUI.Controls.Label();
            numMaxFile = new System.Windows.Forms.NumericUpDown();
            lblMaxFolder = new SDUI.Controls.Label();
            numMaxFolder = new System.Windows.Forms.NumericUpDown();
            lblKeepDays = new SDUI.Controls.Label();
            numKeepDays = new System.Windows.Forms.NumericUpDown();
            checkRecordOnLaunch = new SDUI.Controls.CheckBox();
            checkRecordOnBotStart = new SDUI.Controls.CheckBox();
            checkIncludeHex = new SDUI.Controls.CheckBox();
            checkIncludeLog = new SDUI.Controls.CheckBox();
            btnReloadNames = new SDUI.Controls.Button();
            btnEditNames = new SDUI.Controls.Button();
            lblRecordFile = new SDUI.Controls.Label();
            splitMain = new System.Windows.Forms.SplitContainer();
            listPackets = new PacketListView();
            columnTime = new System.Windows.Forms.ColumnHeader();
            columnContext = new System.Windows.Forms.ColumnHeader();
            columnDirection = new System.Windows.Forms.ColumnHeader();
            columnOrigin = new System.Windows.Forms.ColumnHeader();
            columnOpcode = new System.Windows.Forms.ColumnHeader();
            columnName = new System.Windows.Forms.ColumnHeader();
            columnLength = new System.Windows.Forms.ColumnHeader();
            columnFlags = new System.Windows.Forms.ColumnHeader();
            contextPackets = new System.Windows.Forms.ContextMenuStrip(components);
            menuCopyLine = new System.Windows.Forms.ToolStripMenuItem();
            menuCopyHex = new System.Windows.Forms.ToolStripMenuItem();
            menuCopyToInjector = new System.Windows.Forms.ToolStripMenuItem();
            menuHideOpcode = new System.Windows.Forms.ToolStripMenuItem();
            txtDetail = new System.Windows.Forms.RichTextBox();
            panelTop.SuspendLayout();
            flowTop.SuspendLayout();
            panelMarker.SuspendLayout();
            flowMarker.SuspendLayout();
            tabSide.SuspendLayout();
            tabFilters.SuspendLayout();
            flowFilters.SuspendLayout();
            flowOpcodeRow.SuspendLayout();
            flowPresetRow.SuspendLayout();
            tabInject.SuspendLayout();
            flowInject.SuspendLayout();
            tabRecording.SuspendLayout();
            flowRecording.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numMaxFile).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numMaxFolder).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numKeepDays).BeginInit();
            ((System.ComponentModel.ISupportInitialize)splitMain).BeginInit();
            splitMain.Panel1.SuspendLayout();
            splitMain.Panel2.SuspendLayout();
            splitMain.SuspendLayout();
            contextPackets.SuspendLayout();
            SuspendLayout();
            //
            // timerRefresh
            //
            timerRefresh.Interval = 200;
            timerRefresh.Tick += timerRefresh_Tick;
            //
            // panelTop
            //
            panelTop.BackColor = System.Drawing.Color.Transparent;
            panelTop.Border = new System.Windows.Forms.Padding(0, 0, 0, 1);
            panelTop.BorderColor = System.Drawing.Color.Transparent;
            panelTop.Controls.Add(flowTop);
            panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            panelTop.Location = new System.Drawing.Point(0, 0);
            panelTop.Name = "panelTop";
            panelTop.Radius = 0;
            panelTop.ShadowDepth = 0F;
            panelTop.Size = new System.Drawing.Size(1000, 40);
            panelTop.TabIndex = 0;
            //
            // flowTop
            //
            flowTop.BackColor = System.Drawing.Color.Transparent;
            flowTop.Controls.Add(checkCapture);
            flowTop.Controls.Add(btnRecord);
            flowTop.Controls.Add(checkAutoScroll);
            flowTop.Controls.Add(btnClear);
            flowTop.Controls.Add(lblSearch);
            flowTop.Controls.Add(txtSearch);
            flowTop.Controls.Add(btnOpenFolder);
            flowTop.Controls.Add(lblStatus);
            flowTop.Dock = System.Windows.Forms.DockStyle.Fill;
            flowTop.Name = "flowTop";
            flowTop.Padding = new System.Windows.Forms.Padding(4, 4, 4, 0);
            flowTop.WrapContents = false;
            flowTop.TabIndex = 0;
            //
            // checkCapture
            //
            checkCapture.AutoSize = true;
            checkCapture.Checked = true;
            checkCapture.CheckState = System.Windows.Forms.CheckState.Checked;
            checkCapture.Depth = 0;
            checkCapture.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            checkCapture.MouseLocation = new System.Drawing.Point(-1, -1);
            checkCapture.Name = "checkCapture";
            checkCapture.Ripple = false;
            checkCapture.Size = new System.Drawing.Size(110, 30);
            checkCapture.TabIndex = 0;
            checkCapture.Text = "Live capture";
            checkCapture.UseVisualStyleBackColor = false;
            checkCapture.CheckedChanged += checkCapture_CheckedChanged;
            //
            // btnRecord
            //
            btnRecord.Color = System.Drawing.Color.Transparent;
            btnRecord.Margin = new System.Windows.Forms.Padding(0, 3, 6, 0);
            btnRecord.Name = "btnRecord";
            btnRecord.Radius = 6;
            btnRecord.ShadowDepth = 0F;
            btnRecord.Size = new System.Drawing.Size(120, 25);
            btnRecord.TabIndex = 1;
            btnRecord.Text = "Start recording";
            btnRecord.UseVisualStyleBackColor = true;
            btnRecord.Click += btnRecord_Click;
            //
            // checkAutoScroll
            //
            checkAutoScroll.AutoSize = true;
            checkAutoScroll.Checked = true;
            checkAutoScroll.CheckState = System.Windows.Forms.CheckState.Checked;
            checkAutoScroll.Depth = 0;
            checkAutoScroll.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            checkAutoScroll.MouseLocation = new System.Drawing.Point(-1, -1);
            checkAutoScroll.Name = "checkAutoScroll";
            checkAutoScroll.Ripple = false;
            checkAutoScroll.Size = new System.Drawing.Size(100, 30);
            checkAutoScroll.TabIndex = 2;
            checkAutoScroll.Text = "Auto scroll";
            checkAutoScroll.UseVisualStyleBackColor = false;
            //
            // btnClear
            //
            btnClear.Color = System.Drawing.Color.Transparent;
            btnClear.Margin = new System.Windows.Forms.Padding(0, 3, 12, 0);
            btnClear.Name = "btnClear";
            btnClear.Radius = 6;
            btnClear.ShadowDepth = 0F;
            btnClear.Size = new System.Drawing.Size(70, 25);
            btnClear.TabIndex = 3;
            btnClear.Text = "Clear";
            btnClear.UseVisualStyleBackColor = true;
            btnClear.Click += btnClear_Click;
            //
            // lblSearch
            //
            lblSearch.ApplyGradient = false;
            lblSearch.AutoSize = true;
            lblSearch.Margin = new System.Windows.Forms.Padding(0, 8, 4, 0);
            lblSearch.Name = "lblSearch";
            lblSearch.TabIndex = 4;
            lblSearch.Text = "Search:";
            //
            // txtSearch
            //
            txtSearch.Margin = new System.Windows.Forms.Padding(0, 4, 12, 0);
            txtSearch.Name = "txtSearch";
            txtSearch.PlaceholderText = "opcode or name";
            txtSearch.Size = new System.Drawing.Size(150, 23);
            txtSearch.TabIndex = 5;
            txtSearch.TextChanged += txtSearch_TextChanged;
            //
            // btnOpenFolder
            //
            btnOpenFolder.Color = System.Drawing.Color.Transparent;
            btnOpenFolder.Margin = new System.Windows.Forms.Padding(0, 3, 12, 0);
            btnOpenFolder.Name = "btnOpenFolder";
            btnOpenFolder.Radius = 6;
            btnOpenFolder.ShadowDepth = 0F;
            btnOpenFolder.Size = new System.Drawing.Size(100, 25);
            btnOpenFolder.TabIndex = 6;
            btnOpenFolder.Text = "Open log folder";
            btnOpenFolder.UseVisualStyleBackColor = true;
            btnOpenFolder.Click += btnOpenFolder_Click;
            //
            // lblStatus
            //
            lblStatus.ApplyGradient = false;
            lblStatus.AutoSize = true;
            lblStatus.Margin = new System.Windows.Forms.Padding(0, 8, 0, 0);
            lblStatus.Name = "lblStatus";
            lblStatus.TabIndex = 7;
            lblStatus.Text = "";
            //
            // panelMarker
            //
            panelMarker.BackColor = System.Drawing.Color.Transparent;
            panelMarker.Border = new System.Windows.Forms.Padding(0, 1, 0, 0);
            panelMarker.BorderColor = System.Drawing.Color.Transparent;
            panelMarker.Controls.Add(flowMarker);
            panelMarker.Dock = System.Windows.Forms.DockStyle.Bottom;
            panelMarker.Name = "panelMarker";
            panelMarker.Radius = 0;
            panelMarker.ShadowDepth = 0F;
            panelMarker.Size = new System.Drawing.Size(1000, 36);
            panelMarker.TabIndex = 3;
            //
            // flowMarker
            //
            flowMarker.BackColor = System.Drawing.Color.Transparent;
            flowMarker.Controls.Add(lblMarker);
            flowMarker.Controls.Add(txtMarker);
            flowMarker.Controls.Add(btnAddMarker);
            flowMarker.Dock = System.Windows.Forms.DockStyle.Fill;
            flowMarker.Name = "flowMarker";
            flowMarker.Padding = new System.Windows.Forms.Padding(4, 4, 4, 0);
            flowMarker.WrapContents = false;
            flowMarker.TabIndex = 0;
            //
            // lblMarker
            //
            lblMarker.ApplyGradient = false;
            lblMarker.AutoSize = true;
            lblMarker.Margin = new System.Windows.Forms.Padding(0, 6, 4, 0);
            lblMarker.Name = "lblMarker";
            lblMarker.TabIndex = 0;
            lblMarker.Text = "Marker:";
            //
            // txtMarker
            //
            txtMarker.Margin = new System.Windows.Forms.Padding(0, 2, 6, 0);
            txtMarker.Name = "txtMarker";
            txtMarker.PlaceholderText = "e.g. the bot switched to a wrong target here";
            txtMarker.Size = new System.Drawing.Size(380, 23);
            txtMarker.TabIndex = 1;
            txtMarker.KeyDown += txtMarker_KeyDown;
            //
            // btnAddMarker
            //
            btnAddMarker.Color = System.Drawing.Color.Transparent;
            btnAddMarker.Margin = new System.Windows.Forms.Padding(0, 1, 0, 0);
            btnAddMarker.Name = "btnAddMarker";
            btnAddMarker.Radius = 6;
            btnAddMarker.ShadowDepth = 0F;
            btnAddMarker.Size = new System.Drawing.Size(100, 25);
            btnAddMarker.TabIndex = 2;
            btnAddMarker.Text = "Add marker";
            btnAddMarker.UseVisualStyleBackColor = true;
            btnAddMarker.Click += btnAddMarker_Click;
            //
            // tabSide
            //
            tabSide.Controls.Add(tabFilters);
            tabSide.Controls.Add(tabInject);
            tabSide.Controls.Add(tabRecording);
            tabSide.Dock = System.Windows.Forms.DockStyle.Right;
            tabSide.Name = "tabSide";
            tabSide.SelectedIndex = 0;
            tabSide.Size = new System.Drawing.Size(320, 500);
            tabSide.TabIndex = 2;
            //
            // tabFilters
            //
            tabFilters.Controls.Add(flowFilters);
            tabFilters.Name = "tabFilters";
            tabFilters.Padding = new System.Windows.Forms.Padding(3);
            tabFilters.TabIndex = 0;
            tabFilters.Text = "Filters";
            tabFilters.UseVisualStyleBackColor = true;
            //
            // flowFilters
            //
            flowFilters.AutoScroll = true;
            flowFilters.Controls.Add(lblFilterScope);
            flowFilters.Controls.Add(comboFilterScope);
            flowFilters.Controls.Add(radioExclude);
            flowFilters.Controls.Add(radioInclude);
            flowFilters.Controls.Add(checkShowClient);
            flowFilters.Controls.Add(checkShowServer);
            flowFilters.Controls.Add(checkShowBot);
            flowFilters.Controls.Add(flowOpcodeRow);
            flowFilters.Controls.Add(flowPresetRow);
            flowFilters.Controls.Add(listOpcodes);
            flowFilters.Dock = System.Windows.Forms.DockStyle.Fill;
            flowFilters.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            flowFilters.Name = "flowFilters";
            flowFilters.WrapContents = false;
            flowFilters.TabIndex = 0;
            //
            // lblFilterScope
            //
            lblFilterScope.ApplyGradient = false;
            lblFilterScope.AutoSize = true;
            lblFilterScope.Margin = new System.Windows.Forms.Padding(3, 6, 3, 2);
            lblFilterScope.Name = "lblFilterScope";
            lblFilterScope.TabIndex = 0;
            lblFilterScope.Text = "Edit the filter of:";
            //
            // comboFilterScope
            //
            comboFilterScope.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            comboFilterScope.Items.AddRange(new object[] { "Live view", "Recording" });
            comboFilterScope.Name = "comboFilterScope";
            comboFilterScope.Size = new System.Drawing.Size(280, 24);
            comboFilterScope.TabIndex = 1;
            comboFilterScope.SelectedIndexChanged += comboFilterScope_SelectedIndexChanged;
            //
            // radioExclude
            //
            radioExclude.AutoSize = true;
            radioExclude.Name = "radioExclude";
            radioExclude.Size = new System.Drawing.Size(200, 24);
            radioExclude.TabIndex = 2;
            radioExclude.Text = "Hide the listed opcodes";
            radioExclude.CheckedChanged += filterOption_Changed;
            //
            // radioInclude
            //
            radioInclude.AutoSize = true;
            radioInclude.Name = "radioInclude";
            radioInclude.Size = new System.Drawing.Size(200, 24);
            radioInclude.TabIndex = 3;
            radioInclude.Text = "Only show the listed opcodes";
            radioInclude.CheckedChanged += filterOption_Changed;
            //
            // checkShowClient
            //
            checkShowClient.AutoSize = true;
            checkShowClient.Depth = 0;
            checkShowClient.MouseLocation = new System.Drawing.Point(-1, -1);
            checkShowClient.Name = "checkShowClient";
            checkShowClient.Ripple = false;
            checkShowClient.Size = new System.Drawing.Size(200, 30);
            checkShowClient.TabIndex = 4;
            checkShowClient.Text = "Client -> Server";
            checkShowClient.UseVisualStyleBackColor = false;
            checkShowClient.CheckedChanged += filterOption_Changed;
            //
            // checkShowServer
            //
            checkShowServer.AutoSize = true;
            checkShowServer.Depth = 0;
            checkShowServer.MouseLocation = new System.Drawing.Point(-1, -1);
            checkShowServer.Name = "checkShowServer";
            checkShowServer.Ripple = false;
            checkShowServer.Size = new System.Drawing.Size(200, 30);
            checkShowServer.TabIndex = 5;
            checkShowServer.Text = "Server -> Client";
            checkShowServer.UseVisualStyleBackColor = false;
            checkShowServer.CheckedChanged += filterOption_Changed;
            //
            // checkShowBot
            //
            checkShowBot.AutoSize = true;
            checkShowBot.Depth = 0;
            checkShowBot.MouseLocation = new System.Drawing.Point(-1, -1);
            checkShowBot.Name = "checkShowBot";
            checkShowBot.Ripple = false;
            checkShowBot.Size = new System.Drawing.Size(200, 30);
            checkShowBot.TabIndex = 6;
            checkShowBot.Text = "Sent by the bot";
            checkShowBot.UseVisualStyleBackColor = false;
            checkShowBot.CheckedChanged += filterOption_Changed;
            //
            // flowOpcodeRow
            //
            flowOpcodeRow.AutoSize = true;
            flowOpcodeRow.Controls.Add(txtOpcode);
            flowOpcodeRow.Controls.Add(btnAddOpcode);
            flowOpcodeRow.Controls.Add(btnRemoveOpcode);
            flowOpcodeRow.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            flowOpcodeRow.Name = "flowOpcodeRow";
            flowOpcodeRow.WrapContents = false;
            flowOpcodeRow.TabIndex = 7;
            //
            // txtOpcode
            //
            txtOpcode.Margin = new System.Windows.Forms.Padding(3, 1, 3, 0);
            txtOpcode.Name = "txtOpcode";
            txtOpcode.PlaceholderText = "B021, 3057";
            txtOpcode.Size = new System.Drawing.Size(110, 23);
            txtOpcode.TabIndex = 0;
            txtOpcode.KeyDown += txtOpcode_KeyDown;
            //
            // btnAddOpcode
            //
            btnAddOpcode.Color = System.Drawing.Color.Transparent;
            btnAddOpcode.Name = "btnAddOpcode";
            btnAddOpcode.Radius = 6;
            btnAddOpcode.ShadowDepth = 0F;
            btnAddOpcode.Size = new System.Drawing.Size(75, 25);
            btnAddOpcode.TabIndex = 1;
            btnAddOpcode.Text = "Add";
            btnAddOpcode.UseVisualStyleBackColor = true;
            btnAddOpcode.Click += btnAddOpcode_Click;
            //
            // btnRemoveOpcode
            //
            btnRemoveOpcode.Color = System.Drawing.Color.Transparent;
            btnRemoveOpcode.Name = "btnRemoveOpcode";
            btnRemoveOpcode.Radius = 6;
            btnRemoveOpcode.ShadowDepth = 0F;
            btnRemoveOpcode.Size = new System.Drawing.Size(75, 25);
            btnRemoveOpcode.TabIndex = 2;
            btnRemoveOpcode.Text = "Remove";
            btnRemoveOpcode.UseVisualStyleBackColor = true;
            btnRemoveOpcode.Click += btnRemoveOpcode_Click;
            //
            // flowPresetRow
            //
            flowPresetRow.AutoSize = true;
            flowPresetRow.Controls.Add(btnNoisy);
            flowPresetRow.Controls.Add(btnClearOpcodes);
            flowPresetRow.Name = "flowPresetRow";
            flowPresetRow.WrapContents = false;
            flowPresetRow.TabIndex = 8;
            //
            // btnNoisy
            //
            btnNoisy.Color = System.Drawing.Color.Transparent;
            btnNoisy.Name = "btnNoisy";
            btnNoisy.Radius = 6;
            btnNoisy.ShadowDepth = 0F;
            btnNoisy.Size = new System.Drawing.Size(150, 25);
            btnNoisy.TabIndex = 0;
            btnNoisy.Text = "Add noisy opcodes";
            btnNoisy.UseVisualStyleBackColor = true;
            btnNoisy.Click += btnNoisy_Click;
            //
            // btnClearOpcodes
            //
            btnClearOpcodes.Color = System.Drawing.Color.Transparent;
            btnClearOpcodes.Name = "btnClearOpcodes";
            btnClearOpcodes.Radius = 6;
            btnClearOpcodes.ShadowDepth = 0F;
            btnClearOpcodes.Size = new System.Drawing.Size(120, 25);
            btnClearOpcodes.TabIndex = 1;
            btnClearOpcodes.Text = "Clear list";
            btnClearOpcodes.UseVisualStyleBackColor = true;
            btnClearOpcodes.Click += btnClearOpcodes_Click;
            //
            // listOpcodes
            //
            listOpcodes.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            listOpcodes.IntegralHeight = false;
            listOpcodes.Name = "listOpcodes";
            listOpcodes.Size = new System.Drawing.Size(285, 220);
            listOpcodes.TabIndex = 9;
            //
            // tabInject
            //
            tabInject.Controls.Add(flowInject);
            tabInject.Name = "tabInject";
            tabInject.Padding = new System.Windows.Forms.Padding(3);
            tabInject.TabIndex = 1;
            tabInject.Text = "Inject";
            tabInject.UseVisualStyleBackColor = true;
            //
            // flowInject
            //
            flowInject.AutoScroll = true;
            flowInject.Controls.Add(lblInjectOpcode);
            flowInject.Controls.Add(txtInjectOpcode);
            flowInject.Controls.Add(lblInjectData);
            flowInject.Controls.Add(txtInjectData);
            flowInject.Controls.Add(lblInjectTo);
            flowInject.Controls.Add(comboInjectTo);
            flowInject.Controls.Add(checkInjectEncrypted);
            flowInject.Controls.Add(checkInjectMassive);
            flowInject.Controls.Add(btnInject);
            flowInject.Controls.Add(lblInjectHint);
            flowInject.Dock = System.Windows.Forms.DockStyle.Fill;
            flowInject.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            flowInject.Name = "flowInject";
            flowInject.WrapContents = false;
            flowInject.TabIndex = 0;
            //
            // lblInjectOpcode
            //
            lblInjectOpcode.ApplyGradient = false;
            lblInjectOpcode.AutoSize = true;
            lblInjectOpcode.Margin = new System.Windows.Forms.Padding(3, 6, 3, 2);
            lblInjectOpcode.Name = "lblInjectOpcode";
            lblInjectOpcode.TabIndex = 0;
            lblInjectOpcode.Text = "Opcode (hex):";
            //
            // txtInjectOpcode
            //
            txtInjectOpcode.Name = "txtInjectOpcode";
            txtInjectOpcode.PlaceholderText = "7025";
            txtInjectOpcode.Size = new System.Drawing.Size(120, 23);
            txtInjectOpcode.TabIndex = 1;
            //
            // lblInjectData
            //
            lblInjectData.ApplyGradient = false;
            lblInjectData.AutoSize = true;
            lblInjectData.Margin = new System.Windows.Forms.Padding(3, 6, 3, 2);
            lblInjectData.Name = "lblInjectData";
            lblInjectData.TabIndex = 2;
            lblInjectData.Text = "Data (hex bytes):";
            //
            // txtInjectData
            //
            txtInjectData.Font = new System.Drawing.Font("Consolas", 9F);
            txtInjectData.Multiline = true;
            txtInjectData.Name = "txtInjectData";
            txtInjectData.PlaceholderText = "01 00 0A ...";
            txtInjectData.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            txtInjectData.Size = new System.Drawing.Size(285, 140);
            txtInjectData.TabIndex = 3;
            //
            // lblInjectTo
            //
            lblInjectTo.ApplyGradient = false;
            lblInjectTo.AutoSize = true;
            lblInjectTo.Margin = new System.Windows.Forms.Padding(3, 6, 3, 2);
            lblInjectTo.Name = "lblInjectTo";
            lblInjectTo.TabIndex = 4;
            lblInjectTo.Text = "Send to:";
            //
            // comboInjectTo
            //
            comboInjectTo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            comboInjectTo.Items.AddRange(new object[] { "Server", "Client" });
            comboInjectTo.Name = "comboInjectTo";
            comboInjectTo.Size = new System.Drawing.Size(120, 24);
            comboInjectTo.TabIndex = 5;
            //
            // checkInjectEncrypted
            //
            checkInjectEncrypted.AutoSize = true;
            checkInjectEncrypted.Depth = 0;
            checkInjectEncrypted.MouseLocation = new System.Drawing.Point(-1, -1);
            checkInjectEncrypted.Name = "checkInjectEncrypted";
            checkInjectEncrypted.Ripple = false;
            checkInjectEncrypted.Size = new System.Drawing.Size(120, 30);
            checkInjectEncrypted.TabIndex = 6;
            checkInjectEncrypted.Text = "Encrypted";
            checkInjectEncrypted.UseVisualStyleBackColor = false;
            //
            // checkInjectMassive
            //
            checkInjectMassive.AutoSize = true;
            checkInjectMassive.Depth = 0;
            checkInjectMassive.MouseLocation = new System.Drawing.Point(-1, -1);
            checkInjectMassive.Name = "checkInjectMassive";
            checkInjectMassive.Ripple = false;
            checkInjectMassive.Size = new System.Drawing.Size(120, 30);
            checkInjectMassive.TabIndex = 7;
            checkInjectMassive.Text = "Massive";
            checkInjectMassive.UseVisualStyleBackColor = false;
            //
            // btnInject
            //
            btnInject.Color = System.Drawing.Color.Transparent;
            btnInject.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            btnInject.Name = "btnInject";
            btnInject.Radius = 6;
            btnInject.ShadowDepth = 0F;
            btnInject.Size = new System.Drawing.Size(120, 25);
            btnInject.TabIndex = 8;
            btnInject.Text = "Inject";
            btnInject.UseVisualStyleBackColor = true;
            btnInject.Click += btnInject_Click;
            //
            // lblInjectHint
            //
            lblInjectHint.ApplyGradient = false;
            lblInjectHint.AutoSize = true;
            lblInjectHint.Margin = new System.Windows.Forms.Padding(3, 8, 3, 0);
            lblInjectHint.MaximumSize = new System.Drawing.Size(285, 0);
            lblInjectHint.Name = "lblInjectHint";
            lblInjectHint.TabIndex = 9;
            lblInjectHint.Text = "Tip: right click a packet in the list and choose \"Copy to injector\" to resend or modify it.";
            //
            // tabRecording
            //
            tabRecording.Controls.Add(flowRecording);
            tabRecording.Name = "tabRecording";
            tabRecording.Padding = new System.Windows.Forms.Padding(3);
            tabRecording.TabIndex = 2;
            tabRecording.Text = "Recording";
            tabRecording.UseVisualStyleBackColor = true;
            //
            // flowRecording
            //
            flowRecording.AutoScroll = true;
            flowRecording.Controls.Add(lblMaxFile);
            flowRecording.Controls.Add(numMaxFile);
            flowRecording.Controls.Add(lblMaxFolder);
            flowRecording.Controls.Add(numMaxFolder);
            flowRecording.Controls.Add(lblKeepDays);
            flowRecording.Controls.Add(numKeepDays);
            flowRecording.Controls.Add(checkRecordOnLaunch);
            flowRecording.Controls.Add(checkRecordOnBotStart);
            flowRecording.Controls.Add(checkIncludeHex);
            flowRecording.Controls.Add(checkIncludeLog);
            flowRecording.Controls.Add(btnReloadNames);
            flowRecording.Controls.Add(btnEditNames);
            flowRecording.Controls.Add(lblRecordFile);
            flowRecording.Dock = System.Windows.Forms.DockStyle.Fill;
            flowRecording.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            flowRecording.Name = "flowRecording";
            flowRecording.WrapContents = false;
            flowRecording.TabIndex = 0;
            //
            // lblMaxFile
            //
            lblMaxFile.ApplyGradient = false;
            lblMaxFile.AutoSize = true;
            lblMaxFile.Margin = new System.Windows.Forms.Padding(3, 6, 3, 2);
            lblMaxFile.Name = "lblMaxFile";
            lblMaxFile.TabIndex = 0;
            lblMaxFile.Text = "Start a new file after (MB):";
            //
            // numMaxFile
            //
            numMaxFile.Maximum = new decimal(new int[] { 2048, 0, 0, 0 });
            numMaxFile.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numMaxFile.Name = "numMaxFile";
            numMaxFile.Size = new System.Drawing.Size(120, 23);
            numMaxFile.TabIndex = 1;
            numMaxFile.Value = new decimal(new int[] { 50, 0, 0, 0 });
            numMaxFile.ValueChanged += recordSetting_Changed;
            //
            // lblMaxFolder
            //
            lblMaxFolder.ApplyGradient = false;
            lblMaxFolder.AutoSize = true;
            lblMaxFolder.Margin = new System.Windows.Forms.Padding(3, 6, 3, 2);
            lblMaxFolder.Name = "lblMaxFolder";
            lblMaxFolder.TabIndex = 2;
            lblMaxFolder.Text = "Max size of all packet logs (MB):";
            //
            // numMaxFolder
            //
            numMaxFolder.Maximum = new decimal(new int[] { 102400, 0, 0, 0 });
            numMaxFolder.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numMaxFolder.Name = "numMaxFolder";
            numMaxFolder.Size = new System.Drawing.Size(120, 23);
            numMaxFolder.TabIndex = 3;
            numMaxFolder.Value = new decimal(new int[] { 1024, 0, 0, 0 });
            numMaxFolder.ValueChanged += recordSetting_Changed;
            //
            // lblKeepDays
            //
            lblKeepDays.ApplyGradient = false;
            lblKeepDays.AutoSize = true;
            lblKeepDays.Margin = new System.Windows.Forms.Padding(3, 6, 3, 2);
            lblKeepDays.Name = "lblKeepDays";
            lblKeepDays.TabIndex = 4;
            lblKeepDays.Text = "Delete logs older than (days, 0 = never):";
            //
            // numKeepDays
            //
            numKeepDays.Maximum = new decimal(new int[] { 365, 0, 0, 0 });
            numKeepDays.Name = "numKeepDays";
            numKeepDays.Size = new System.Drawing.Size(120, 23);
            numKeepDays.TabIndex = 5;
            numKeepDays.Value = new decimal(new int[] { 7, 0, 0, 0 });
            numKeepDays.ValueChanged += recordSetting_Changed;
            //
            // checkRecordOnLaunch
            //
            checkRecordOnLaunch.AutoSize = true;
            checkRecordOnLaunch.Depth = 0;
            checkRecordOnLaunch.Margin = new System.Windows.Forms.Padding(3, 8, 3, 0);
            checkRecordOnLaunch.MouseLocation = new System.Drawing.Point(-1, -1);
            checkRecordOnLaunch.Name = "checkRecordOnLaunch";
            checkRecordOnLaunch.Ripple = false;
            checkRecordOnLaunch.Size = new System.Drawing.Size(250, 30);
            checkRecordOnLaunch.TabIndex = 6;
            checkRecordOnLaunch.Text = "Start recording when RSBot starts";
            checkRecordOnLaunch.UseVisualStyleBackColor = false;
            checkRecordOnLaunch.CheckedChanged += recordSetting_Changed;
            //
            // checkRecordOnBotStart
            //
            checkRecordOnBotStart.AutoSize = true;
            checkRecordOnBotStart.Depth = 0;
            checkRecordOnBotStart.MouseLocation = new System.Drawing.Point(-1, -1);
            checkRecordOnBotStart.Name = "checkRecordOnBotStart";
            checkRecordOnBotStart.Ripple = false;
            checkRecordOnBotStart.Size = new System.Drawing.Size(250, 30);
            checkRecordOnBotStart.TabIndex = 7;
            checkRecordOnBotStart.Text = "Start recording when the bot starts";
            checkRecordOnBotStart.UseVisualStyleBackColor = false;
            checkRecordOnBotStart.CheckedChanged += recordSetting_Changed;
            //
            // checkIncludeHex
            //
            checkIncludeHex.AutoSize = true;
            checkIncludeHex.Depth = 0;
            checkIncludeHex.MouseLocation = new System.Drawing.Point(-1, -1);
            checkIncludeHex.Name = "checkIncludeHex";
            checkIncludeHex.Ripple = false;
            checkIncludeHex.Size = new System.Drawing.Size(250, 30);
            checkIncludeHex.TabIndex = 8;
            checkIncludeHex.Text = "Write the hex dump of each packet";
            checkIncludeHex.UseVisualStyleBackColor = false;
            checkIncludeHex.CheckedChanged += recordSetting_Changed;
            //
            // checkIncludeLog
            //
            checkIncludeLog.AutoSize = true;
            checkIncludeLog.Depth = 0;
            checkIncludeLog.MouseLocation = new System.Drawing.Point(-1, -1);
            checkIncludeLog.Name = "checkIncludeLog";
            checkIncludeLog.Ripple = false;
            checkIncludeLog.Size = new System.Drawing.Size(250, 30);
            checkIncludeLog.TabIndex = 9;
            checkIncludeLog.Text = "Write the bot log lines";
            checkIncludeLog.UseVisualStyleBackColor = false;
            checkIncludeLog.CheckedChanged += recordSetting_Changed;
            //
            // btnReloadNames
            //
            btnReloadNames.Color = System.Drawing.Color.Transparent;
            btnReloadNames.Margin = new System.Windows.Forms.Padding(3, 10, 3, 3);
            btnReloadNames.Name = "btnReloadNames";
            btnReloadNames.Radius = 6;
            btnReloadNames.ShadowDepth = 0F;
            btnReloadNames.Size = new System.Drawing.Size(180, 25);
            btnReloadNames.TabIndex = 10;
            btnReloadNames.Text = "Reload opcode names";
            btnReloadNames.UseVisualStyleBackColor = true;
            btnReloadNames.Click += btnReloadNames_Click;
            //
            // btnEditNames
            //
            btnEditNames.Color = System.Drawing.Color.Transparent;
            btnEditNames.Name = "btnEditNames";
            btnEditNames.Radius = 6;
            btnEditNames.ShadowDepth = 0F;
            btnEditNames.Size = new System.Drawing.Size(180, 25);
            btnEditNames.TabIndex = 11;
            btnEditNames.Text = "Edit custom opcode names";
            btnEditNames.UseVisualStyleBackColor = true;
            btnEditNames.Click += btnEditNames_Click;
            //
            // lblRecordFile
            //
            lblRecordFile.ApplyGradient = false;
            lblRecordFile.AutoSize = true;
            lblRecordFile.Margin = new System.Windows.Forms.Padding(3, 10, 3, 0);
            lblRecordFile.MaximumSize = new System.Drawing.Size(285, 0);
            lblRecordFile.Name = "lblRecordFile";
            lblRecordFile.TabIndex = 12;
            lblRecordFile.Text = "Not recording";
            //
            // splitMain
            //
            splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            splitMain.Name = "splitMain";
            splitMain.Orientation = System.Windows.Forms.Orientation.Horizontal;
            //
            // splitMain.Panel1
            //
            splitMain.Panel1.Controls.Add(listPackets);
            //
            // splitMain.Panel2
            //
            splitMain.Panel2.Controls.Add(txtDetail);
            splitMain.Size = new System.Drawing.Size(680, 500);
            splitMain.SplitterDistance = 300;
            splitMain.TabIndex = 1;
            //
            // listPackets
            //
            listPackets.BorderStyle = System.Windows.Forms.BorderStyle.None;
            listPackets.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { columnTime, columnContext, columnDirection, columnOrigin, columnOpcode, columnName, columnLength, columnFlags });
            listPackets.ContextMenuStrip = contextPackets;
            listPackets.Dock = System.Windows.Forms.DockStyle.Fill;
            listPackets.Font = new System.Drawing.Font("Consolas", 9F);
            listPackets.Name = "listPackets";
            listPackets.TabIndex = 0;
            listPackets.RetrieveVirtualItem += listPackets_RetrieveVirtualItem;
            listPackets.SelectedIndexChanged += listPackets_SelectedIndexChanged;
            //
            // columnTime
            //
            columnTime.Text = "Time";
            columnTime.Width = 95;
            //
            // columnContext
            //
            columnContext.Text = "Srv";
            columnContext.Width = 35;
            //
            // columnDirection
            //
            columnDirection.Text = "Dir";
            columnDirection.Width = 50;
            //
            // columnOrigin
            //
            columnOrigin.Text = "Origin";
            columnOrigin.Width = 55;
            //
            // columnOpcode
            //
            columnOpcode.Text = "Opcode";
            columnOpcode.Width = 65;
            //
            // columnName
            //
            columnName.Text = "Name";
            columnName.Width = 230;
            //
            // columnLength
            //
            columnLength.Text = "Len";
            columnLength.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            columnLength.Width = 55;
            //
            // columnFlags
            //
            columnFlags.Text = "Flags";
            columnFlags.Width = 55;
            //
            // contextPackets
            //
            contextPackets.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { menuCopyLine, menuCopyHex, menuCopyToInjector, menuHideOpcode });
            contextPackets.Name = "contextPackets";
            contextPackets.Opening += contextPackets_Opening;
            //
            // menuCopyLine
            //
            menuCopyLine.Name = "menuCopyLine";
            menuCopyLine.Text = "Copy as log text";
            menuCopyLine.Click += menuCopyLine_Click;
            //
            // menuCopyHex
            //
            menuCopyHex.Name = "menuCopyHex";
            menuCopyHex.Text = "Copy payload as hex";
            menuCopyHex.Click += menuCopyHex_Click;
            //
            // menuCopyToInjector
            //
            menuCopyToInjector.Name = "menuCopyToInjector";
            menuCopyToInjector.Text = "Copy to injector";
            menuCopyToInjector.Click += menuCopyToInjector_Click;
            //
            // menuHideOpcode
            //
            menuHideOpcode.Name = "menuHideOpcode";
            menuHideOpcode.Text = "Add opcode to the live view filter";
            menuHideOpcode.Click += menuHideOpcode_Click;
            //
            // txtDetail
            //
            txtDetail.BorderStyle = System.Windows.Forms.BorderStyle.None;
            txtDetail.DetectUrls = false;
            txtDetail.Dock = System.Windows.Forms.DockStyle.Fill;
            txtDetail.Font = new System.Drawing.Font("Consolas", 9F);
            txtDetail.Name = "txtDetail";
            txtDetail.ReadOnly = true;
            txtDetail.TabIndex = 0;
            txtDetail.Text = "";
            txtDetail.WordWrap = false;
            //
            // Main
            //
            Controls.Add(splitMain);
            Controls.Add(tabSide);
            Controls.Add(panelMarker);
            Controls.Add(panelTop);
            Name = "Main";
            Size = new System.Drawing.Size(1000, 576);
            panelTop.ResumeLayout(false);
            flowTop.ResumeLayout(false);
            flowTop.PerformLayout();
            panelMarker.ResumeLayout(false);
            flowMarker.ResumeLayout(false);
            flowMarker.PerformLayout();
            tabSide.ResumeLayout(false);
            tabFilters.ResumeLayout(false);
            flowFilters.ResumeLayout(false);
            flowFilters.PerformLayout();
            flowOpcodeRow.ResumeLayout(false);
            flowOpcodeRow.PerformLayout();
            flowPresetRow.ResumeLayout(false);
            tabInject.ResumeLayout(false);
            flowInject.ResumeLayout(false);
            flowInject.PerformLayout();
            tabRecording.ResumeLayout(false);
            flowRecording.ResumeLayout(false);
            flowRecording.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numMaxFile).EndInit();
            ((System.ComponentModel.ISupportInitialize)numMaxFolder).EndInit();
            ((System.ComponentModel.ISupportInitialize)numKeepDays).EndInit();
            splitMain.Panel1.ResumeLayout(false);
            splitMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitMain).EndInit();
            splitMain.ResumeLayout(false);
            contextPackets.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Timer timerRefresh;
        private SDUI.Controls.Panel panelTop;
        private System.Windows.Forms.FlowLayoutPanel flowTop;
        private SDUI.Controls.CheckBox checkCapture;
        private SDUI.Controls.Button btnRecord;
        private SDUI.Controls.CheckBox checkAutoScroll;
        private SDUI.Controls.Button btnClear;
        private SDUI.Controls.Label lblSearch;
        private System.Windows.Forms.TextBox txtSearch;
        private SDUI.Controls.Button btnOpenFolder;
        private SDUI.Controls.Label lblStatus;
        private SDUI.Controls.Panel panelMarker;
        private System.Windows.Forms.FlowLayoutPanel flowMarker;
        private SDUI.Controls.Label lblMarker;
        private System.Windows.Forms.TextBox txtMarker;
        private SDUI.Controls.Button btnAddMarker;
        private SDUI.Controls.TabControl tabSide;
        private System.Windows.Forms.TabPage tabFilters;
        private System.Windows.Forms.FlowLayoutPanel flowFilters;
        private SDUI.Controls.Label lblFilterScope;
        private SDUI.Controls.ComboBox comboFilterScope;
        private SDUI.Controls.Radio radioExclude;
        private SDUI.Controls.Radio radioInclude;
        private SDUI.Controls.CheckBox checkShowClient;
        private SDUI.Controls.CheckBox checkShowServer;
        private SDUI.Controls.CheckBox checkShowBot;
        private System.Windows.Forms.FlowLayoutPanel flowOpcodeRow;
        private System.Windows.Forms.TextBox txtOpcode;
        private SDUI.Controls.Button btnAddOpcode;
        private SDUI.Controls.Button btnRemoveOpcode;
        private System.Windows.Forms.FlowLayoutPanel flowPresetRow;
        private SDUI.Controls.Button btnNoisy;
        private SDUI.Controls.Button btnClearOpcodes;
        private System.Windows.Forms.ListBox listOpcodes;
        private System.Windows.Forms.TabPage tabInject;
        private System.Windows.Forms.FlowLayoutPanel flowInject;
        private SDUI.Controls.Label lblInjectOpcode;
        private System.Windows.Forms.TextBox txtInjectOpcode;
        private SDUI.Controls.Label lblInjectData;
        private System.Windows.Forms.TextBox txtInjectData;
        private SDUI.Controls.Label lblInjectTo;
        private SDUI.Controls.ComboBox comboInjectTo;
        private SDUI.Controls.CheckBox checkInjectEncrypted;
        private SDUI.Controls.CheckBox checkInjectMassive;
        private SDUI.Controls.Button btnInject;
        private SDUI.Controls.Label lblInjectHint;
        private System.Windows.Forms.TabPage tabRecording;
        private System.Windows.Forms.FlowLayoutPanel flowRecording;
        private SDUI.Controls.Label lblMaxFile;
        private System.Windows.Forms.NumericUpDown numMaxFile;
        private SDUI.Controls.Label lblMaxFolder;
        private System.Windows.Forms.NumericUpDown numMaxFolder;
        private SDUI.Controls.Label lblKeepDays;
        private System.Windows.Forms.NumericUpDown numKeepDays;
        private SDUI.Controls.CheckBox checkRecordOnLaunch;
        private SDUI.Controls.CheckBox checkRecordOnBotStart;
        private SDUI.Controls.CheckBox checkIncludeHex;
        private SDUI.Controls.CheckBox checkIncludeLog;
        private SDUI.Controls.Button btnReloadNames;
        private SDUI.Controls.Button btnEditNames;
        private SDUI.Controls.Label lblRecordFile;
        private System.Windows.Forms.SplitContainer splitMain;
        private PacketListView listPackets;
        private System.Windows.Forms.ColumnHeader columnTime;
        private System.Windows.Forms.ColumnHeader columnContext;
        private System.Windows.Forms.ColumnHeader columnDirection;
        private System.Windows.Forms.ColumnHeader columnOrigin;
        private System.Windows.Forms.ColumnHeader columnOpcode;
        private System.Windows.Forms.ColumnHeader columnName;
        private System.Windows.Forms.ColumnHeader columnLength;
        private System.Windows.Forms.ColumnHeader columnFlags;
        private System.Windows.Forms.ContextMenuStrip contextPackets;
        private System.Windows.Forms.ToolStripMenuItem menuCopyLine;
        private System.Windows.Forms.ToolStripMenuItem menuCopyHex;
        private System.Windows.Forms.ToolStripMenuItem menuCopyToInjector;
        private System.Windows.Forms.ToolStripMenuItem menuHideOpcode;
        private System.Windows.Forms.RichTextBox txtDetail;
    }
}
