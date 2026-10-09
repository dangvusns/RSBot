using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using RSBot.Core.Components.Tracing;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Exchange;
using RSBot.Core.Objects.Spawn;
using RSBot.Social.Bundle;
using Action = System.Action;

namespace RSBot.Social.Views;

[ToolboxItem(false)]
public partial class Main : SDUI.Controls.DoubleBufferedControl
{
    /// <summary>
    ///     The prefix of the trace tuning keys, shared with the chat commands of the Party plugin.
    /// </summary>
    private const string TraceConfigPrefix = "RSBot.Party.Trace.";

    private readonly UiEventSubscriptions _uiEvents;
    private readonly List<Action> _translations = new();
    private SDUI.Controls.Button _sendWhisper;
    private System.Windows.Forms.Label _lblRecipient;
    private System.Windows.Forms.Label _lblSaved;
    private string _feedbackKey;
    private string _feedbackDefault;
    private string _feedbackArgument;
    private bool _refreshingPlayers;

    private readonly Timer _timer = new() { Interval = 2000 };

    private SDUI.Controls.ListView _listPlayers;
    private SDUI.Controls.ListView _listEquipment;
    private SDUI.Controls.Label _lblPlayers;
    private TextBox _txtWhisper;

    private SDUI.Controls.ListView _listGuild;
    private System.Windows.Forms.Label _lblGuild;
    private System.Windows.Forms.Label _lblNotice;
    private GuildInfo _shownGuild;

    private SDUI.Controls.ComboBox _comboExchangeMode;
    private SDUI.Controls.CheckBox _checkAutoConfirm;
    private SDUI.Controls.CheckBox _checkAutoApprove;
    private bool _loadingSettings;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Main" /> class.
    /// </summary>
    public Main()
    {
        InitializeComponent();
        _uiEvents = new UiEventSubscriptions(this);

        BuildPlayersTab();
        BuildGuildTab();
        BuildExchangeTab();
        BuildExchangeWindow();

        // Refreshes only the visible tab, on the UI thread
        _timer.Tick += (s, e) => RefreshVisibleTab();
        components ??= new Container();
        components.Add(_timer);
        VisibleChanged += (s, e) => UpdateRefreshTimer();
        EnabledChanged += (s, e) => UpdateRefreshTimer();
        tabMain.SelectedIndexChanged += (s, e) => UpdateRefreshTimer();
        BackColorChanged += (s, e) => ApplyTheme();
        ApplyLanguage();
        ApplyTheme();
        UpdateWhisperState();
        UpdateRefreshTimer();
    }

    /// <summary>
    ///     Loads the settings of the current character.
    /// </summary>
    public void LoadSettings()
    {
        _loadingSettings = true;
        try
        {
            var mode = PlayerConfig.Get(ExchangeAutomation.ModeKey, (int)ExchangeMode.Manual);
            _comboExchangeMode.SelectedIndex = mode >= 0 && mode < _comboExchangeMode.Items.Count ? mode : 0;
            _checkAutoConfirm.Checked = PlayerConfig.Get(ExchangeAutomation.AutoConfirmKey, false);
            _checkAutoApprove.Checked = PlayerConfig.Get(ExchangeAutomation.AutoApproveKey, false);
            _checkShowRequests.Checked = PlayerConfig.Get(ShowRequestsKey, true);
        }
        finally
        {
            _loadingSettings = false;
        }

        // Another character might have another guild
        _shownGuild = null;
        if (Visible) RefreshVisibleTab();
    }

    #region Players

