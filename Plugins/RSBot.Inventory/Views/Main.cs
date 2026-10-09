using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Event;
using RSBot.Core.Extensions;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Inventory;
using SDUI;
using SDUI.Controls;
using Button = SDUI.Controls.Button;
using ListViewExtensions = RSBot.Core.Extensions.ListViewExtensions;

namespace RSBot.Inventory.Views;

[ToolboxItem(false)]
public partial class Main : DoubleBufferedControl
{
    /// <summary>
    ///     <inheritdoc />
    /// </summary>
    private int _inventoryDirty = 1;
    private System.Windows.Forms.Timer _inventoryTimer;

    /// <summary>
    ///     The items used at the training place, parsed once per list rebuild.
    /// </summary>
    private System.Collections.Generic.HashSet<string> _itemsAtTrainingPlace;

    private Font _boldItemFont;

    /// <summary>
    ///     Shows the stats of the selected item.
    /// </summary>
    private RichTextBox _itemDetails;

    /// <summary>
    ///     The font of the item name in the details panel.
    /// </summary>
    private Font _itemDetailsTitleFont;

    /// <summary>
    ///     <inheritdoc />
    /// </summary>
    private int _selectedIndex;
    private int _renderedIndex = -1;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Main" /> class.
    /// </summary>
    public Main()
    {
        InitializeComponent();
        SubscribeEvents();

        listViewMain.SmallImageList = ListViewExtensions.StaticItemsImageList;

        var backColor = ColorScheme.BorderColor.Determine().Alpha(85);
        buttonInventory.ForeColor = backColor.Determine();
        buttonInventory.Color = backColor;

        InitializeDetailsPanel();
        components ??= new Container();
        _inventoryTimer = new System.Windows.Forms.Timer(components) { Interval = 250 };
        _inventoryTimer.Tick += (_, _) =>
        {
            if (!Visible || !Enabled || FindForm()?.WindowState == FormWindowState.Minimized) return;
            if (Game.Player != null && Interlocked.Exchange(ref _inventoryDirty, 0) != 0)
                ApplyInventoryList();
        };
        _inventoryTimer.Start();
        Disposed += (_, _) =>
        {
            EventManager.UnsubscribeEvent("OnLoadCharacter", (System.Action)OnLoadCharacter);
            EventManager.UnsubscribeEvent("OnUpdateInventoryItem", (Action<byte>)OnUpdateInventoryItem);
            EventManager.UnsubscribeEvent("OnUseItem", (Action<byte>)OnUpdateInventoryItem);
            EventManager.UnsubscribeEvent("OnInventoryUpdate", (System.Action)UpdateInventoryList);
            _boldItemFont?.Dispose();
            _itemDetailsTitleFont?.Dispose();
            _itemDetails.Font.Dispose();
        };
    }

    /// <summary>
    ///     Creates the panel that shows the white and blue stats of the selected item.
    /// </summary>
    private void InitializeDetailsPanel()
    {
        _itemDetails = new RichTextBox
        {
            Name = "itemDetails",
            Dock = DockStyle.Right,
            Width = LogicalToDeviceUnits(260),
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(24, 24, 28),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9F),
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false,
            TabStop = false,
        };

        _itemDetailsTitleFont = new Font(_itemDetails.Font, FontStyle.Bold);

        // Docked after the top/bottom panels and before the list that fills the rest.
        Controls.Add(_itemDetails);
        Controls.SetChildIndex(_itemDetails, 1);

