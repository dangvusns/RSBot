using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RSBot.Manager.Models;
using RSBot.Manager.Services;

namespace RSBot.Manager.Views;

/// <summary>
///     A themed dialog with one row per field and Lưu/Hủy buttons.
/// </summary>
internal abstract class DialogBase : Form
{
    private readonly TableLayoutPanel _root;
    private readonly TableLayoutPanel _fields;

    protected DialogBase(string title)
    {
        Theme.ApplyForm(this);

        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        FieldWidth = Theme.Scale(this, 340);

        // The form is sized from this panel in OnLoad; a self-sizing form ignores docked children
        _root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        _fields = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, Theme.Scale(this, 14), 0, 0),
        };

        var save = Theme.CreateButton("Lưu", true);
        save.Margin = new Padding(0);
        save.Click += (_, _) =>
        {
            var error = ValidateInput();
            if (error != null)
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Apply();
            DialogResult = DialogResult.OK;
        };

        var cancel = Theme.CreateButton("Hủy");
        cancel.DialogResult = DialogResult.Cancel;

        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        _root.Controls.Add(_fields, 0, 0);
        _root.Controls.Add(buttons, 0, 1);
        Controls.Add(_root);

        AcceptButton = save;
        CancelButton = cancel;
    }

    /// <summary>
    ///     The width of the input column.
    /// </summary>
    protected int FieldWidth { get; }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        var padding = Theme.Scale(this, 18);
        var size = _root.GetPreferredSize(Size.Empty);

        _root.Location = new Point(padding, padding);
        ClientSize = new Size(size.Width + padding * 2, size.Height + padding * 2);

        // CenterParent was applied with the old size
        if (Owner != null)
            Location = new Point(
                Owner.Left + (Owner.Width - Width) / 2,
                Owner.Top + (Owner.Height - Height) / 2
            );
    }

    protected T AddField<T>(string label, T control)
        where T : Control
    {
        control.Width = FieldWidth;
        control.Anchor = AnchorStyles.Left;
        control.Margin = new Padding(0, Theme.Scale(this, 5), 0, Theme.Scale(this, 5));

        _fields.Controls.Add(Theme.CreateLabel(label));
        _fields.Controls.Add(control);

        return control;
    }

    /// <summary>
    ///     A masked text box with a button that shows or hides the text.
    /// </summary>
    protected TextBox AddPasswordField(string label)
    {
        var textBox = Theme.CreateTextBox();
        textBox.UseSystemPasswordChar = true;
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(0, 0, Theme.Scale(this, 8), 0);

        var toggle = Theme.CreateButton("Hiện");
        toggle.Margin = new Padding(0);
        toggle.Padding = new Padding(Theme.Scale(this, 8), 0, Theme.Scale(this, 8), 0);
        toggle.TabStop = false;
        toggle.Click += (_, _) =>
        {
            textBox.UseSystemPasswordChar = !textBox.UseSystemPasswordChar;
            toggle.Text = textBox.UseSystemPasswordChar ? "HIỆN" : "ẨN";
        };

        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Height = Math.Max(textBox.PreferredHeight, toggle.PreferredSize.Height),
            Margin = new Padding(0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(textBox, 0, 0);
        row.Controls.Add(toggle, 1, 0);

        AddField(label, row);

        return textBox;
    }

    protected static ComboBox CreateComboBox(ComboBoxStyle style)
    {
        return new ComboBox
        {
            DropDownStyle = style,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat,
        };
    }

    protected static NumericUpDown CreateNumber(decimal min, decimal max, decimal value)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            DecimalPlaces = 0,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
        };
    }

    /// <summary>
    ///     Returns the message to show when the input is wrong, or null when it can be saved.
    /// </summary>
    protected abstract string ValidateInput();

    protected abstract void Apply();
}

/// <summary>
///     "Thêm" and "Sửa": login id, password, character, server and the template profile.
/// </summary>
internal sealed class AccountDialog : DialogBase
{
    private const string NoTemplate = "(Không dùng)";

    private readonly ManagerAccount _account;
    private readonly bool _isNew;
    private readonly IReadOnlyCollection<ManagerAccount> _existing;

    private readonly TextBox _loginId;
    private readonly TextBox _password;
    private readonly TextBox _character;
    private readonly ComboBox _server;
    private readonly ComboBox _template;

