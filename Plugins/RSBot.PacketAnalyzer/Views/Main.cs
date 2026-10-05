using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RSBot.Core.Network;
using RSBot.PacketAnalyzer.Components;
using SDUI;
using SDUI.Controls;

namespace RSBot.PacketAnalyzer.Views;

[ToolboxItem(false)]
public partial class Main : DoubleBufferedControl
{
    private const int MaxCaptures = 20_000;
    private const int MaxRows = 10_000;
    private const int TrimSlack = 1_000;

    private readonly List<PacketCapture> _captures = new();
    private readonly List<PacketCapture> _rows = new();
    private readonly List<PacketCapture> _drained = new();

    private string _search = string.Empty;
    private bool _loading;
    private bool _injectConfirmed;
    private string _lastStatus;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Main" /> class.
    /// </summary>
    public Main()
    {
        InitializeComponent();

        PacketHub.Initialize();
        LoadSettings();
        ApplyTheme();

        ColorScheme.ThemeChanged += (_, _) => ApplyTheme();

        timerRefresh.Start();
    }

    private PacketFilter EditedFilter =>
        comboFilterScope.SelectedIndex == 1 ? PacketHub.RecordFilter : PacketHub.ViewFilter;

    private PacketCapture SelectedCapture =>
        listPackets.SelectedIndices.Count > 0 && listPackets.SelectedIndices[0] < _rows.Count
            ? _rows[listPackets.SelectedIndices[0]]
            : null;

    #region Settings

    private void LoadSettings()
    {
        _loading = true;

        var recorder = PacketHub.Recorder;

        checkCapture.Checked = PacketHub.LiveCapture;
        numMaxFile.Value = Math.Clamp(recorder.MaxFileMB, (int)numMaxFile.Minimum, (int)numMaxFile.Maximum);
        numMaxFolder.Value = Math.Clamp(recorder.MaxFolderMB, (int)numMaxFolder.Minimum, (int)numMaxFolder.Maximum);
        numKeepDays.Value = Math.Clamp(recorder.KeepDays, (int)numKeepDays.Minimum, (int)numKeepDays.Maximum);
        checkRecordOnLaunch.Checked = recorder.RecordOnLaunch;
        checkRecordOnBotStart.Checked = recorder.RecordOnBotStart;
        checkIncludeHex.Checked = recorder.IncludeHex;
        checkIncludeLog.Checked = recorder.IncludeLog;
        comboInjectTo.SelectedIndex = 0;
        comboFilterScope.SelectedIndex = 0;

        _loading = false;

        LoadFilter();
        UpdateStatus();
    }

    private void LoadFilter()
    {
        _loading = true;

        var filter = EditedFilter;

        radioInclude.Checked = filter.IncludeOnly;
        radioExclude.Checked = !filter.IncludeOnly;
        checkShowClient.Checked = filter.ShowClient;
        checkShowServer.Checked = filter.ShowServer;
        checkShowBot.Checked = filter.ShowBot;

        listOpcodes.BeginUpdate();
        listOpcodes.Items.Clear();
        foreach (var opcode in filter.Opcodes)
            listOpcodes.Items.Add(new OpcodeItem(opcode));
        listOpcodes.EndUpdate();

        _loading = false;
    }

    private void ApplyTheme()
    {
        foreach (Control control in new Control[] { listPackets, txtDetail, listOpcodes })
        {
            control.BackColor = ColorScheme.BackColor;
            control.ForeColor = ColorScheme.ForeColor;
        }

        foreach (var page in new[] { tabFilters, tabInject, tabRecording })
        {
            page.UseVisualStyleBackColor = false;
            page.BackColor = ColorScheme.BackColor;
            page.ForeColor = ColorScheme.ForeColor;
        }
    }

    #endregion Settings

    #region Packet list

