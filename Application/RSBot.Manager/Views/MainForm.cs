using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using RSBot.Manager.Models;
using RSBot.Manager.Services;

namespace RSBot.Manager.Views;

internal sealed class MainForm : Form
{
    private const string Empty = "—";
    private const string AllGroups = "(Tất cả nhóm)";

    private readonly List<BotInstance> _instances = new();
    private readonly Label _folderLabel;
    private readonly Label _totalsLabel;
    private readonly Label _launchLabel;
    private readonly ComboBox _groupFilter;
    private readonly DataGridView _grid;
    private readonly ContextMenuStrip _menu;
    private readonly Timer _timer;

    private bool _polling;
    private string _appliedGroup;
    private bool _refreshingGroups;

    /// <summary>
    ///     The bots the right-click menu acts on.
    /// </summary>
    private List<BotInstance> _menuTargets = new();

    public MainForm()
    {
        Theme.ApplyForm(this);

        Text = "RSBot Manager";
        // Designed at 100% scaling, never larger than the screen
        var screen = Screen.PrimaryScreen.WorkingArea;
        Size = new Size(
            Math.Min(Theme.Scale(this, 1400), screen.Width * 9 / 10),
            Math.Min(Theme.Scale(this, 820), screen.Height * 9 / 10)
        );
        MinimumSize = new Size(Theme.Scale(this, 900), Theme.Scale(this, 450));
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(Theme.Scale(this, 14));
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        // Header: bot folder and the actions for the checked bots
        _folderLabel = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Gold,
            Font = new Font("Segoe UI", 11f),
            Margin = new Padding(0, 10, 0, 14),
        };

        var openButton = Theme.CreateButton("Mở Bot", true);
        openButton.Click += (_, _) => RunOn(CheckedOrSelected(), LaunchAsync);

        var startButton = Theme.CreateButton("Bắt Đầu");
        startButton.Click += (_, _) => RunOn(CheckedOrSelected(), b => b.SendAsync("start"));

        var stopButton = Theme.CreateButton("Dừng");
        stopButton.Click += (_, _) => RunOn(CheckedOrSelected(), b => b.SendAsync("stop"));

        var hideButton = Theme.CreateButton("Ẩn");
        hideButton.Click += (_, _) => RunOn(CheckedOrSelected(), b => SetWindowsVisibleAsync(b, false));

        var showButton = Theme.CreateButton("Hiện");
        showButton.Click += (_, _) => RunOn(CheckedOrSelected(), b => SetWindowsVisibleAsync(b, true));

