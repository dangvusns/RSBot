using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Exchange;
using RSBot.Core.Objects.Spawn;
using Action = System.Action;

namespace RSBot.Social.Views;

/// <summary>
///     The exchange window of the Exchange tab: invite a nearby player, put items and gold in, see the partner's offer,
///     then confirm, approve or cancel.
/// </summary>
public partial class Main
{
    private SDUI.Controls.CheckBox _checkShowRequests;
    private System.Windows.Forms.Label _lblRequest;
    private SDUI.Controls.Button _btnAcceptRequest;
    private SDUI.Controls.Button _btnRefuseRequest;

    private SDUI.Controls.ListView _listExchangePlayers;
    private SDUI.Controls.ListView _listExchangeInventory;
    private SDUI.Controls.ListView _listMyOffer;
    private SDUI.Controls.ListView _listPartnerOffer;
    private TextBox _txtMyGold;
    private System.Windows.Forms.Label _lblPartnerGold;
    private System.Windows.Forms.Label _lblExchangeStatus;

    private SDUI.Controls.Button _btnInvite;
    private SDUI.Controls.Button _btnPutItem;
    private SDUI.Controls.Button _btnTakeItem;
    private SDUI.Controls.Button _btnSetGold;
    private SDUI.Controls.Button _btnConfirm;
    private SDUI.Controls.Button _btnApprove;
    private SDUI.Controls.Button _btnCancel;

    private const string ShowRequestsKey = "RSBot.Social.Exchange.ShowRequests";

    private readonly List<SDUI.Controls.Button> _exchangeButtons = new();

    /// <summary>
    ///     The last error, kept on screen after the server closed the exchange because of it.
    /// </summary>
    private string _lastExchangeError;

    private bool _exchangeConfirmed;
    private bool _exchangePartnerConfirmed;