    private void BuildPlayersTab()
    {
        var top = CreateLayout(2);
        top.Dock = DockStyle.Top;
        top.Padding = new Padding(Px(8));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _lblPlayers = new SDUI.Controls.Label
        {
            Name = "PlayersCount", Dock = DockStyle.Fill, Text = "Players around: 0",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var refresh = new SDUI.Controls.Button
        {
            AutoSize = true, MinimumSize = new Size(Px(90), Px(30)), Text = "Refresh", Radius = 6,
        };
        refresh.Click += (s, e) => RefreshPlayers();
        Localize(refresh, "Refresh", "Refresh");
        top.Controls.Add(_lblPlayers, 0, 0);
        top.Controls.Add(refresh, 1, 0);

        _listPlayers = CreateList(("Name", 140), ("Guild", 140), ("Job", 70), ("PvP cape", 70), ("Stall", 150), ("Distance", 70));
        _listPlayers.Name = "PlayersList";
        _listPlayers.SelectedIndexChanged += (s, e) =>
        {
            if (_refreshingPlayers) return;
            _feedbackKey = null;
            RefreshEquipment();
            UpdateWhisperState();
        };
        _listPlayers.ContextMenuStrip = CreatePlayerMenu();

        _listEquipment = CreateList(("Equipment of the selected player", 360), ("Plus", 60));
        _listEquipment.Name = "EquipmentList";
        _listEquipment.Dock = DockStyle.Bottom;
        _listEquipment.Height = Px(150);

        var whisper = CreateLayout(2);
        whisper.Dock = DockStyle.Bottom;
        whisper.Padding = new Padding(Px(8));
        whisper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        whisper.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        whisper.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        whisper.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _txtWhisper = new TextBox
        {
            Name = "WhisperMessage", Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };
        _sendWhisper = new SDUI.Controls.Button
        {
            AutoSize = true, MinimumSize = new Size(Px(110), Px(30)), Text = "Whisper", Radius = 6,
        };
        _sendWhisper.Click += (s, e) => SendWhisper();
        Localize(_sendWhisper, "Whisper", "Whisper");
        _lblRecipient = CreateWrappingLabel("Recipient");
        whisper.Controls.Add(_txtWhisper, 0, 0);
        whisper.Controls.Add(_sendWhisper, 1, 0);
        whisper.Controls.Add(_lblRecipient, 0, 1);
        whisper.SetColumnSpan(_lblRecipient, 2);
        SizeWrappingLabels(whisper, _lblRecipient);
        _txtWhisper.TextChanged += (s, e) => UpdateWhisperState();
        _txtWhisper.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            SendWhisper();
        };

        // Docking order: the last added control is docked first
        tabPlayers.Controls.Add(_listPlayers);
        tabPlayers.Controls.Add(_listEquipment);
        tabPlayers.Controls.Add(whisper);
        tabPlayers.Controls.Add(top);
    }

