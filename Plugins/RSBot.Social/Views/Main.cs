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

    private readonly Timer _timer = new() { Interval = 2000 };

    private SDUI.Controls.ListView _listPlayers;
    private SDUI.Controls.ListView _listEquipment;
    private SDUI.Controls.Label _lblPlayers;
    private TextBox _txtWhisper;

    private SDUI.Controls.ListView _listGuild;
    private SDUI.Controls.Label _lblGuild;
    private SDUI.Controls.Label _lblNotice;
    private GuildInfo _shownGuild;

    private ComboBox _comboExchangeMode;
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
        _timer.Start();
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
    }

    #region Players

    private void BuildPlayersTab()
    {
        var top = new SDUI.Controls.Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.Transparent };
        _lblPlayers = new SDUI.Controls.Label { Location = new Point(8, 10), Size = new Size(300, 20), Text = "Players around: 0" };

        var refresh = new SDUI.Controls.Button { Location = new Point(320, 6), Size = new Size(90, 26), Text = "Refresh", Radius = 6 };
        refresh.Click += (s, e) => RefreshPlayers();

        top.Controls.Add(_lblPlayers);
        top.Controls.Add(refresh);

        _listPlayers = CreateList(("Name", 140), ("Guild", 140), ("Job", 70), ("PvP cape", 70), ("Stall", 150), ("Distance", 70));
        _listPlayers.SelectedIndexChanged += (s, e) => RefreshEquipment();
        _listPlayers.ContextMenuStrip = CreatePlayerMenu();

        _listEquipment = CreateList(("Equipment of the selected player", 360), ("Plus", 60));
        _listEquipment.Dock = DockStyle.Bottom;
        _listEquipment.Height = 150;

        var whisper = new SDUI.Controls.Panel { Dock = DockStyle.Bottom, Height = 36, BackColor = Color.Transparent };
        _txtWhisper = new TextBox { Location = new Point(8, 7), Size = new Size(400, 23) };

        var send = new SDUI.Controls.Button { Location = new Point(416, 5), Size = new Size(110, 26), Text = "Whisper", Radius = 6 };
        send.Click += (s, e) => SendWhisper();

        whisper.Controls.Add(_txtWhisper);
        whisper.Controls.Add(send);

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

        return menu;
    }

    private void RefreshPlayers()
    {
        if (!SpawnManager.TryGetEntities<SpawnedPlayer>(out var entities) || entities == null)
            entities = Enumerable.Empty<SpawnedPlayer>();

        var players = entities.OrderBy(p => p.DistanceToPlayer).ToList();
        var selected = GetSelectedUniqueId();

        _listPlayers.BeginUpdate();
        _listPlayers.Items.Clear();

        foreach (var player in players)
        {
            var item = new ListViewItem(player.Name ?? string.Empty) { Tag = player.UniqueId };
            item.SubItems.Add(player.Guild?.Name ?? string.Empty);
            item.SubItems.Add(player.Job == JobType.None ? string.Empty : player.Job.ToString());
            item.SubItems.Add(player.PvpCape == PvpFlag.None ? string.Empty : player.PvpCape.ToString());
            item.SubItems.Add(player.Stall?.Name ?? string.Empty);
            item.SubItems.Add(Math.Round(player.DistanceToPlayer).ToString("0"));

            if (selected == player.UniqueId)
                item.Selected = true;

            _listPlayers.Items.Add(item);
        }

        _listPlayers.EndUpdate();
        _lblPlayers.Text = $"Players around: {players.Count}";
    }

    private void RefreshEquipment()
    {
        _listEquipment.BeginUpdate();
        _listEquipment.Items.Clear();

        var player = GetSelectedPlayer();
        if (player != null)
        {
            AddEquipment(player.Inventory, string.Empty);
            AddEquipment(player.Avatars, "Avatar: ");
        }

        _listEquipment.EndUpdate();
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
        if (text.Length == 0 || _listPlayers.SelectedItems.Count == 0)
            return;

        // Works also when the player walked out of sight meanwhile
        var name = _listPlayers.SelectedItems[0].Text;

        try
        {
            ChatManager.SendPrivate(name, text);
            _txtWhisper.Clear();
        }
        catch (Exception e)
        {
            Log.Fatal(e);
        }
    }

    private static void StartTrace(SpawnedPlayer player, TraceMode mode)
    {
        // Like the traceme command: the bot would walk the character away
        if (mode == TraceMode.Smart && Kernel.Bot.Running)
            Kernel.Bot.Stop();

        var options = mode == TraceMode.Smart ? TraceOptions.Overlap() : TraceOptions.Close();

        TraceManager.Start(player.Name, mode, options.ApplyConfig(TraceConfigPrefix), player);
    }

    private void WithSelected(Action<SpawnedPlayer> action)
    {
        var player = GetSelectedPlayer();
        if (player == null)
        {
            Log.Warn("[Social] The selected player is not around anymore");
            return;
        }

        try
        {
            action(player);
        }
        catch (Exception e)
        {
            Log.Fatal(e);
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
        var top = new SDUI.Controls.Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.Transparent };
        _lblGuild = new SDUI.Controls.Label { Location = new Point(8, 8), Size = new Size(690, 20), Text = "No guild data yet" };
        _lblNotice = new SDUI.Controls.Label { Location = new Point(8, 34), Size = new Size(690, 20), Text = string.Empty };

        top.Controls.Add(_lblGuild);
        top.Controls.Add(_lblNotice);

        _listGuild = CreateList(("Name", 140), ("Nickname", 120), ("Level", 60), ("GP", 90), ("Status", 70), ("Rank", 80));

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
        _listGuild.Items.Clear();

        if (guild == null)
        {
            _lblGuild.Text = "No guild data yet (the server sends it after joining the game)";
            _lblNotice.Text = string.Empty;
        }
        else
        {
            var online = guild.Members.Count(m => m.IsOnline);

            _lblGuild.Text = $"{guild.Name}  -  Level {guild.Level}  -  {guild.GatheredPoints} GP  -  {online}/{guild.Members.Count} online at login";
            _lblNotice.Text = string.IsNullOrEmpty(guild.NoticeTitle) ? string.Empty : $"Notice: {guild.NoticeTitle}";

            foreach (var member in guild.Members.OrderByDescending(m => m.IsOnline).ThenBy(m => m.Name))
            {
                var item = new ListViewItem(member.Name ?? string.Empty);
                item.SubItems.Add(member.Nickname ?? string.Empty);
                item.SubItems.Add(member.Level.ToString());
                item.SubItems.Add(member.GatheredPoints.ToString());
                item.SubItems.Add(member.IsOnline ? "Online" : "Offline");
                item.SubItems.Add(member.IsMaster ? "Master" : string.Empty);

                if (!member.IsOnline)
                    item.ForeColor = Color.Gray;

                _listGuild.Items.Add(item);
            }
        }

        _listGuild.EndUpdate();
    }

    #endregion

    #region Exchange

    private void BuildExchangeTab()
    {
        var group = new SDUI.Controls.GroupBox
        {
            Text = "Exchange invitations",
            Location = new Point(8, 8),
            Size = new Size(520, 250),
            Padding = new Padding(4, 12, 4, 4),
            Radius = 10,
            ShadowDepth = 4,
        };

        var label = new SDUI.Controls.Label { Location = new Point(16, 32), Size = new Size(480, 20), Text = "When a player invites me to an exchange:" };

        _comboExchangeMode = new ComboBox { Location = new Point(16, 56), Size = new Size(480, 23), DropDownStyle = ComboBoxStyle.DropDownList };
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
        PlayerConfig.Save();
    }

    #endregion

    private void RefreshVisibleTab()
    {
        if (!Visible || !Game.Ready || Game.Player == null)
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

    private static SDUI.Controls.ListView CreateList(params (string Text, int Width)[] columns)
    {
        var list = new SDUI.Controls.ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            MultiSelect = false,
            View = System.Windows.Forms.View.Details,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            UseCompatibleStateImageBehavior = false,
        };

        foreach (var column in columns)
            list.Columns.Add(column.Text, column.Width);

        return list;
    }
}
