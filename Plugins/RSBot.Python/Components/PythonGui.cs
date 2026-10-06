using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using SDUI;

namespace RSBot.Python.Components;

/// <summary>
///     Controls created by Python plugins, one tab page per plugin.
///     Python never touches a control directly: values are cached here so Python can read them
///     without waiting for the UI thread, and changes are applied on the UI thread asynchronously.
/// </summary>
internal static class PythonGui
{
    private static readonly object _lock = new();
    private static readonly Dictionary<int, Entry> _controls = new();
    private static readonly Dictionary<string, TabPage> _pages = new();
    private static int _nextId;

    private sealed class Entry
    {
        public string PluginKey;
        public string Kind;
        public Control Control;
        public string Text = string.Empty;
        public bool Checked;
        public readonly List<string> Items = new();
        public int SelectedIndex = -1;
        public bool Updating;
    }

    #region Pages

    public static void CreatePage(string pluginKey, string title)
    {
        RunOnUi(() =>
        {
            lock (_lock)
                if (_pages.ContainsKey(pluginKey))
                    return;

            var page = new TabPage(title) { AutoScroll = true, UseVisualStyleBackColor = false };
            ApplyTheme(page);

            lock (_lock)
                _pages[pluginKey] = page;

            Views.View.Instance.AddPluginPage(page);
        });
    }

    public static void RemovePage(string pluginKey)
    {
        lock (_lock)
            foreach (var id in _controls.Where(c => c.Value.PluginKey == pluginKey).Select(c => c.Key).ToList())
                _controls.Remove(id);

        RunOnUi(() =>
        {
            TabPage page;
            lock (_lock)
                if (!_pages.Remove(pluginKey, out page))
                    return;

            Views.View.Instance.RemovePluginPage(page);
            page.Dispose();
        });
    }

    #endregion

    #region Controls

    public static int CreateControl(string pluginKey, string kind, string text, int x, int y, int width, int height)
    {
        var entry = new Entry
        {
            PluginKey = pluginKey,
            Kind = kind,
            Text = text ?? string.Empty,
        };

        var id = Interlocked.Increment(ref _nextId);
        lock (_lock)
            _controls[id] = entry;

        RunOnUi(() =>
        {
            TabPage page;
            lock (_lock)
                if (!_pages.TryGetValue(pluginKey, out page) || !_controls.ContainsKey(id))
                    return;

            var control = Build(id, entry);
            if (control == null)
                return;

            // Plugins use 96 DPI pixels
            control.Location = new Point(page.LogicalToDeviceUnits(x), page.LogicalToDeviceUnits(y));
            if (width > 0)
            {
                if (control.AutoSize)
                    control.AutoSize = false;
                control.Width = page.LogicalToDeviceUnits(width);
            }

            if (height > 0)
            {
                if (control.AutoSize)
                    control.AutoSize = false;
                control.Height = page.LogicalToDeviceUnits(height);
            }

            entry.Control = control;
            page.Controls.Add(control);
            ApplyState(entry);
        });

        return id;
    }

