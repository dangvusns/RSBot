using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows.Forms;
using RSBot.Core;
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
        foreach (var check in new[] { checkEnabled, checkNormal, checkDebug, checkWarning, checkError })
            check.CheckedChanged += (s, e) =>
            {
                UpdateFilters();
                SaveConfig();
            };
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

        // Debug lines are only built while someone shows or writes them
        RSBot.Core.Log.ShowDebug = checkEnabled.Checked && checkDebug.Checked;
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
        checkDebug.Checked = GlobalConfig.Get("RSBot.Log.ShowDebug", false);
        checkNormal.Checked = GlobalConfig.Get("RSBot.Log.ShowNormal", true);
        checkWarning.Checked = GlobalConfig.Get("RSBot.Log.ShowWarning", true);
        checkError.Checked = GlobalConfig.Get("RSBot.Log.ShowError", true);
    }

    private void SaveConfig()
    {
        GlobalConfig.Set("RSBot.Log.logEnabled", checkEnabled.Checked);
        GlobalConfig.Set("RSBot.Log.ShowDebug", checkDebug.Checked);
        GlobalConfig.Set("RSBot.Log.ShowNormal", checkNormal.Checked);
        GlobalConfig.Set("RSBot.Log.ShowWarning", checkWarning.Checked);
        GlobalConfig.Set("RSBot.Log.ShowError", checkError.Checked);
    }

    private void checkEnabled_CheckedChanged(object sender, EventArgs e)
    {
        // Saved with the other filters
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
