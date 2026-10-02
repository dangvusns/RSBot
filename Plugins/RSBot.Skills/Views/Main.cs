using RSBot.Core;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Extensions;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Skill;
using RSBot.Skills.Components;
using SDUI.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CheckBox = SDUI.Controls.CheckBox;
using ListViewExtensions = RSBot.Core.Extensions.ListViewExtensions;

namespace RSBot.Skills.Views;

[ToolboxItem(false)]
public partial class Main : DoubleBufferedControl
{
    private System.Windows.Forms.Timer _buffTimer;
    private readonly HashSet<System.Windows.Forms.ListView> _coolingLists = new();
    private readonly System.Windows.Forms.ListView[] _cooldownViews;
    private readonly List<(string Name, Delegate Handler)> _subscriptions = new();

    /// <summary>
    ///     Shown behind the name of an opener skill in the attack skill list.
    /// </summary>
    private const string OpenerMarker = " [Opener]";

    /// <summary>
    ///     Shown behind the name of a buff that is only cast against strong monsters.
    /// </summary>
    private const string StrongTargetMarker = " [Strong mobs]";

    /// <summary>
    ///     Initializes a new instance of the <see cref="Main" /> class.
    /// </summary>
    public Main()
    {
        InitializeComponent();
        SubscribeEvents();
        _cooldownViews = new System.Windows.Forms.ListView[] { listActiveBuffs, listAttackingSkills, listBuffs, listSkills };
        checkAcceptResurrectionPartyOnly.Enabled = checkAcceptResurrection.Checked;
        checkAcceptResurrection.CheckedChanged += (s, e) =>
            checkAcceptResurrectionPartyOnly.Enabled = checkAcceptResurrection.Checked;

        listAttackingSkills.SmallImageList = ListViewExtensions.StaticImageList;
        listBuffs.SmallImageList = ListViewExtensions.StaticImageList;
        listSkills.SmallImageList = ListViewExtensions.StaticImageList;
        // Details view uses SmallImageList for item icons
        listActiveBuffs.SmallImageList = ListViewExtensions.StaticImageList;

        _lock = new object();

        // Start periodic invalidation for active-buff overlays
        _buffTimer = new() { Interval = 100 };
        _buffTimer.Tick += BuffTimer_Tick;
        VisibleChanged += (s, e) => { if (!IsDisposed && !Disposing) _buffTimer.Enabled = Visible && Enabled; };
        EnabledChanged += (s, e) => { if (!IsDisposed && !Disposing) _buffTimer.Enabled = Visible && Enabled; };
        _buffTimer.Enabled = Visible && Enabled;

        // Use owner-draw to reliably draw overlay on each item after default rendering
        listActiveBuffs.OwnerDraw = true;
        listActiveBuffs.DrawItem += ListSkill_DrawItem;

        // Also owner-draw attack and skill lists to render cooldown overlays on cast
        listAttackingSkills.OwnerDraw = true;
        listAttackingSkills.DrawItem += ListSkill_DrawItem;

        listBuffs.OwnerDraw = true;
        listBuffs.DrawItem += ListSkill_DrawItem;

        listSkills.OwnerDraw = true;
        listSkills.DrawItem += ListSkill_DrawItem;

        // Ensure timer is disposed when control is disposed
        Disposed += (s, e) =>
        {
            _buffTimer.Stop();
            _buffTimer.Dispose();
            foreach (var subscription in _subscriptions)
                EventManager.UnsubscribeEvent(subscription.Name, subscription.Handler);
            _subscriptions.Clear();
        };
    }

    /// <summary>
    ///     Subscribes the events.
    /// </summary>
    private void SubscribeViewEvent(string name, System.Action handler) =>
        SubscribeViewEvent(name, (Delegate)handler);

    private void SubscribeViewEvent(string name, Delegate handler)
    {
        _subscriptions.Add((name, handler));
        EventManager.SubscribeEvent(name, handler);
    }

    private void SubscribeEvents()
    {
        SubscribeViewEvent("OnLoadCharacter", OnLoadCharacter);

        SubscribeViewEvent("OnSkillLearned", new Action<SkillInfo>(OnSkillLearned));
        SubscribeViewEvent("OnSkillUpgraded", new Action<SkillInfo, SkillInfo>(OnSkillUpgraded));
        SubscribeViewEvent("OnWithdrawSkill", new Action<SkillInfo, SkillInfo>(OnWithdrawSkill));
        SubscribeViewEvent("OnLearnSkillMastery", new Action<MasteryInfo>(OnLearnSkillMastery));

        // Active-buff list mutations are marshalled to the UI thread: events fire on the network
        // thread, and BuffTimer_Tick enumerating the list concurrently throws inside ListView.
        SubscribeViewEvent("OnAddBuff", new Action<SkillInfo>(b => RunOnUiThread(() => OnAddBuff(b))));
        SubscribeViewEvent("OnRemoveBuff", new Action<SkillInfo>(b => RunOnUiThread(() => OnRemoveBuff(b))));
        SubscribeViewEvent("OnResurrectionRequest", OnResurrectionRequest);
        SubscribeViewEvent("OnExpSpUpdate", OnSpUpdated);
        SubscribeViewEvent("OnAddItemPerk",
            new Action<uint, uint>((t, k) => RunOnUiThread(() => OnAddItemPerk(t, k))));
        SubscribeViewEvent("OnRemoveItemPerk",
            new Action<uint, ItemPerk>((t, p) => RunOnUiThread(() => OnRemoveItemPerk(t, p))));
    }