        _groupFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat,
            Width = Theme.Scale(this, 170),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(Theme.Scale(this, 10), 0, 0, 0),
        };
        _groupFilter.SelectedIndexChanged += (_, _) =>
        {
            if (!_refreshingGroups)
                ApplyGroupFilter();
        };

        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        actions.Controls.AddRange(new Control[] { openButton, startButton, stopButton, hideButton, showButton, _groupFilter });

        var left = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Dock = DockStyle.Left,
            WrapContents = false,
        };
        left.Controls.Add(_folderLabel);
        left.Controls.Add(actions);

        var folderButton = Theme.CreateButton("Chọn thư mục bot");
        folderButton.Click += (_, _) => ChooseBotFolder();

        var right = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0),
        };
        var settingsButton = Theme.CreateButton("Cài đặt");
        settingsButton.Click += (_, _) => EditSettings();

        right.Controls.AddRange(new Control[] { folderButton, settingsButton });

        var header = new Panel { Dock = DockStyle.Top, Height = Theme.Scale(this, 112) };
        header.Controls.Add(left);
        header.Controls.Add(right);

        // Footer: totals and account editing
        _totalsLabel = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Gold,
            Font = Theme.Total,
            Dock = DockStyle.Left,
            Padding = new Padding(12, 18, 0, 0),
        };

        var addButton = Theme.CreateButton("+ Thêm");
        addButton.Click += (_, _) => AddAccount();

        var editButton = Theme.CreateButton("Sửa");
        editButton.Click += (_, _) => EditAccount();

        var deleteButton = Theme.CreateButton("Xóa");
        deleteButton.Click += (_, _) => DeleteAccount();

        var footerButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0),
        };
        footerButtons.Controls.AddRange(new Control[] { addButton, editButton, deleteButton });

        // Launch queue progress, between the totals and the buttons
        _launchLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(Theme.Scale(this, 16), 0, 0, 0),
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.Scale(this, 64), BackColor = Theme.Panel };
        footer.Controls.Add(_totalsLabel);
        footer.Controls.Add(footerButtons);
        footer.Controls.Add(_launchLabel);
        _launchLabel.BringToFront(); // docked last, so it fills the space left by the others

        _grid = new DataGridView { Dock = DockStyle.Fill };
        Theme.ApplyGrid(_grid);
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
        _grid.CellMouseDown += Grid_CellMouseDown;
        _grid.CellPainting += Grid_CellPainting;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex > 0)
                EditAccount();
        };

        _menu = CreateMenu();

        Controls.Add(_grid);
        Controls.Add(footer);
        Controls.Add(header);

        _timer = new Timer { Interval = 1000 };
        _timer.Tick += async (_, _) => await PollAsync();

        Load += (_, _) =>
        {
            ManagerStore.LoadSettings();
            ReloadAccounts();
            _timer.Start();
        };
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            foreach (var instance in _instances)
                instance.Dispose();
        };
    }

    #region Grid

    private void BuildColumns()
    {
        _grid.Columns.Clear();

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "check",
            HeaderText = "☐",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Width = Theme.Scale(_grid, 44),
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });

        AddTextColumn("account", "Tài khoản", 140, 130);
        AddTextColumn("group", "Nhóm", 100, 80);
        AddTextColumn("character", "Nhân vật", 140, 130);
        AddTextColumn("state", "Trạng thái", 150, 140);
        AddTextColumn("hpmp", "HP / MP", 170, 160);
        AddTextColumn("online", "Online", 80, 70);
        AddTextColumn("greenhour", "Giờ Xanh", 150, 140);
        AddTextColumn("gold", "Gold hiện có", 120, 110);
        AddTextColumn("goldPicked", "Gold nhặt", 110, 100);
        AddTextColumn("elixirs", "LKD", 80, 60);
        AddTextColumn("tablets", "Tấm lót", 90, 70);
        AddTextColumn("equipment", "Trang bị", 90, 70);
    }

    /// <summary>
    ///     Adds a column that fills the free width but never gets narrower than its header text
    ///     or <paramref name="minimumWidth" /> (at 100% scaling). The grid scrolls when they do not fit.
    /// </summary>
    private void AddTextColumn(string name, string header, int weight, int minimumWidth)
    {
        var headerWidth = TextRenderer.MeasureText(header, Theme.Bold).Width + Theme.Scale(_grid, 28);

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = weight,
            MinimumWidth = Math.Max(headerWidth, Theme.Scale(_grid, minimumWidth)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
    }

    private void ReloadAccounts()
    {
        foreach (var instance in _instances)
            instance.Dispose();

        _instances.Clear();
        _grid.Rows.Clear();

        _folderLabel.Text = ManagerStore.HasValidBotFolder
            ? ManagerStore.BotFolder
            : "Chưa chọn thư mục bot (thư mục có RSBot.exe)";

        BuildColumns();

        foreach (var account in ManagerStore.Data.Accounts)
            AddRow(new BotInstance(account));

        RefreshGroups();
        RefreshGrid();
    }

    /// <summary>
    ///     Fills the group filter with the groups of the accounts and keeps the selected one when it still exists.
    /// </summary>
    private void RefreshGroups()
    {
        var selected = _groupFilter.SelectedItem as string;
        var groups = ManagerStore.Data.Accounts
            .Select(a => a.Group)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Refilling the list changes the selection twice; filter once at the end
        _refreshingGroups = true;
        try
        {
            _groupFilter.BeginUpdate();
            _groupFilter.Items.Clear();
            _groupFilter.Items.Add(AllGroups);
            foreach (var group in groups)
                _groupFilter.Items.Add(group);
            _groupFilter.EndUpdate();

            _groupFilter.Visible = groups.Count > 0;
            _groupFilter.SelectedItem = selected != null && _groupFilter.Items.Contains(selected) ? selected : AllGroups;
        }
        finally
        {
            _refreshingGroups = false;
        }

        ApplyGroupFilter();
    }

    /// <summary>
    ///     Shows only the accounts of the selected group. Hidden rows are left out of every action.
    /// </summary>
    private void ApplyGroupFilter()
    {
        if (_grid.Columns.Count == 0)
            return;

        var group = _groupFilter.SelectedItem as string;
        var all = group == null || group == AllGroups;
        var changed = group != _appliedGroup;
        _appliedGroup = group;

        bool Shows(DataGridViewRow row) =>
            all || string.Equals(((BotInstance)row.Tag).Account.Group, group, StringComparison.OrdinalIgnoreCase);

        // The current row cannot be hidden
        if (_grid.CurrentRow != null && !Shows(_grid.CurrentRow))
            _grid.CurrentCell = null;

        _grid.EndEdit();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            row.Visible = Shows(row);

            // Another group starts with nothing checked, and a hidden row never stays checked
            if (changed || !row.Visible)
                row.Cells["check"].Value = false;
        }

        if (changed)
            _grid.Columns[0].HeaderText = "☐";
    }

    private void AddRow(BotInstance instance)
    {
        _instances.Add(instance);

        var index = _grid.Rows.Add();
        _grid.Rows[index].Tag = instance;
        _grid.Rows[index].Cells["check"].Value = false;
    }

    private async Task PollAsync()
    {
        if (_polling || !ManagerStore.HasValidBotFolder)
            return;

        _polling = true;
        try
        {
            await Task.WhenAll(_instances.ToArray().Select(i => i.PollAsync()));

            RefreshGrid();
        }
        finally
        {
            _polling = false;
        }
    }

    private void RefreshGrid()
    {
        if (WindowState == FormWindowState.Minimized)
            return;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not BotInstance instance)
                continue;

            var status = instance.Status;
            var inGame = status?.State is "InGame" or "Running";

            Set(row, "account", instance.Account.LoginId);
            Set(row, "group", instance.Account.Group ?? string.Empty);
            Set(row, "character", status?.CharName ?? instance.Account.Character);

            var state = row.Cells["state"];
            var stateText = DescribeState(instance);
            var stateColor = status == null ? Theme.Muted
                : status.State == "Running" ? Theme.Good
                : Theme.Text;
            if (status == null && !instance.Account.CanReadPassword)
            {
                stateText = "Cần nhập lại mật khẩu";
                stateColor = Theme.Orange;
            }
            Set(row, "state", stateText);
            if (state.Style.ForeColor != stateColor)
                state.Style.ForeColor = stateColor;
            var toolTip = string.Join(
                Environment.NewLine,
                new[] { instance.LastError, instance.RestartInfo }.Where(t => t != null)
            );
            if (state.ToolTipText != toolTip)
                state.ToolTipText = toolTip;

            // The bars are drawn in Grid_CellPainting; the text only changes the cell so it is repainted
            Set(row, "hpmp", inGame ? $"{status.Hp}/{status.MaxHp}/{status.Mp}/{status.MaxMp}" : Empty);
            Set(row, "online", status != null ? FormatDuration(status.UptimeSeconds) : Empty);
            var (greenText, greenColor) = DescribeGreenHour(status);
            var greenHour = row.Cells["greenhour"];
            Set(row, "greenhour", greenText);
            if (greenHour.Style.ForeColor != greenColor)
                greenHour.Style.ForeColor = greenColor;
            Set(row, "gold", inGame ? status.Gold.ToString("N0") : Empty);
            Set(row, "goldPicked", status != null ? status.GoldPicked.ToString("N0") : Empty);
            Set(row, "elixirs", status != null ? status.ElixirsPicked.ToString("N0") : Empty);
            Set(row, "tablets", status != null ? status.TabletsPicked.ToString("N0") : Empty);
            Set(row, "equipment", status != null ? status.EquipmentPicked.ToString("N0") : Empty);
        }

        RefreshTotals();
        RefreshLaunchQueue();
    }

    private void RefreshTotals()
    {
        var statuses = _instances.Select(i => i.Status).Where(s => s != null).ToList();
        var online = statuses.Count(s => s.State is "InGame" or "Running");
        var running = statuses.Count(s => s.State == "Running");

        var parts = new[]
        {
            $"Đang chạy: {running}/{_instances.Count}",
            $"Online: {online}",
            $"Gold hiện có: {statuses.Aggregate(0UL, (sum, s) => sum + s.Gold):N0}",
            $"Gold nhặt: {statuses.Sum(s => s.GoldPicked):N0}",
            $"LKD: {statuses.Sum(s => s.ElixirsPicked):N0}",
            $"Tấm lót: {statuses.Sum(s => s.TabletsPicked):N0}",
            $"Trang bị: {statuses.Sum(s => s.EquipmentPicked):N0}",
        };

        _totalsLabel.Text = string.Join("    ", parts);
    }

    private void RefreshLaunchQueue()
    {
        var queued = BotInstance.QueuedLaunches;
        var wait = (int)Math.Ceiling((BotInstance.NextLaunchAt - DateTime.Now).TotalSeconds);

        _launchLabel.Text = queued > 0
            ? $"Đang chờ mở {queued} bot · bot tiếp theo sau {Math.Max(wait, 0)} giây"
            : string.Empty;
    }

    private static void Set(DataGridViewRow row, string column, string value)
    {
        var cell = row.Cells[column];
        if (!Equals(cell.Value, value))
            cell.Value = value;
    }

    private static string DescribeState(BotInstance instance)
    {
        var status = instance.Status;
        if (status == null && instance.RestartAt is { } restartAt)
            return $"Tự mở lại sau {Math.Max(0, (int)Math.Ceiling((restartAt - DateTime.Now).TotalSeconds))} giây";

        if (status == null)
            return instance.IsStarting ? "Đang mở..."
                : instance.LastError != null ? "Lỗi (xem chú thích)"
                : "Chưa chạy";

        var text = status.State switch
        {
            "NotLoggedIn" => "Chưa đăng nhập",
            "LoggingIn" => "Đang đăng nhập",
            "Running" => "Đang chạy",
            "InGame" => "Đã dừng",
            _ => status.State,
        };

        return status.Clientless ? text + " · Clientless" : text;
    }

    /// <summary>
    ///     Green time ("Xanh"): full experience. Orange time ("Cam"): half experience after the green hours.
    ///     Red ("Đỏ"): both are used up. Uses the server's fatigue time when it sends one, otherwise the
    ///     time the bot counted in game since the daily reset and the hours from the settings.
    /// </summary>
    private static (string Text, Color Color) DescribeGreenHour(BotStatus status)
    {
        if (status?.GreenSecondsLeft != null)
            return status.GreenSecondsLeft > 0
                ? ($"Xanh · còn {FormatDuration(status.GreenSecondsLeft.Value)}", Theme.Good)
                : ("Cam", Theme.Orange);

        if (status?.PlayedSecondsToday == null)
            return (Empty, Theme.Muted);

        var played = status.PlayedSecondsToday.Value;
        var green = ManagerStore.Data.GreenHours * 3600L;
        var orange = green + ManagerStore.Data.OrangeHours * 3600L;

        if (played < green)
            return ($"Xanh · còn {FormatDuration(green - played)}", Theme.Good);

        if (played < orange)
            return ($"Cam · còn {FormatDuration(orange - played)}", Theme.Orange);

        return ("Đỏ · hết giờ", Theme.Bad);
    }

    private static string FormatDuration(long seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);

        return $"{(int)time.TotalHours}:{time.Minutes:00}";
    }

    /// <summary>
    ///     Draws HP and MP as two bars with their values instead of text.
    /// </summary>
    private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "hpmp")
            return;

        var status = (_grid.Rows[e.RowIndex].Tag as BotInstance)?.Status;
        if (status?.State is not ("InGame" or "Running"))
            return; // default painting shows the "—"

        e.PaintBackground(e.CellBounds, true);

        var padding = Theme.Scale(_grid, 8);
        var gap = Theme.Scale(_grid, 3);
        var barHeight = Math.Min(Theme.Scale(_grid, 14), (e.CellBounds.Height - padding - gap) / 2);
        var width = e.CellBounds.Width - padding * 2;
        var top = e.CellBounds.Top + (e.CellBounds.Height - barHeight * 2 - gap) / 2;

        var hp = new Rectangle(e.CellBounds.Left + padding, top, width, barHeight);
        var mp = new Rectangle(hp.Left, hp.Bottom + gap, width, barHeight);

        DrawBar(e.Graphics, hp, status.Hp, status.MaxHp, Theme.HpBar);
        DrawBar(e.Graphics, mp, status.Mp, status.MaxMp, Theme.MpBar);

        e.Handled = true;
    }

    private static void DrawBar(Graphics graphics, Rectangle bounds, int value, int maximum, Color color)
    {
        using (var track = new SolidBrush(Theme.BarTrack))
            graphics.FillRectangle(track, bounds);

        var ratio = maximum > 0 ? Math.Clamp(value / (double)maximum, 0, 1) : 0;
        var filled = new Rectangle(bounds.Left, bounds.Top, (int)(bounds.Width * ratio), bounds.Height);
        if (filled.Width > 0)
            using (var fill = new SolidBrush(color))
                graphics.FillRectangle(fill, filled);

        using (var border = new Pen(Theme.Border))
            graphics.DrawRectangle(border, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);

        TextRenderer.DrawText(
            graphics,
            $"{value:N0} / {maximum:N0}",
            Theme.BarText,
            bounds,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
        );
    }

    private void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex != 0 || _grid.Rows.Count == 0)
            return;

        _grid.EndEdit();

        // Header checkbox: check all, or uncheck all when everything is checked
        var visible = _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible).ToList();
        var checkAll = visible.Any(r => !IsChecked(r));
        foreach (var row in visible)
            row.Cells["check"].Value = checkAll;

        _grid.Columns[0].HeaderText = checkAll ? "☑" : "☐";
    }

    private void Grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0)
            return;

        var row = _grid.Rows[e.RowIndex];
        _grid.ClearSelection();
        row.Selected = true;
        _grid.CurrentCell = row.Cells[Math.Max(e.ColumnIndex, 1)];

        // A checked row acts on all checked rows, an unchecked row only on itself
        _menuTargets = IsChecked(row) ? CheckedInstances() : new List<BotInstance> { (BotInstance)row.Tag };

        _menu.Show(Cursor.Position);
    }

    private static bool IsChecked(DataGridViewRow row) => row.Cells["check"].Value is true;

    private List<BotInstance> CheckedInstances() =>
        _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible && IsChecked(r)).Select(r => (BotInstance)r.Tag).ToList();

    private List<BotInstance> CheckedOrSelected()
    {
        var targets = CheckedInstances();
        if (targets.Count == 0 && _grid.CurrentRow is { Visible: true, Tag: BotInstance selected })
            targets.Add(selected);

        return targets;
    }

    private BotInstance SelectedInstance => _grid.CurrentRow?.Tag as BotInstance;

    #endregion

    #region Actions

    private ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip();
        Theme.ApplyMenu(menu);

        void AddItem(string text, Func<BotInstance, Task> action) =>
            menu.Items.Add(text, null, (_, _) => RunOn(_menuTargets, action));

        AddItem("Mở Bot", LaunchAsync);
        AddItem("Bắt Đầu", b => b.SendAsync("start"));
        AddItem("Dừng", b => b.SendAsync("stop"));
        menu.Items.Add(new ToolStripSeparator());
        AddItem("Hiện Bot", b => b.SendAsync("showBot"));
        AddItem("Ẩn Bot", b => b.SendAsync("hideBot"));
        menu.Items.Add(new ToolStripSeparator());
        AddItem("Hiện Client", b => b.SendAsync("showClient"));
        AddItem("Ẩn Client", b => b.SendAsync("hideClient"));
        AddItem("Go Clientless", b => b.SendAsync("goClientless"));
        AddItem("Go Client", b => b.SendAsync("goClient"));
        menu.Items.Add(new ToolStripSeparator());
        AddItem("Lấy tọa độ", b => b.SendAsync("setAreaHere"));
        menu.Items.Add("Chọn tọa độ", null, (_, _) => ChooseArea(_menuTargets));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Tắt Bot", null, (_, _) => CloseBots(_menuTargets));

        return menu;
    }

    /// <summary>
    ///     Hides or shows the bot window and, when it runs, the game client.
    /// </summary>
    private static async Task SetWindowsVisibleAsync(BotInstance instance, bool visible)
    {
        await instance.SendAsync(visible ? "showBot" : "hideBot");

        if (instance.Status?.ClientRunning == true)
            await instance.SendAsync(visible ? "showClient" : "hideClient");
    }

    private Task LaunchAsync(BotInstance instance)
    {
        if (!ManagerStore.HasValidBotFolder)
            throw new InvalidOperationException("Chưa chọn thư mục bot");

        return instance.LaunchAsync();
    }

    /// <summary>
    ///     Runs the action on every bot at the same time and lists the bots where it failed.
    /// </summary>
    private async void RunOn(List<BotInstance> targets, Func<BotInstance, Task> action)
    {
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Chọn ít nhất một tài khoản.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var errors = new List<string>();
        await Task.WhenAll(targets.Select(async target =>
        {
            try
            {
                await action(target);
            }
            catch (Exception ex)
            {
                lock (errors)
                    errors.Add($"{target.Account.LoginId}: {ex.Message}");
            }
        }));

        RefreshGrid();

        if (errors.Count > 0)
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ChooseArea(List<BotInstance> targets)
    {
        var current = targets.Select(t => t.Status).FirstOrDefault(s => s != null);

        using var dialog = new AreaDialog(current?.PosX ?? 0, current?.PosY ?? 0);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var args = new { x = dialog.X, y = dialog.Y, radius = dialog.Radius };
        RunOn(targets, b => b.SendAsync("setArea", args));
    }

    private void CloseBots(List<BotInstance> targets)
    {
        var names = string.Join(", ", targets.Select(t => t.Account.LoginId));
        if (MessageBox.Show(this, $"Tắt bot và client của: {names}?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            != DialogResult.Yes)
            return;

        RunOn(targets, b => b.CloseAsync());
    }

    private void ChooseBotFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Chọn thư mục có RSBot.exe",
            UseDescriptionForTitle = true,
            InitialDirectory = ManagerStore.BotFolder ?? string.Empty,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        if (!File.Exists(Path.Combine(dialog.SelectedPath, "RSBot.exe")))
        {
            MessageBox.Show(this, "Không tìm thấy RSBot.exe trong thư mục này.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ManagerStore.SetBotFolder(dialog.SelectedPath);
        ReloadAccounts();
    }

    private void EditSettings()
    {
        using var dialog = new SettingsDialog(ManagerStore.Data);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        ManagerStore.SaveData();
        RefreshGrid();
    }

    private bool RequireBotFolder()
    {
        if (ManagerStore.HasValidBotFolder)
            return true;

        MessageBox.Show(this, "Chọn thư mục bot trước.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void AddAccount()
    {
        if (!RequireBotFolder())
            return;

        using var dialog = new AccountDialog(null, ManagerStore.Data.Accounts);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            ProfileWriter.Write(dialog.Account);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không ghi được profile: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        ManagerStore.Data.Accounts.Add(dialog.Account);
        ManagerStore.SaveData();

        AddRow(new BotInstance(dialog.Account));
        RefreshGroups();
        RefreshGrid();
    }

    private void EditAccount()
    {
        var instance = SelectedInstance;
        if (instance == null || !RequireBotFolder())
            return;

        // Edit a copy so a failed profile write leaves the account unchanged
        var account = instance.Account;
        var copy = new ManagerAccount
        {
            LoginId = account.LoginId,
            PasswordProtected = account.PasswordProtected,
            Character = account.Character,
            Server = account.Server,
            TemplateProfile = account.TemplateProfile,
            Clientless = account.Clientless,
            Group = account.Group,
            AutoRestart = account.AutoRestart,
        };

        using var dialog = new AccountDialog(copy, ManagerStore.Data.Accounts);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            ProfileWriter.Write(copy);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không ghi được profile: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        account.PasswordProtected = copy.PasswordProtected;
        account.Character = copy.Character;
        account.Server = copy.Server;
        account.Clientless = copy.Clientless;
        account.Group = copy.Group;
        account.AutoRestart = copy.AutoRestart;

        ManagerStore.SaveData();
        RefreshGroups();
        RefreshGrid();

        if (instance.IsConnected)
            MessageBox.Show(this, "Bot đang chạy, thay đổi có hiệu lực ở lần mở bot sau.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void DeleteAccount()
    {
        var instance = SelectedInstance;
        if (instance == null)
            return;

        if (instance.IsConnected)
        {
            MessageBox.Show(this, "Tắt bot trước khi xóa tài khoản.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Xóa tài khoản {instance.Account.LoginId} khỏi danh sách?\n\n"
                + "Có: xóa luôn profile RSBot (cấu hình, nhân vật)\n"
                + "Không: chỉ xóa khỏi danh sách, giữ profile",
            Text,
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question
        );
        if (answer == DialogResult.Cancel)
            return;

        try
        {
            ProfileWriter.Remove(instance.Account, answer == DialogResult.Yes);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không xóa được profile: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        ManagerStore.Data.Accounts.Remove(instance.Account);
        ManagerStore.SaveData();

        _grid.Rows.Remove(_grid.Rows.Cast<DataGridViewRow>().First(r => r.Tag == instance));
        _instances.Remove(instance);
        instance.Dispose();

        RefreshGroups();
        RefreshGrid();
    }

    #endregion
}
