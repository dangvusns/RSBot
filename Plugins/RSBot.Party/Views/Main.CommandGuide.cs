using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RSBot.Core.Components;
using SDUI;

namespace RSBot.Party.Views;

public partial class Main
{
    private Panel _commandSettingsScroll;
    private TableLayoutPanel _commandSettingsLayout;
    private Control[] _commandSettingsControls;
    private readonly List<(System.Windows.Forms.Label Label, string Key, string Fallback)> _commandGuideLabels = new();

    private void InitializeCommandGuide()
    {
        // Keep the existing controls and handlers. Translate their original keys explicitly after reparenting.
        _commandSettingsControls = groupBox2.Controls.Cast<Control>()
            .Where(c => c != labelCommandsInfo && c != separator1 && c != separator2 && c != separator10)
            .OrderBy(c => c.Top).ThenBy(c => c.Left).ToArray();
        labelCommandsInfo.Visible = false;
        separator1.Visible = false;
        separator2.Visible = false;
        separator10.Visible = false;

        _commandSettingsScroll = new Panel { AutoScroll = true, Name = "commandSettingsScroll" };
        _commandSettingsLayout = new TableLayoutPanel
        {
            Name = "commandSettingsLayout",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            Padding = new Padding(8)
        };
        _commandSettingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _commandSettingsScroll.Controls.Add(_commandSettingsLayout);
        groupBox2.Controls.Add(_commandSettingsScroll);
        foreach (var control in _commandSettingsControls)
        {
            control.Anchor = AnchorStyles.Left;
            control.Margin = new Padding(0, 3, 0, 3);
            _commandSettingsLayout.Controls.Add(control, 0, _commandSettingsLayout.RowCount++);
        }

        AddGuideLabel("CommandGuideTitle", "Available chat commands", true);
        AddGuideLabel("CommandGuideIntro", "Send commands through in-game chat. Only enabled commanders in your list or the party leader are obeyed. [player] is optional; omit it to follow the sender. Party leaders also apply start, stop, radius, area/setarea and getpost locally.");
        AddGuideLabel("CommandGuideTrace", "• trace / traceme — Follow and stand at the player's destination.\n   Examples: traceme · trace LilQuanVu1");
        AddGuideLabel("CommandGuideNoTrace", "• notrace — Stop following.\n   Example: notrace");
        AddGuideLabel("CommandGuideSit", "• sitdown — Toggle sitting or standing.\n   Example: sitdown");
        AddGuideLabel("CommandGuideStart", "• start — Exit following and start the bot.\n   Example: start");
        AddGuideLabel("CommandGuideStop", "• stop — Stop the bot. Use notrace to stop following.\n   Example: stop");
        AddGuideLabel("CommandGuideReturn", "• town / return — Return to town using a return scroll.\n   Examples: town · return");
        AddGuideLabel("CommandGuideTeleport", "• teleport from,to — Use a nearby teleporter to reach a matching destination.\n   Example: teleport ferry,donwhang");
        AddGuideLabel("CommandGuideRadius", "• radius value — Set the training radius.\n   Example: radius 50");
        AddGuideLabel("CommandGuideArea", "• area / setarea x,y,radius — Set the training position and radius.\n   Examples: area 100,200,50 · setarea 100,200,50");
        AddGuideLabel("CommandGuideGetPost", "• getpost [radius] — Set the training position to where each bot stands now; the radius is optional.\n   Examples: getpost · getpost 30");
        AddGuideLabel("CommandGuideInvite", "• invite / inviteme — Invite the sender to the party (the sender must be near).\n   Example: inviteme");
        AddGuideLabel("CommandGuideLeave", "• leave / leavept — Leave the party.\n   Example: leave");
        AddGuideLabel("CommandGuideStatus", "• status — Reply with level, HP, MP, bot state and location.\n   Example: status");
        AddGuideLabel("CommandGuideLogout", "• logout — Stop the bot and leave the game without relogging in.\n   Example: logout");
        AddGuideLabel("CommandGuideHelp", "• help — Reply with the list of commands.\n   Example: help");
        AddGuideLabel("CommandGuidePrivate", "Private messages work from anywhere; the sender doesn't have to be near.");

        groupBox2.SizeChanged += (s, e) => UpdateCommandGuideLayout();
        FontChanged += (s, e) => UpdateCommandGuideLayout();
        DpiChangedAfterParent += (s, e) => UpdateCommandGuideLayout();
        VisibleChanged += (s, e) => UpdateCommandGuideLayout();
        _commandSettingsLayout.SizeChanged += (s, e) => SizeCommandGuideLabels();
        TranslateCommandGuide();
    }

