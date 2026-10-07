using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using SDUI.Controls;

namespace RSBot.Log.Views;

[ToolboxItem(false)]
public partial class Main : DoubleBufferedControl
{
    private const int MaximumCharacters = 250000;
    private const int RetainedCharacters = 180000;
    private readonly object _queueLock = new();
    private readonly Queue<string> _pending = new();
    private int _pendingCharacters;
    private volatile int _enabledLevels;
    private volatile bool _disposed;
    private readonly Timer _displayTimer;

    public Main()
    {
        InitializeComponent();
        LoadConfig();
        if (!Kernel.Debug)
        {
            checkDebug.Checked = false;
            checkDebug.Visible = false;
        }
        foreach (var check in new[] { checkEnabled, checkNormal, checkDebug, checkWarning, checkError })
            check.CheckedChanged += (s, e) => UpdateFilters();
        UpdateFilters();

        components ??= new Container();
        _displayTimer = new Timer(components) { Interval = 250 };
        _displayTimer.Tick += (s, e) => FlushDisplay();
        VisibleChanged += (s, e) => UpdateTimer();
        EnabledChanged += (s, e) => UpdateTimer();
        EventManager.SubscribeEvent("OnAddLog", new Action<string, LogLevel>(AppendLog));
        Disposed += (s, e) =>
        {
            _disposed = true;
            EventManager.UnsubscribeEvent("OnAddLog", new Action<string, LogLevel>(AppendLog));
            lock (_queueLock)
            {
                _pending.Clear();
                _pendingCharacters = 0;
            }
        };
        UpdateTimer();
        InitializeFileSettings();
    }

    /// <summary>
    ///     Adds the log file level and retention settings next to the filters.
    /// </summary>
    private void InitializeFileSettings()
    {
        var levels = new[] { LogLevel.Debug, LogLevel.Notify, LogLevel.Warning, LogLevel.Error };

        var comboLevel = new System.Windows.Forms.ComboBox
        {
            DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
            Width = LogicalToDeviceUnits(85),
            Margin = new System.Windows.Forms.Padding(LogicalToDeviceUnits(4), LogicalToDeviceUnits(11), 0, 0),
        };
        foreach (var level in levels)
            comboLevel.Items.Add(level);

        var configuredLevel = GlobalConfig.GetEnum("RSBot.Log.File.Level", LogLevel.Warning);
        comboLevel.SelectedItem = Array.IndexOf(levels, configuredLevel) >= 0 ? configuredLevel : LogLevel.Warning;
        comboLevel.SelectedIndexChanged += (_, _) =>
        {
            GlobalConfig.Set("RSBot.Log.File.Level", comboLevel.SelectedItem.ToString());
            LogFileWriter.ReloadSettings();
        };

        var numKeepDays = new System.Windows.Forms.NumericUpDown
        {
            Minimum = 0,
            Maximum = 365,
            Width = LogicalToDeviceUnits(55),
            Margin = new System.Windows.Forms.Padding(LogicalToDeviceUnits(4), LogicalToDeviceUnits(11), 0, 0),
        };
        numKeepDays.Value = Math.Clamp(GlobalConfig.Get("RSBot.Log.File.KeepDays", 7), 0, 365);
        numKeepDays.ValueChanged += (_, _) =>
        {
            GlobalConfig.Set("RSBot.Log.File.KeepDays", (int)numKeepDays.Value);
            LogFileWriter.ReloadSettings();
        };

        System.Windows.Forms.Label createLabel(string text) =>
            new()
            {
                AutoSize = true,
                Text = text,
                BackColor = Color.Transparent,
                Margin = new System.Windows.Forms.Padding(LogicalToDeviceUnits(8), LogicalToDeviceUnits(15), 0, 0),
            };

        var panelFile = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Right,
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        panelFile.Controls.Add(createLabel("Log file:"));
        panelFile.Controls.Add(comboLevel);
        panelFile.Controls.Add(createLabel("Keep days:"));
        panelFile.Controls.Add(numKeepDays);

        panel1.Controls.Add(panelFile);
    }