    private SDUI.Controls.ContextMenuStrip CreatePlayerMenu()
    {
        var menu = new SDUI.Controls.ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem("Trace (stand on the player)", null, (s, e) => WithSelected(p => StartTrace(p))));
        menu.Items.Add(new ToolStripMenuItem("Stop trace", null, (s, e) => TraceManager.Stop()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Invite to party", null, (s, e) => WithSelected(p => Game.Party.Invite(p.UniqueId))));
        menu.Items.Add(new ToolStripMenuItem("Invite to guild", null, (s, e) => WithSelected(p => GuildManager.Invite(p.UniqueId))));
        menu.Items.Add(new ToolStripMenuItem("Invite to exchange", null, (s, e) => WithSelected(p => ExchangeInstance.Invite(p.UniqueId))));

        var keys = new[] { "TraceSmart", "StopTrace", "InviteParty", "InviteGuild", "InviteExchange" };
        var index = 0;
        foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
        {
            var key = keys[index++];
            var fallback = item.Text;
            item.Name = key;
            _translations.Add(() => item.Text = TextFor(key, fallback));
        }
        menu.Opening += (s, e) =>
        {
            foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
                item.Enabled = Game.Ready && (item.Name == "StopTrace" || GetSelectedPlayer() != null);
        };
        components ??= new Container();
        components.Add(menu);
        return menu;
    }

    private void RefreshPlayers()
    {
        if (!SpawnManager.TryGetEntities<SpawnedPlayer>(out var entities) || entities == null)
            entities = Enumerable.Empty<SpawnedPlayer>();

        var players = entities.ToList();
        var current = players.Select(player => player.UniqueId).ToHashSet();
        var rows = _listPlayers.Items.Cast<ListViewItem>().ToDictionary(item => (uint)item.Tag);
        _refreshingPlayers = true;
        _listPlayers.BeginUpdate();
        try
        {
            foreach (var row in rows)
                if (!current.Contains(row.Key)) _listPlayers.Items.Remove(row.Value);

            foreach (var player in players.Where(player => !rows.ContainsKey(player.UniqueId)).OrderBy(player => player.DistanceToPlayer))
            {
                var item = new ListViewItem(new string[6]) { Tag = player.UniqueId };
                _listPlayers.Items.Add(item);
                rows.Add(player.UniqueId, item);
            }
            foreach (var player in players)
            {
                var item = rows[player.UniqueId];
                var values = new[] {
                    player.Name ?? string.Empty,
                    player.Guild?.Name ?? string.Empty,
                    player.Job == JobType.None ? string.Empty : player.Job.ToString(),
                    player.PvpCape == PvpFlag.None ? string.Empty : player.PvpCape.ToString(),
                    player.Stall?.Name ?? string.Empty,
                    Math.Round(player.DistanceToPlayer).ToString("0"),
                };
                for (var index = 0; index < values.Length; index++)
                    if (item.SubItems[index].Text != values[index]) item.SubItems[index].Text = values[index];
            }
        }
        finally
        {
            _listPlayers.EndUpdate();
            _refreshingPlayers = false;
        }
        var countText = string.Format(TextFor("PlayersCount", "Players around: {0}"), players.Count);
        if (_lblPlayers.Text != countText) _lblPlayers.Text = countText;
        RefreshEquipment();
        UpdateWhisperState();
    }

    private void RefreshEquipment()
    {
        var player = GetSelectedPlayer();
        var equipment = new List<(string Name, string Plus)>();
        void Add(Dictionary<RefObjItem, byte> items, string prefix)
        {
            if (items == null) return;
            foreach (var pair in items.ToArray())
                equipment.Add((prefix + pair.Key.GetRealName(), pair.Value > 0 ? "+" + pair.Value : string.Empty));
        }
        if (player != null)
        {
            Add(player.Inventory, string.Empty);
            Add(player.Avatars, TextFor("AvatarPrefix", "Avatar: "));
        }
        if (_listEquipment.Items.Count == equipment.Count && equipment.Select((entry, index) =>
            _listEquipment.Items[index].Text == entry.Name &&
            _listEquipment.Items[index].SubItems[1].Text == entry.Plus).All(same => same))
            return;

        _listEquipment.BeginUpdate();
        try
        {
            _listEquipment.Items.Clear();
            foreach (var entry in equipment)
                _listEquipment.Items.Add(new ListViewItem(new[] { entry.Name, entry.Plus }));
        }
        finally { _listEquipment.EndUpdate(); }
    }

    private void SendWhisper()
    {
        var text = _txtWhisper.Text.Trim();
        if (!Game.Ready || text.Length == 0 || _listPlayers.SelectedItems.Count == 0)
        {
            SetFeedback("SelectRecipient", "Select a player and enter a message.");
            return;
        }

        // Works also when the player walked out of sight meanwhile
        var name = _listPlayers.SelectedItems[0].Text;

        try
        {
            ChatManager.SendPrivate(name, text);
            _txtWhisper.Clear();
            SetFeedback("WhisperSent", "Whisper sent to {0}.", name);
        }
        catch (Exception e)
        {
            Log.Fatal(e);
            SetFeedback("ActionFailed", "Action failed. See the Log tab for details.");
        }
    }

    private static void StartTrace(SpawnedPlayer player)
    {
        // Like the traceme command: the bot would walk the character away
        if (Kernel.Bot?.Running == true)
            Kernel.Bot.Stop();

        TraceManager.Start(player.Name, TraceMode.Smart, TraceOptions.Commander(TraceConfigPrefix), player);
    }

    private void WithSelected(Action<SpawnedPlayer> action)
    {
        var player = GetSelectedPlayer();
        if (player == null)
        {
            SetFeedback("PlayerGone", "The selected player is no longer nearby.");
            Log.Warn("[Social] The selected player is not around anymore");
            return;
        }

        try
        {
            action(player);
            SetFeedback("ActionSent", "Request sent for {0}.", player.Name);
        }
        catch (Exception e)
        {
            Log.Fatal(e);
            SetFeedback("ActionFailed", "Action failed. See the Log tab for details.");
        }
    }

    private uint GetSelectedUniqueId()
    {
        if (_listPlayers.SelectedItems.Count == 0 || _listPlayers.SelectedItems[0].Tag is not uint uniqueId)
            return 0;

        return uniqueId;
    }

    private SpawnedPlayer GetSelectedPlayer()
    {
        var uniqueId = GetSelectedUniqueId();
        if (uniqueId == 0)
            return null;

        return SpawnManager.TryGetEntity<SpawnedPlayer>(uniqueId, out var player) ? player : null;
    }

    #endregion

    #region Guild

    private void BuildGuildTab()
    {
        var top = CreateLayout(1);
        top.Dock = DockStyle.Top;
        top.Padding = new Padding(Px(8));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _lblGuild = CreateWrappingLabel("GuildSummary", "No guild data yet");
        _lblNotice = CreateWrappingLabel("GuildNotice");
        top.Controls.Add(_lblGuild, 0, 0);
        top.Controls.Add(_lblNotice, 0, 1);
        SizeWrappingLabels(top, _lblGuild, _lblNotice);

        _listGuild = CreateList(("Name", 140), ("Nickname", 120), ("Level", 60), ("GP", 90), ("Status", 70), ("Rank", 80));

        _listGuild.Name = "GuildList";
        tabGuild.Controls.Add(_listGuild);
        tabGuild.Controls.Add(top);
    }

    private void RefreshGuild()
    {
        var guild = GuildManager.Guild;
        if (ReferenceEquals(guild, _shownGuild) && _listGuild.Items.Count > 0)
            return;

        _shownGuild = guild;

        _listGuild.BeginUpdate();
        try
        {
            _listGuild.Items.Clear();

            if (guild == null)
            {
                _lblGuild.Text = TextFor("NoGuild", "No guild data yet (received after joining the game)");
                _lblNotice.Text = string.Empty;
            }
            else
            {
                var online = guild.Members.Count(m => m.IsOnline);

                _lblGuild.Text = string.Format(TextFor("GuildSummary", "{0} - Level {1} - {2} GP - {3}/{4} online at login"), guild.Name, guild.Level, guild.GatheredPoints, online, guild.Members.Count);
                _lblNotice.Text = string.IsNullOrEmpty(guild.NoticeTitle) ? string.Empty : string.Format(TextFor("Notice", "Notice: {0}"), guild.NoticeTitle);

                foreach (var member in guild.Members.OrderByDescending(m => m.IsOnline).ThenBy(m => m.Name))
                {
                    var item = new ListViewItem(member.Name ?? string.Empty);
                    item.SubItems.Add(member.Nickname ?? string.Empty);
                    item.SubItems.Add(member.Level.ToString());
                    item.SubItems.Add(member.GatheredPoints.ToString());
                    item.SubItems.Add(member.IsOnline ? TextFor("Online", "Online") : TextFor("Offline", "Offline"));
                    item.SubItems.Add(member.IsMaster ? TextFor("Master", "Master") : string.Empty);

                    if (!member.IsOnline)
                        item.ForeColor = Color.Gray;

                    _listGuild.Items.Add(item);
                }
            }
        }
        finally { _listGuild.EndUpdate(); }
    }

    #endregion

    #region Exchange

    private void BuildExchangeTab()
    {
        var group = new SDUI.Controls.GroupBox
        {
            Name = "ExchangeInvitations",
            Text = "Exchange invitations",
            Dock = DockStyle.Top,
            Padding = new Padding(Px(16), Px(32), Px(16), Px(16)),
            Radius = 10,
            ShadowDepth = 0,
        };

        var label = CreateWrappingLabel("ExchangePrompt", "When a player invites me to an exchange:");

        _comboExchangeMode = new SDUI.Controls.ComboBox { Name = "ExchangeMode", DrawMode = DrawMode.OwnerDrawFixed, Location = new Point(Px(16), Px(56)), Size = new Size(Px(480), Px(23)), DropDownStyle = ComboBoxStyle.DropDownList };
        _comboExchangeMode.Items.AddRange(
            new object[]
            {
                "Do nothing (answer it yourself)",
                "Refuse all invitations",
                "Accept all invitations",
                "Accept only from the commander list (Party > commands)",
            }
        );
        _comboExchangeMode.SelectedIndex = 0;
        _comboExchangeMode.SelectedIndexChanged += (s, e) => SaveSettings();

        _checkAutoConfirm = CreateCheck("Confirm when the partner confirmed", 100);
        _checkAutoApprove = CreateCheck("Approve when both sides confirmed", 135);

        var hint = CreateWrappingLabel("ExchangeHint",
            "Items are never added automatically. Check the offer of the partner before you enable auto approve.");
        _lblSaved = CreateWrappingLabel("SaveStatus",
            "Changes apply immediately and are saved automatically.");
        var modeDescription = CreateWrappingLabel("ExchangeModeDescription");
        // It only repeated the selected mode under the drop-down
        modeDescription.Visible = false;
        var content = CreateLayout(1);
        content.Dock = DockStyle.Top;
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var row = 0;
        foreach (var control in new Control[] { label, _comboExchangeMode, modeDescription,
            _checkAutoConfirm, _checkAutoApprove, hint, _lblSaved })
        {
            control.Margin = new Padding(0, 0, 0, Px(8));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(control, 0, row++);
        }
        _comboExchangeMode.Dock = DockStyle.Top;
        group.Controls.Add(content);
        SizeWrappingLabels(content, label, modeDescription, hint, _lblSaved);
        _comboExchangeMode.SelectedIndexChanged += (s, e) =>
            modeDescription.Text = _comboExchangeMode.SelectedItem?.ToString() ?? string.Empty;
        Action sizeCombo = () =>
        {
            _comboExchangeMode.ItemHeight = Math.Max(Px(22), _comboExchangeMode.Font.Height + Px(8));
            var widest = _comboExchangeMode.Items.Cast<object>()
                .Select(item => TextRenderer.MeasureText(item.ToString(), _comboExchangeMode.Font).Width)
                .DefaultIfEmpty(0).Max();
            _comboExchangeMode.DropDownWidth = Math.Max(_comboExchangeMode.Width, widest + Px(32));
        };
        _comboExchangeMode.FontChanged += (s, e) => sizeCombo();
        _comboExchangeMode.SizeChanged += (s, e) => sizeCombo();
        _translations.Add(() => sizeCombo());

        Localize(group, "ExchangeInvitations", "Exchange invitations");
        Localize(label, "ExchangePrompt", label.Text);
        Localize(_checkAutoConfirm, "AutoConfirm", _checkAutoConfirm.Text);
        Localize(_checkAutoApprove, "AutoApprove", _checkAutoApprove.Text);
        Localize(hint, "ExchangeHint", hint.Text);
        _translations.Add(() =>
        {
            var selected = _comboExchangeMode.SelectedIndex;
            var loading = _loadingSettings;
            _loadingSettings = true;
            try
            {
                _comboExchangeMode.Items.Clear();
                _comboExchangeMode.Items.AddRange(new object[] {
                    TextFor("ModeManual", "Do nothing (answer it yourself)"),
                    TextFor("ModeRefuse", "Refuse all invitations"),
                    TextFor("ModeAccept", "Accept all invitations"),
                    TextFor("ModeCommanders", "Accept only from the commander list (Party > commands)") });
                _comboExchangeMode.SelectedIndex = selected;
            }
            finally { _loadingSettings = loading; }
        });
        tabExchange.AutoScroll = true;
        tabExchange.Padding = new Padding(Px(8));
        // Checkboxes are single-line controls: allow horizontal scrolling at very narrow widths.
        Action layout = () =>
        {
            var minimumWidth = Math.Max(_checkAutoConfirm.PreferredSize.Width,
                _checkAutoApprove.PreferredSize.Width) + group.Padding.Horizontal;
            group.MinimumSize = new Size(minimumWidth, 0);
            group.Height = content.PreferredSize.Height + group.Padding.Vertical;
        };
        content.SizeChanged += (s, e) => layout();
        tabExchange.FontChanged += (s, e) => layout();
        _translations.Add(() =>
        {
            modeDescription.Text = _comboExchangeMode.SelectedItem?.ToString() ?? string.Empty;
            sizeCombo();
            layout();
        });
        tabExchange.Controls.Add(group);
        layout();
    }

    private SDUI.Controls.CheckBox CreateCheck(string text, int y)
    {
        var check = new SDUI.Controls.CheckBox
        {
            AutoSize = true,
            Location = new Point(16, y),
            Text = text,
            UseVisualStyleBackColor = false,
        };
        check.CheckedChanged += (s, e) => SaveSettings();

        return check;
    }

    private void SaveSettings()
    {
        if (_loadingSettings)
            return;

        PlayerConfig.Set(ExchangeAutomation.ModeKey, _comboExchangeMode.SelectedIndex);
        PlayerConfig.Set(ExchangeAutomation.AutoConfirmKey, _checkAutoConfirm.Checked);
        PlayerConfig.Set(ExchangeAutomation.AutoApproveKey, _checkAutoApprove.Checked);
        try
        {
            PlayerConfig.Save();
            _lblSaved.Text = TextFor("SavedImmediately", "Changes apply immediately and are saved automatically.");
        }
        catch (Exception e)
        {
            _lblSaved.Text = TextFor("SaveFailed", "Could not save settings. See the Log tab.");
            Log.Fatal(e);
        }
    }

    #endregion

    private void RefreshVisibleTab()
    {
        if (!Visible || !Enabled || FindForm()?.WindowState == FormWindowState.Minimized || !Game.Ready || Game.Player == null)
            return;

        try
        {
            if (tabMain.SelectedIndex == 0)
                RefreshPlayers();
            else if (tabMain.SelectedIndex == 1)
                RefreshGuild();
        }
        catch (Exception e)
        {
            Log.Fatal(e);
        }
    }


    private static TableLayoutPanel CreateLayout(int columns) => new()
    {
        ColumnCount = columns,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
    };

    private static System.Windows.Forms.Label CreateWrappingLabel(string name, string text = "") => new()
    {
        Name = name,
        Text = text,
        AutoSize = true,
        Dock = DockStyle.Top,
        BackColor = Color.Transparent,
        UseMnemonic = false,
        Margin = new Padding(0, 4, 0, 4),
    };

    private static void SizeWrappingLabels(TableLayoutPanel layout, params System.Windows.Forms.Label[] labels)
    {
        var updating = false;
        void UpdateWidths()
        {
            if (updating) return;
            updating = true;
            try
            {
                foreach (var label in labels)
                {
                    var width = Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal - label.Margin.Horizontal);
                    var maximum = new Size(width, 0);
                    if (label.MaximumSize != maximum) label.MaximumSize = maximum;
                }
            }
            finally { updating = false; }
        }
        layout.SizeChanged += (s, e) => UpdateWidths();
        UpdateWidths();
    }

