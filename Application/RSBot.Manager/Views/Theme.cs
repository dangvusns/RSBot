using System;
using System.Drawing;
using System.Windows.Forms;

namespace RSBot.Manager.Views;

/// <summary>
///     The dark and gold look of the manager.
/// </summary>
internal static class Theme
{
    public static readonly Color Back = Color.FromArgb(30, 27, 20);
    public static readonly Color Panel = Color.FromArgb(38, 34, 24);
    public static readonly Color Row = Color.FromArgb(42, 37, 26);
    public static readonly Color RowSelected = Color.FromArgb(90, 74, 34);
    public static readonly Color Border = Color.FromArgb(92, 79, 46);
    public static readonly Color Gold = Color.FromArgb(217, 182, 90);
    public static readonly Color Text = Color.FromArgb(232, 226, 208);
    public static readonly Color Muted = Color.FromArgb(140, 130, 105);
    public static readonly Color Good = Color.FromArgb(132, 196, 108);
    public static readonly Color Bad = Color.FromArgb(214, 112, 92);

    public static readonly Font Font = new("Segoe UI", 10f);
    public static readonly Font Bold = new("Segoe UI", 10f, FontStyle.Bold);
    public static readonly Font ButtonFont = new("Segoe UI", 9.5f, FontStyle.Bold);
    public static readonly Font Total = new("Segoe UI", 11f, FontStyle.Bold);

    /// <summary>
    ///     Converts a size designed at 100% display scaling to the scaling of the control's screen.
    ///     Fonts follow the Windows scaling by themselves, fixed pixel sizes do not.
    /// </summary>
    public static int Scale(Control control, int pixels)
    {
        return (int)Math.Round(pixels * control.DeviceDpi / 96f);
    }

    public static void ApplyForm(Form form)
    {
        form.BackColor = Back;
        form.ForeColor = Text;
        form.Font = Font;
    }

    public static Button CreateButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text.ToUpperInvariant(),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 6, 12, 6),
            Margin = new Padding(0, 0, 10, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(70, 58, 28) : Panel,
            ForeColor = Text,
            Font = ButtonFont,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = primary ? Gold : Border;
        button.FlatAppearance.MouseOverBackColor = RowSelected;
        button.FlatAppearance.MouseDownBackColor = Border;

        return button;
    }

    public static TextBox CreateTextBox()
    {
        return new TextBox
        {
            BackColor = Panel,
            ForeColor = Text,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
        };
    }

    public static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Gold,
            Font = Bold,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 6),
        };
    }

    public static void ApplyGrid(DataGridView grid)
    {
        grid.BackgroundColor = Back;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.GridColor = Border;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight = Scale(grid, 44);
        grid.RowTemplate.Height = Scale(grid, 40);
        grid.ScrollBars = ScrollBars.Both;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        grid.ColumnHeadersDefaultCellStyle.BackColor = Panel;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Gold;
        grid.ColumnHeadersDefaultCellStyle.Font = Bold;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Panel;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;

        grid.DefaultCellStyle.BackColor = Row;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.Font = Font;
        grid.DefaultCellStyle.SelectionBackColor = RowSelected;
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
    }

    public static void ApplyMenu(ContextMenuStrip menu)
    {
        menu.BackColor = Panel;
        menu.ForeColor = Text;
        menu.Font = new Font("Segoe UI", 11f);
        menu.ShowImageMargin = false;
        menu.Renderer = new MenuRenderer();
    }

    private sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer()
            : base(new MenuColors()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Muted;
            base.OnRenderItemText(e);
        }
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Panel;

        public override Color MenuBorder => Border;

        public override Color MenuItemBorder => RowSelected;

        public override Color MenuItemSelected => RowSelected;

        public override Color SeparatorDark => Border;

        public override Color SeparatorLight => Panel;

        public override Color ImageMarginGradientBegin => Panel;

        public override Color ImageMarginGradientMiddle => Panel;

        public override Color ImageMarginGradientEnd => Panel;
    }
}
