using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using RSBot.Core.Extensions;
using RSBot.Core.Objects.Shopping;
using SDUI.Controls;
using ListViewExtensions = RSBot.Core.Extensions.ListViewExtensions;

namespace RSBot.Items.Views;

/// <summary>
///     The town tab: one list of every NPC item to keep in stock, and the town loop options.
/// </summary>
public partial class Main
{
    private const int ColumnLevel = 1;
    private const int ColumnQuantity = 2;

    /// <summary>
    ///     Every item sold by an NPC (no item mall), keyed by item code name.
    /// </summary>
    private List<TownCatalogItem> _townCatalog;

    private System.Windows.Forms.ListView _listTown;
    private SDUI.Controls.TextBox _txtTownSearch;
    private bool _populatingTown;

    private SDUI.Controls.CheckBox _checkSkipGuildStorage;
    private System.Windows.Forms.NumericUpDown _numGuildStorageRetry;
    private SDUI.Controls.CheckBox _checkStopIfInventoryFull;
    private SDUI.Controls.CheckBox _checkNoSpeedInScript;
    private SDUI.Controls.CheckBox _checkSellExcess;
    private SDUI.Controls.CheckBox _checkDropItems;
    private SDUI.Controls.CheckBox _checkRandomizeWalk;
    private System.Windows.Forms.NumericUpDown _numRandomizeWalkRange;

    private ToolStripMenuItem _btnAddToDrop;
    private ToolStripMenuItem _btnDontDrop;

    private sealed record TownCatalogItem(RefObjItem Item, string Name, string SoldAt);

    private int Px(int value) => LogicalToDeviceUnits(value);

    /// <summary>
    ///     Replaces the old per-trader shopping controls with the town buy list and options.
    /// </summary>
    private void InitializeTownTab()
    {
        tabBuyFilter.Text = "Town";
        tabBuyFilter.Controls.Remove(splitContainer);
        tabBuyFilter.Controls.Remove(separator5);

        var tabTown = new SDUI.Controls.TabControl { Dock = DockStyle.Fill, ItemSize = new Size(80, 24) };
        var pageBuy = new TabPage("Buy") { BackColor = Color.White, Padding = new Padding(Px(6)) };
        var pageOptions = new TabPage("Options") { BackColor = Color.White, Padding = new Padding(Px(10)) };
        tabTown.Controls.Add(pageBuy);
        tabTown.Controls.Add(pageOptions);

        BuildBuyPage(pageBuy);
        BuildOptionsPage(pageOptions);

        // Docking order: the fill control is added first so it takes the space left by the general setup box
        tabBuyFilter.Controls.Add(tabTown);
        tabTown.BringToFront();
    }

    private void BuildBuyPage(TabPage page)
    {
        _listTown = new System.Windows.Forms.ListView
        {
            Dock = DockStyle.Fill,
            View = System.Windows.Forms.View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.FixedSingle,
            SmallImageList = ListViewExtensions.StaticItemsImageList,
        };
        _listTown.Columns.Add("Name", Px(330));
        _listTown.Columns.Add("Level", Px(60));
        _listTown.Columns.Add("Quantity", Px(80));
        _listTown.Columns.Add("Sold at", Px(220));
        _listTown.ItemChecked += listTown_ItemChecked;
        _listTown.MouseDoubleClick += listTown_MouseDoubleClick;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Px(62),
            Padding = new Padding(0, Px(6), 0, 0),
            WrapContents = true,
        };