    private static string TextFor(string key, string fallback) =>
        LanguageManager.GetLangBySpecificKey("RSBot.Social", key, fallback);

    private void Localize(Control control, string key, string fallback)
    {
        control.Name = key;
        _translations.Add(() => control.Text = TextFor(key, fallback));
    }

    public void ApplyLanguage()
    {
        tabPlayers.Text = TextFor("Players", "Players");
        tabGuild.Text = TextFor("Guild", "Guild");
        tabExchange.Text = TextFor("Exchange", "Exchange");
        foreach (var translate in _translations) translate();
        _lblPlayers.Text = string.Format(TextFor("PlayersCount", "Players around: {0}"), _listPlayers.Items.Count);
        _lblGuild.Text = TextFor("NoGuild", "No guild data yet (received after joining the game)");
        if (_lblSaved != null) _lblSaved.Text = TextFor("SavedImmediately", "Changes apply immediately and are saved automatically.");
        _shownGuild = null;
        UpdateWhisperState();
        RefreshVisibleTab();
    }

    private void UpdateRefreshTimer()
    {
        if (IsDisposed || Disposing) return;
        _timer.Enabled = !IsDisposed && Visible && Enabled && tabMain.SelectedIndex != 2;
        if (_timer.Enabled) RefreshVisibleTab();
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (_txtWhisper == null) return;
        foreach (var page in new[] { tabPlayers, tabGuild, tabExchange })
        {
            page.BackColor = SDUI.ColorScheme.BackColor;
            page.ForeColor = SDUI.ColorScheme.ForeColor;
        }
        foreach (var list in new[] { _listPlayers, _listEquipment, _listGuild, _listExchangePlayers, _listExchangeInventory, _listMyOffer, _listPartnerOffer }.Where(l => l != null))
        {
            list.BackColor = SDUI.ColorScheme.BackColor;
            list.ForeColor = SDUI.ColorScheme.ForeColor;
        }
        ApplyLabelTheme(this);
        _txtWhisper.BackColor = SDUI.ColorScheme.BackColor;
        _txtWhisper.ForeColor = SDUI.ColorScheme.ForeColor;
    }

