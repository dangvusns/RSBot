using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Extensions;
using RSBot.Core.Objects.Skill;
using SDUI.Controls;
using Button = SDUI.Controls.Button;
using CheckBox = SDUI.Controls.CheckBox;
using ListView = SDUI.Controls.ListView;
using ListViewExtensions = RSBot.Core.Extensions.ListViewExtensions;

namespace RSBot.Party.Views;

/// <summary>
///     The "Healing" tab: heal skills the Training bot casts when the player's or the party's HP drops (Training › HealingBundle).
/// </summary>
public partial class Main
{
    // Same keys as RSBot.Training.Bundle.Healing.HealingBundle
    private const string HealMemberEnabledKey = "RSBot.Party.Healing.Member.Enabled";
    private const string HealMemberPercentKey = "RSBot.Party.Healing.Member.Percent";
    private const string HealGroupEnabledKey = "RSBot.Party.Healing.Group.Enabled";
    private const string HealGroupPercentKey = "RSBot.Party.Healing.Group.Percent";
    private const string HealSelfEnabledKey = "RSBot.Party.Healing.Self.Enabled";
    private const string HealSelfPercentKey = "RSBot.Party.Healing.Self.Percent";
    private const string HealSkillsKey = "RSBot.Party.Healing.Skills";

    private CheckBox _checkHealMember;
    private CheckBox _checkHealGroup;
    private CheckBox _checkHealSelf;
    private NumUpDown _numHealMember;
    private NumUpDown _numHealGroup;
    private NumUpDown _numHealSelf;
    private ListView _listHealAvailable;
    private ListView _listHealSelected;

