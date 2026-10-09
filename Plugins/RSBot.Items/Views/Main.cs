using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Extensions;
using RSBot.Core.Objects;
using SDUI.Controls;
using CheckBox = SDUI.Controls.CheckBox;
using GroupBox = SDUI.Controls.GroupBox;
using ListViewExtensions = RSBot.Core.Extensions.ListViewExtensions;

namespace RSBot.Items.Views;

[ToolboxItem(false)]
public partial class Main : DoubleBufferedControl
{
    private readonly UiEventSubscriptions _uiEvents;

    /// <summary>
    ///     The index of the filter row a mouse drag selection started on, -1 when not dragging.
    /// </summary>
    private int _dragAnchorIndex = -1;
    private bool _loadingSettings;
    private int _sellQueryVersion;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Main" /> class.
    /// </summary>
    public Main()
    {

        InitializeComponent();
        _uiEvents = new UiEventSubscriptions(this);
        SubscribeEvents();

        InitializeTownTab();
        InitializeDropFilter();

        listFilter.MultiSelect = true;
        listFilter.HideSelection = false;
        listFilter.KeyDown += listFilter_KeyDown;
        listFilter.MouseDown += listFilter_MouseDown;
        listFilter.MouseMove += listFilter_MouseMove;
        listFilter.MouseUp += (_, _) => _dragAnchorIndex = -1;
    }

    /// <summary>
    ///     Subscribes the events.
    /// </summary>
    private void SubscribeEvents()
    {
        _uiEvents.Subscribe("OnLoadGameData", OnLoadGameData);
        _uiEvents.Subscribe("OnEnterGame", LoadSettings);
    }

    /// <summary>
    ///     Loads the search result item image.
    /// </summary>
    private async void LoadSearchResultItemImagesAsync()
    {
        listFilter.BeginUpdate();
        try
        {
            foreach (ListViewItem item in listFilter.Items)
            {
                var refItem = (RefObjItem)item.Tag;

                if (!searchImageList.Images.ContainsKey(refItem.CodeName))
                    searchImageList.Images.Add(refItem.CodeName, refItem.GetIcon());

                item.ImageKey = refItem.CodeName;
            }

        }
        catch (Exception ex)
        {
            Log.Fatal(ex);
        }
        finally
        {
            listFilter.EndUpdate();
        }

        await Task.Yield();
    }

    /// <summary>
    ///     Queries the sell items.
    /// </summary>
    private async Task QuerySellItemsAsync()
    {
        if (IsDisposed || Disposing)
            return;
        var version = ++_sellQueryVersion;
        try
        {
            listFilter.Visible = false;
            listFilter.BeginUpdate();
            listFilter.Items.Clear();
            listFilter.EndUpdate();

            var filters = new List<TypeIdFilter>();

            #region Weapons

            if (checkSword.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 2));

            if (checkBlade.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 3));