    private static Control Build(int id, Entry entry)
    {
        switch (entry.Kind)
        {
            case "label":
                return new SDUI.Controls.Label { AutoSize = true, Text = entry.Text };

            case "button":
            {
                var button = new SDUI.Controls.Button { Text = entry.Text, AutoSize = true };
                button.Click += (_, _) => PythonPluginManager.PostGuiEvent(id, null);
                return button;
            }

            case "checkbox":
            {
                var checkBox = new SDUI.Controls.CheckBox { Text = entry.Text, AutoSize = true };
                checkBox.CheckedChanged += (_, _) =>
                {
                    if (entry.Updating)
                        return;

                    lock (_lock)
                        entry.Checked = checkBox.Checked;

                    PythonPluginManager.PostGuiEvent(id, checkBox.Checked);
                };
                return checkBox;
            }

            case "textbox":
            {
                var textBox = new TextBox { Text = entry.Text, Width = 150 };
                ApplyTheme(textBox);
                textBox.TextChanged += (_, _) =>
                {
                    if (entry.Updating)
                        return;

                    lock (_lock)
                        entry.Text = textBox.Text;

                    PythonPluginManager.PostGuiEvent(id, textBox.Text);
                };
                return textBox;
            }

            case "combobox":
            {
                var comboBox = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    FlatStyle = FlatStyle.Flat,
                    Width = 150,
                };
                ApplyTheme(comboBox);
                comboBox.SelectedIndexChanged += (_, _) => OnSelectionChanged(id, entry, comboBox.SelectedIndex);
                return comboBox;
            }

            case "listbox":
            {
                var listBox = new ListBox
                {
                    Width = 150,
                    Height = 120,
                    IntegralHeight = false,
                    BorderStyle = BorderStyle.FixedSingle,
                };
                ApplyTheme(listBox);
                listBox.SelectedIndexChanged += (_, _) => OnSelectionChanged(id, entry, listBox.SelectedIndex);
                return listBox;
            }

            default:
                return null;
        }
    }

    private static void OnSelectionChanged(int id, Entry entry, int index)
    {
        if (entry.Updating)
            return;

        lock (_lock)
        {
            entry.SelectedIndex = index;
            entry.Text = index >= 0 && index < entry.Items.Count ? entry.Items[index] : string.Empty;
        }

        PythonPluginManager.PostGuiEvent(id, index);
    }

    #endregion

    #region Get/Set (called from Python)

    public static string Get(int id, string property)
    {
        lock (_lock)
        {
            if (!_controls.TryGetValue(id, out var entry))
                return string.Empty;

            return property switch
            {
                "text" => entry.Text,
                "checked" => entry.Checked ? "1" : "0",
                "items" => JsonSerializer.Serialize(entry.Items),
                "selected_index" => entry.SelectedIndex.ToString(),
                _ => string.Empty,
            };
        }
    }

    public static void Set(int id, string property, string value)
    {
        Entry entry;
        lock (_lock)
        {
            if (!_controls.TryGetValue(id, out entry))
                return;

            switch (property)
            {
                case "text":
                    entry.Text = value;
                    break;

                case "checked":
                    entry.Checked = value == "1";
                    break;

                case "add":
                    entry.Items.Add(value);
                    break;

                case "remove_at":
                    if (int.TryParse(value, out var removeIndex) && removeIndex >= 0 && removeIndex < entry.Items.Count)
                    {
                        entry.Items.RemoveAt(removeIndex);
                        if (entry.SelectedIndex >= entry.Items.Count)
                            entry.SelectedIndex = entry.Items.Count - 1;
                    }

                    break;

                case "clear":
                    entry.Items.Clear();
                    entry.SelectedIndex = -1;
                    break;

                case "selected_index":
                    if (int.TryParse(value, out var selectIndex))
                        entry.SelectedIndex = Math.Clamp(selectIndex, -1, entry.Items.Count - 1);
                    break;
            }
        }

        RunOnUi(() =>
        {
            var control = entry.Control;
            if (control == null || control.IsDisposed)
                return;

            switch (property)
            {
                case "enabled":
                    control.Enabled = value == "1";
                    return;

                case "visible":
                    control.Visible = value == "1";
                    return;

                case "position":
                    var parts = value.Split(',');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
                        control.Location = new Point(control.LogicalToDeviceUnits(x), control.LogicalToDeviceUnits(y));
                    return;
            }

            ApplyState(entry);
        });
    }

    /// <summary>
    ///     Copies the cached values to the control. UI thread only.
    /// </summary>
    private static void ApplyState(Entry entry)
    {
        var control = entry.Control;
        if (control == null || control.IsDisposed)
            return;

        string text;
        bool isChecked;
        string[] items;
        int selected;
        lock (_lock)
        {
            text = entry.Text;
            isChecked = entry.Checked;
            items = entry.Items.ToArray();
            selected = entry.SelectedIndex;
        }

        entry.Updating = true;
        try
        {
            switch (control)
            {
                case ListBox listBox:
                    SyncItems(listBox.Items, items);
                    listBox.SelectedIndex = selected < listBox.Items.Count ? selected : -1;
                    break;

                case ComboBox comboBox:
                    SyncItems(comboBox.Items, items);
                    comboBox.SelectedIndex = selected < comboBox.Items.Count ? selected : -1;
                    break;

                case SDUI.Controls.CheckBox checkBox:
                    if (checkBox.Text != text)
                        checkBox.Text = text;
                    checkBox.Checked = isChecked;
                    break;

                default:
                    if (control.Text != text)
                        control.Text = text;
                    break;
            }
        }
        finally
        {
            entry.Updating = false;
        }
    }

    private static void SyncItems(IList current, string[] items)
    {
        if (current.Count == items.Length && current.Cast<object>().Select(i => i?.ToString()).SequenceEqual(items))
            return;

        current.Clear();
        foreach (var item in items)
            current.Add(item);
    }

    #endregion

    private static void ApplyTheme(Control control)
    {
        control.BackColor = ColorScheme.BackColor;
        control.ForeColor = ColorScheme.ForeColor;
    }

    private static void RunOnUi(Action action)
    {
        Views.View.Instance.RunOnUi(action);
    }
}