        _txtTownSearch = new SDUI.Controls.TextBox { Size = new Size(Px(260), Px(26)), MultiLine = false };
        _txtTownSearch.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.SuppressKeyPress = true;
            PopulateTownList(_txtTownSearch.Text);
        };

        var btnSearch = new SDUI.Controls.Button { Text = "Search", Size = new Size(Px(80), Px(26)) };
        btnSearch.Click += (_, _) => PopulateTownList(_txtTownSearch.Text);

        var btnClear = new SDUI.Controls.Button { Text = "Clear", Size = new Size(Px(80), Px(26)) };
        btnClear.Click += (_, _) => ClearTownList();

        var btnRefresh = new SDUI.Controls.Button { Text = "Refresh", Size = new Size(Px(80), Px(26)) };
        btnRefresh.Click += (_, _) =>
        {
            _txtTownSearch.Text = string.Empty;
            PopulateTownList(string.Empty);
        };

        var btnReset = new SDUI.Controls.Button { Text = "Reset", Size = new Size(Px(80), Px(26)) };
        btnReset.Click += (_, _) => ResetTownBuyList();

        var lblHint = new SDUI.Controls.Label
        {
            AutoSize = true,
            Text = "Double click an item to change its quantity, tick the box to buy it.",
            Margin = new Padding(Px(3), Px(4), 0, 0),
        };

        buttons.Controls.AddRange(new Control[] { _txtTownSearch, btnSearch, btnClear, btnRefresh, btnReset });
        buttons.SetFlowBreak(btnReset, true);
        buttons.Controls.Add(lblHint);

        page.Controls.Add(_listTown);
        page.Controls.Add(buttons);
    }

    private void BuildOptionsPage(TabPage page)
    {
        var y = Px(10);
        var rowHeight = Px(36);

        SDUI.Controls.CheckBox addCheck(string text)
        {
            var check = new SDUI.Controls.CheckBox
            {
                AutoSize = true,
                Location = new Point(Px(10), y),
                Text = text,
                UseVisualStyleBackColor = false,
            };
            check.CheckedChanged += (_, _) => SaveTownSettings();
            page.Controls.Add(check);

            y += rowHeight;
            return check;
        }

        System.Windows.Forms.NumericUpDown addNumber(string text, int max)
        {
            page.Controls.Add(
                new SDUI.Controls.Label
                {
                    AutoSize = true,
                    Location = new Point(Px(14), y + Px(4)),
                    Text = text,
                }
            );

            var number = new System.Windows.Forms.NumericUpDown
            {
                Location = new Point(Px(330), y),
                Size = new Size(Px(70), Px(26)),
                Minimum = 0,
                Maximum = max,
            };
            number.ValueChanged += (_, _) => SaveTownSettings();
            page.Controls.Add(number);

            y += rowHeight;
            return number;
        }

        _checkSkipGuildStorage = addCheck("Skip guild storage if a guild member is nearby");
        _numGuildStorageRetry = addNumber("Guild storage enter retry", 20);
        _checkStopIfInventoryFull = addCheck("Stop bot if the inventory is full when buying items");
        _checkNoSpeedInScript = addCheck("Do not use speed potions during the town script");
        _checkSellExcess = addCheck("Sell excess potions, arrows and bolts");
        _checkDropItems = addCheck("Drop items in town (items marked \"Drop\" in the item filter)");
        _checkRandomizeWalk = addCheck("Randomize walk coordinates of the town script");
        _numRandomizeWalkRange = addNumber("Randomize range (meters)", 10);
    }

    /// <summary>
    ///     Adds the drop column and menu entries to the item filter.
    /// </summary>
    private void InitializeDropFilter()
    {
        listFilter.Columns.Add("Drop");

        _btnAddToDrop = new ToolStripMenuItem("Drop in town") { ForeColor = Color.FromArgb(0, 0, 0) };
        _btnAddToDrop.Click += (_, _) => SetSelectedDropFilter(true);

        _btnDontDrop = new ToolStripMenuItem("Don't drop") { ForeColor = Color.FromArgb(0, 0, 0) };
        _btnDontDrop.Click += (_, _) => SetSelectedDropFilter(false);

        contextList.Items.Insert(contextList.Items.IndexOf(toolStripSeparator2), _btnAddToDrop);
        contextList.Items.Add(_btnDontDrop);
    }

    private void SetSelectedDropFilter(bool drop)
    {
        SetSelectedFilterColumn(6, drop ? "√" : "•");
        UpdateFilterList(ShoppingManager.DropFilter, GetSelectedFilterCodeNames(), drop);

        ShoppingManager.SaveFilters();
    }

    private void LoadTownSettings()
    {
        _checkSkipGuildStorage.Checked = PlayerConfig.Get("RSBot.Town.SkipGuildStorageIfMemberNearby", false);
        _numGuildStorageRetry.Value = Math.Clamp(PlayerConfig.Get("RSBot.Town.GuildStorageRetry", 3), 0, 20);
        _checkStopIfInventoryFull.Checked = PlayerConfig.Get("RSBot.Town.StopIfInventoryFull", false);
        _checkNoSpeedInScript.Checked = PlayerConfig.Get("RSBot.Town.NoSpeedInScript", false);
        _checkSellExcess.Checked = PlayerConfig.Get("RSBot.Town.SellExcess", false);
        _checkDropItems.Checked = PlayerConfig.Get("RSBot.Town.DropItems", false);
        _checkRandomizeWalk.Checked = PlayerConfig.Get("RSBot.Town.RandomizeWalk", false);
        _numRandomizeWalkRange.Value = Math.Clamp(PlayerConfig.Get("RSBot.Town.RandomizeWalkRange", 2), 0, 10);
    }

    private void SaveTownSettings()
    {
        if (_loadingSettings)
            return;

        PlayerConfig.Set("RSBot.Town.SkipGuildStorageIfMemberNearby", _checkSkipGuildStorage.Checked);
        PlayerConfig.Set("RSBot.Town.GuildStorageRetry", (int)_numGuildStorageRetry.Value);
        PlayerConfig.Set("RSBot.Town.StopIfInventoryFull", _checkStopIfInventoryFull.Checked);
        PlayerConfig.Set("RSBot.Town.NoSpeedInScript", _checkNoSpeedInScript.Checked);
        PlayerConfig.Set("RSBot.Town.SellExcess", _checkSellExcess.Checked);
        PlayerConfig.Set("RSBot.Town.DropItems", _checkDropItems.Checked);
        PlayerConfig.Set("RSBot.Town.RandomizeWalk", _checkRandomizeWalk.Checked);
        PlayerConfig.Set("RSBot.Town.RandomizeWalkRange", (int)_numRandomizeWalkRange.Value);
    }

    /// <summary>
    ///     Builds the list of every item NPCs sell, once per game data load.
    /// </summary>
    private List<TownCatalogItem> GetTownCatalog()
    {
        if (_townCatalog != null)
            return _townCatalog;

        var items = new Dictionary<string, TownCatalogItem>();

        foreach (var group in Game.ReferenceManager.ShopGroups.Values)
        {
            if (group.CodeName.Contains("MALL_"))
                continue;

            var goods = Game.ReferenceManager.GetRefShopGoods(group);
            if (goods == null)
                continue;

            foreach (var good in goods)
            {
                var itemCodeName = Game.ReferenceManager.GetRefPackageItem(good.RefPackageItemCodeName)?.RefItemCodeName;
                if (itemCodeName == null || itemCodeName.Contains("_MALL_") || items.ContainsKey(itemCodeName))
                    continue;

                var item = Game.ReferenceManager.GetRefItem(itemCodeName);
                if (item == null)
                    continue;

                var tab = Game.ReferenceManager.GetTab(good.RefTabCodeName);
                var soldAt = tab == null ? string.Empty : Game.ReferenceManager.GetTranslation(tab.StrID128_Tab);

                items[itemCodeName] = new TownCatalogItem(item, item.GetRealName(), soldAt);
            }
        }

        // Consumables first, then by type and level
        _townCatalog = items
            .Values.OrderByDescending(i => i.Item.TypeID2 == 3)
            .ThenBy(i => i.Item.TypeID2)
            .ThenBy(i => i.Item.TypeID3)
            .ThenBy(i => i.Item.TypeID4)
            .ThenBy(i => i.Item.ReqLevel1)
            .ThenBy(i => i.Name)
            .ToList();

        return _townCatalog;
    }

    /// <summary>
    ///     Shows the NPC items whose name contains the filter, or all of them if it's empty.
    /// </summary>
    private void PopulateTownList(string filter)
    {
        if (Game.ReferenceManager?.ShopGroups == null)
            return;

        var catalog = GetTownCatalog();
        if (!string.IsNullOrWhiteSpace(filter))
            catalog = catalog.Where(i => i.Name.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        FillTownList(catalog);
    }

    /// <summary>
    ///     Shows the items that are set to be bought.
    /// </summary>
    private void ShowConfiguredTownItems()
    {
        var configured = ShoppingManager.ShoppingList.Select(e => e.ItemCodeName).ToHashSet();

        FillTownList(GetTownCatalog().Where(i => configured.Contains(i.Item.CodeName)).ToList());
    }

    private void FillTownList(IList<TownCatalogItem> items)
    {
        _populatingTown = true;
        _listTown.BeginUpdate();
        _listTown.Items.Clear();

        var rows = new System.Windows.Forms.ListViewItem[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var catalogItem = items[i];
            var entry = ShoppingManager.ShoppingList.Find(e => e.ItemCodeName == catalogItem.Item.CodeName);

            var row = new System.Windows.Forms.ListViewItem(catalogItem.Name)
            {
                Name = catalogItem.Item.CodeName,
                Tag = catalogItem.Item,
                Checked = entry?.Enabled == true,
            };
            row.SubItems.Add(catalogItem.Item.ReqLevel1.ToString());
            row.SubItems.Add((entry?.Quantity ?? 0).ToString());
            row.SubItems.Add(catalogItem.SoldAt);
            row.LoadItemImageAsync(catalogItem.Item);

            rows[i] = row;
        }

        _listTown.Items.AddRange(rows);
        _listTown.EndUpdate();
        _populatingTown = false;
    }

    private void ClearTownList()
    {
        _listTown.Items.Clear();
        _txtTownSearch.Text = string.Empty;
    }

    private void ResetTownBuyList()
    {
        if (
            MessageBox.Show(this, "Reset all town buy settings?", "Town", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            != DialogResult.Yes
        )
            return;

        ShoppingManager.ShoppingList.Clear();
        ShoppingManager.SaveBuyList();

        _populatingTown = true;
        _listTown.BeginUpdate();

        foreach (System.Windows.Forms.ListViewItem row in _listTown.Items)
        {
            row.Checked = false;
            row.SubItems[ColumnQuantity].Text = "0";
        }

        _listTown.EndUpdate();
        _populatingTown = false;
    }

    private void listTown_ItemChecked(object sender, ItemCheckedEventArgs e)
    {
        if (_populatingTown)
            return;

        var entry = GetOrAddTownEntry(e.Item.Name);
        entry.Enabled = e.Item.Checked;

        SaveTownEntry(entry);
    }

    private void listTown_MouseDoubleClick(object sender, MouseEventArgs e)
    {
        var row = _listTown.HitTest(e.Location).Item;
        if (row?.Tag is not RefObjItem refItem)
            return;

        var entry = GetOrAddTownEntry(refItem.CodeName);

        var title = LanguageManager.GetLang("InputDialogTitle");
        var content = LanguageManager.GetLang("InputDialogContent");
        var itemName = LanguageManager.GetLang("InputDialogItemName", refItem.GetRealName(), refItem.MaxStack);
        var dialog = new InputDialog(title, itemName, content, InputDialog.InputType.Numeric);
        dialog.Numeric.Value = entry.Quantity;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            SaveTownEntry(entry);
            return;
        }

        entry.Quantity = Math.Max(0, Convert.ToInt32(dialog.Value));
        row.SubItems[ColumnQuantity].Text = entry.Quantity.ToString();

        // Setting a quantity on an unticked item enables it, like ticking it afterwards would
        if (entry.Quantity > 0 && !row.Checked)
        {
            _populatingTown = true;
            row.Checked = true;
            _populatingTown = false;

            entry.Enabled = true;
        }

        SaveTownEntry(entry);
    }

    private static TownBuyEntry GetOrAddTownEntry(string itemCodeName)
    {
        var entry = ShoppingManager.ShoppingList.Find(e => e.ItemCodeName == itemCodeName);
        if (entry != null)
            return entry;

        entry = new TownBuyEntry { ItemCodeName = itemCodeName };
        ShoppingManager.ShoppingList.Add(entry);

        return entry;
    }

    /// <summary>
    ///     Saves the buy list, dropping entries that neither buy anything nor keep a quantity.
    /// </summary>
    private static void SaveTownEntry(TownBuyEntry entry)
    {
        if (!entry.Enabled && entry.Quantity <= 0)
            ShoppingManager.ShoppingList.Remove(entry);

        ShoppingManager.SaveBuyList();
    }
}