            if (checkSpear.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 4));

            if (checkGlave.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 5));

            if (checkBow.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 6));

            if (check1HSword.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 7));

            if (check2HSword.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 8));

            if (checkAxe.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 9));

            if (checkWRod.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 10));

            if (checkStaff.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 11));

            if (checkXBow.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 12));

            if (checkDagger.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 13));

            if (checkHarp.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 14));

            if (checkCRod.Checked)
                filters.Add(new TypeIdFilter(3, 1, 6, 15));

            #endregion Weapons

            #region Equipment

            var clothTypes = new byte[3, 2];

            if (checkClothes.Checked)
            {
                if (checkEuropean.Checked)
                    clothTypes[0, 0] = 9;

                if (checkChinese.Checked)
                    clothTypes[0, 1] = 1;
            }

            if (checkLight.Checked)
            {
                if (checkEuropean.Checked)
                    clothTypes[1, 0] = 10;

                if (checkChinese.Checked)
                    clothTypes[1, 1] = 2;
            }

            if (checkHeavy.Checked)
            {
                if (checkEuropean.Checked)
                    clothTypes[2, 0] = 11;

                if (checkChinese.Checked)
                    clothTypes[2, 1] = 3;
            }

            for (var x = 0; x < 3; x++)
            for (var z = 0; z < 2; z++)
            {
                var cloth = clothTypes[x, z];
                if (cloth == 0)
                    continue;

                if (checkHead.Checked)
                    filters.Add(new TypeIdFilter(3, 1, cloth, 1));

                if (checkShoulder.Checked)
                    filters.Add(new TypeIdFilter(3, 1, cloth, 2));

                if (checkChest.Checked)
                    filters.Add(new TypeIdFilter(3, 1, cloth, 3));

                if (checkLegs.Checked)
                    filters.Add(new TypeIdFilter(3, 1, cloth, 4));

                if (checkHand.Checked)
                    filters.Add(new TypeIdFilter(3, 1, cloth, 5));

                if (checkBoot.Checked)
                    filters.Add(new TypeIdFilter(3, 1, cloth, 6));
            }

            #region Accessory

            if (checkRing.Checked && checkEuropean.Checked)
                filters.Add(new TypeIdFilter(3, 1, 12, 3));

            if (checkRing.Checked && checkChinese.Checked)
                filters.Add(new TypeIdFilter(3, 1, 5, 3));

            if (checkRing.Checked && !checkEuropean.Checked && !checkChinese.Checked)
            {
                filters.Add(new TypeIdFilter(3, 1, 5, 3));
                filters.Add(new TypeIdFilter(3, 1, 12, 3));
            }

            if (checkNecklace.Checked && checkEuropean.Checked)
                filters.Add(new TypeIdFilter(3, 1, 12, 2));

            if (checkNecklace.Checked && checkChinese.Checked)
                filters.Add(new TypeIdFilter(3, 1, 5, 2));

            if (checkNecklace.Checked && !checkEuropean.Checked && !checkChinese.Checked)
            {
                filters.Add(new TypeIdFilter(3, 1, 5, 2));
                filters.Add(new TypeIdFilter(3, 1, 12, 2));
            }

            if (checkEarring.Checked && checkEuropean.Checked)
                filters.Add(new TypeIdFilter(3, 1, 12, 1));

            if (checkEarring.Checked && checkChinese.Checked)
                filters.Add(new TypeIdFilter(3, 1, 5, 1));

            if (checkEarring.Checked && !checkEuropean.Checked && !checkChinese.Checked)
            {
                filters.Add(new TypeIdFilter(3, 1, 5, 1));
                filters.Add(new TypeIdFilter(3, 1, 12, 1));
            }

            #endregion Accessory

            #region Shields

            if (checkShield.Checked && checkChinese.Checked)
                filters.Add(new TypeIdFilter(3, 1, 4, 1));

            if (checkShield.Checked && checkEuropean.Checked)
                filters.Add(new TypeIdFilter(3, 1, 4, 2));

            if (checkShield.Checked && !checkEuropean.Checked && !checkChinese.Checked)
            {
                filters.Add(new TypeIdFilter(3, 1, 4, 1));
                filters.Add(new TypeIdFilter(3, 1, 4, 2));
            }

            #endregion Shields

            #endregion Equipment

            if (checkAlchemy.Checked)
                filters.AddRange(GetAlchemyFilters());

            if (checkQuest.Checked)
                filters.Add(new TypeIdFilter(p => (p as RefObjItem).IsQuest));

            if (checkAmmo.Checked)
            {
                filters.Add(new TypeIdFilter(3, 3, 4, 1));
                filters.Add(new TypeIdFilter(3, 3, 4, 2));
            }

            if (checkCoin.Checked)
                filters.Add(new TypeIdFilter(3, 3, 5, 1));

            if (checkOther.Checked)
                filters.Add(new TypeIdFilter { CompareByTypeID2 = true, TypeID2 = 3 });

            if (filters.Count == 0)
                filters.Add(new TypeIdFilter { CompareByTypeID1 = true, TypeID1 = 3 });

            var gender = ObjectGender.Neutral;

            if (checkMale.Checked && checkFemale.Checked)
            {
                /* nothing do anything, already neutral */
            }
            else if (checkMale.Checked)
            {
                gender = ObjectGender.Male;
            }
            else if (checkFemale.Checked)
            {
                gender = ObjectGender.Female;
            }

            var degreeFrom = Convert.ToByte(numDegreeFrom.Value);
            var degreeTo = Convert.ToByte(numDegreeTo.Value);
            var rareItems = checkBoxRareItems.Checked;
            var search = txtSellSearch.Text;
            var items = await Task.Run(() => Game.ReferenceManager.GetFilteredItems(
                filters, degreeFrom, degreeTo, gender, rareItems, search));
            if (IsDisposed || Disposing || version != _sellQueryVersion)
                return;

            if (items.Count == 0)
            {
                listFilter.Visible = true;
                MessageBox.Show(this, LanguageManager.GetLang("NoResultsFound"), "Warning");
                return;
            }

            await PopulateSellListAsync(items);
            labelResult.Text = $"{items.Count}";
        }
        catch (Exception ex)
        {
            Log.Fatal(ex);
            if (!IsDisposed && !Disposing && version == _sellQueryVersion)
                listFilter.Visible = true;
        }
    }

    /// <summary>
    ///     Populates the sell list.
    /// </summary>
    /// <param name="items">The items.</param>
    private Task PopulateSellListAsync(List<RefObjItem> items)
    {
        listFilter.BeginUpdate();
        try
        {
            // Looked up once per row; the filter lists are searched linearly otherwise
            var pickup = new Dictionary<string, bool>();
            foreach (var filter in PickupManager.PickupFilter)
                if (filter.CodeName != null)
                    pickup.TryAdd(filter.CodeName, filter.PickOnlyChar);

            var sell = ShoppingManager.SellFilter.ToHashSet();
            var store = ShoppingManager.StoreFilter.ToHashSet();
            var drop = ShoppingManager.DropFilter.ToHashSet();

            string getSubItemString(RefObjItem item)
            {
                if (!pickup.TryGetValue(item.CodeName, out var pickOnlyChar))
                    return "•";

                if (pickOnlyChar)
                    return "√ (C)";

                return "√";
            }

            var listViewItems = new ListViewItem[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];

                var listViewItem = new ListViewItem
                {
                    Text = item.GetRealName(true),
                    Tag = item.CodeName,
                    SubItems =
                    {
                        $"{item.ReqLevel1} (Dg.{item.Degree})",
                        ((ObjectGender)item.ReqGender).ToString(),
                        getSubItemString(item),
                        sell.Contains(item.CodeName) ? "√" : "•",
                        store.Contains(item.CodeName) ? "√" : "•",
                        drop.Contains(item.CodeName) ? "√" : "•",
                    },
                };

                listViewItems[i] = listViewItem;
            }

            listFilter.Items.AddRange(listViewItems);
        }
        finally
        {
            listFilter.EndUpdate();
        }

        //LoadSearchResultItemImagesAsync();

        listFilter.Visible = true;

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Gets the chinese gear filters.
    /// </summary>
    /// <returns></returns>
    private List<TypeIdFilter> GetAlchemyFilters()
    {
        var result = new List<TypeIdFilter>();

        for (byte i = 1; i <= 3; i++)
            result.Add(
                new TypeIdFilter
                {
                    TypeID1 = 3,
                    TypeID2 = 3,
                    TypeID3 = 10,
                    TypeID4 = i,
                }
            );

        for (byte i = 1; i <= 10; i++)
            result.Add(
                new TypeIdFilter
                {
                    TypeID1 = 3,
                    TypeID2 = 3,
                    TypeID3 = 11,
                    TypeID4 = i,
                }
            );

        return result;
    }

    /// <summary>
    ///     Fired when the core finished to load the game data
    /// </summary>
    private void OnLoadGameData()
    {
        _townCatalog = null;
    }

    /// <summary>
    ///     Loads the settings.
    /// </summary>
    private void LoadSettings()
    {
        _loadingSettings = true;

        try
        {
            checkEnable.Checked = PlayerConfig.Get("RSBot.Shopping.Enabled", true);
            checkRepairGear.Checked = PlayerConfig.Get("RSBot.Shopping.RepairGear", true);
            checkSellItemsFromPet.Checked = PlayerConfig.Get("RSBot.Shopping.SellPetItems", true);
            checkPickupGold.Checked = PlayerConfig.Get("RSBot.Items.Pickup.Gold", true);
            checkPickupRare.Checked = PlayerConfig.Get("RSBot.Items.Pickup.Rare", true);
            checkPickupBlue.Checked = PlayerConfig.Get("RSBot.Items.Pickup.Blue", true);
            checkEnableAbilityPet.Checked = PlayerConfig.Get("RSBot.Items.Pickup.EnableAbilityPet", true);
            checkStoreItemsFromPet.Checked = PlayerConfig.Get("RSBot.Shopping.StorePetItems", true);
            checkDontPickupInBerzerk.Checked = PlayerConfig.Get("RSBot.Items.Pickup.DontPickupInBerzerk", true);
            cbJustpickmyitems.Checked = PlayerConfig.Get("RSBot.Items.Pickup.JustPickMyItems", false);
            cbDontPickupWhileBotting.Checked = PlayerConfig.Get<bool>("RSBot.Items.Pickup.DontPickupWhileBotting");

            checkQuestItems.Checked = PlayerConfig.Get<bool>("RSBot.Items.Pickup.Quest", true);
            checkAllEquips.Checked = PlayerConfig.Get<bool>("RSBot.Items.Pickup.AnyEquips");
            checkEverything.Checked = PlayerConfig.Get<bool>("RSBot.Items.Pickup.Everything");

            ShoppingManager.Enabled = checkEnable.Checked;
            ShoppingManager.RepairGear = checkRepairGear.Checked;
            ShoppingManager.SellPetItems = checkSellItemsFromPet.Checked;
            ShoppingManager.StorePetItems = checkStoreItemsFromPet.Checked;

            ShoppingManager.LoadFilters();
            ShoppingManager.LoadBuyList();
            PickupManager.LoadFilter();

            LoadTownSettings();
            ShowConfiguredTownItems();
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private async void txtSellSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
            await QuerySellItemsAsync();
    }

    private void contextList_Opening(object sender, CancelEventArgs e)
    {
        var codeNames = GetSelectedFilterCodeNames();
        if (codeNames.Count == 0)
        {
            btnAddToSell.Checked = false;
            btnAddToStore.Checked = false;
            btnPickup.Checked = false;
            btnPickOnlyCharacter.Checked = false;
            _btnAddToDrop.Checked = false;
            return;
        }

        // A mark is shown only when every selected item has it.
        var sell = ShoppingManager.SellFilter.ToHashSet();
        var store = ShoppingManager.StoreFilter.ToHashSet();
        var pickup = PickupManager.PickupFilter.Where(p => !p.PickOnlyChar).Select(p => p.CodeName).ToHashSet();
        var pickupChar = PickupManager.PickupFilter.Where(p => p.PickOnlyChar).Select(p => p.CodeName).ToHashSet();

        btnAddToSell.Checked = codeNames.All(sell.Contains);
        btnAddToStore.Checked = codeNames.All(store.Contains);
        btnPickup.Checked = codeNames.All(pickup.Contains);
        btnPickOnlyCharacter.Checked = codeNames.All(pickupChar.Contains);
        _btnAddToDrop.Checked = codeNames.All(ShoppingManager.DropFilter.ToHashSet().Contains);
    }

    /// <summary>
    ///     Gets the code names of the selected filter items.
    /// </summary>
    private List<string> GetSelectedFilterCodeNames()
    {
        return listFilter.SelectedItems.Cast<ListViewItem>().Select(item => (string)item.Tag).ToList();
    }

    /// <summary>
    ///     Sets the text of a column for all selected filter items.
    /// </summary>
    private void SetSelectedFilterColumn(int column, string text)
    {
        listFilter.BeginUpdate();

        foreach (ListViewItem item in listFilter.SelectedItems)
            item.SubItems[column].Text = text;

        listFilter.EndUpdate();
    }

    /// <summary>
    ///     Adds or removes the code names from a filter list.
    /// </summary>
    private static void UpdateFilterList(List<string> filter, IEnumerable<string> codeNames, bool add)
    {
        var set = codeNames.ToHashSet();

        filter.RemoveAll(set.Contains);
        if (add)
            filter.AddRange(set);
    }

    private void listFilter_KeyDown(object sender, KeyEventArgs e)
    {
        if (!e.Control || e.KeyCode != Keys.A)
            return;

        listFilter.BeginUpdate();

        foreach (ListViewItem item in listFilter.Items)
            item.Selected = true;

        listFilter.EndUpdate();

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void listFilter_MouseDown(object sender, MouseEventArgs e)
    {
        _dragAnchorIndex = -1;

        if (e.Button != MouseButtons.Left || ModifierKeys != Keys.None)
            return;

        _dragAnchorIndex = listFilter.GetItemAt(e.X, e.Y)?.Index ?? -1;
    }

    /// <summary>
    ///     Selects all rows between the row the mouse was pressed on and the row under the mouse.
    /// </summary>
    private void listFilter_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragAnchorIndex < 0 || e.Button != MouseButtons.Left)
            return;

        var current = listFilter.GetItemAt(e.X, e.Y);
        if (current == null)
            return;

        var from = Math.Min(_dragAnchorIndex, current.Index);
        var to = Math.Max(_dragAnchorIndex, current.Index);

        listFilter.BeginUpdate();

        foreach (ListViewItem item in listFilter.Items)
        {
            var selected = item.Index >= from && item.Index <= to;
            if (item.Selected != selected)
                item.Selected = selected;
        }

        listFilter.EndUpdate();
        current.EnsureVisible();
    }

    #region Shopping manager

    // The legacy shopping controls are still created by the designer but replaced by the town tab (Main.Town.cs).
    private void comboStore_SelectedIndexChanged(object sender, EventArgs e) { }

    private void menuAddToShoppingList_Click(object sender, EventArgs e) { }

    private void menuRemoveItem_Click(object sender, EventArgs e) { }

    private void menuChangeAmount_Click(object sender, EventArgs e) { }

    private void checkShoppingSetting_CheckedChanged(object sender, EventArgs e)
    {
        if (_loadingSettings)
            return;

        ShoppingManager.RepairGear = checkRepairGear.Checked;
        PlayerConfig.Set("RSBot.Shopping.RepairGear", checkRepairGear.Checked);

        ShoppingManager.Enabled = checkEnable.Checked;
        PlayerConfig.Set("RSBot.Shopping.Enabled", checkEnable.Checked);

        PlayerConfig.Set("RSBot.Shopping.StorePetItems", checkStoreItemsFromPet.Checked);
        ShoppingManager.StorePetItems = checkStoreItemsFromPet.Checked;

        PlayerConfig.Set("RSBot.Shopping.SellPetItems", checkSellItemsFromPet.Checked);
        ShoppingManager.SellPetItems = checkSellItemsFromPet.Checked;
    }

    private void txtShopSearch_TextChanged(object sender, EventArgs e) { }

    #endregion Shopping manager

    #region SellFilter

    /// <summary>
    ///     Handles the Click event of the btnReload control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private async void btnApply_Click(object sender, EventArgs e)
    {
        await QuerySellItemsAsync();
    }

    /// <summary>
    ///     Handles the Click event of the btnAddToSell control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnAddToSell_Click(object sender, EventArgs e)
    {
        SetSelectedFilterColumn(4, "√");
        UpdateFilterList(ShoppingManager.SellFilter, GetSelectedFilterCodeNames(), true);

        ShoppingManager.SaveFilters();
    }

    /// <summary>
    ///     Handles the Click event of the btnPickup control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnPickup_Click(object sender, EventArgs e)
    {
        SetSelectedFilterColumn(3, "√");
        PickupManager.AddFilters(GetSelectedFilterCodeNames());
    }

    /// <summary>
    ///     Handles the Click event of the btnPickOnlyCharacter control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnPickOnlyCharacter_Click(object sender, EventArgs e)
    {
        SetSelectedFilterColumn(3, "√ (C)");
        PickupManager.AddFilters(GetSelectedFilterCodeNames(), true);
    }

    /// <summary>
    ///     Handles the Click event of the btnAddToStore control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnAddToStore_Click(object sender, EventArgs e)
    {
        SetSelectedFilterColumn(5, "√");
        UpdateFilterList(ShoppingManager.StoreFilter, GetSelectedFilterCodeNames(), true);

        ShoppingManager.SaveFilters();
    }

    /// <summary>
    ///     Handles the Click event of the btnDontSell control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnDontSell_Click(object sender, EventArgs e)
    {
        SetSelectedFilterColumn(4, "•");
        UpdateFilterList(ShoppingManager.SellFilter, GetSelectedFilterCodeNames(), false);

        ShoppingManager.SaveFilters();
    }

    /// <summary>
    ///     Handles the Click event of the btnDontStore control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnDontStore_Click(object sender, EventArgs e)
    {
        SetSelectedFilterColumn(5, "•");
        UpdateFilterList(ShoppingManager.StoreFilter, GetSelectedFilterCodeNames(), false);

        ShoppingManager.SaveFilters();
    }

    /// <summary>
    ///     Handles the Click event of the btnDontPickup control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnDontPickup_Click(object sender, EventArgs e)
    {
        SetSelectedFilterColumn(3, "•");
        PickupManager.RemoveFilters(GetSelectedFilterCodeNames());
    }

    /// <summary>
    ///     Handles the Click event of the btnResetFilter control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnResetFilter_Click(object sender, EventArgs e)
    {
        foreach (var group in filterPanel.Controls.OfType<GroupBox>())
        foreach (var checkBox in group.Controls.OfType<CheckBox>())
            checkBox.Checked = false;

        listFilter.Items.Clear();
        numDegreeFrom.Value = 0;
        numDegreeTo.Value = 0;
        labelResult.Text = string.Empty;
    }

    #endregion SellFilter

    #region Pickup

    /// <summary>
    ///     Handles the CheckedChanged event of the checkShowEquipment control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void checkShowEquipment_CheckedChanged(object sender, EventArgs e) { }

    private void checkPickupSettings_CheckedChanged(object sender, EventArgs e)
    {
        if (_loadingSettings)
            return;

        PlayerConfig.Set("RSBot.Items.Pickup.Everything", checkEverything.Checked);
        PlayerConfig.Set("RSBot.Items.Pickup.AnyEquips", checkAllEquips.Checked);
        PlayerConfig.Set("RSBot.Items.Pickup.Quest", checkQuestItems.Checked);
        PlayerConfig.Set("RSBot.Items.Pickup.DontPickupWhileAttacking", cbDontPickupWhileBotting.Checked);
        PlayerConfig.Set("RSBot.Items.Pickup.DontPickupWhileBotting", cbDontPickupWhileBotting.Checked);
        PlayerConfig.Set("RSBot.Items.Pickup.JustPickMyItems", cbJustpickmyitems.Checked);
        PlayerConfig.Set("RSBot.Items.Pickup.DontPickupInBerzerk", checkDontPickupInBerzerk.Checked);
        PlayerConfig.Set("RSBot.Items.Pickup.EnableAbilityPet", checkEnableAbilityPet.Checked);
        PlayerConfig.Set("RSBot.Items.Pickup.Blue", checkPickupBlue.Checked);
        PlayerConfig.Set("RSBot.Items.Pickup.Rare", checkPickupRare.Checked);
        PlayerConfig.Set("RSBot.Items.Pickup.Gold", checkPickupGold.Checked);
    }
    #endregion Pickup
}