    private void UpdateFilters()
    {
        var levels = 0;
        if (checkEnabled.Checked)
        {
            if (checkNormal.Checked) levels |= 1 << (int)LogLevel.Notify;
            if (checkDebug.Checked) levels |= 1 << (int)LogLevel.Debug;
            if (checkWarning.Checked) levels |= 1 << (int)LogLevel.Warning;
            if (checkError.Checked) levels |= 1 << (int)LogLevel.Error;
            levels |= 1 << (int)LogLevel.Fatal;
        }
        _enabledLevels = levels;
    }

    public void AppendLog(string message, LogLevel level = LogLevel.Notify)
    {
        if (_disposed || (_enabledLevels & (1 << (int)level)) == 0) return;
        var now = DateTime.Now;
        var line = $"[{now:HH:mm:ss}]\t<{level}> \t{message}{Environment.NewLine}";
        // The log file is written by LogFileWriter in Core, independently of these filters
        lock (_queueLock)
        {
            if (_disposed) return;
            Enqueue(line);
        }
    }

    private void Enqueue(string line)
    {
        // Bound hidden-page history and individual messages as well as visible text.
        if (line.Length > MaximumCharacters) line = line.Substring(0, MaximumCharacters - Environment.NewLine.Length) + Environment.NewLine;
        _pending.Enqueue(line);
        _pendingCharacters += line.Length;
        while (_pendingCharacters > MaximumCharacters && _pending.Count > 1)
            _pendingCharacters -= _pending.Dequeue().Length;
    }

    private void UpdateTimer()
    {
        if (IsDisposed || Disposing) return;
        _displayTimer.Enabled = Visible && Enabled;
        if (_displayTimer.Enabled) FlushDisplay();
    }

    private void FlushDisplay()
    {
        if (!Visible || !Enabled || IsDisposed || FindForm()?.WindowState == FormWindowState.Minimized) return;
        var batch = new StringBuilder();
        lock (_queueLock)
        {
            while (_pending.Count > 0 && batch.Length < 64000)
            {
                var line = _pending.Dequeue();
                _pendingCharacters -= line.Length;
                batch.Append(line);
            }
        }
        if (batch.Length == 0) return;
        var selectionStart = txtLog.SelectionStart;
        var selectionLength = txtLog.SelectionLength;
        var followTail = selectionLength == 0 && selectionStart == txtLog.TextLength;
        txtLog.AppendText(batch.ToString());
        var removed = 0;
        if (txtLog.TextLength > MaximumCharacters)
        {
            // Cut at a line start without copying the whole text out of the control
            var line = txtLog.GetLineFromCharIndex(txtLog.TextLength - RetainedCharacters);
            removed = txtLog.GetFirstCharIndexFromLine(line + 1);
            if (removed <= 0)
                removed = txtLog.TextLength - RetainedCharacters;

            txtLog.Select(0, removed);
            txtLog.SelectedText = string.Empty;
        }
        if (followTail)
        {
            txtLog.Select(txtLog.TextLength, 0);
            txtLog.ScrollToCaret();
        }
        else
        {
            var start = Math.Clamp(selectionStart - removed, 0, txtLog.TextLength);
            var end = Math.Clamp(selectionStart + selectionLength - removed, start, txtLog.TextLength);
            txtLog.Select(start, end - start);
        }
    }

    private void LoadConfig()
    {
        checkEnabled.Checked = GlobalConfig.Get("RSBot.Log.logEnabled", true);
    }

    private void checkEnabled_CheckedChanged(object sender, EventArgs e)
    {
        GlobalConfig.Set("RSBot.Log.logEnabled", checkEnabled.Checked.ToString());
    }

    private void btnReset_Click(object sender, EventArgs e)
    {
        lock (_queueLock)
        {
            _pending.Clear();
            _pendingCharacters = 0;
        }
        txtLog.Clear();
    }
}