    public AccountDialog(ManagerAccount account, IReadOnlyCollection<ManagerAccount> existing)
        : base(account == null ? "Thêm tài khoản" : "Sửa tài khoản")
    {
        _isNew = account == null;
        _account = account ?? new ManagerAccount();
        _existing = existing;

        _loginId = AddField("Tài khoản", Theme.CreateTextBox());
        _password = AddPasswordField("Mật khẩu");
        _character = AddField("Nhân vật", Theme.CreateTextBox());
        _server = AddField("Server", CreateComboBox(ComboBoxStyle.DropDown));
        _template = AddField("Profile mẫu", CreateComboBox(ComboBoxStyle.DropDownList));

        _server.Items.AddRange(ProfileWriter.GetKnownServers());

        _template.Items.Add(NoTemplate);
        _template.Items.AddRange(ProfileWriter.GetProfiles());
        _template.SelectedIndex = _template.Items.Count > 1 ? 1 : 0;

        if (_isNew)
            return;

        // The login id is also the profile name, so it cannot change after the profile exists
        _loginId.Text = _account.LoginId;
        _loginId.ReadOnly = true;
        _character.Text = _account.Character;
        _server.Text = _account.Server;
        _password.Text = _account.Password;

        _template.SelectedItem = string.IsNullOrEmpty(_account.TemplateProfile)
            ? NoTemplate
            : _account.TemplateProfile;
        _template.Enabled = false;
    }

    public ManagerAccount Account => _account;

    protected override string ValidateInput()
    {
        var loginId = _loginId.Text.Trim();

        if (string.IsNullOrEmpty(loginId))
            return "Nhập tài khoản.";

        if (loginId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || loginId.Contains('|'))
            return "Tài khoản có ký tự không dùng được làm tên profile.";

        if (loginId.Equals("Profiles", StringComparison.OrdinalIgnoreCase))
            return "Không dùng được tên \"Profiles\".";

        if (_isNew && _existing.Any(a => a.LoginId.Equals(loginId, StringComparison.OrdinalIgnoreCase)))
            return "Tài khoản này đã có trong danh sách.";

        if (string.IsNullOrEmpty(_password.Text))
            return "Nhập mật khẩu.";

        if (string.IsNullOrWhiteSpace(_character.Text))
            return "Nhập tên nhân vật.";

        if (_character.Text.Trim().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "Tên nhân vật có ký tự không hợp lệ.";

        if (string.IsNullOrWhiteSpace(_server.Text))
            return "Nhập tên server.";

        return null;
    }

    protected override void Apply()
    {
        _account.LoginId = _loginId.Text.Trim();
        _account.Character = _character.Text.Trim();
        _account.Server = _server.Text.Trim();

        _account.Password = _password.Text;

        if (_isNew)
            _account.TemplateProfile = _template.SelectedItem as string == NoTemplate
                ? null
                : _template.SelectedItem as string;
    }
}

/// <summary>
///     "Chọn tọa độ": the training area as world coordinates and a radius.
/// </summary>
internal sealed class AreaDialog : DialogBase
{
    private readonly NumericUpDown _x;
    private readonly NumericUpDown _y;
    private readonly NumericUpDown _radius;

    public AreaDialog(float x, float y)
        : base("Chọn tọa độ")
    {
        _x = AddField("X", CreateNumber(-100000, 100000, (decimal)x));
        _y = AddField("Y", CreateNumber(-100000, 100000, (decimal)y));
        _radius = AddField("Bán kính", CreateNumber(1, 500, 50));
    }

    public float X { get; private set; }

    public float Y { get; private set; }

    public int Radius { get; private set; }

    protected override string ValidateInput() => null;

    protected override void Apply()
    {
        X = (float)_x.Value;
        Y = (float)_y.Value;
        Radius = (int)_radius.Value;
    }
}

/// <summary>
///     The "Giờ Xanh" windows. The loot columns come from the bot's Statistics tab and need no settings.
/// </summary>
internal sealed class SettingsDialog : DialogBase
{
    private readonly ManagerData _data;
    private readonly TextBox _windows;

    public SettingsDialog(ManagerData data)
        : base("Cài đặt")
    {
        _data = data;

        _windows = AddField("Giờ Xanh", CreateMultiline(string.Join(Environment.NewLine, data.BlueHourWindows)));

        var help = new Label
        {
            Text = "Mỗi dòng một khung giờ \"HH:mm-HH:mm\", ví dụ 20:00-22:00",
            AutoSize = true,
            MaximumSize = new Size(FieldWidth, 0),
            ForeColor = Theme.Muted,
        };
        AddField(string.Empty, help);
    }

    private static TextBox CreateMultiline(string text)
    {
        var textBox = Theme.CreateTextBox();
        textBox.Multiline = true;
        textBox.ScrollBars = ScrollBars.Vertical;
        textBox.Height = Theme.Scale(textBox, 120);
        textBox.Text = text;

        return textBox;
    }

    private static string[] Lines(TextBox textBox) =>
        textBox.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

    protected override string ValidateInput()
    {
        foreach (var line in Lines(_windows))
            if (!BlueHours.TryParseWindow(line, out _, out _))
                return $"Khung giờ không đúng: {line}";

        return null;
    }

    protected override void Apply()
    {
        _data.BlueHourWindows = Lines(_windows).ToList();
    }
}