    private void timerRefresh_Tick(object sender, EventArgs e)
    {
        UpdateStatus();

        _drained.Clear();
        PacketHub.Drain(_drained, 5_000);
        if (_drained.Count == 0)
            return;

        var removedRows = 0;
        foreach (var capture in _drained)
        {
            _captures.Add(capture);
            if (IsVisible(capture))
                _rows.Add(capture);
        }

        if (_captures.Count > MaxCaptures + TrimSlack)
            _captures.RemoveRange(0, _captures.Count - MaxCaptures);

        if (_rows.Count > MaxRows + TrimSlack)
        {
            removedRows = _rows.Count - MaxRows;
            _rows.RemoveRange(0, removedRows);
        }

        RefreshList(removedRows);
    }

    private bool IsVisible(PacketCapture capture)
    {
        if (!PacketHub.ViewFilter.Matches(capture))
            return false;

        if (_search.Length == 0)
            return true;

        return capture.Opcode.ToString("X4").Contains(_search, StringComparison.OrdinalIgnoreCase)
            || OpcodeNames.Get(capture.Opcode, capture.Destination).Contains(_search, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Applies the view filter and the search to all captured packets again.
    /// </summary>
    private void RebuildRows()
    {
        _rows.Clear();
        _rows.AddRange(_captures.Where(IsVisible).TakeLast(MaxRows));

        listPackets.SelectedIndices.Clear();
        txtDetail.Clear();

        RefreshList(0);
    }

    private void RefreshList(int removedRows)
    {
        var selected = listPackets.SelectedIndices.Count > 0 ? listPackets.SelectedIndices[0] : -1;

        listPackets.BeginUpdate();
        listPackets.VirtualListSize = _rows.Count;

        if (removedRows > 0 && selected >= 0)
        {
            listPackets.SelectedIndices.Clear();
            if (selected - removedRows >= 0)
                listPackets.SelectedIndices.Add(selected - removedRows);
        }

        if (checkAutoScroll.Checked && _rows.Count > 0)
            listPackets.EnsureVisible(_rows.Count - 1);

        listPackets.EndUpdate();
        listPackets.Invalidate();
    }

    private void listPackets_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _rows.Count)
        {
            e.Item = new ListViewItem(Enumerable.Repeat(string.Empty, 8).ToArray());
            return;
        }

        var capture = _rows[e.ItemIndex];

        e.Item = new ListViewItem(
            new[]
            {
                capture.Time.ToString("HH:mm:ss.fff"),
                PacketFormatter.Context(capture),
                PacketFormatter.Direction(capture),
                PacketFormatter.Origin(capture),
                $"0x{capture.Opcode:X4}",
                OpcodeNames.Get(capture.Opcode, capture.Destination),
                capture.Payload.Length.ToString(),
                PacketFormatter.Flags(capture),
            }
        );
    }

    private void listPackets_SelectedIndexChanged(object sender, EventArgs e)
    {
        var capture = SelectedCapture;

        txtDetail.Text = capture == null ? string.Empty : PacketFormatter.Format(capture, true);
    }

    private void UpdateStatus()
    {
        var recorder = PacketHub.Recorder;

        var status = $"{_rows.Count} shown / {_captures.Count} captured";
        if (recorder.IsRecording)
            status += $"  |  REC {recorder.CurrentFileSize / 1024} KB";

        if (status == _lastStatus)
            return;

        _lastStatus = status;
        lblStatus.Text = status;
        btnRecord.Text = recorder.IsRecording ? "Stop recording" : "Start recording";
        lblRecordFile.Text = recorder.IsRecording && recorder.CurrentFile != null
            ? $"Recording to:{Environment.NewLine}{recorder.CurrentFile}"
            : "Not recording";
    }

    #endregion Packet list

    #region Toolbar

    private void checkCapture_CheckedChanged(object sender, EventArgs e)
    {
        if (!_loading)
            PacketHub.LiveCapture = checkCapture.Checked;
    }