    /// <summary>
    ///     Builds the exchange window below the invitation settings of the Exchange tab.
    /// </summary>
    private void BuildExchangeWindow()
    {
        var window = new TableLayoutPanel
        {
            Name = "ExchangeWindow",
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 6,
            BackColor = Color.Transparent,
            Padding = new Padding(0, Px(8), 0, 0),
            MinimumSize = new Size(Px(760), Px(380)),
        };
        window.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        window.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        window.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(64)));
        window.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        window.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        window.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // requests
        window.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // headers
        window.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // lists
        window.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // refresh, gold
        window.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // confirm, approve, cancel
        window.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // status

        // Row 0: incoming requests
        _checkShowRequests = new SDUI.Controls.CheckBox { AutoSize = true, Text = "Show requests", UseVisualStyleBackColor = false };
        Localize(_checkShowRequests, "ShowRequests", "Show requests");
        _checkShowRequests.CheckedChanged += (s, e) =>
        {
            if (!_loadingSettings)
            {
                PlayerConfig.Set(ShowRequestsKey, _checkShowRequests.Checked);
                PlayerConfig.Save();
            }
            UpdateRequestRow();
        };
        _lblRequest = new System.Windows.Forms.Label { AutoSize = true, UseMnemonic = false, Margin = new Padding(Px(12), Px(6), Px(8), 0) };
        _btnAcceptRequest = CreateExchangeButton("AcceptRequest", "Accept", () => AnswerRequest(true));
        _btnRefuseRequest = CreateExchangeButton("RefuseRequest", "Refuse", () => AnswerRequest(false));
        var requests = CreateFlow(_checkShowRequests, _lblRequest, _btnAcceptRequest, _btnRefuseRequest);
        window.Controls.Add(requests, 0, 0);
        window.SetColumnSpan(requests, 5);

        // Row 1: headers
        window.Controls.Add(CreateHeader("HeaderPlayers", "Players"), 0, 1);
        window.Controls.Add(CreateHeader("HeaderInventory", "Inventory"), 1, 1);
        window.Controls.Add(CreateHeader("HeaderMyItems", "My items"), 3, 1);
        window.Controls.Add(CreateHeader("HeaderPartnerItems", "Other player's items"), 4, 1);

        // Row 2: lists
        _listExchangePlayers = CreateList(("Name", 120), ("Distance", 60));
        _listExchangePlayers.Name = "ExchangePlayers";
        _listExchangePlayers.SelectedIndexChanged += (s, e) => UpdateExchangeButtons();

        _listExchangeInventory = CreateList(("Item", 200), ("Amount", 55));
        _listExchangeInventory.Name = "ExchangeInventory";
        _listExchangeInventory.MultiSelect = true;
        _listExchangeInventory.ShowItemToolTips = true;
        _listExchangeInventory.SelectedIndexChanged += (s, e) => UpdateExchangeButtons();
        _listExchangeInventory.DoubleClick += (s, e) => PutSelectedItems();

        _listMyOffer = CreateList(("Item", 200), ("Amount", 55));
        _listMyOffer.Name = "ExchangeMyItems";
        _listMyOffer.MultiSelect = true;
        _listMyOffer.ShowItemToolTips = true;
        _listMyOffer.SelectedIndexChanged += (s, e) => UpdateExchangeButtons();
        _listMyOffer.DoubleClick += (s, e) => TakeSelectedItems();

        _listPartnerOffer = CreateList(("Item", 200), ("Amount", 55));
        _listPartnerOffer.Name = "ExchangePartnerItems";
        _listPartnerOffer.ShowItemToolTips = true;

        _btnPutItem = CreateExchangeButton("PutItem", "►", PutSelectedItems);
        _btnTakeItem = CreateExchangeButton("TakeItem", "◄", TakeSelectedItems);
        var arrows = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.None,
            Margin = Padding.Empty,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        arrows.Controls.Add(_btnPutItem);
        arrows.Controls.Add(_btnTakeItem);

        foreach (var list in new[] { _listExchangePlayers, _listExchangeInventory, _listMyOffer, _listPartnerOffer })
            list.BorderStyle = BorderStyle.FixedSingle;

        window.Controls.Add(_listExchangePlayers, 0, 2);
        window.Controls.Add(_listExchangeInventory, 1, 2);
        window.Controls.Add(arrows, 2, 2);
        window.Controls.Add(_listMyOffer, 3, 2);
        window.Controls.Add(_listPartnerOffer, 4, 2);

        // Row 3: refresh / invite, gold
        _btnInvite = CreateExchangeButton("InviteExchangeButton", "Exchange", InviteSelectedPlayer);
        window.Controls.Add(CreateFlow(CreateExchangeButton("RefreshPlayers", "Refresh", RefreshExchangePlayers), _btnInvite), 0, 3);
        window.Controls.Add(CreateFlow(CreateExchangeButton("RefreshInventory", "Refresh", RefreshExchangeInventory)), 1, 3);

        var myGoldLabel = new System.Windows.Forms.Label { AutoSize = true, Text = "Gold:", Margin = new Padding(0, Px(11), Px(6), 0) };
        Localize(myGoldLabel, "ExchangeMyGold", "Gold:");
        _txtMyGold = new TextBox
        {
            Name = "ExchangeGold",
            Width = Px(130),
            Text = "0",
            TextAlign = HorizontalAlignment.Right,
            Margin = new Padding(0, Px(8), Px(6), 0),
        };
        _txtMyGold.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            SetExchangeGold();
        };
        _btnSetGold = CreateExchangeButton("SetGold", "G", SetExchangeGold);
        window.Controls.Add(CreateFlow(myGoldLabel, _txtMyGold, _btnSetGold), 3, 3);

        _lblPartnerGold = new System.Windows.Forms.Label { AutoSize = true, Text = "Gold: 0", Margin = new Padding(0, Px(11), 0, 0) };
        window.Controls.Add(_lblPartnerGold, 4, 3);

        // Row 4: confirm, approve, cancel
        _btnConfirm = CreateExchangeButton("Confirm", "Confirm", () => Game.Player?.Exchange?.Confirm());
        _btnApprove = CreateExchangeButton("Approve", "Approve", () => Game.Player?.Exchange?.Approve());
        _btnCancel = CreateExchangeButton("Cancel", "Cancel", () => Game.Player?.Exchange?.Cancel());
        var actions = CreateFlow(_btnConfirm, _btnApprove, _btnCancel);
        window.Controls.Add(actions, 0, 4);
        window.SetColumnSpan(actions, 5);

        // Row 5: status, on its own row so a long text never runs under the buttons
        _lblExchangeStatus = new System.Windows.Forms.Label
        {
            AutoSize = true,
            UseMnemonic = false,
            Margin = new Padding(0, Px(4), 0, Px(4)),
        };
        window.Controls.Add(_lblExchangeStatus, 0, 5);
        window.SetColumnSpan(_lblExchangeStatus, 5);

        // SDUI buttons do not size themselves to the caption; size them now and after a language or font change
        FitExchangeButtons();
        _translations.Add(FitExchangeButtons);
        window.FontChanged += (s, e) => FitExchangeButtons();
        window.DpiChangedAfterParent += (s, e) => FitExchangeButtons();

        // Below the invitation settings, which are docked to the top
        tabExchange.Controls.Add(window);
        tabExchange.Controls.SetChildIndex(window, 0);

        tabMain.SelectedIndexChanged += (s, e) =>
        {
            if (tabMain.SelectedTab != tabExchange || Game.Player == null)
                return;

            RefreshExchangePlayers();
            RefreshExchangeInventory();
        };

        SubscribeExchangeEvents();
        UpdateRequestRow();
        UpdateExchangeState();
    }

    private void SubscribeExchangeEvents()
    {
        _uiEvents.Subscribe("OnExchangeRequest", () => OnUi(UpdateRequestRow));
        _uiEvents.Subscribe("OnStartExchange", () => OnUi(() =>
        {
            _exchangeConfirmed = false;
            _exchangePartnerConfirmed = false;
            _txtMyGold.Text = "0";
            _lastExchangeError = null;
            UpdateRequestRow();
            UpdateExchangeState();
        }));
        _uiEvents.Subscribe("OnUpdateExchangeItems", () => OnUi(() =>
        {
            // The offer changed after the error, so the error no longer explains the state
            _lastExchangeError = null;
            UpdateExchangeState();
        }));
        _uiEvents.Subscribe("OnExchangeConfirmed", () => OnUi(() =>
        {
            _exchangeConfirmed = true;
            UpdateExchangeState();
        }));
        _uiEvents.Subscribe("OnExchangePartnerConfirmed", () => OnUi(() =>
        {
            _exchangePartnerConfirmed = true;
            UpdateExchangeState();
        }));
        _uiEvents.Subscribe("OnApproveExchange", () => OnUi(EndExchange));
        _uiEvents.Subscribe("OnExchangeOperationFailed", new Action<string>(message => OnUi(() => ShowExchangeError(message))));
        _uiEvents.Subscribe("OnExchangeApproveFailed", new Action<ushort>(code =>
            OnUi(() => ShowExchangeError(string.Format(TextFor("ExchangeApproveFailed", "Could not approve: {0}."), ExchangeErrors.Describe(code))))));
        _uiEvents.Subscribe("OnExchangeCanceledReason", new Action<ushort>(code =>
            OnUi(() =>
            {
                _lastExchangeError ??= string.Format(TextFor("ExchangeCanceledReason", "The exchange was canceled: {0}."), ExchangeErrors.Describe(code));

                // The events can arrive in any order; refresh in case the cancel was already shown
                if (Game.Player?.Exchange == null)
                    UpdateExchangeState();
            })));
        _uiEvents.Subscribe("OnCancelExchange", () => OnUi(EndExchange));
    }

    /// <summary>
    ///     Exchange events are raised on the network thread.
    /// </summary>
    private void OnUi(Action action) => _uiEvents.Post(action);

    private void EndExchange()
    {
        _exchangeConfirmed = false;
        _exchangePartnerConfirmed = false;
        UpdateExchangeState();
        RefreshExchangeInventory();
    }

    private Control CreateHeader(string key, string text)
    {
        var label = new System.Windows.Forms.Label
        {
            AutoSize = true,
            Text = text,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, Px(4), 0, Px(4)),
        };
        Localize(label, key, text);

        return label;
    }

    private static FlowLayoutPanel CreateFlow(params Control[] controls)
    {
        var flow = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        flow.Controls.AddRange(controls);

        return flow;
    }

    private SDUI.Controls.Button CreateExchangeButton(string key, string text, Action onClick)
    {
        var button = new SDUI.Controls.Button
        {
            AutoSize = false,
            Text = text,
            Radius = 6,
            Margin = new Padding(0, Px(4), Px(8), Px(4)),
        };
        button.Click += (s, e) => onClick();
        _exchangeButtons.Add(button);

        // The arrows and the gold button have no words to translate
        if (text.Any(char.IsLetter) && text.Length > 1)
            Localize(button, key, text);

        return button;
    }

    /// <summary>
    ///     Makes each button as large as its caption: at least 90 x 32 at 96 DPI, 48 wide for the arrows and the gold button.
    /// </summary>
    private void FitExchangeButtons()
    {
        foreach (var button in _exchangeButtons)
        {
            var text = TextRenderer.MeasureText(button.Text, button.Font);
            var minimumWidth = Px(button.Text.Length <= 2 ? 48 : 90);
            button.Size = new Size(Math.Max(minimumWidth, text.Width + Px(28)), Math.Max(Px(32), text.Height + Px(12)));
        }
    }

    /// <summary>
    ///     Shows why the last exchange action failed until the next change of the exchange.
    /// </summary>
    private void ShowExchangeError(string message)
    {
        _lastExchangeError = message;
        _lblExchangeStatus.Text = message;
        _lblExchangeStatus.ForeColor = Color.IndianRed;
    }

    private string GetExchangeStatusText(ExchangeInstance exchange)
    {
        if (exchange == null)
            return TextFor("ExchangeIdle", "No exchange. Select a player and press Exchange.");

        var partner = exchange.ExchangePlayerName;

        if (_exchangeConfirmed && _exchangePartnerConfirmed)
            return exchange.IsEmpty
                ? TextFor("ExchangeEmpty", "Both sides confirmed, but nothing is offered: the server would cancel an empty exchange.")
                : string.Format(TextFor("ExchangeBothConfirmed", "Both sides confirmed with {0}: check the offer, then Approve."), partner);

        if (_exchangeConfirmed)
            return string.Format(TextFor("ExchangeWaitPartner", "Confirmed, waiting for {0} to confirm."), partner);

        if (_exchangePartnerConfirmed)
            return string.Format(TextFor("ExchangePartnerConfirmedText", "{0} confirmed. Confirm to continue."), partner);

        return string.Format(TextFor("ExchangeOpen", "Exchanging with {0}."), partner);
    }

    #region Requests

    private void UpdateRequestRow()
    {
        var request = Game.AcceptanceRequest;
        var pending = _checkShowRequests.Checked
            && request?.Type == InviteRequestType.Exchange
            && Game.Player?.Exchange == null;

        _lblRequest.Text = pending
            ? string.Format(TextFor("ExchangeRequestFrom", "{0} wants to exchange"), request.Player?.Name ?? $"#{request.PlayerUniqueId}")
            : string.Empty;
        _btnAcceptRequest.Visible = pending;
        _btnRefuseRequest.Visible = pending;
    }

    private void AnswerRequest(bool accept)
    {
        var request = Game.AcceptanceRequest;
        if (request?.Type != InviteRequestType.Exchange)
            return;

        if (accept)
            request.Accept();
        else
            request.Refuse();

        Game.AcceptanceRequest = null;
        UpdateRequestRow();
    }

    #endregion

    #region Lists

    private void RefreshExchangePlayers()
    {
        if (!SpawnManager.TryGetEntities<SpawnedPlayer>(out var players) || players == null)
            players = Enumerable.Empty<SpawnedPlayer>();

        var selected = _listExchangePlayers.SelectedItems.Count > 0 ? (uint?)_listExchangePlayers.SelectedItems[0].Tag : null;

        _listExchangePlayers.BeginUpdate();
        _listExchangePlayers.Items.Clear();

        foreach (var player in players.OrderBy(p => p.DistanceToPlayer))
        {
            var item = new ListViewItem(new[] { player.Name ?? string.Empty, Math.Round(player.DistanceToPlayer).ToString("0") })
            {
                Tag = player.UniqueId,
                Selected = player.UniqueId == selected,
            };
            _listExchangePlayers.Items.Add(item);
        }

        _listExchangePlayers.EndUpdate();
        UpdateExchangeButtons();
    }

    /// <summary>
    ///     Lists the items of the normal inventory part that are not in the exchange window yet.
    /// </summary>
    private void RefreshExchangeInventory()
    {
        var inventory = Game.Player?.Inventory;
        if (inventory == null)
            return;

        var offered = Game.Player.Exchange?.SendingItems?.Select(i => i.SourceSlot).ToHashSet() ?? new HashSet<byte>();

        _listExchangeInventory.BeginUpdate();
        _listExchangeInventory.Items.Clear();

        foreach (var item in inventory.GetNormalPartItems().Where(i => !offered.Contains(i.Slot)).OrderBy(i => i.Slot))
            _listExchangeInventory.Items.Add(CreateItemRow(item, item.Slot));

        _listExchangeInventory.EndUpdate();
        UpdateExchangeButtons();
    }

    private static ListViewItem CreateItemRow(InventoryItem item, byte tag)
    {
        var name = item.Record?.GetRealName() ?? $"#{item.ItemId}";
        if (item.OptLevel > 0)
            name += $" (+{item.OptLevel})";

        return new ListViewItem(new[] { name, item.Amount.ToString() })
        {
            Tag = tag,
            ToolTipText = string.Join(Environment.NewLine, item.GetDescriptionLines().Select(l => l.Text)),
        };
    }

    private static void FillOffer(SDUI.Controls.ListView list, List<ExchangeItem> items)
    {
        list.BeginUpdate();
        list.Items.Clear();

        if (items != null)
            foreach (var item in items.OrderBy(i => i.ExchangeSlot))
                list.Items.Add(CreateItemRow(item.Item, item.ExchangeSlot));

        list.EndUpdate();
    }

    #endregion

    #region Actions

    private void InviteSelectedPlayer()
    {
        if (_listExchangePlayers.SelectedItems.Count == 0 || Game.Player?.Exchange != null)
            return;

        ExchangeInstance.Invite((uint)_listExchangePlayers.SelectedItems[0].Tag);
        _lblExchangeStatus.Text = TextFor("ExchangeInviteSent", "Invitation sent, waiting for the player...");
    }

    private void PutSelectedItems()
    {
        var exchange = Game.Player?.Exchange;
        if (exchange == null || _exchangeConfirmed)
            return;

        // 12 slots in the exchange window
        var free = 12 - (exchange.SendingItems?.Count ?? 0);
        foreach (var row in _listExchangeInventory.SelectedItems.Cast<ListViewItem>().Take(Math.Max(0, free)))
            exchange.AddItem((byte)row.Tag);
    }

    private void TakeSelectedItems()
    {
        var exchange = Game.Player?.Exchange;
        if (exchange == null || _exchangeConfirmed)
            return;

        foreach (var row in _listMyOffer.SelectedItems.Cast<ListViewItem>())
            exchange.RemoveItem((byte)row.Tag);
    }

    private void SetExchangeGold()
    {
        var exchange = Game.Player?.Exchange;
        if (exchange == null || _exchangeConfirmed)
            return;

        var text = _txtMyGold.Text.Replace(",", string.Empty).Replace(".", string.Empty).Trim();
        if (!ulong.TryParse(text, out var gold) || gold > Game.Player.Gold)
        {
            _lblExchangeStatus.Text = string.Format(TextFor("ExchangeGoldInvalid", "Enter an amount between 0 and {0:N0}."), Game.Player.Gold);
            return;
        }

        exchange.SetGold(gold);
    }

    #endregion

    #region State

    private void UpdateExchangeState()
    {
        var exchange = Game.Player?.Exchange;

        FillOffer(_listMyOffer, exchange?.SendingItems);
        FillOffer(_listPartnerOffer, exchange?.ReceivingItems);

        var partnerGold = exchange?.ReceivingGold ?? 0;
        _lblPartnerGold.Text = string.Format(TextFor("ExchangePartnerGold", "Gold: {0:N0}"), partnerGold);
        if (exchange != null && !_txtMyGold.Focused && exchange.SendingGold > 0)
            _txtMyGold.Text = exchange.SendingGold.ToString("N0");

        if (exchange == null && _lastExchangeError != null)
        {
            // Keep showing why the server closed the exchange
            _lblExchangeStatus.ForeColor = Color.IndianRed;
            _lblExchangeStatus.Text = _lastExchangeError;
        }
        else
        {
            _lblExchangeStatus.ForeColor = SDUI.ColorScheme.ForeColor;
            _lblExchangeStatus.Text = GetExchangeStatusText(exchange);
        }

        RefreshExchangeInventory();
        UpdateExchangeButtons();
    }

    private void UpdateExchangeButtons()
    {
        if (_btnConfirm == null)
            return;

        var exchanging = Game.Player?.Exchange != null;
        var editable = exchanging && !_exchangeConfirmed;

        _btnInvite.Enabled = Game.Ready && !exchanging && _listExchangePlayers.SelectedItems.Count > 0;
        _btnPutItem.Enabled = editable && _listExchangeInventory.SelectedItems.Count > 0;
        _btnTakeItem.Enabled = editable && _listMyOffer.SelectedItems.Count > 0;
        _btnSetGold.Enabled = editable;
        _txtMyGold.Enabled = editable;
        _btnConfirm.Enabled = editable;
        _btnApprove.Enabled = exchanging && _exchangeConfirmed && _exchangePartnerConfirmed && !Game.Player.Exchange.IsEmpty;
        _btnCancel.Enabled = exchanging;
    }

    #endregion
}
