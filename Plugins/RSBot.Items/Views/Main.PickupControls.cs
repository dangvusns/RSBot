using System;
using System.Collections.Generic;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Components;
using CheckBox = SDUI.Controls.CheckBox;

namespace RSBot.Items.Views;

public partial class Main
{
    private CheckBox _separatePickupRules;
    private CheckBox _pausePlayerPickup;
    private CheckBox _pickupFallback;
    private SDUI.Controls.NumUpDown _petPickupRadius;
    private readonly Dictionary<(bool Pet, PickupCategories Category), CheckBox> _pickupCategories = new();

    private void InitializePickupControls()
    {
        var separate = new TabPage("Pet and character") { Name = "pickupSeparateRules", AutoScroll = true };
        tabMain.TabPages.Add(separate);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(Px(10)),
        };
        separate.Controls.Add(flow);
        _separatePickupRules = AddPickupCheck(flow, "pickupEnableSeparate", "Use separate pet and character categories",
            "SeparateRules");
        _pausePlayerPickup = AddPickupCheck(flow, "pickupPausePlayer", "Pause character pickup (pet continues)",
            "PausePlayer");
        _pickupFallback = AddPickupCheck(flow, "pickupFallback", "Character pickup when pet is unavailable",
            "FallbackWithoutPet");

        var radiusRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        radiusRow.Controls.Add(new SDUI.Controls.Label
        {
            Name = "pickupPetRadius", Text = "Pet radius (0 = training area)", AutoSize = true,
            Margin = new Padding(0, Px(6), Px(8), 0),
        });
        _petPickupRadius = new SDUI.Controls.NumUpDown
        {
            Name = "pickupRadiusValue", Minimum = 0, Maximum = 100, Width = Px(80),
        };
        _petPickupRadius.ValueChanged += (_, _) =>
        {
            if (!_loadingSettings)
                PlayerConfig.Set("RSBot.Items.Pickup.PetRadius", (int)_petPickupRadius.Value);
        };
        radiusRow.Controls.Add(_petPickupRadius);
        flow.Controls.Add(radiusRow);

        foreach (var pet in new[] { false, true })
        {
            var group = new SDUI.Controls.GroupBox
            {
                Name = pet ? "pickupPetCategories" : "pickupPlayerCategories",
                Text = pet ? "Pet categories" : "Character categories",
                AutoSize = true, Padding = new Padding(Px(10), Px(26), Px(10), Px(10)),
            };
            var categories = new FlowLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown, WrapContents = false,
            };
            group.Controls.Add(categories);
            foreach (var category in new[] { PickupCategories.Gold, PickupCategories.Rare, PickupCategories.Blue,
                         PickupCategories.Equipment, PickupCategories.Quest, PickupCategories.Everything })
            {
                var check = new CheckBox
                {
                    Name = "pickup" + (pet ? "Pet" : "Player") + category,
                    Text = category.ToString(), AutoSize = true,
                };
                _pickupCategories.Add((pet, category), check);
                check.CheckedChanged += (_, _) => SavePickupCategories(pet);
                categories.Controls.Add(check);
            }
            flow.Controls.Add(group);
        }
        _separatePickupRules.CheckedChanged += (_, _) => UpdatePickupCategoryControls();
        UpdatePickupCategoryControls();
    }

    private CheckBox AddPickupCheck(Control parent, string name, string text, string key)
    {
        var check = new CheckBox { Name = name, Text = text, AutoSize = true };
        check.CheckedChanged += (_, _) =>
        {
            if (!_loadingSettings)
                PlayerConfig.Set("RSBot.Items.Pickup." + key, check.Checked);
        };
        parent.Controls.Add(check);
        return check;
    }

    private void UpdatePickupCategoryControls()
    {
        foreach (var check in _pickupCategories.Values)
            check.Enabled = _separatePickupRules.Checked;
    }

    private void LoadPickupControls()
    {
        _separatePickupRules.Checked = PickupManager.SeparateRules;
        _pausePlayerPickup.Checked = PickupManager.PlayerPaused;
        _pickupFallback.Checked = PickupManager.FallbackWithoutPet;
        _petPickupRadius.Value = PickupManager.PetRadius;
        foreach (var entry in _pickupCategories)
            entry.Value.Checked = (PickupManager.Categories(entry.Key.Pet) & entry.Key.Category) != 0;
        UpdatePickupCategoryControls();
    }

    private void SavePickupCategories(bool pet)
    {
        if (_loadingSettings)
            return;
        var categories = PickupCategories.None;
        foreach (var entry in _pickupCategories)
            if (entry.Key.Pet == pet && entry.Value.Checked)
                categories |= entry.Key.Category;
        PlayerConfig.Set("RSBot.Items.Pickup." + (pet ? "PetCategories" : "PlayerCategories"), (int)categories);
    }
}