    /// <summary>
    ///     Builds the tab in code; sizes go through <see cref="ProtectPx" />.
    /// </summary>
    private void InitializeHealingTab()
    {
        var page = new TabPage
        {
            Name = "tpHealing",
            Text = ProtectText("HealingTab", "Healing"),
            BackColor = Color.White,
            Padding = new Padding(ProtectPx(8)),
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        for (var i = 0; i < 4; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(
            CreateHealRow(
                ProtectText("HealMember", "Heal party member when HP % drops below"),
                70,
                out _checkHealMember,
                out _numHealMember
            ),
            0,
            0
        );
        layout.Controls.Add(
            CreateHealRow(
                ProtectText("HealGroup", "Group heal all party members when average HP % drops below"),
                60,
                out _checkHealGroup,
                out _numHealGroup
            ),
            0,
            1
        );
        layout.Controls.Add(
            CreateHealRow(ProtectText("HealSelf", "Heal self when HP % drops below"), 60, out _checkHealSelf, out _numHealSelf),
            0,
            2
        );

        var info = new System.Windows.Forms.Label
        {
            Name = "labelHealingInfo",
            Text = ProtectText("HealingInfo", "Cast while the Training bot runs. The first skill of the list that fits is used: a skill with a target "
                    + "for a member, a party skill (e.g. Group Healing) for the group. Party members' HP is known in 10% steps."
            ),
            AutoSize = true,
            MaximumSize = new Size(ProtectPx(700), 0),
            UseMnemonic = false,
            Margin = new Padding(0, ProtectPx(4), 0, ProtectPx(8)),
        };
        layout.Controls.Add(info, 0, 3);

        _listHealAvailable = CreateHealList("listHealAvailable", ProtectText("HealAvailable", "Heal skills"));
        _listHealSelected = CreateHealList("listHealSelected", ProtectText("HealSelected", "Heal skills to use (in order)"));
        _listHealAvailable.DoubleClick += (s, e) => AddHealSkills();
        _listHealSelected.DoubleClick += (s, e) => RemoveHealSkills();

        var lists = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        lists.Controls.Add(_listHealAvailable, 0, 0);
        lists.Controls.Add(
            CreateHealButtons(
                ("buttonHealAdd", "►", AddHealSkills),
                ("buttonHealRemove", "◄", RemoveHealSkills),
                ("buttonHealRefresh", "↻", LoadHealingSkills)
            ),
            1,
            0
        );
        lists.Controls.Add(_listHealSelected, 2, 0);
        lists.Controls.Add(
            CreateHealButtons(
                ("buttonHealUp", "▲", () => MoveHealSkills(MoveDirection.Up)),
                ("buttonHealDown", "▼", () => MoveHealSkills(MoveDirection.Down))
            ),
            3,
            0
        );

        layout.Controls.Add(lists, 0, 4);
        page.Controls.Add(layout);

        tabMain.Controls.Add(page);
    }

    private Control CreateHealRow(string text, int defaultPercent, out CheckBox check, out NumUpDown number)
    {
        var row = new System.Windows.Forms.FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, ProtectPx(4)),
        };

        check = new CheckBox
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, ProtectPx(4), ProtectPx(8), 0),
        };
        check.CheckedChanged += (s, e) => SaveHealingSettings();

        number = new NumUpDown
        {
            Minimum = 1,
            Maximum = 99,
            Value = defaultPercent,
            Size = new Size(ProtectPx(80), ProtectPx(25)),
        };
        number.ValueChanged += (s, e) => SaveHealingSettings();

        row.Controls.Add(check);
        row.Controls.Add(number);

        return row;
    }

    private ListView CreateHealList(string name, string header)
    {
        var list = new ListView
        {
            Name = name,
            Dock = DockStyle.Fill,
            View = System.Windows.Forms.View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            SmallImageList = ListViewExtensions.StaticImageList,
        };
        list.Columns.Add(header, ProtectPx(240));

        return list;
    }

    private Control CreateHealButtons(params (string Name, string Text, Action OnClick)[] buttons)
    {
        var panel = new System.Windows.Forms.FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(ProtectPx(6), 0, ProtectPx(6), 0),
        };

        foreach (var (name, text, onClick) in buttons)
        {
            var button = new Button
            {
                Name = name,
                Text = text,
                AutoSize = false,
                Color = Color.Transparent,
                Radius = 6,
                Size = new Size(ProtectPx(48), ProtectPx(36)),
                Margin = new Padding(0, 0, 0, ProtectPx(8)),
            };
            button.Click += (s, e) => onClick();
            panel.Controls.Add(button);
        }

        return panel;
    }

    /// <summary>
    ///     Fills the list of learned skills that restore HP; called when the character is loaded.
    /// </summary>
    private void LoadHealingSkills()
    {
        if (_listHealAvailable == null || Game.Player?.Skills == null)
            return;

        _listHealAvailable.BeginUpdate();
        _listHealAvailable.Items.Clear();

        var skills = Game.Player.Skills.KnownSkills.Where(s =>
            s.Enabled && !s.IsPassive && !s.IsLowLevel() && s.TryGetRestoredStats(out var health, out _) && health
        );

        foreach (var skill in skills.OrderBy(s => s.Record.GetRealName()))
            _listHealAvailable.Items.Add(CreateHealSkillItem(skill));

        _listHealAvailable.EndUpdate();

        LoadHealingSelectedSkills();
    }

    private static ListViewItem CreateHealSkillItem(SkillInfo skill)
    {
        // The image is looked up from the SkillInfo tag
        var item = new ListViewItem(skill.Record.GetRealName()) { Tag = skill };
        item.LoadSkillImageAsync();

        return item;
    }

    /// <summary>
    ///     Gets the skill id of a heal list row; rows of skills not learned now keep the saved id.
    /// </summary>
    private static uint GetHealSkillId(ListViewItem item)
    {
        return item.Tag is SkillInfo skill ? skill.Id : (uint)item.Tag;
    }

    /// <summary>
    ///     Shows the saved heal skill list; the saved ids are mapped to the level learned now.
    /// </summary>
    private void LoadHealingSelectedSkills()
    {
        if (_listHealSelected == null)
            return;

        _listHealSelected.BeginUpdate();
        _listHealSelected.Items.Clear();

        foreach (var id in PlayerConfig.GetArray<uint>(HealSkillsKey))
        {
            var skill = Game.Player?.Skills?.FindLearnedSkill(id);
            _listHealSelected.Items.Add(skill != null ? CreateHealSkillItem(skill) : new ListViewItem($"Skill {id}") { Tag = id });
        }

        _listHealSelected.EndUpdate();
    }

    /// <summary>
    ///     Shows the saved healing settings; called from <c>LoadSettings</c>.
    /// </summary>
    private void LoadHealingSettings()
    {
        if (_checkHealMember == null)
            return;

        _checkHealMember.Checked = PlayerConfig.Get(HealMemberEnabledKey, false);
        _numHealMember.Value = Math.Clamp(PlayerConfig.Get(HealMemberPercentKey, 70), 1, 99);
        _checkHealGroup.Checked = PlayerConfig.Get(HealGroupEnabledKey, false);
        _numHealGroup.Value = Math.Clamp(PlayerConfig.Get(HealGroupPercentKey, 60), 1, 99);
        _checkHealSelf.Checked = PlayerConfig.Get(HealSelfEnabledKey, false);
        _numHealSelf.Value = Math.Clamp(PlayerConfig.Get(HealSelfPercentKey, 60), 1, 99);

        LoadHealingSkills();
    }

    private void SaveHealingSettings()
    {
        // Not while LoadSettings fills the controls
        if (!_applySettings || _checkHealMember == null)
            return;

        PlayerConfig.Set(HealMemberEnabledKey, _checkHealMember.Checked);
        PlayerConfig.Set(HealMemberPercentKey, (int)_numHealMember.Value);
        PlayerConfig.Set(HealGroupEnabledKey, _checkHealGroup.Checked);
        PlayerConfig.Set(HealGroupPercentKey, (int)_numHealGroup.Value);
        PlayerConfig.Set(HealSelfEnabledKey, _checkHealSelf.Checked);
        PlayerConfig.Set(HealSelfPercentKey, (int)_numHealSelf.Value);
        PlayerConfig.SetArray(HealSkillsKey, _listHealSelected.Items.OfType<ListViewItem>().Select(GetHealSkillId));

        // Saving reloads the Training bundles, which read the settings
        PlayerConfig.Save();
    }

    private void AddHealSkills()
    {
        var existing = _listHealSelected.Items.OfType<ListViewItem>().Select(GetHealSkillId).ToHashSet();
        var added = false;

        foreach (var item in _listHealAvailable.SelectedItems.OfType<ListViewItem>())
        {
            if (item.Tag is not SkillInfo skill || !existing.Add(skill.Id))
                continue;

            _listHealSelected.Items.Add(CreateHealSkillItem(skill));
            added = true;
        }

        if (added)
            SaveHealingSettings();
    }

    private void RemoveHealSkills()
    {
        if (_listHealSelected.SelectedItems.Count == 0)
            return;

        foreach (var item in _listHealSelected.SelectedItems.OfType<ListViewItem>().ToArray())
            _listHealSelected.Items.Remove(item);

        SaveHealingSettings();
    }

    private void MoveHealSkills(MoveDirection direction)
    {
        if (_listHealSelected.SelectedItems.Count == 0)
            return;

        _listHealSelected.MoveSelectedItems(direction);
        SaveHealingSettings();
    }
}