    private void RunOnUiThread(System.Action action)
    {
        if (IsDisposed || Disposing)
            return;

        if (!IsHandleCreated || !InvokeRequired)
        {
            action();
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>
    ///     Called when [remove item perk].
    /// </summary>
    /// <param name="targetId">The target identifier.</param>
    /// <param name="removedPerk">The removed perk.</param>
    private void OnRemoveItemPerk(uint targetId, ItemPerk removedPerk)
    {
        if (targetId != Game.Player.UniqueId || removedPerk == null)
            return;

        for (var i = 0; i < listActiveBuffs.Items.Count; i++)
        {
            var listItem = listActiveBuffs.Items[i];

            if (listItem?.Tag is not ItemPerk perkInfo || perkInfo.Token != removedPerk.Token)
                continue;

            listItem.Remove();
            return;
        }
    }

    /// <summary>
    ///     Called when [add item perk].
    /// </summary>
    /// <param name="targetId">The target identifier.</param>
    /// <param name="token">The token.</param>
    private void OnAddItemPerk(uint targetId, uint token)
    {
        if (targetId != Game.Player.UniqueId)
            return;

        if (!Game.Player.State.ActiveItemPerks.TryGetValue(token, out var perk))
            return;

        var item = new ListViewItem { Text = perk.Item?.GetRealName(), Tag = perk };

        listActiveBuffs.Items.Add(item);

        Log.Debug($"OnAddItemPerk: added perk itemId={perk.ItemId} token={token}");
        item.LoadSkillImage();
        listActiveBuffs.Invalidate();
    }

    /// <summary>
    ///     Will be triggered if EXP/SP were gained. Increases the selected mastery level (if available)
    /// </summary>
    private void OnSpUpdated()
    {
        if (_selectedMastery == null || !checkLearnMastery.Checked)
            return;

        while (_selectedMastery.Level + numMasteryGap.Value < Game.Player.Level)
        {
            if (!checkLearnMasteryBotStopped.Checked && !Kernel.Bot.Running)
                break;

            var nextMasteryLevel = Game.ReferenceManager.GetRefLevel((byte)(_selectedMastery.Level + 1));

            if (nextMasteryLevel.Exp_M > Game.Player.SkillPoints)
            {
                Log.Debug(
                    $"Auto. upping mastery cancelled due to insufficient skill points. Required: {nextMasteryLevel.Exp_M}"
                );

                break;
            }

            Log.Notify($"Auto. train mastery [{_selectedMastery.Record.Name} to lv. {nextMasteryLevel}");
            LearnMasteryHandler.LearnMastery(_selectedMastery.Record.ID);
            Thread.Sleep(500);
        }
    }

    /// <summary>
    ///     Loads the settings.
    /// </summary>
    private void LoadSettings()
    {
        const string key = "RSBot.Skills.";

        foreach (var checkbox in panelPlayerSkills.Controls.OfType<CheckBox>())
            checkbox.Checked = PlayerConfig.Get(key + checkbox.Name, checkbox.Checked);

        foreach (var checkbox in groupBoxAttackingSkills.Controls.OfType<CheckBox>())
            checkbox.Checked = PlayerConfig.Get(key + checkbox.Name, checkbox.Checked);

        foreach (var checkbox in groupBoxAutomatedResurrection.Controls.OfType<CheckBox>())
            checkbox.Checked = PlayerConfig.Get(key + checkbox.Name, checkbox.Checked);

        foreach (var checkbox in groupBoxAdvancedBuff.Controls.OfType<CheckBox>())
            checkbox.Checked = PlayerConfig.Get(key + checkbox.Name, checkbox.Checked);

        foreach (var checkbox in grpMasteryUpdate.Controls.OfType<CheckBox>())
            checkbox.Checked = PlayerConfig.Get(key + checkbox.Name, checkbox.Checked);

        foreach (var num in grpMasteryUpdate.Controls.OfType<NumUpDown>())
            num.Value = PlayerConfig.Get(key + num.Name, num.Value);

        foreach (var num in groupBoxAutomatedResurrection.Controls.OfType<NumUpDown>())
            num.Value = PlayerConfig.Get(key + num.Name, num.Value);

        foreach (var checkbox in groupAdvancedSetup.Controls.OfType<CheckBox>())
            checkbox.Checked = PlayerConfig.Get(key + checkbox.Name, checkbox.Checked);
    }

    /// <summary>
    ///     Saves the settings.
    /// </summary>
    private void ApplySettings()
    {
        const string key = "RSBot.Skills.";
        foreach (var checkbox in panelPlayerSkills.Controls.OfType<CheckBox>())
            PlayerConfig.Set(key + checkbox.Name, checkbox.Checked);

        foreach (var checkbox in groupBoxAttackingSkills.Controls.OfType<CheckBox>())
            PlayerConfig.Set(key + checkbox.Name, checkbox.Checked);

        foreach (var checkbox in groupBoxAutomatedResurrection.Controls.OfType<CheckBox>())
            PlayerConfig.Set(key + checkbox.Name, checkbox.Checked);

        foreach (var checkbox in groupBoxAdvancedBuff.Controls.OfType<CheckBox>())
            PlayerConfig.Set(key + checkbox.Name, checkbox.Checked);

        foreach (var checkbox in grpMasteryUpdate.Controls.OfType<CheckBox>())
            PlayerConfig.Set(key + checkbox.Name, checkbox.Checked);

        foreach (var num in grpMasteryUpdate.Controls.OfType<NumUpDown>())
            PlayerConfig.Set(key + num.Name, num.Value);

        foreach (var num in groupBoxAutomatedResurrection.Controls.OfType<NumUpDown>())
            PlayerConfig.Set(key + num.Name, num.Value);

        foreach (var checkbox in groupAdvancedSetup.Controls.OfType<CheckBox>())
            PlayerConfig.Set(key + checkbox.Name, checkbox.Checked);
    }

    /// <summary>
    ///     Handles the CheckedChanged event of the settings control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void settings_CheckedChanged(object sender, EventArgs e)
    {
        if (_settingsLoaded)
            ApplySettings();
    }

    /// <summary>
    ///     Handles the ValueChanged event of the numSettings control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void numSettings_ValueChanged(object sender, EventArgs e)
    {
        if (_settingsLoaded)
            ApplySettings();
    }

    /// <summary>
    ///     Applies the attack skills.
    /// </summary>
    private void ApplyAttackSkills()
    {
        foreach (var collection in SkillManager.Skills.Values)
            collection.Clear();

        SkillManager.OpenerSkills.Clear();

        for (var i = 0; i < comboMonsterType.Items.Count; i++)
        {
            var rarity = GetRarityByIndex(i);
            var skillIds = PlayerConfig.GetArray<uint>("RSBot.Skills.Attacks_" + i);

            foreach (var skillId in skillIds)
            {
                var skillInfo = Game.Player.Skills.GetSkillInfoById(skillId);
                if (skillInfo == null)
                    continue;

                SkillManager.Skills[rarity].Add(skillInfo);
            }

            var openers = PlayerConfig.GetArray<uint>("RSBot.Skills.Openers_" + i);
            if (openers.Length > 0)
                SkillManager.OpenerSkills[rarity] = new HashSet<uint>(openers);
        }
    }

    /// <summary>
    ///     Gets the monster type of the entry at the specified index of the monster type combo box.
    /// </summary>
    /// <param name="index">The index.</param>
    private static MonsterRarity GetRarityByIndex(int index)
    {
        return index switch
        {
            1 => MonsterRarity.Champion,
            2 => MonsterRarity.Giant,
            3 => MonsterRarity.GeneralParty,
            4 => MonsterRarity.ChampionParty,
            5 => MonsterRarity.GiantParty,
            6 => MonsterRarity.Elite,
            7 => MonsterRarity.EliteStrong,
            8 => MonsterRarity.Unique,
            9 => MonsterRarity.Event,
            _ => MonsterRarity.General,
        };
    }

    /// <summary>
    ///     Applies the buff skills.
    /// </summary>
    private void ApplyBuffSkills()
    {
        SkillManager.Buffs.Clear();
        SkillManager.StrongTargetBuffs = new HashSet<uint>(PlayerConfig.GetArray<uint>("RSBot.Skills.StrongTargetBuffs"));

        Game.Player.TryGetAbilitySkills(out var abilitySkills);

        foreach (var buffId in PlayerConfig.GetArray<uint>("RSBot.Skills.Buffs"))
        {
            var skillInfo = Game.Player.Skills.GetSkillInfoById(buffId);
            if (skillInfo == null)
            {
                skillInfo = abilitySkills.FirstOrDefault(p => p.Id == buffId);
                if (skillInfo == null)
                    continue;
            }

            SkillManager.Buffs.Add(skillInfo);
        }
    }

    /// <summary>
    ///     Loads the masteries.
    /// </summary>
    private void LoadMasteries()
    {
        var selectedMastery = PlayerConfig.Get<string>("RSBot.Skills.selectedMastery");
        comboLearnMastery.BeginUpdate();
        comboLearnMastery.Items.Clear();

        foreach (var mastery in Game.Player.Skills.Masteries)
            comboLearnMastery.Items.Add(new MasteryComboBoxItem { Level = mastery.Level, Record = mastery.Record });

        foreach (MasteryComboBoxItem item in comboLearnMastery.Items)
            if (item.Record.NameCode == selectedMastery)
                comboLearnMastery.SelectedItem = item;

        comboLearnMastery.EndUpdate();

        comboLearnMastery.Update();
    }

    /// <summary>
    ///     Loads the available teleport skills into the combo box
    /// </summary>
    private void LoadTeleportSkills()
    {
        comboTeleportSkill.BeginUpdate();
        comboTeleportSkill.Items.Clear();

        var selectedTeleportSkill = PlayerConfig.Get<uint>("RSBot.Skills.TeleportSkill");
        foreach (
            var skill in Game.Player.Skills.KnownSkills.Where(s =>
                s.CanBeCasted && s.Record.Action_ActionDuration == 0 && s.Record.Params[2] == 500
            )
        )
        {
            var index = comboTeleportSkill.Items.Add(
                new TeleportSkillComboBoxItem { Level = skill.Record.Basic_Level, Record = skill.Record }
            );

            if (selectedTeleportSkill == skill.Record.ID)
            {
                comboTeleportSkill.SelectedIndex = index;
                SkillManager.TeleportSkill = skill;
            }
        }

        comboLearnMastery.EndUpdate();
    }

    /// <summary>
    ///     Loads the attacks.
    /// </summary>
    /// <param name="index">The index.</param>
    private void LoadAttacks(int index = 0)
    {
        lock (_lock)
        {
            listAttackingSkills.BeginUpdate();
            listAttackingSkills.Items.Clear();

            var skillArray = PlayerConfig.GetArray<uint>("RSBot.Skills.Attacks_" + index);
            var openers = PlayerConfig.GetArray<uint>("RSBot.Skills.Openers_" + index);
            foreach (var skillId in skillArray)
            {
                var skillInfo = Game.Player.Skills.GetSkillInfoById(skillId);
                if (skillInfo == null)
                    continue;

                var name = skillInfo.Record.GetRealName();
                if (openers.Contains(skillId))
                    name += OpenerMarker;

                var item = new ListViewItem(name) { Tag = skillInfo };
                item.SubItems.Add("lv. " + skillInfo.Record.Basic_Level);
                listAttackingSkills.Items.Add(item);
                item.LoadSkillImageAsync();
            }

            listAttackingSkills.EndUpdate();
        }
    }

    /// <summary>
    ///     Loads the buffs.
    /// </summary>
    private void LoadBuffs()
    {
        lock (_lock)
        {
            listBuffs.BeginUpdate();
            listBuffs.Items.Clear();

            Game.Player.TryGetAbilitySkills(out var abilitySkills);

            var buffs = PlayerConfig.GetArray<uint>("RSBot.Skills.Buffs");
            var strongTargetBuffs = PlayerConfig.GetArray<uint>("RSBot.Skills.StrongTargetBuffs");
            foreach (var buffId in buffs)
            {
                var buffInfo = Game.Player.Skills.GetSkillInfoById(buffId);
                if (buffInfo == null)
                {
                    buffInfo = abilitySkills.FirstOrDefault(p => p.Id == buffId);
                    if (buffInfo == null)
                        continue;
                }

                var name = buffInfo.Record.GetRealName();
                if (strongTargetBuffs.Contains(buffId))
                    name += StrongTargetMarker;

                var item = new ListViewItem(name) { Tag = buffInfo };

                item.SubItems.Add("lv. " + buffInfo.Record.Basic_Level);
                listBuffs.Items.Add(item);
                item.LoadSkillImageAsync();
            }

            listBuffs.EndUpdate();
        }
    }

    /// <summary>
    ///     Loads the imbues.
    /// </summary>
    private void LoadImbues()
    {
        lock (_lock)
        {
            comboImbue.Items.Clear();

            var selectedImbue = PlayerConfig.Get<int>("RSBot.Skills.Imbue");

            comboImbue.SelectedIndex = comboImbue.Items.Add("None");

            foreach (var skill in Game.Player.Skills.KnownSkills.Where(s => s.IsImbue && s.Enabled))
            {
                /*
                if (skill.IsLowLevel())
                    continue;
                */
                var index = comboImbue.Items.Add(skill);

                if (selectedImbue == 0)
                    continue;

                if (selectedImbue == skill.Id)
                    comboImbue.SelectedIndex = index;
            }
        }
    }

    /// <summary>
    ///     Loads the resurrection skills.
    /// </summary>
    private void LoadResurrectionSkills()
    {
        lock (_lock)
        {
            comboResurrectionSkill.Items.Clear();
            comboResurrectionSkill.Items.Add("None");

            foreach (
                var skill in Game.Player.Skills.KnownSkills.Where(s =>
                    s.Record != null
                    && ((s.Record.TargetEtc_SelectDeadBody && !s.Record.TargetGroup_Enemy_M) || s.Record.GroupID == 659)
                )
            ) //group res
            {
                if (skill.IsLowLevel())
                    continue;

                var index = comboResurrectionSkill.Items.Add(skill);
                var resurrectionSkillId = PlayerConfig.Get<int>("RSBot.Skills.ResurrectionSkill");
                if (skill.Id == resurrectionSkillId)
                    comboResurrectionSkill.SelectedIndex = index;
            }

            if (comboResurrectionSkill.SelectedIndex <= 0)
                comboResurrectionSkill.SelectedIndex = 0;
        }
    }

    /// <summary>
    ///     Loads the skills.
    /// </summary>
    private void LoadSkills()
    {
        lock (_lock)
        {
            var player = Game.Player;
            if (player == null)
                return;

            LoadTeleportSkills();
            LoadResurrectionSkills();
            LoadTeleportSkills();
            LoadImbues();
            LoadBuffs();
            LoadMasteries();
            LoadAttacks(comboMonsterType.SelectedIndex);

            listSkills.BeginUpdate();
            listSkills.Items.Clear();
            listSkills.Groups.Clear();

            if (Game.Player.TryGetAbilitySkills(out var abilitySkills))
            {
                var group = new ListViewGroup("Ability") { Tag = 0 };
                listSkills.Groups.Add(group);

                foreach (var skill in abilitySkills)
                {
                    if (skill.IsPassive)
                        continue;

                    var listViewItem = new ListViewItem(skill.Record.GetRealName()) { Tag = skill };
                    listViewItem.SubItems.Add("lv. " + skill.Record.Basic_Level);
                    listViewItem.Group = group;
                    listSkills.Items.Add(listViewItem);

                    listViewItem.LoadSkillImage();
                }
            }

            foreach (var mastery in player.Skills.Masteries)
            {
                var group = new ListViewGroup(
                    Game.ReferenceManager.GetTranslation(mastery.Record.NameCode) + " (lv. " + mastery.Level + ")"
                );
                group.Tag = mastery.Id;
                listSkills.Groups.Add(group);
            }

            foreach (
                var skill in player.Skills.KnownSkills.Where(s => s.Enabled && s.Record.ReqCommon_Mastery1 != 1000)
            )
            {
                if (skill.IsPassive)
                    continue;

                if (skill.IsImbue)
                    continue;

                if (checkHideLowerLevelSkills.Checked && skill.IsLowLevel())
                    continue;

                //if (!skill.IsAttack && skill.Record.Target_Required && !skill.Record.TargetGroup_Self)
                //continue;

                var name = skill.Record.GetRealName();

                var item = new ListViewItem(name) { Tag = skill };
                item.SubItems.Add("lv. " + skill.Record.Basic_Level);

                foreach (
                    var group in listSkills
                        .Groups.Cast<ListViewGroup>()
                        .Where(group => Convert.ToInt32(group.Tag) == skill.Record.ReqCommon_Mastery1)
                )
                    item.Group = group;

                if (skill.IsAttack && checkShowAttacks.Checked)
                    listSkills.Items.Add(item);
                else if (!skill.IsAttack && !skill.IsImbue && checkShowBuffs.Checked)
                    listSkills.Items.Add(item);

                item.LoadSkillImage();
            }

            listSkills.EndUpdate();
        }
    }

    /// <summary>
    ///     Saves the attacks.
    /// </summary>
    private void SaveAttacks()
    {
        var savedSkills = listAttackingSkills.Items.Cast<ListViewItem>().Select(p => ((SkillInfo)p.Tag).Id).ToArray();

        PlayerConfig.SetArray("RSBot.Skills.Attacks_" + comboMonsterType.SelectedIndex, savedSkills);

        // Removed skills are no openers anymore
        var openersKey = "RSBot.Skills.Openers_" + comboMonsterType.SelectedIndex;
        PlayerConfig.SetArray(openersKey, PlayerConfig.GetArray<uint>(openersKey).Intersect(savedSkills).ToArray());

        ApplyAttackSkills();
    }

    /// <summary>
    ///     Saves the buffs.
    /// </summary>
    private void SaveBuffs()
    {
        var savedBuffs = listBuffs.Items.Cast<ListViewItem>().Select(p => ((SkillInfo)p.Tag).Id).ToArray();

        PlayerConfig.SetArray("RSBot.Skills.Buffs", savedBuffs);

        // Removed buffs are no strong target buffs anymore
        PlayerConfig.SetArray(
            "RSBot.Skills.StrongTargetBuffs",
            PlayerConfig.GetArray<uint>("RSBot.Skills.StrongTargetBuffs").Intersect(savedBuffs).ToArray()
        );

        ApplyBuffSkills();
    }

    /// <summary>
    ///     Run the event after added the buff from the character
    /// </summary>
    /// <param name="buffInfo">The added <see cref="BuffInfo" /></param>
    private void OnAddBuff(SkillInfo buffInfo)
    {
        try
        {
            var item = new ListViewItem { Text = buffInfo.Record.GetRealName(), Tag = buffInfo };

            item.SubItems.Add("lv. " + buffInfo.Record.Basic_Level);

            listActiveBuffs.Items.Add(item);
            item.LoadSkillImageAsync();
        }
        catch { }
    }

    /// <summary>
    ///     Run the event after removed the buff from the character
    /// </summary>
    /// <param name="buffInfo">The removed <see cref="BuffInfo" /></param>
    private void OnRemoveBuff(SkillInfo removingBuff)
    {
        try
        {
            for (var i = 0; i < listActiveBuffs.Items.Count; i++)
            {
                var listItem = listActiveBuffs.Items[i];
                if (listItem == null)
                    continue;

                var itemBuffInfo = listItem.Tag as SkillInfo;
                // The token identifies the active buff, its id can differ from the removed skill's id
                if (itemBuffInfo != null && itemBuffInfo.Token == removingBuff.Token)
                {
                    listItem?.Remove();
                    return;
                }
            }
        }
        catch { }
    }

    private void BuffTimer_Tick(object sender, EventArgs e)
    {
        if (!Visible || !Enabled || FindForm()?.WindowState == FormWindowState.Minimized) return;
        foreach (var list in _cooldownViews)
        {
            if (!list.Visible) continue;
            var cooling = list.Items.Cast<ListViewItem>()
                .Any(item => item.Tag is SkillInfo skill && skill.HasCooldown);
            // One final repaint removes an overlay after its cooldown expires.
            if (cooling || _coolingLists.Remove(list)) list.Invalidate();
            if (cooling) _coolingLists.Add(list);
        }
    }

    private static void DrawRectCooldown(Graphics graphics, Rectangle rect, float percent, Color color)
    {
        percent = Math.Clamp(percent, 0f, 1f);
        if (percent <= 0f || rect.Width <= 0 || rect.Height <= 0)
            return;

        var fade = Math.Min(1f, percent / 0.15f);
        using var brush = new SolidBrush(Color.FromArgb((int)(160 * fade), color));
        if (percent >= 1f)
        {
            graphics.FillRectangle(brush, rect);
            return;
        }

        // A large pie clipped to the icon retains a rectangular countdown without a temporary bitmap.
        var state = graphics.Save();
        try
        {
            graphics.SetClip(rect, CombineMode.Intersect);
            var radius = (float)Math.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height);
            graphics.FillPie(brush,
                rect.Left + rect.Width / 2f - radius,
                rect.Top + rect.Height / 2f - radius,
                radius * 2, radius * 2, -90, -360 * percent);
        }
        finally { graphics.Restore(state); }
    }

    private void ListSkill_DrawItem(object sender, DrawListViewItemEventArgs e)
    {
        try
        {
            var listView = (SDUI.Controls.ListView)sender;
            var item = e.Item;
            if (item?.Tag is not SkillInfo skill)
                return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // background
            e.DrawBackground();

            // --- ICON RECT
            Rectangle iconRect = listView.GetItemRect(item.Index, ItemBoundsPortion.Icon);

            // --- DRAW ICON
            Image img = null;
            if (!string.IsNullOrEmpty(item.ImageKey) &&
                listView.SmallImageList?.Images.ContainsKey(item.ImageKey) == true)
            {
                img = listView.SmallImageList.Images[item.ImageKey];
            }
            else if (item.ImageIndex >= 0 &&
                     listView.SmallImageList != null &&
                     item.ImageIndex < listView.SmallImageList.Images.Count)
            {
                img = listView.SmallImageList.Images[item.ImageIndex];
            }

            if (img != null)
                g.DrawImage(img, iconRect);

            // --- TEXT (skill name)
            Rectangle textRect = new Rectangle(
                iconRect.Right + 6,
                e.Bounds.Top,
                e.Bounds.Width - (iconRect.Right - e.Bounds.Left) - 6,
                e.Bounds.Height);

            TextRenderer.DrawText(
                g,
                item.Text,
                listView.Font,
                textRect,
                listView.ForeColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);

            // --- COOLDOWN RECT
            int size = Math.Min(iconRect.Width, iconRect.Height);
            Rectangle cooldownRect = new Rectangle(
                iconRect.Left + (iconRect.Width - size) / 2,
                iconRect.Top + (iconRect.Height - size) / 2,
                size,
                size);

            var percent = (float)skill.CooldownPercent;
            if (percent <= 0f)
                return;

            using var bgbrush = new SolidBrush(Color.FromArgb(100, Color.Black));
            g.FillRectangle(bgbrush, cooldownRect);

            DrawRectCooldown(
                g,
                cooldownRect,
                percent,
                Color.Black);


            // --- CENTERED SECONDS TEXT
            int seconds = (int)Math.Ceiling(skill.CooldownRemainingMilliseconds / 1000f);
            string label = seconds > 60 ? (seconds / 60f).ToString("0.0") : seconds.ToString();

            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            using var textBrush = new SolidBrush(Color.White);

            g.DrawString(label, listView.Font, textBrush, cooldownRect, sf);
        }
        catch
        {
            // deliberately swallow – draw loop must never crash
        }
    }


    /// <summary>
    ///     Check the skills upgraded or withdrawn
    /// </summary>
    /// <param name="oldSkill">The old skill</param>
    /// <param name="newSkill">The new skill</param>
    private void CheckSkillWithdrawnOrUpgraded(SkillInfo oldSkill, SkillInfo newSkill)
    {
        for (var i = 0; i < comboMonsterType.Items.Count; i++)
        {
            var skills = PlayerConfig.GetArray<uint>($"RSBot.Skills.Attacks_{i}").ToList();
            var index = skills.IndexOf(oldSkill.Id);
            if (index == -1)
                continue;

            if (oldSkill.Id == newSkill.Id)
                skills.RemoveAt(index);
            else
                skills[index] = newSkill.Id;

            PlayerConfig.SetArray($"RSBot.Skills.Attacks_{i}", skills);
        }

        var buffs = PlayerConfig.GetArray<uint>("RSBot.Skills.Buffs").ToList();
        var buffIndex = buffs.IndexOf(oldSkill.Id);
        if (buffIndex != -1)
        {
            // remove skill
            if (newSkill.Id == oldSkill.Id)
                buffs.RemoveAt(buffIndex);
            else
                buffs[buffIndex] = newSkill.Id;

            PlayerConfig.SetArray("RSBot.Skills.Buffs", buffs);
        }

        var resurrectionSkill = PlayerConfig.Get<uint>("RSBot.Skills.ResurrectionSkill");
        if (resurrectionSkill == oldSkill.Id)
        {
            if (oldSkill.Id == newSkill.Id)
                SkillManager.ResurrectionSkill = null;
            else
                resurrectionSkill = newSkill.Id;

            PlayerConfig.Set("RSBot.Skills.ResurrectionSkill", resurrectionSkill);
        }

        var selectedImbue = PlayerConfig.Get<uint>("RSBot.Skills.Imbue");
        if (selectedImbue == oldSkill.Id)
        {
            if (oldSkill.Id == newSkill.Id)
                SkillManager.ImbueSkill = null;
            else
                selectedImbue = newSkill.Id;

            PlayerConfig.Set("RSBot.Skills.Imbue", selectedImbue);
        }

        var selectedTeleportSkill = PlayerConfig.Get<uint>("RSBot.Skills.TeleportSkill");
        if (selectedTeleportSkill == oldSkill.Id)
        {
            if (oldSkill.Id == newSkill.Id)
                SkillManager.TeleportSkill = null;
            else
                selectedTeleportSkill = newSkill.Id;

            PlayerConfig.Set("RSBot.Skills.TeleportSkill", selectedTeleportSkill);
        }

        LoadSkills();
        ApplyAttackSkills();
        ApplyBuffSkills();

        PlayerConfig.Save();
    }

    /// <summary>
    ///     Call after skill learned
    /// </summary>
    /// <param name="learnedSkill"></param>
    private void OnSkillLearned(SkillInfo learnedSkill)
    {
        Log.NotifyLang("SkillLearned", learnedSkill.Record.GetRealName());
        LoadSkills();
    }

    /// <summary>
    ///     Call after learned skill upgraded
    /// </summary>
    /// <param name="skill">The old skill.</param>
    /// <param name="newSkill">The new skill.</param>
    private void OnSkillUpgraded(SkillInfo oldSkill, SkillInfo newSkill)
    {
        Log.NotifyLang("SkillUpgraded", newSkill);

        CheckSkillWithdrawnOrUpgraded(oldSkill, newSkill);
    }

    /// <summary>
    ///     Core_s the on withdraw skill.
    /// </summary>
    /// <param name="oldSkill">The old skill.</param>
    /// <param name="newSkill">The new skill.</param>
    private void OnWithdrawSkill(SkillInfo oldSkill, SkillInfo newSkill)
    {
        Log.NotifyLang("SkillWithdrawn", oldSkill);

        CheckSkillWithdrawnOrUpgraded(oldSkill, newSkill);
    }

    /// <summary>
    ///     Core_s the on learn skill mastery.
    /// </summary>
    /// <param name="info">The information.</param>
    private void OnLearnSkillMastery(MasteryInfo info)
    {
        Log.NotifyLang("MasteryUpgraded", info.Record.Name);

        LoadSkills();
    }

    /// <summary>
    ///     Main_s the on load character.
    /// </summary>
    private void OnLoadCharacter()
    {
        comboMonsterType.SelectedIndex = 0;

        UpdateSavedSkillLevels();
        LoadSkills();

        ApplyAttackSkills();
        ApplyBuffSkills();

        RunOnUiThread(listActiveBuffs.Items.Clear);
    }

    /// <summary>
    ///     The config stores the id of one skill level. If a skill was upgraded or withdrawn while the bot was not
    ///     watching (another bot, the plain client, a copied profile), that id is no longer learned and the skill
    ///     would silently drop out. Map such ids to the level that is learned now and save them.
    /// </summary>
    private void UpdateSavedSkillLevels()
    {
        var changed = false;

        uint Resolve(uint savedId)
        {
            if (savedId == 0 || Game.Player.Skills.HasSkill(savedId))
                return savedId;

            // Item ability skills (e.g. Devil's Spirit) are not learned skills; they stay as they are.
            var learned = Game.Player.Skills.FindLearnedSkill(savedId);
            if (learned == null || learned.Id == savedId)
                return savedId;

            Log.Notify($"[Skills] {learned.Record.GetRealName()} updated to lv. {learned.Record.Basic_Level}");
            changed = true;

            return learned.Id;
        }

        var arrayKeys = Enumerable
            .Range(0, comboMonsterType.Items.Count)
            .Select(i => "RSBot.Skills.Attacks_" + i)
            .Append("RSBot.Skills.Buffs");

        foreach (var key in arrayKeys)
        {
            var ids = PlayerConfig.GetArray<uint>(key).ToArray();
            var resolved = ids.Select(Resolve).ToArray();
            if (!resolved.SequenceEqual(ids))
                PlayerConfig.SetArray(key, resolved);
        }

        string[] singleKeys =
        {
            "RSBot.Skills.Imbue",
            "RSBot.Skills.ResurrectionSkill",
            "RSBot.Skills.TeleportSkill",
            "RSBot.Protection.HpSkill",
            "RSBot.Protection.MpSkill",
            "RSBot.Protection.BadStatusSkill",
        };

        foreach (var key in singleKeys)
        {
            var id = PlayerConfig.Get<uint>(key);
            var resolved = Resolve(id);
            if (resolved != id)
                PlayerConfig.Set(key, resolved);
        }

        if (changed)
            PlayerConfig.Save();
    }

    /// <summary>
    ///     Core_s the on resurrection request
    /// </summary>
    private void OnResurrectionRequest()
    {
        const string key = "RSBot.Skills.";
        var request = Game.AcceptanceRequest;
        if (request == null || !PlayerConfig.Get<bool>(key + checkAcceptResurrection.Name))
            return;

        // Party only: the requester has to be visible and a member of the own party
        if (PlayerConfig.Get<bool>(key + checkAcceptResurrectionPartyOnly.Name))
        {
            var name = request.Player?.Name;
            if (name == null || Game.Party?.GetMemberByName(name) == null)
                return;
        }

        request.Accept();
    }

    /// <summary>
    ///     Handles the Click event of the btnMoveAttackSkillDown control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnMoveAttackSkillDown_Click(object sender, EventArgs e)
    {
        listAttackingSkills.MoveSelectedItems(MoveDirection.Down);
        SaveAttacks();
    }

    /// <summary>
    ///     Handles the Click event of the btnMoveAttackSkillUp control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnMoveAttackSkillUp_Click(object sender, EventArgs e)
    {
        listAttackingSkills.MoveSelectedItems(MoveDirection.Up);
        SaveAttacks();
    }

    /// <summary>
    ///     Handles the Click event of the btnMoveBuffSkillDown control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnMoveBuffSkillDown_Click(object sender, EventArgs e)
    {
        listBuffs.MoveSelectedItems(MoveDirection.Down);
        SaveBuffs();
    }

    /// <summary>
    ///     Handles the Click event of the btnMoveBuffSkillUp control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnMoveBuffSkillUp_Click(object sender, EventArgs e)
    {
        listBuffs.MoveSelectedItems(MoveDirection.Up);
        SaveBuffs();
    }

    /// <summary>
    ///     Handles the Click event of the btnRemoveAttackSkill control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnRemoveAttackSkill_Click(object sender, EventArgs e)
    {
        foreach (ListViewItem item in listAttackingSkills.SelectedItems)
            item.Remove();

        SaveAttacks();
    }

    /// <summary>
    ///     Handles the Click event of the btnRemoveBuffSkill control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void btnRemoveBuffSkill_Click(object sender, EventArgs e)
    {
        foreach (ListViewItem item in listBuffs.SelectedItems)
            item.Remove();

        SaveBuffs();
    }

    /// <summary>
    ///     Handles the Opening event of the attack skill context menu.
    /// </summary>
    private void attackSkillContextMenu_Opening(object sender, CancelEventArgs e)
    {
        var selected = GetSelectedSkillIds(listAttackingSkills);
        if (selected.Length == 0)
        {
            e.Cancel = true;
            return;
        }

        var openers = PlayerConfig.GetArray<uint>("RSBot.Skills.Openers_" + comboMonsterType.SelectedIndex);
        menuToggleOpener.Checked = selected.All(id => openers.Contains(id));
    }

    /// <summary>
    ///     Marks the selected attack skills as openers, or removes the mark when all of them are openers already.
    /// </summary>
    private void menuToggleOpener_Click(object sender, EventArgs e)
    {
        var key = "RSBot.Skills.Openers_" + comboMonsterType.SelectedIndex;
        PlayerConfig.SetArray(key, ToggleIds(PlayerConfig.GetArray<uint>(key), GetSelectedSkillIds(listAttackingSkills)));

        LoadAttacks(comboMonsterType.SelectedIndex);
        ApplyAttackSkills();
    }

    /// <summary>
    ///     Handles the Opening event of the buff context menu.
    /// </summary>
    private void buffContextMenu_Opening(object sender, CancelEventArgs e)
    {
        var selected = GetSelectedSkillIds(listBuffs);
        if (selected.Length == 0)
        {
            e.Cancel = true;
            return;
        }

        var strongTargetBuffs = PlayerConfig.GetArray<uint>("RSBot.Skills.StrongTargetBuffs");
        menuToggleStrongTargetBuff.Checked = selected.All(id => strongTargetBuffs.Contains(id));
    }

    /// <summary>
    ///     Limits the selected buffs to strong monsters, or removes the limit when all of them are limited already.
    /// </summary>
    private void menuToggleStrongTargetBuff_Click(object sender, EventArgs e)
    {
        const string key = "RSBot.Skills.StrongTargetBuffs";
        PlayerConfig.SetArray(key, ToggleIds(PlayerConfig.GetArray<uint>(key), GetSelectedSkillIds(listBuffs)));

        LoadBuffs();
        ApplyBuffSkills();
    }

    private static uint[] GetSelectedSkillIds(System.Windows.Forms.ListView listView)
    {
        return listView.SelectedItems.Cast<ListViewItem>().Select(p => ((SkillInfo)p.Tag).Id).ToArray();
    }

    /// <summary>
    ///     Removes the selected ids if all of them are set, otherwise adds the missing ones.
    /// </summary>
    private static uint[] ToggleIds(uint[] current, uint[] selected)
    {
        return selected.All(id => current.Contains(id)) ? current.Except(selected).ToArray() : current.Union(selected).ToArray();
    }

    /// <summary>
    ///     Handles the SelectedIndexChanged event of the comboImue control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void comboImbue_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (comboImbue.SelectedIndex < 0)
            return;

        SkillInfo imbue = null;

        if (comboImbue.SelectedIndex > 0)
            imbue = comboImbue.SelectedItem as SkillInfo;

        SkillManager.ImbueSkill = imbue;
        PlayerConfig.Set("RSBot.Skills.Imbue", imbue == null ? 0 : imbue.Id);
    }

