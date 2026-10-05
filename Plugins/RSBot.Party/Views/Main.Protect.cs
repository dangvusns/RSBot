using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Components;
using SDUI;
using SDUI.Controls;
using Button = SDUI.Controls.Button;
using CheckBox = SDUI.Controls.CheckBox;
using ListView = SDUI.Controls.ListView;

namespace RSBot.Party.Views;

/// <summary>
///     The "Protect" tab: players whose attackers the Training bot attacks first (Training › TargetBundle).
/// </summary>
public partial class Main
{
    internal const string ProtectEnabledKey = "RSBot.Party.Protect.Enabled";
    internal const string ProtectPlayersKey = "RSBot.Party.Protect.Players";

    private CheckBox _checkProtectEnabled;
    private ListView _listProtectedPlayers;

    /// <summary>
    ///     Builds the tab in code; it is not auto-scaled, so sizes go through <see cref="ProtectPx" />.
    /// </summary>
    private void InitializeProtectTab()
    {
        var page = new TabPage
        {
            Name = "tpProtect",
            Text = ProtectText("ProtectTab", "Protect"),
            BackColor = Color.White,
            Padding = new Padding(ProtectPx(8)),
        };

        // Buttons above the list: when the page is taller than the window (high DPI), only the list's end is cut off
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _checkProtectEnabled = new CheckBox
        {
            Name = "checkProtectEnabled",
            Text = ProtectText("ProtectEnabled", "Protect these players: attack the monsters that attack them"),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, ProtectPx(4)),
        };
        _checkProtectEnabled.CheckedChanged += (s, e) => SaveProtectSettings();

        var info = new System.Windows.Forms.Label
        {
            Name = "labelProtectInfo",
            Text = ProtectText("ProtectInfo", "A monster attacking a listed player is attacked at once, even when the bot is fighting another one. "
                    + "Only monsters inside the training area are attacked; the player must be in sight. Works for any player name."
            ),
            AutoSize = true,
            MaximumSize = new Size(ProtectPx(700), 0),
            UseMnemonic = false,
            Margin = new Padding(0, 0, 0, ProtectPx(8)),
        };

        _listProtectedPlayers = new ListView
        {
            Name = "listProtectedPlayers",
            Dock = DockStyle.Fill,
            View = System.Windows.Forms.View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.None,
            MultiSelect = true,
        };
        _listProtectedPlayers.Columns.Add(ProtectText("CharName", "Name"), ProtectPx(260));

        var buttons = new System.Windows.Forms.FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, ProtectPx(8)),
        };
        buttons.Controls.Add(CreateProtectButton("buttonProtectAdd", ProtectText("Add", "Add"), AddProtectedPlayer));
        buttons.Controls.Add(CreateProtectButton("buttonProtectRemove", ProtectText("Remove", "Remove"), RemoveProtectedPlayers));
        buttons.Controls.Add(
            CreateProtectButton("buttonProtectAddParty", ProtectText("ProtectAddParty", "Add party members"), AddPartyToProtected)
        );

        layout.Controls.Add(_checkProtectEnabled, 0, 0);
        layout.Controls.Add(info, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        layout.Controls.Add(_listProtectedPlayers, 0, 3);
        page.Controls.Add(layout);

        tabMain.Controls.Add(page);

        // Right click in the party list: next to "Add to buffing"
        var menuAddToProtect = new ToolStripMenuItem
        {
            Name = "menuItemAddToProtect",
            Text = ProtectText("ProtectMenuAdd", "Add to protect list"),
        };
        menuAddToProtect.Click += (s, e) => AddSelectedPartyMembersToProtected();
        contextParty.Items.Add(menuAddToProtect);
    }

    /// <summary>
    ///     Adds the members selected in the party list (not the player itself) to the protect list.
    /// </summary>
    private void AddSelectedPartyMembersToProtected()
    {
        var names = listParty.SelectedItems
            .OfType<ListViewItem>()
            .Select(i => (i.Tag as RSBot.Core.Objects.Party.PartyMember)?.Name)
            .Where(n => !string.IsNullOrEmpty(n) && n != Game.Player?.Name)
            .ToArray();

        if (names.Length == 0)
            return;

        AddProtectedNames(names);
        Log.Notify($"[Protect] Protected players: {string.Join(", ", GetProtectedNames())}");
    }

    private Button CreateProtectButton(string name, string text, Action onClick)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(ProtectPx(100), ProtectPx(30)),
            Color = Color.Transparent,
            Radius = 6,
            Margin = new Padding(0, 0, ProtectPx(8), 0),
        };
        button.Click += (s, e) => onClick();

        return button;
    }

    /// <summary>
    ///     Gets the translated text of the key, or the English fallback when the language file does not have it.
    /// </summary>
    private static string ProtectText(string key, string fallback)
    {
        return LanguageManager.GetLangBySpecificKey("RSBot.Party", key, fallback);
    }

    /// <summary>
    ///     Converts 96 DPI pixels to the current display.
    /// </summary>
    private int ProtectPx(int value) => LogicalToDeviceUnits(value);

    /// <summary>
    ///     Shows the saved protect settings; called from <c>LoadSettings</c>.
    /// </summary>
    private void LoadProtectSettings()
    {
        if (_checkProtectEnabled == null)
            return;

        _checkProtectEnabled.Checked = PlayerConfig.Get(ProtectEnabledKey, false);

        _listProtectedPlayers.Items.Clear();
        foreach (var name in PlayerConfig.GetArray<string>(ProtectPlayersKey))
            if (!string.IsNullOrWhiteSpace(name))
                _listProtectedPlayers.Items.Add(name);
    }

    private void SaveProtectSettings()
    {
        // Not while LoadSettings fills the controls
        if (!_applySettings || _checkProtectEnabled == null)
            return;

        PlayerConfig.Set(ProtectEnabledKey, _checkProtectEnabled.Checked);
        PlayerConfig.SetArray(ProtectPlayersKey, GetProtectedNames());

        // Saving reloads the Training bundles, which read the list
        PlayerConfig.Save();
    }

    private string[] GetProtectedNames()
    {
        return _listProtectedPlayers.Items.OfType<ListViewItem>().Select(i => i.Text).ToArray();
    }

    private void AddProtectedPlayer()
    {
        var dialog = new InputDialog(
            "Input",
            LanguageManager.GetLang("CharName"),
            ProtectText("ProtectEnterName", "Enter the name of the player to protect")
        );

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        AddProtectedNames(dialog.Value?.ToString());
    }

    private void AddPartyToProtected()
    {
        var names = Game.Party?.Members?.Select(m => m.Name).Where(n => n != Game.Player?.Name).ToArray();
        if (names == null || names.Length == 0)
            return;

        AddProtectedNames(names);
    }

    private void AddProtectedNames(params string[] names)
    {
        var existing = GetProtectedNames();
        var added = false;

        foreach (var raw in names)
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name) || existing.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            _listProtectedPlayers.Items.Add(name);
            existing = existing.Append(name).ToArray();
            added = true;
        }

        if (added)
            SaveProtectSettings();
    }

    private void RemoveProtectedPlayers()
    {
        if (_listProtectedPlayers.SelectedItems.Count == 0)
            return;

        foreach (var item in _listProtectedPlayers.SelectedItems.OfType<ListViewItem>().ToArray())
            _listProtectedPlayers.Items.Remove(item);

        SaveProtectSettings();
    }
}