    private void btnRecord_Click(object sender, EventArgs e)
    {
        if (PacketHub.Recorder.IsRecording)
            PacketHub.Recorder.Stop();
        else
            PacketHub.Recorder.Start();

        _lastStatus = null;
        UpdateStatus();
    }

    private void btnClear_Click(object sender, EventArgs e)
    {
        _captures.Clear();
        RebuildRows();
    }

    private void txtSearch_TextChanged(object sender, EventArgs e)
    {
        var search = txtSearch.Text.Trim();
        if (search.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            search = search[2..];

        _search = search;
        RebuildRows();
    }

    private void btnOpenFolder_Click(object sender, EventArgs e)
    {
        var recorder = PacketHub.Recorder;
        var folder = recorder.IsRecording && recorder.CurrentFile != null
            ? Path.GetDirectoryName(recorder.CurrentFile)
            : PacketRecorder.RootDirectory;

        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void btnAddMarker_Click(object sender, EventArgs e)
    {
        var text = txtMarker.Text.Trim();
        if (text.Length == 0)
            return;

        if (!PacketHub.Recorder.IsRecording)
        {
            MessageBox.Show(
                "Markers are written to the packet log. Start recording first.",
                "Packet Analyzer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        PacketHub.Recorder.AddMarker(text);
        txtMarker.Clear();
    }

    private void txtMarker_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;

        e.SuppressKeyPress = true;
        btnAddMarker_Click(sender, e);
    }

    #endregion Toolbar

    #region Filters

    private void comboFilterScope_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (!_loading)
            LoadFilter();
    }

    private void filterOption_Changed(object sender, EventArgs e)
    {
        if (_loading)
            return;

        var filter = EditedFilter;
        filter.IncludeOnly = radioInclude.Checked;
        filter.ShowClient = checkShowClient.Checked;
        filter.ShowServer = checkShowServer.Checked;
        filter.ShowBot = checkShowBot.Checked;
        filter.Save();

        OnFilterChanged(filter);
    }

    private void btnAddOpcode_Click(object sender, EventArgs e)
    {
        var opcodes = new List<ushort>();
        foreach (var text in txtOpcode.Text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!OpcodeNames.TryParseOpcode(text, out var opcode))
            {
                MessageBox.Show($"'{text}' is not a valid hex opcode.", "Packet Analyzer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            opcodes.Add(opcode);
        }

        if (opcodes.Count == 0)
            return;

        EditedFilter.Add(opcodes);
        txtOpcode.Clear();

        OnFilterChanged(EditedFilter);
    }

    private void txtOpcode_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;

        e.SuppressKeyPress = true;
        btnAddOpcode_Click(sender, e);
    }

    private void btnRemoveOpcode_Click(object sender, EventArgs e)
    {
        if (listOpcodes.SelectedItem is not OpcodeItem item)
            return;

        EditedFilter.Remove(item.Opcode);
        OnFilterChanged(EditedFilter);
    }

    private void btnNoisy_Click(object sender, EventArgs e)
    {
        EditedFilter.Add(PacketFilter.NoisyOpcodes);
        OnFilterChanged(EditedFilter);
    }

    private void btnClearOpcodes_Click(object sender, EventArgs e)
    {
        EditedFilter.Clear();
        OnFilterChanged(EditedFilter);
    }

    private void OnFilterChanged(PacketFilter filter)
    {
        if (filter == EditedFilter)
            LoadFilter();

        if (filter == PacketHub.ViewFilter)
            RebuildRows();
    }

    #endregion Filters

    #region Context menu

    private void contextPackets_Opening(object sender, CancelEventArgs e)
    {
        e.Cancel = SelectedCapture == null;
    }

    private void menuCopyLine_Click(object sender, EventArgs e)
    {
        if (SelectedCapture is { } capture)
            Clipboard.SetText(PacketFormatter.Format(capture, true));
    }

    private void menuCopyHex_Click(object sender, EventArgs e)
    {
        if (SelectedCapture is { } capture && capture.Payload.Length > 0)
            Clipboard.SetText(PacketFormatter.HexString(capture.Payload));
    }

    private void menuCopyToInjector_Click(object sender, EventArgs e)
    {
        if (SelectedCapture is not { } capture)
            return;

        txtInjectOpcode.Text = capture.Opcode.ToString("X4");
        txtInjectData.Text = PacketFormatter.HexString(capture.Payload);
        comboInjectTo.SelectedIndex = capture.Destination == PacketDestination.Server ? 0 : 1;
        checkInjectEncrypted.Checked = capture.Encrypted;
        checkInjectMassive.Checked = capture.Massive;

        tabSide.SelectedTab = tabInject;
    }

    private void menuHideOpcode_Click(object sender, EventArgs e)
    {
        if (SelectedCapture is not { } capture)
            return;

        PacketHub.ViewFilter.Add(new[] { capture.Opcode });
        OnFilterChanged(PacketHub.ViewFilter);
    }

    #endregion Context menu

    #region Injection

    private void btnInject_Click(object sender, EventArgs e)
    {
        if (!PacketInjector.IsConnected)
        {
            MessageBox.Show("Not connected to a server.", "Packet Analyzer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!OpcodeNames.TryParseOpcode(txtInjectOpcode.Text, out var opcode))
        {
            MessageBox.Show("The opcode must be a hex number, e.g. 7025.", "Packet Analyzer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!PacketInjector.TryParseHex(txtInjectData.Text, out var data, out var error))
        {
            MessageBox.Show(error, "Packet Analyzer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (checkInjectEncrypted.Checked && checkInjectMassive.Checked)
        {
            MessageBox.Show("A packet can not be encrypted and massive at the same time.", "Packet Analyzer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_injectConfirmed)
        {
            var answer = MessageBox.Show(
                "Injected packets are sent as they are. A malformed packet can disconnect the character or get the account flagged."
                    + Environment.NewLine
                    + Environment.NewLine
                    + "Do you want to continue?",
                "Packet Analyzer",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (answer != DialogResult.Yes)
                return;

            _injectConfirmed = true;
        }

        var destination = comboInjectTo.SelectedIndex == 1 ? PacketDestination.Client : PacketDestination.Server;

        PacketInjector.Inject(opcode, data, destination, checkInjectEncrypted.Checked, checkInjectMassive.Checked);
    }

    #endregion Injection

    #region Recording settings

    private void recordSetting_Changed(object sender, EventArgs e)
    {
        if (_loading)
            return;

        var recorder = PacketHub.Recorder;

        recorder.MaxFileMB = (int)numMaxFile.Value;
        recorder.MaxFolderMB = (int)numMaxFolder.Value;
        recorder.KeepDays = (int)numKeepDays.Value;
        recorder.RecordOnLaunch = checkRecordOnLaunch.Checked;
        recorder.RecordOnBotStart = checkRecordOnBotStart.Checked;
        recorder.IncludeHex = checkIncludeHex.Checked;
        recorder.IncludeLog = checkIncludeLog.Checked;
    }

    private void btnReloadNames_Click(object sender, EventArgs e)
    {
        OpcodeNames.Reload();

        LoadFilter();
        listPackets.Invalidate();
    }

    private void btnEditNames_Click(object sender, EventArgs e)
    {
        if (!File.Exists(OpcodeNames.UserFilePath))
            OpcodeNames.Reload();

        Process.Start(new ProcessStartInfo(OpcodeNames.UserFilePath) { UseShellExecute = true });
    }

    #endregion Recording settings

    private sealed class OpcodeItem
    {
        public OpcodeItem(ushort opcode)
        {
            Opcode = opcode;
        }

        public ushort Opcode { get; }

        public override string ToString()
        {
            var name = OpcodeNames.Get(Opcode, PacketDestination.Client);
            if (name == "?")
                name = OpcodeNames.Get(Opcode, PacketDestination.Server);

            return $"0x{Opcode:X4}  {name}";
        }
    }
}
