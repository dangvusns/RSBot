using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RSBot.Python.Components;
using SDUI;

namespace RSBot.Python.Views;

/// <summary>
///     The Python tab: the plugin list, a log, and one tab per plugin that creates a GUI.
///     Built in code; sizes are 96 DPI values scaled to the display.
/// </summary>
[ToolboxItem(false)]
public class Main : SDUI.Controls.DoubleBufferedControl
{
    private const int MaxLogLines = 500;

    private readonly System.Windows.Forms.TabControl _tabs;
    private readonly TabPage _pagePlugins;
    private readonly ListView _listPlugins;
    private readonly System.Windows.Forms.TextBox _txtLog;
    private readonly List<Action> _pendingUiActions = new();
    private readonly ConcurrentQueue<string> _pendingLogLines = new();
    private readonly System.Windows.Forms.Timer _logTimer;

    private bool _refreshing;
    private bool _pendingFlushed;

    public Main()
    {
        _tabs = new System.Windows.Forms.TabControl { Dock = DockStyle.Fill };
        _pagePlugins = new TabPage("Plugins") { UseVisualStyleBackColor = false, Padding = new Padding(Px(6)) };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = Px(36),
            WrapContents = false,
            Padding = new Padding(0, Px(2), 0, 0),
        };

        var btnRefresh = new SDUI.Controls.Button { Text = "Refresh list", Size = new Size(Px(100), Px(26)) };
        btnRefresh.Click += (_, _) => RefreshPlugins();

        var btnReload = new SDUI.Controls.Button { Text = "Reload enabled", Size = new Size(Px(110), Px(26)) };
        btnReload.Click += (_, _) => ReloadEnabled();

        var btnFolder = new SDUI.Controls.Button { Text = "Open folder", Size = new Size(Px(100), Px(26)) };
        btnFolder.Click += (_, _) => OpenFolder();

        var lblHint = new SDUI.Controls.Label
        {
            AutoSize = true,
            Text = "Tick a plugin to run it. Plugins are .py files in Data\\Python\\Plugins.",
            Margin = new Padding(Px(8), Px(6), 0, 0),
        };

        buttons.Controls.AddRange(new Control[] { btnRefresh, btnReload, btnFolder, lblHint });

        _listPlugins = new ListView
        {
            Dock = DockStyle.Fill,
            View = System.Windows.Forms.View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _listPlugins.Columns.Add("Name", Px(170));
        _listPlugins.Columns.Add("Description", Px(330));
        _listPlugins.Columns.Add("Author", Px(110));
        _listPlugins.Columns.Add("Version", Px(70));
        _listPlugins.Columns.Add("File", Px(150));
        _listPlugins.ItemChecked += OnPluginChecked;

        _txtLog = new System.Windows.Forms.TextBox
        {
            Dock = DockStyle.Bottom,
            Height = Px(170),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
        };

        var splitter = new Splitter { Dock = DockStyle.Bottom, Height = Px(4) };

        // Docking order: the fill control is added first so it takes the space left by the others
        _pagePlugins.Controls.Add(_listPlugins);
        _pagePlugins.Controls.Add(splitter);
        _pagePlugins.Controls.Add(_txtLog);
        _pagePlugins.Controls.Add(buttons);

        _tabs.TabPages.Add(_pagePlugins);
        Controls.Add(_tabs);

        ApplyTheme();
        ColorScheme.ThemeChanged += (_, _) => ApplyTheme();

        HandleCreated += (_, _) => FlushPendingUiActions();

        // Lines are added in batches; appending them one by one re-renders the text box for every line
        _logTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _logTimer.Tick += (_, _) => FlushLog();
        _logTimer.Start();
        Disposed += (_, _) => _logTimer.Dispose();
    }

    private int Px(int value)
    {
        return LogicalToDeviceUnits(value);
    }

    private void ApplyTheme()
    {
        foreach (Control control in new Control[] { _listPlugins, _txtLog, _pagePlugins })
        {
            control.BackColor = ColorScheme.BackColor;
            control.ForeColor = ColorScheme.ForeColor;
        }

        foreach (TabPage page in _tabs.TabPages)
        {
            page.BackColor = ColorScheme.BackColor;
            page.ForeColor = ColorScheme.ForeColor;
        }
    }

    #region Thread helpers

    /// <summary>
    ///     Runs the action on the UI thread without waiting. Actions queued before the window exists run once it does.
    /// </summary>
    public void RunOnUi(Action action)
    {
        if (IsDisposed)
            return;

        // Until the queued actions were handed to the window, new ones queue behind them to keep the order
        lock (_pendingUiActions)
        {
            if (!_pendingFlushed)
            {
                _pendingUiActions.Add(action);
                return;
            }
        }

        if (!InvokeRequired)
        {
            Safe(action);
            return;
        }

        try
        {
            BeginInvoke(new Action(() => Safe(action)));
        }
        catch (InvalidOperationException)
        {
            // The window is closing
        }
    }

