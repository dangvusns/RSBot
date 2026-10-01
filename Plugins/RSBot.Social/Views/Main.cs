using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using RSBot.Core.Components.Tracing;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Exchange;
using RSBot.Core.Objects.Spawn;
using RSBot.Social.Bundle;

namespace RSBot.Social.Views;

[ToolboxItem(false)]
public partial class Main : SDUI.Controls.DoubleBufferedControl
{
    /// <summary>
    ///     The prefix of the trace tuning keys, shared with the chat commands of the Party plugin.
    /// </summary>
    private const string TraceConfigPrefix = "RSBot.Party.Trace.";

    private readonly List<Action> _translations = new();
    private SDUI.Controls.Button _sendWhisper;
    private SDUI.Controls.Label _lblRecipient;
    private SDUI.Controls.Label _lblSaved;
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
    private SDUI.Controls.Label _lblGuild;
    private SDUI.Controls.Label _lblNotice;
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
        CheckForIllegalCrossThreadCalls = false;
        InitializeComponent();

        BuildPlayersTab();
        BuildGuildTab();
        BuildExchangeTab();

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
        var top = new SDUI.Controls.Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.Transparent };
        _lblPlayers = new SDUI.Controls.Label { Location = new Point(8, 10), Size = new Size(300, 20), Text = "Players around: 0" };

        var refresh = new SDUI.Controls.Button { Location = new Point(320, 6), Size = new Size(90, 26), Text = "Refresh", Radius = 6 };
        refresh.Click += (s, e) => RefreshPlayers();

        Localize(refresh, "Refresh", "Refresh");
        _lblPlayers.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _lblPlayers.Name = "PlayersCount";
        refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        top.Resize += (s, e) =>
        {
            refresh.Left = Math.Max(8, top.ClientSize.Width - refresh.Width - 8);
            _lblPlayers.Width = Math.Max(20, refresh.Left - 16);
        };
        top.Controls.Add(_lblPlayers);
        top.Controls.Add(refresh);

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
        _listEquipment.Height = 150;

        var whisper = new SDUI.Controls.Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Color.Transparent };
        _txtWhisper = new TextBox { Name = "WhisperMessage", Location = new Point(8, 7), Size = new Size(400, 23) };

        _sendWhisper = new SDUI.Controls.Button { Location = new Point(416, 5), Size = new Size(110, 26), Text = "Whisper", Radius = 6 };
        _sendWhisper.Click += (s, e) => SendWhisper();

        whisper.Controls.Add(_txtWhisper);
        Localize(_sendWhisper, "Whisper", "Whisper");
        _lblRecipient = new SDUI.Controls.Label { Name = "Recipient", Dock = DockStyle.Bottom, Height = 30 };
        whisper.Controls.Add(_sendWhisper);
        whisper.Controls.Add(_lblRecipient);
        whisper.Resize += (s, e) =>
        {
            _sendWhisper.Left = Math.Max(8, whisper.ClientSize.Width - _sendWhisper.Width - 8);
            _txtWhisper.Width = Math.Max(20, _sendWhisper.Left - 16);
        };
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

        menu.Items.Add(new ToolStripMenuItem("Trace (game)", null, (s, e) => WithSelected(p => StartTrace(p, TraceMode.GameTrace))));
        menu.Items.Add(new ToolStripMenuItem("Trace (stand on the player)", null, (s, e) => WithSelected(p => StartTrace(p, TraceMode.Smart))));
        menu.Items.Add(new ToolStripMenuItem("Stop trace", null, (s, e) => TraceManager.Stop()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Invite to party", null, (s, e) => WithSelected(p => Game.Party.Invite(p.UniqueId))));
        menu.Items.Add(new ToolStripMenuItem("Invite to guild", null, (s, e) => WithSelected(p => GuildManager.Invite(p.UniqueId))));
        menu.Items.Add(new ToolStripMenuItem("Invite to exchange", null, (s, e) => WithSelected(p => ExchangeInstance.Invite(p.UniqueId))));

        var keys = new[] { "TraceGame", "TraceSmart", "StopTrace", "InviteParty", "InviteGuild", "InviteExchange" };
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

        var players = entities.OrderBy(p => p.DistanceToPlayer).ToList();
        var selected = GetSelectedUniqueId();

        _refreshingPlayers = true;
        _listPlayers.BeginUpdate();
        try
        {
            _listPlayers.Items.Clear();

            foreach (var player in players)
            {
                var item = new ListViewItem(player.Name ?? string.Empty) { Tag = player.UniqueId };
                item.SubItems.Add(player.Guild?.Name ?? string.Empty);
                item.SubItems.Add(player.Job == JobType.None ? string.Empty : player.Job.ToString());
                item.SubItems.Add(player.PvpCape == PvpFlag.None ? string.Empty : player.PvpCape.ToString());
                item.SubItems.Add(player.Stall?.Name ?? string.Empty);
                item.SubItems.Add(Math.Round(player.DistanceToPlayer).ToString("0"));

                _listPlayers.Items.Add(item);
                if (selected == player.UniqueId)
                    item.Selected = true;
            }
        }
        finally
        {
            _listPlayers.EndUpdate();
            _refreshingPlayers = false;
        }
        _lblPlayers.Text = string.Format(TextFor("PlayersCount", "Players around: {0}"), players.Count);
        RefreshEquipment();
        UpdateWhisperState();
    }

    private void RefreshEquipment()
    {
        _listEquipment.BeginUpdate();
        try
        {
            _listEquipment.Items.Clear();

            var player = GetSelectedPlayer();
            if (player != null)
            {
                AddEquipment(player.Inventory, string.Empty);
                AddEquipment(player.Avatars, TextFor("AvatarPrefix", "Avatar: "));
            }
        }
        finally { _listEquipment.EndUpdate(); }
    }

    private void AddEquipment(Dictionary<RefObjItem, byte> items, string prefix)
    {
        if (items == null)
            return;

        foreach (var pair in items.ToArray())
        {
            var item = new ListViewItem(prefix + pair.Key.GetRealName());
            item.SubItems.Add(pair.Value > 0 ? "+" + pair.Value : string.Empty);

            _listEquipment.Items.Add(item);
        }
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

    private static void StartTrace(SpawnedPlayer player, TraceMode mode)
    {
        // Like the traceme command: the bot would walk the character away
        if (mode == TraceMode.Smart && Kernel.Bot?.Running == true)
            Kernel.Bot.Stop();

        var options = mode == TraceMode.Smart ? TraceOptions.Overlap() : TraceOptions.Close();

        TraceManager.Start(player.Name, mode, options.ApplyConfig(TraceConfigPrefix), player);
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
        var top = new SDUI.Controls.Panel { Dock = DockStyle.Top, Height = 80, BackColor = Color.Transparent };
        _lblGuild = new SDUI.Controls.Label { Dock = DockStyle.Top, Height = 40, Text = "No guild data yet" };
        _lblNotice = new SDUI.Controls.Label { Dock = DockStyle.Fill, Text = string.Empty };

        _lblGuild.Name = "GuildSummary";
        _lblNotice.Name = "GuildNotice";
        top.Controls.Add(_lblNotice);
        top.Controls.Add(_lblGuild);

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
            Location = new Point(8, 8),
            Size = new Size(520, 250),
            Padding = new Padding(4, 12, 4, 4),
            Radius = 10,
            ShadowDepth = 4,
        };

        var label = new SDUI.Controls.Label { Location = new Point(16, 32), Size = new Size(480, 20), Text = "When a player invites me to an exchange:" };

        _comboExchangeMode = new SDUI.Controls.ComboBox { Name = "ExchangeMode", DrawMode = DrawMode.OwnerDrawFixed, Location = new Point(16, 56), Size = new Size(480, 23), DropDownStyle = ComboBoxStyle.DropDownList };
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

        var hint = new SDUI.Controls.Label
        {
            Location = new Point(16, 180),
            Size = new Size(480, 50),
            Text = "Items are never added automatically. Check the offer of the partner before you enable auto approve.",
        };

        group.Controls.Add(label);
        group.Controls.Add(_comboExchangeMode);
        group.Controls.Add(_checkAutoConfirm);
        group.Controls.Add(_checkAutoApprove);
        group.Controls.Add(hint);

        _lblSaved = new SDUI.Controls.Label { Name = "SaveStatus", AutoSize = true,
            Text = "Changes apply immediately and are saved automatically." };
        group.Controls.Add(_lblSaved);
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
        Action layout = () =>
        {
            group.Width = Math.Max(280, tabExchange.ClientSize.Width - 32);
            var width = group.Width - 32;
            var y = 32;
            foreach (var control in new Control[] { label, _comboExchangeMode, _checkAutoConfirm, _checkAutoApprove, hint, _lblSaved })
            {
                control.AutoSize = false;
                control.SetBounds(16, y, width, control == _comboExchangeMode ? _comboExchangeMode.PreferredHeight : Math.Max(30, control.GetPreferredSize(new Size(width, 0)).Height + 8));
                y += control.Height + 8;
            }
            group.Height = y + 8;
        };
        tabExchange.Resize += (s, e) => layout();
        _lblSaved.TextChanged += (s, e) => layout();
        group.TextChanged += (s, e) => layout();
        _translations.Add(layout);
        tabExchange.Controls.Add(group);
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
        if (!Visible || !Enabled || !Game.Ready || Game.Player == null)
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
        foreach (var list in new[] { _listPlayers, _listEquipment, _listGuild })
        {
            list.BackColor = SDUI.ColorScheme.BackColor;
            list.ForeColor = SDUI.ColorScheme.ForeColor;
        }
        _txtWhisper.BackColor = SDUI.ColorScheme.BackColor;
        _txtWhisper.ForeColor = SDUI.ColorScheme.ForeColor;
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
            var header = list.Columns.Add(column.Text, column.Width);
            var key = "Column" + new string(column.Text.Where(char.IsLetterOrDigit).ToArray());
            _translations.Add(() => header.Text = TextFor(key, column.Text));
        }

        return list;
    }
}