    /// <summary>
    ///     Handles the SelectedIndexChanged event of the comboMonsterType control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void comboMonsterType_SelectedIndexChanged(object sender, EventArgs e)
    {
        LoadAttacks(comboMonsterType.SelectedIndex);
    }

    /// <summary>
    ///     Handles the SelectedIndexChanged event of the comboResurrectionSkill control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void comboResurrectionSkill_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (comboResurrectionSkill.SelectedIndex < 0)
            return;

        SkillInfo skill = null;

        if (comboResurrectionSkill.SelectedIndex > 0)
            skill = comboResurrectionSkill.SelectedItem as SkillInfo;

        SkillManager.ResurrectionSkill = skill;
        PlayerConfig.Set("RSBot.Skills.ResurrectionSkill", skill == null ? 0 : skill.Id);
    }

    /// <summary>
    ///     Handles the CheckedChanged event of the filters control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void Filter_CheckedChanged(object sender, EventArgs e)
    {
        if (_settingsLoaded)
            ApplySettings();

        LoadSkills();
    }

    /// <summary>
    ///     Handles the Click event of the menuAddAttack control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void menuAddAttack_Click(object sender, EventArgs e)
    {
        foreach (ListViewItem item in listSkills.SelectedItems)
        {
            var selectedRefSkill = item.Tag as SkillInfo;

            if (
                listAttackingSkills
                    .Items.Cast<ListViewItem>()
                    .Any(p =>
                        ((SkillInfo)p.Tag).Record.Action_Overlap != 0
                        && ((SkillInfo)p.Tag).Record.Action_Overlap == selectedRefSkill.Record.Action_Overlap
                    )
            )
                continue;

            //if (selectedRefSkill != null && selectedRefSkill.IsAttack)
            if (selectedRefSkill != null && (selectedRefSkill.Record.TargetGroup_Enemy_M || selectedRefSkill.IsAttack))
                listAttackingSkills.Items.Add((ListViewItem)item.Clone());
        }

        SaveAttacks();
    }

    /// <summary>
    ///     Handles the Click event of the menuAddBuff control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
    private void menuAddBuff_Click(object sender, EventArgs e)
    {
        foreach (ListViewItem item in listSkills.SelectedItems)
        {
            var selectedRefSkill = item.Tag as SkillInfo;
            if (
                listBuffs
                    .Items.Cast<ListViewItem>()
                    .Any(p =>
                        ((SkillInfo)p.Tag).Record.Action_Overlap != 0
                        && ((SkillInfo)p.Tag).Record.Action_Overlap == selectedRefSkill.Record.Action_Overlap
                    )
            )
                continue;

            if (selectedRefSkill != null && !selectedRefSkill.IsAttack && !selectedRefSkill.Record.TargetGroup_Enemy_M)
                listBuffs.Items.Add((ListViewItem)item.Clone());
        }

        SaveBuffs();
    }

    private void comboLearnMastery_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (comboLearnMastery.SelectedIndex < 0)
            return;

        var selectedItem = (MasteryComboBoxItem)comboLearnMastery.SelectedItem;
        _selectedMastery = selectedItem;

        PlayerConfig.Set("RSBot.Skills.selectedMastery", selectedItem.Record.NameCode);
    }

    private void listSkills_MouseDoubleClick(object sender, MouseEventArgs e)
    {
        if (listSkills.SelectedItems.Count <= 0)
            return;

        if (!Kernel.Debug)
            return;

        if (listSkills.SelectedItems[0].Tag is not SkillInfo skillInfo)
            return;

        var itemForm = new SkillProperties(skillInfo.Record);
        itemForm.Show();
    }

    private void useToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (listSkills.SelectedItems.Count <= 0)
            return;

        if (listSkills.SelectedItems[0].Tag is not SkillInfo skillInfo)
            return;

        if (skillInfo.IsAttack)
            return;

        skillInfo.Cast(buff: true);
    }

    private void skillContextMenu_Opening(object sender, CancelEventArgs e)
    {
        useToPartyMemberToolStripMenuItem.DropDownItems.Clear();

        if (!Game.Party.IsInParty)
            return;

        foreach (var member in Game.Party.Members)
        {
            if (member == null)
                return;

            useToPartyMemberToolStripMenuItem.DropDown.Items.Add(
                member.Name,
                null,
                (menuItemSender, _2) =>
                {
                    try
                    {
                        if (listSkills.SelectedItems.Count <= 0)
                            return;

                        if (listSkills.SelectedItems[0].Tag is not SkillInfo skillInfo)
                            return;

                        if (skillInfo.IsAttack)
                            return;

                        var menuItem = menuItemSender as ToolStripMenuItem;
                        if (menuItem == null)
                            return;

                        var member = Game.Party.GetMemberByName(menuItem.Text);
                        if (member == null)
                            return;

                        skillInfo.Cast(member.Player.UniqueId, true);
                    }
                    catch { }
                }
            );
        }
    }

    private void listActiveBuffs_MouseDoubleClick(object sender, MouseEventArgs e)
    {
        if (!Kernel.Debug)
            return;

        var propertiesWindow = listActiveBuffs.SelectedItems[0].Tag switch
        {
            SkillInfo skillInfo => new BuffProperties(skillInfo),
            ItemPerk itemPerk => new BuffProperties(itemPerk),
            _ => null,
        };

        propertiesWindow?.Show();
    }

    private void comboTeleportSkill_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (comboTeleportSkill.SelectedItem is not TeleportSkillComboBoxItem comboItem)
            return;

        PlayerConfig.Set("RSBot.Skills.TeleportSkill", comboItem.Record.ID);

        SkillManager.TeleportSkill = Game.Player.Skills.GetSkillInfoById(comboItem.Record.ID);
    }

    /// <summary>
    ///     Occurs before Main form is displayed.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Main_Load(object sender, EventArgs e)
    {
        _settingsLoaded = false;
        LoadSettings();
        _settingsLoaded = true;
    }

    private class MasteryComboBoxItem
    {
        public byte Level;
        public RefSkillMastery Record;

        public override string ToString()
        {
            return Record.Name + $" lv.{Level}";
        }
    }

    private class TeleportSkillComboBoxItem
    {
        public byte Level;
        public RefSkill Record;

        public override string ToString()
        {
            return Record.GetRealName() + $" lv.{Level}";
        }
    }

    #region Fields

    private readonly object _lock;
    private MasteryComboBoxItem _selectedMastery;
    private bool _settingsLoaded;

    #endregion Fields
}