    private void AddGuideLabel(string key, string fallback, bool heading = false)
    {
        var label = new System.Windows.Forms.Label
        {
            Name = key,
            Text = fallback,
            AutoSize = true,
            MaximumSize = new Size(Math.Max(100, groupBox2.ClientSize.Width - 80), 0),
            UseMnemonic = false,
            Margin = new Padding(0, heading ? 16 : 6, 0, 6)
        };
        if (heading)
        {
            var headingFont = new Font(Font, FontStyle.Bold);
            label.Font = headingFont;
            label.Disposed += (s, e) => headingFont.Dispose();
        }
        _commandGuideLabels.Add((label, key, fallback));
        _commandSettingsLayout.Controls.Add(label, 0, _commandSettingsLayout.RowCount++);
    }

    internal void TranslateCommandGuide()
    {
        if (_commandSettingsLayout == null)
            return;
        foreach (var control in _commandSettingsControls)
            control.Text = LanguageManager.GetLangBySpecificKey("RSBot.Party",
                "RSBot.Party.Main.TabControl.TabPage.GroupBox." + control.Name, control.Text);
        foreach (var entry in _commandGuideLabels)
            entry.Label.Text = LanguageManager.GetLangBySpecificKey("RSBot.Party", entry.Key, entry.Fallback)
                .Replace("\\n", "\n");
        UpdateCommandGuideLayout();
    }

    private void UpdateCommandGuideLayout()
    {
        if (_commandSettingsScroll == null || IsDisposed)
            return;
        var padding = (int)Math.Ceiling(8 * DeviceDpi / 96f);
        var top = (int)Math.Ceiling(34 * DeviceDpi / 96f);
        _commandSettingsScroll.SetBounds(padding, top,
            Math.Max(1, groupBox2.ClientSize.Width - padding * 2),
            Math.Max(1, groupBox2.ClientSize.Height - top - padding));
        foreach (var control in _commandSettingsControls)
        {
            if (control == textBoxLeaveIfMasterNotName)
                continue;
            var text = TextRenderer.MeasureText(control.Text, control.Font);
            control.Size = new Size(text.Width + padding * 4, Math.Max(control.Font.Height + padding * 2, padding * 4));
        }
        // Preserve full checkbox captions; narrow windows may scroll horizontally instead of clipping.
        var settingsWidth = _commandSettingsControls.Max(c =>
            TextRenderer.MeasureText(c.Text, c.Font).Width + padding * 6);
        _commandSettingsLayout.MinimumSize = new Size(settingsWidth, 0);
        textBoxLeaveIfMasterNotName.Width = Math.Max(padding * 20, settingsWidth - padding * 2);
        _commandSettingsLayout.BackColor = ColorScheme.BackColor;
        _commandSettingsLayout.ForeColor = ColorScheme.ForeColor;
        _commandSettingsScroll.BackColor = ColorScheme.BackColor;
        foreach (var entry in _commandGuideLabels)
            entry.Label.ForeColor = ColorScheme.ForeColor;
        SizeCommandGuideLabels();
    }

    private void SizeCommandGuideLabels()
    {
        var width = Math.Max(1, _commandSettingsLayout.ClientSize.Width - _commandSettingsLayout.Padding.Horizontal);
        foreach (var entry in _commandGuideLabels)
        {
            var maximum = new Size(width, 0);
            if (entry.Label.MaximumSize != maximum)
                entry.Label.MaximumSize = maximum;
        }
    }
}
