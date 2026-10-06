using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CheckBox = SDUI.Controls.CheckBox;

namespace RSBot.Protection.Views;

/// <summary>
///     Fixes the designer layout at high DPI: SDUI check boxes keep a 30 px height and their
///     designer width, so captions were cut off and rows did not line up.
/// </summary>
public partial class Main
{
    /// <summary>
    ///     The DPI the designer sizes were made at, see <c>AutoScaleDimensions</c>.
    /// </summary>
    private const float DesignDpi = 120f;

    private bool _layoutInProgress;

    private void InitializeResponsiveLayout()
    {
        foreach (var check in AllCheckBoxes())
        {
            check.AutoSize = false;
            // Translations are applied after the constructor
            check.TextChanged += (s, e) => UpdateResponsiveLayout();
        }

        SizeChanged += (s, e) => UpdateResponsiveLayout();
        FontChanged += (s, e) => UpdateResponsiveLayout();
        DpiChangedAfterParent += (s, e) => UpdateResponsiveLayout();
        VisibleChanged += (s, e) =>
        {
            if (Visible)
                UpdateResponsiveLayout();
        };

        UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        if (_layoutInProgress || IsDisposed)
            return;

        _layoutInProgress = true;
        SuspendLayout();
        try
        {
            int Pixels(int designValue) => (int)Math.Ceiling(designValue * DeviceDpi / DesignDpi);

            // The left groups use the free space up to the right column, so their right-hand options fit
            var leftWidth = Math.Max(Pixels(585), groupBackTown.Left - Pixels(7) - groupHPMP.Left);
            foreach (var group in new Control[] { groupHPMP, groupBadStatus, groupPet })
                group.Width = leftWidth;

            // The designer placed the note over the pet group
            label22.AutoSize = true;
            label22.Location = new Point(groupPet.Left, groupPet.Bottom + Pixels(5));

            var rowHeight = Pixels(30);
            foreach (var check in AllCheckBoxes())
            {
                check.Height = rowHeight;
                var needed = check.GetPreferredSize(Size.Empty).Width + Pixels(4);
                check.Width = Math.Max(Pixels(24), Math.Min(needed, FreeWidth(check)));
            }
        }
        finally
        {
            ResumeLayout(true);
            _layoutInProgress = false;
        }
    }

    /// <summary>
    ///     The width up to the next control on the same row, or up to the group border.
    /// </summary>
    private int FreeWidth(Control control)
    {
        var gap = (int)Math.Ceiling(4 * DeviceDpi / 96f);
        var right = control.Parent.ClientSize.Width - gap;

        foreach (Control sibling in control.Parent.Controls)
        {
            if (sibling == control || sibling.Left <= control.Left)
                continue;

            // Same row: the vertical spans overlap
            if (sibling.Top < control.Bottom && sibling.Bottom > control.Top)
                right = Math.Min(right, sibling.Left - gap);
        }

        return right - control.Left;
    }

    private CheckBox[] AllCheckBoxes()
    {
        return new Control[] { groupHPMP, groupBadStatus, groupPet, groupBackTown, groupStatPoints }
            .SelectMany(g => g.Controls.OfType<CheckBox>())
            .ToArray();
    }
}