    private void FlushPendingUiActions()
    {
        lock (_pendingUiActions)
        {
            foreach (var action in _pendingUiActions)
                BeginInvoke(new Action(() => Safe(action)));

            _pendingUiActions.Clear();
            _pendingFlushed = true;
        }
    }

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Core.Log.Error($"[Python] UI: {e.Message}");
        }
    }

    #endregion

    #region Log

    public void AppendLog(string text)
    {
        _pendingLogLines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {text.Replace("\n", Environment.NewLine)}");

        // Bound the queue while the tab is not shown
        while (_pendingLogLines.Count > MaxLogLines)
            _pendingLogLines.TryDequeue(out _);
    }

    private void FlushLog()
    {
        if (_pendingLogLines.IsEmpty || IsDisposed)
            return;

        var batch = new System.Text.StringBuilder();
        while (_pendingLogLines.TryDequeue(out var line))
        {
            if (batch.Length > 0 || _txtLog.TextLength > 0)
                batch.Append(Environment.NewLine);

            batch.Append(line);
        }

        _txtLog.AppendText(batch.ToString());

        // Keep the newest half once the limit is reached, cutting at a line start
        var lineCount = _txtLog.GetLineFromCharIndex(_txtLog.TextLength) + 1;
        if (lineCount > MaxLogLines)
        {
            var cut = _txtLog.GetFirstCharIndexFromLine(lineCount - MaxLogLines / 2);
            if (cut > 0)
            {
                _txtLog.Select(0, cut);
                _txtLog.SelectedText = string.Empty;
                _txtLog.Select(_txtLog.TextLength, 0);
                _txtLog.ScrollToCaret();
            }
        }
    }

    #endregion

    #region Plugin pages

    public void AddPluginPage(TabPage page)
    {
        _tabs.TabPages.Add(page);
    }

    public void RemovePluginPage(TabPage page)
    {
        _tabs.TabPages.Remove(page);
    }

    #endregion

    #region Plugin list

    /// <summary>
    ///     Rescans the plugin folder and shows which plugins are running.
    /// </summary>
    public void RefreshPlugins()
    {
        RunOnUi(() =>
        {
            List<PythonPluginInfo> plugins;
            try
            {
                plugins = PythonPluginManager.ScanPlugins();
            }
            catch (Exception e)
            {
                AppendLog($"Could not read the plugin folder: {e.Message}");
                return;
            }

            _refreshing = true;
            _listPlugins.BeginUpdate();
            try
            {
                _listPlugins.Items.Clear();
                foreach (var plugin in plugins)
                {
                    var item = new ListViewItem(
                        new[] { plugin.Name, plugin.Description, plugin.Author, plugin.Version, plugin.FileName }
                    )
                    {
                        Tag = plugin,
                        Checked = PythonPluginManager.IsLoaded(plugin.FileName),
                    };

                    _listPlugins.Items.Add(item);
                }
            }
            finally
            {
                _listPlugins.EndUpdate();
                _refreshing = false;
            }
        });
    }

    private void OnPluginChecked(object sender, ItemCheckedEventArgs e)
    {
        if (_refreshing || e.Item.Tag is not PythonPluginInfo plugin)
            return;

        if (!PythonPluginManager.IsInitialized)
        {
            AppendLog("Python is not available, see the messages above.");
            _refreshing = true;
            e.Item.Checked = false;
            _refreshing = false;
            return;
        }

        if (e.Item.Checked)
            PythonPluginManager.Load(
                plugin,
                loaded =>
                {
                    PythonPluginManager.SaveEnabledPlugins();
                    if (!loaded)
                        RefreshPlugins();
                }
            );
        else
            PythonPluginManager.Unload(plugin.FileName, PythonPluginManager.SaveEnabledPlugins);
    }

    /// <summary>
    ///     Unloads and loads the running plugins again, e.g. after editing them.
    /// </summary>
    private void ReloadEnabled()
    {
        var running = PythonPluginManager
            .ScanPlugins()
            .Where(p => PythonPluginManager.IsLoaded(p.FileName))
            .ToList();

        foreach (var plugin in running)
        {
            PythonPluginManager.Unload(plugin.FileName);
            PythonPluginManager.Load(PythonPluginInfo.Read(plugin.FilePath));
        }

        PythonWorker.Post(RefreshPlugins);
    }

    private void OpenFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(PythonPluginManager.PluginsDirectory);
            Process.Start("explorer.exe", PythonPluginManager.PluginsDirectory);
        }
        catch (Exception e)
        {
            AppendLog(e.Message);
        }
    }

    #endregion
}