    private static void ApplyLabelTheme(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is System.Windows.Forms.Label)
                control.ForeColor = SDUI.ColorScheme.ForeColor;
            ApplyLabelTheme(control);
        }
    }

    private void SetFeedback(string key, string fallback, string argument = null)
    {
        _feedbackKey = key;
        _feedbackDefault = fallback;
        _feedbackArgument = argument;
        UpdateWhisperState();
    }

    private void UpdateWhisperState()
    {
        if (_sendWhisper == null || _lblRecipient == null) return;
        var selected = _listPlayers.SelectedItems.Count > 0 ? _listPlayers.SelectedItems[0].Text : null;
        _sendWhisper.Enabled = Game.Ready && selected != null && !string.IsNullOrWhiteSpace(_txtWhisper.Text);
        _lblRecipient.Text = _feedbackKey != null
            ? string.Format(TextFor(_feedbackKey, _feedbackDefault), _feedbackArgument)
            : selected == null ? TextFor("SelectRecipient", "Select a player and enter a message.")
            : string.Format(TextFor("Recipient", "Whisper to: {0} | Right-click the player for trace and invite actions."), selected);
    }

    /// <summary>
    ///     Converts 96 DPI pixels to the current display: this view is built in code and is not auto-scaled.
    /// </summary>
    private int Px(int value) => LogicalToDeviceUnits(value);

    private SDUI.Controls.ListView CreateList(params (string Text, int Width)[] columns)
    {
        var list = new SDUI.Controls.ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            MultiSelect = false,
            View = System.Windows.Forms.View.Details,
            BorderStyle = BorderStyle.None,
            BackColor = SDUI.ColorScheme.BackColor,
            ForeColor = SDUI.ColorScheme.ForeColor,
            UseCompatibleStateImageBehavior = false,
        };

        foreach (var column in columns)
        {
            var header = list.Columns.Add(column.Text, Px(column.Width));
            var key = "Column" + new string(column.Text.Where(char.IsLetterOrDigit).ToArray());
            _translations.Add(() => header.Text = TextFor(key, column.Text));
        }

        return list;
    }
}