        listViewMain.SelectedIndexChanged += (_, _) => ShowItemDetails(GetSelectedInventoryItem());
    }

    /// <summary>
    ///     Gets the item of the selected row.
    /// </summary>
    private InventoryItem GetSelectedInventoryItem()
    {
        return listViewMain.SelectedItems.Count == 1 ? listViewMain.SelectedItems[0].Tag as InventoryItem : null;
    }

    /// <summary>
    ///     Writes the description of the item into the details panel.
    /// </summary>
    private void ShowItemDetails(InventoryItem item)
    {
        if (_itemDetails == null)
            return;

        if (_itemDetails.InvokeRequired)
        {
            _itemDetails.BeginInvoke((MethodInvoker)(() => ShowItemDetails(item)));
            return;
        }

        _itemDetails.Clear();

        if (item == null)
            return;

        foreach (var line in item.GetDescriptionLines())
        {
            _itemDetails.SelectionStart = _itemDetails.TextLength;
            _itemDetails.SelectionColor = line.Kind switch
            {
                ItemDescriptionLineKind.Title => Color.FromArgb(255, 214, 102),
                ItemDescriptionLineKind.WhiteStat => Color.White,
                ItemDescriptionLineKind.BlueStat => Color.FromArgb(80, 160, 255),
                ItemDescriptionLineKind.Binding => Color.FromArgb(255, 150, 60),
                _ => Color.Silver,
            };
            _itemDetails.SelectionFont = line.Kind == ItemDescriptionLineKind.Title ? _itemDetailsTitleFont : _itemDetails.Font;

            _itemDetails.AppendText(line.Text + Environment.NewLine);

            if (line.Kind == ItemDescriptionLineKind.Title)
                _itemDetails.AppendText(Environment.NewLine);
        }

        _itemDetails.SelectionStart = 0;
        _itemDetails.ScrollToCaret();
    }

    /// <summary>
    ///     Subscribes the events.
    /// </summary>
    private void SubscribeEvents()
    {
        EventManager.SubscribeEvent("OnLoadCharacter", OnLoadCharacter);
        EventManager.SubscribeEvent("OnUpdateInventoryItem", new Action<byte>(OnUpdateInventoryItem));
        EventManager.SubscribeEvent("OnUseItem", new Action<byte>(OnUpdateInventoryItem));
        EventManager.SubscribeEvent("OnInventoryUpdate", UpdateInventoryList);
    }

    private void OnLoadCharacter()
    {
        UpdateInventoryList();
    }

    /// <summary>
    ///     Calling when update inventory item
    /// </summary>
    /// <param name="slot"></param>
    private void OnUpdateInventoryItem(byte slot) => UpdateInventoryList();

    /// <summary>
    ///     Updates the inventory list.
    /// </summary>
    public void UpdateInventoryList() => Interlocked.Exchange(ref _inventoryDirty, 1);

    private void ApplyInventoryList()
    {
        if (!Visible)
            return;

        _itemsAtTrainingPlace = null;

        if (Game.Player == null)
            return;

        var selectedKey = _renderedIndex == _selectedIndex && listViewMain.SelectedItems.Count == 1
            ? listViewMain.SelectedItems[0].Name : null;
        _renderedIndex = _selectedIndex;
        listViewMain.BeginUpdate();
        try
        {
            listViewMain.Items.Clear();
            ShowItemDetails(null);

            switch (_selectedIndex)
            {
                case 0:
                    var itemsPlayer = Game.Player.Inventory.GetNormalPartItems();
                    foreach (var item in itemsPlayer)
                        AddItem(item);

                    lblFreeSlots.Text = Game.Player.Inventory.FreeSlots + "/" + Game.Player.Inventory.NormalPartSize;
                    pbInventoryStatus.Value = Game.Player.Inventory.FreeSlots;
                    pbInventoryStatus.Maximum = Game.Player.Inventory.NormalPartSize;
                    break;

                case 1:

                    var items = Game.Player.Inventory.GetEquippedPartItems();
                    foreach (var item in items)
                        AddItem(item);

                    int maxSlots =
                        (
                            Game.ClientType == GameClientType.Global
                            || Game.ClientType == GameClientType.Korean
                            || Game.ClientType == GameClientType.VTC_Game
                            || Game.ClientType == GameClientType.RuSro
                            || Game.ClientType == GameClientType.Turkey
                            || Game.ClientType == GameClientType.Taiwan
                            || Game.ClientType == GameClientType.Japanese
                        )
                            ? 17
                            : 13; //4 slots for relics

                    lblFreeSlots.Text = (maxSlots - items.Count) + " / " + maxSlots;

                    pbInventoryStatus.Value = (maxSlots - items.Count);
                    pbInventoryStatus.Maximum = maxSlots;

                    break;

                case 2:

                    foreach (var item in Game.Player.Avatars)
                        AddItem(item);

                    lblFreeSlots.Text = Game.Player.Avatars.FreeSlots + " / " + Game.Player.Avatars.Capacity;

                    pbInventoryStatus.Value = Game.Player.Avatars.FreeSlots;
                    pbInventoryStatus.Maximum = Game.Player.Avatars.Capacity;

                    break;

                case 3:

                    if (!Game.Player.HasActiveAbilityPet)
                    {
                        return;
                    }

                    foreach (var item in Game.Player.AbilityPet.Inventory)
                        AddItem(item);

                    lblFreeSlots.Text =
                        Game.Player.AbilityPet.Inventory.FreeSlots + "/" + Game.Player.AbilityPet.Inventory.Capacity;

                    pbInventoryStatus.Value = Game.Player.AbilityPet.Inventory.FreeSlots;
                    pbInventoryStatus.Maximum = Game.Player.AbilityPet.Inventory.Capacity;

                    break;

                case 4:

                    if (Game.Player.Storage == null)
                    {
                        return;
                    }

                    foreach (var item in Game.Player.Storage)
                        AddItem(item);

                    lblFreeSlots.Text = Game.Player.Storage.FreeSlots + "/" + Game.Player.Storage.Capacity;

                    pbInventoryStatus.Value = Game.Player.Storage.FreeSlots;
                    pbInventoryStatus.Maximum = Game.Player.Storage.Capacity;

                    break;

                case 5:

                    if (Game.Player.GuildStorage == null)
                    {
                        return;
                    }

                    foreach (var item in Game.Player.GuildStorage)
                        AddItem(item);

                    lblFreeSlots.Text = Game.Player.GuildStorage.FreeSlots + "/" + Game.Player.GuildStorage.Capacity;

                    pbInventoryStatus.Value = Game.Player.GuildStorage.FreeSlots;
                    pbInventoryStatus.Maximum = Game.Player.GuildStorage.Capacity;

                    break;

                case 6:

                    if (Game.Player.JobTransport == null)
                    {
                        return;
                    }

                    foreach (var item in Game.Player.JobTransport.Inventory)
                        AddItem(item);

                    lblFreeSlots.Text =
                        Game.Player.JobTransport.Inventory.FreeSlots
                        + "/"
                        + Game.Player.JobTransport.Inventory.Capacity;

                    pbInventoryStatus.Value = Game.Player.JobTransport.Inventory.FreeSlots;
                    pbInventoryStatus.Maximum = Game.Player.JobTransport.Inventory.Capacity;

                    break;

                case 7:

                    if (Game.Player.Job2SpecialtyBag == null)
                    {
                        return;
                    }

                    foreach (var item in Game.Player.Job2SpecialtyBag)
                        AddItem(item);

                    lblFreeSlots.Text =
                        Game.Player.Job2SpecialtyBag.FreeSlots + "/" + Game.Player.Job2SpecialtyBag.Capacity;

                    pbInventoryStatus.Value = Game.Player.Job2SpecialtyBag.FreeSlots;
                    pbInventoryStatus.Maximum = Game.Player.Job2SpecialtyBag.Capacity;

                    break;

                case 8:

                    if (Game.Player.Job2 == null)
                    {
                        return;
                    }

                    foreach (var item in Game.Player.Job2)
                        AddItem(item);

                    lblFreeSlots.Text = Game.Player.Job2.FreeSlots + "/" + Game.Player.Job2.Capacity;

                    pbInventoryStatus.Value = Game.Player.Job2.FreeSlots;
                    pbInventoryStatus.Maximum = Game.Player.Job2.Capacity;

                    break;

                case 9:

                    if (!Game.Player.HasActiveFellowPet)
                    {
                        return;
                    }

                    foreach (var item in Game.Player.Fellow.Inventory)
                        AddItem(item);

                    lblFreeSlots.Text =
                        Game.Player.Fellow.Inventory.FreeSlots + "/" + Game.Player.Fellow.Inventory.Capacity;

                    pbInventoryStatus.Value = Game.Player.Fellow.Inventory.FreeSlots;
                    pbInventoryStatus.Maximum = Game.Player.Fellow.Inventory.Capacity;

                    break;
            }

            if (selectedKey != null && listViewMain.Items.ContainsKey(selectedKey))
                listViewMain.Items[selectedKey].Selected = true;
        }
        catch (InvalidOperationException)
        {
            // Inventory can change while its live collection is enumerated; retry the next visible tick.
            UpdateInventoryList();
        }
        finally
        {
            listViewMain.EndUpdate();
        }
    }

    /// <summary>
    ///     Adds the item.
    /// </summary>
    /// <param name="item">The item.</param>
    private void AddItem(InventoryItem item)
    {
        if (item == null)
            return;

        var name = item.Record?.GetRealName() ?? "";
        if (item.OptLevel > 0)
            name += " (+" + item.OptLevel + ")";

        var lvItem = listViewMain.Items.Add(item.Slot.ToString(), name, 0);
        lvItem.Tag = item;
        lvItem.SubItems.Add(item.Amount.ToString());

        if (item.Record.IsEquip)
            lvItem.SubItems.Add(item.Record.GetRarityName());

        if (_selectedIndex == 0)
        {
            _itemsAtTrainingPlace ??= PlayerConfig.GetArray<string>("RSBot.Inventory.ItemsAtTrainplace").ToHashSet();

            if (_itemsAtTrainingPlace.Contains(item.Record.CodeName))
                lvItem.Font = _boldItemFont ??= new Font(lvItem.Font, FontStyle.Bold);
        }

        lvItem.LoadItemImageAsync(item.Record);
    }

    /// <summary>
    ///     Handles the visible changed event of the parent.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="System.EventArgs" /> instance containing the event data.</param>
    private void Main_VisibleChanged(object sender, EventArgs e)
    {
        UpdateInventoryList();
    }

    /// <summary>
    ///     Handles the Click event of the btnReload control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="System.EventArgs" /> instance containing the event data.</param>
    private async void buttonUseItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
            return;

        var listViewItem = listViewMain.SelectedItems[0];
        if (listViewItem.Tag is not InventoryItem inventoryItem)
            return;

        switch (inventoryItem.UseKind)
        {
            case ItemUseKind.Simple:
                await RunItemActionAsync(() => inventoryItem.Use());
                break;

            case ItemUseKind.GlobalChat:
                var dialog = new InputDialog(
                    "Global chat",
                    inventoryItem.Record.GetRealName(),
                    "Enter the message to send with this item."
                );
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    inventoryItem.UseGlobalChat(dialog.Value?.ToString());
                break;

            case ItemUseKind.Unknown:
                var result = MessageBox.Show(
                    this,
                    "This item may need extra input the bot can not send. The server may disconnect you. Use anyway?",
                    inventoryItem.Record.GetRealName(),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );
                if (result == DialogResult.Yes)
                    await RunItemActionAsync(() => inventoryItem.Use());
                break;

            // ReverseScroll, OnActivePet and OnDeadPetItem are used from the sub menus.
        }
    }

    /// <summary>
    ///     Handles the mouse double click event of the listviewmain control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="System.EventArgs" /> instance containing the event data.</param>
    private void listViewMain_MouseDoubleClick(object sender, MouseEventArgs e)
    {
        if (listViewMain.SelectedItems.Count <= 0)
            return;

        if (!Kernel.Debug)
            return;

        var itemForm = new ItemProperties(listViewMain.SelectedItems[0].Tag as InventoryItem);
        itemForm.Show();
    }

    /// <summary>
    ///     Handles the selected index changed event of the button's control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="System.EventArgs" /> instance containing the event data.</param>
    private void ButtonSwitcher(object sender, EventArgs e)
    {
        var button = sender as Button;
        if (_selectedIndex == button.TabIndex)
            return;

        _selectedIndex = button.TabIndex;

        //Only character inventory, storage and guild storage sorting is supported for now!
        btnSort.Visible = _selectedIndex == 0;
        checkAutoSort.Visible = _selectedIndex is 0 or 4 or 5;

        foreach (var control in topPanel.Controls.OfType<Button>())
        {
            if (control.TabIndex > 9)
                continue;

            control.Color = Color.Transparent;

            if (control == button)
            {
                var backColor = ColorScheme.BorderColor.Determine().Alpha(85);
                control.ForeColor = backColor.Determine();
                control.Color = backColor;
            }

            control.Invalidate();
        }

        UpdateInventoryList();
    }

    private async void dropToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
            return;

        var listViewItem = listViewMain.SelectedItems[0];
        var inventoryItem = listViewItem.Tag as InventoryItem;
        if (inventoryItem == null)
            return;

        var cos = _selectedIndex == 3;
        var petId = Game.Player.AbilityPet?.UniqueId;
        await RunItemActionAsync(() => inventoryItem.Drop(cos, petId));
    }

    private void contextMenuStrip_Opening(object sender, CancelEventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
        {
            e.Cancel = true;
            return;
        }

        var listViewItem = listViewMain.SelectedItems[0];
        if (listViewItem.Tag is not InventoryItem inventoryItem)
            return;

        if (_selectedIndex != 0)
        {
            useToolStripMenuItem.Visible = false;
            moveToLastDeathPositionToolStripMenuItem.Visible = false;
            moveToLastRecallPositionToolStripMenuItem.Visible = false;
            moveToPetToolStripMenuItem.Visible = false;
            moveToPlayerToolStripMenuItem.Visible = _selectedIndex == 3;
            selectMapLocationToolStripMenuItem.Visible = false;
            return;
        }

        var canUse = (inventoryItem.Record.CanUse & ObjectUseType.Yes) != 0;
        if (canUse)
        {
            var useItems = PlayerConfig.GetArray<string>("RSBot.Inventory.ItemsAtTrainplace");
            useItemAtTrainingPlaceMenuItem.Checked = useItems.Contains(inventoryItem.Record.CodeName);
            useItemAtTrainingPlaceMenuItem.Enabled = true;

            var purposiveItems = PlayerConfig.GetArray<string>("RSBot.Inventory.AutoUseAccordingToPurpose");
            autoUseAccordingToPurposeToolStripMenuItem.Checked = purposiveItems.Contains(inventoryItem.Record.CodeName);
            autoUseAccordingToPurposeToolStripMenuItem.Enabled = true;
        }
        else
        {
            useItemAtTrainingPlaceMenuItem.Checked = false;
            useItemAtTrainingPlaceMenuItem.Enabled = false;

            autoUseAccordingToPurposeToolStripMenuItem.Checked = false;
            autoUseAccordingToPurposeToolStripMenuItem.Enabled = false;
        }

        var useKind = inventoryItem.UseKind;
        var isReverseScroll = useKind == ItemUseKind.ReverseScroll;
        useToolStripMenuItem.Visible = !isReverseScroll;
        useToolStripMenuItem.Enabled = useKind != ItemUseKind.None;
        BuildUseTargetMenu(inventoryItem, useKind);
        moveToLastDeathPositionToolStripMenuItem.Visible = isReverseScroll;
        moveToLastRecallPositionToolStripMenuItem.Visible = isReverseScroll;
        selectMapLocationToolStripMenuItem.Visible = isReverseScroll;
        dropToolStripMenuItem.Visible = inventoryItem.Record.CanDrop != ObjectDropType.No;

        moveToPetToolStripMenuItem.Visible = Game.Player.AbilityPet != null && _selectedIndex != 3;
        moveToPlayerToolStripMenuItem.Visible = _selectedIndex == 3;

        if (isReverseScroll)
        {
            var tagItem = selectMapLocationToolStripMenuItem.Tag as InventoryItem;
            if (tagItem != inventoryItem)
            {
                selectMapLocationToolStripMenuItem.Tag = inventoryItem;
                selectMapLocationToolStripMenuItem.DropDownItems.Clear();

                foreach (var item in Game.ReferenceManager.OptionalTeleports)
                {
                    var mapName = Game.ReferenceManager.GetTranslation(item.Value.Region.ToString());

                    var menuItem = new ToolStripMenuItem { Text = mapName };

                    menuItem.Click += async (itemSender, itemEvent) =>
                    {
                        await RunItemActionAsync(() => inventoryItem.UseTo(7, item.Value.ID));
                    };

                    selectMapLocationToolStripMenuItem.DropDownItems.Add(menuItem);
                }
            }
        }
    }

    /// <summary>
    ///     Fills the "Use" sub menu with the targets of items that are used on a pet.
    /// </summary>
    private void BuildUseTargetMenu(InventoryItem inventoryItem, ItemUseKind useKind)
    {
        useToolStripMenuItem.DropDownItems.Clear();

        if (useKind == ItemUseKind.OnActivePet)
        {
            var pets = new RSBot.Core.Objects.Cos.Cos[]
            {
                Game.Player.Growth,
                Game.Player.Fellow,
                Game.Player.Vehicle,
                Game.Player.JobTransport,
                Game.Player.AbilityPet,
            };

            foreach (var pet in pets.Where(p => p != null).Distinct())
            {
                var petName = string.IsNullOrWhiteSpace(pet.Name) ? pet.Record?.GetRealName() : pet.Name;
                var menuItem = new ToolStripMenuItem { Text = $"Use on {petName}" };
                menuItem.Click += (_, _) => inventoryItem.UseFor(pet.UniqueId);

                useToolStripMenuItem.DropDownItems.Add(menuItem);
            }
        }
        else if (useKind == ItemUseKind.OnDeadPetItem)
        {
            foreach (var petItem in Game.Player.Inventory.GetItems(p => p.Record.IsPet && p.State == InventoryItemState.Dead))
            {
                var menuItem = new ToolStripMenuItem { Text = $"Revive {petItem.Record.GetRealName()}" };
                menuItem.Click += async (_, _) => await RunItemActionAsync(() => inventoryItem.UseTo(petItem.Slot));

                useToolStripMenuItem.DropDownItems.Add(menuItem);
            }
        }
        else
        {
            return;
        }

        if (useToolStripMenuItem.DropDownItems.Count == 0)
            useToolStripMenuItem.Enabled = false;
    }

    private async void moveToLastRecallPositionToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
            return;

        var listViewItem = listViewMain.SelectedItems[0];
        var inventoryItem = listViewItem.Tag as InventoryItem;
        if (inventoryItem == null)
            return;

        await RunItemActionAsync(() => inventoryItem.UseTo(2));
    }

    private async void moveToLastDeathPositionToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
            return;

        var listViewItem = listViewMain.SelectedItems[0];
        var inventoryItem = listViewItem.Tag as InventoryItem;
        if (inventoryItem == null)
            return;

        await RunItemActionAsync(() => inventoryItem.UseTo(3));
    }

    private void moveToPetToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
            return;

        var listViewItem = listViewMain.SelectedItems[0];
        var inventoryItem = listViewItem.Tag as InventoryItem;
        if (inventoryItem == null)
            return;

        if (Game.Player.AbilityPet == null)
            return;

        var freeSlot = Game.Player.AbilityPet.Inventory.GetFreeSlot();
        if (freeSlot == 0xFF)
            return;

        var packet = new Packet(0x7034);
        packet.WriteByte(InventoryOperation.SP_MOVE_ITEM_PC_PET);
        packet.WriteUInt(Game.Player.AbilityPet.UniqueId);
        packet.WriteByte(inventoryItem.Slot);
        packet.WriteByte(freeSlot);
        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    private void moveToPlayerToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedIndices.Count != 1)
            return;

        var listViewItem = listViewMain.SelectedItems[0];
        var inventoryItem = listViewItem.Tag as InventoryItem;
        if (inventoryItem == null)
            return;

        if (Game.Player.AbilityPet == null)
            return;

        var freeSlot = Game.Player.Inventory.GetFreeSlot();
        if (freeSlot == 0xFF)
            return;

        var packet = new Packet(0x7034);
        packet.WriteByte(InventoryOperation.SP_MOVE_ITEM_PET_PC);
        packet.WriteUInt(Game.Player.AbilityPet.UniqueId);
        packet.WriteByte(inventoryItem.Slot);
        packet.WriteByte(freeSlot);
        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    private static async Task RunItemActionAsync(System.Action action)
    {
        try
        {
            await Task.Run(action);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex);
        }
    }

    private async void btnSort_Click(object sender, EventArgs e)
    {
        var inventory = Game.Player?.Inventory;
        await RunItemActionAsync(() => inventory?.Sort());
    }

    private void checkAutoSort_CheckedChanged(object sender, EventArgs e)
    {
        PlayerConfig.Set("RSBot.Inventory.AutoSort", checkAutoSort.Checked);
    }

    private void useItemAtTrainingPlaceMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedItems.Count == 0)
            return;

        var lvItem = listViewMain.SelectedItems[0];
        var itemsToUse = PlayerConfig.GetArray<string>("RSBot.Inventory.ItemsAtTrainplace").ToList();
        var selectedItem = (InventoryItem)lvItem.Tag;
        if (selectedItem == null)
            return;

        var useSelectedItem = itemsToUse.Contains(selectedItem.Record.CodeName);

        if (useSelectedItem)
        {
            lvItem.Font = Font;
            itemsToUse.Remove(selectedItem.Record.CodeName);
        }
        else
        {
            lvItem.Font = new Font(lvItem.Font, FontStyle.Bold);
            itemsToUse.Add(selectedItem.Record.CodeName);
        }

        useItemAtTrainingPlaceMenuItem.Checked = !useItemAtTrainingPlaceMenuItem.Checked;
        PlayerConfig.SetArray("RSBot.Inventory.ItemsAtTrainplace", itemsToUse);
    }

    private void autoUseAccordingToPurposeToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (listViewMain.SelectedItems.Count == 0)
            return;

        var lvItem = listViewMain.SelectedItems[0];

        var itemsToUse = PlayerConfig.GetArray<string>("RSBot.Inventory.AutoUseAccordingToPurpose").ToList();
        var selectedItem = (InventoryItem)lvItem.Tag;
        if (selectedItem == null)
            return;

        var useSelectedItem = itemsToUse.Contains(selectedItem.Record.CodeName);

        if (useSelectedItem)
        {
            lvItem.Font = Font;
            itemsToUse.Remove(selectedItem.Record.CodeName);
        }
        else
        {
            lvItem.Font = new Font(lvItem.Font, FontStyle.Bold);
            itemsToUse.Add(selectedItem.Record.CodeName);
        }

        useItemAtTrainingPlaceMenuItem.Checked = !useItemAtTrainingPlaceMenuItem.Checked;
        PlayerConfig.SetArray("RSBot.Inventory.AutoUseAccordingToPurpose", itemsToUse);
    }

    /// <summary>
    ///     Occurs before Main form is displayed.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Main_Load(object sender, EventArgs e)
    {
        checkAutoSort.Checked = PlayerConfig.Get("RSBot.Inventory.AutoSort", false);
    }
}
