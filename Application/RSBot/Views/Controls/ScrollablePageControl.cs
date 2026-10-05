using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using SDUI.Controls;

namespace RSBot.Views.Controls;

/// <summary>
/// Keeps a page's original layout accessible when its viewport is smaller at high DPI.
/// Pages remain direct children so plugin lookup and SDUI's title tabs keep working.
/// </summary>
internal sealed class ScrollablePageControl : WindowPageControl
{
    private readonly ConditionalWeakTable<Control, PageSize> _logicalSizes = new();
    private bool _layingOut;

    public ScrollablePageControl()
    {
        AutoScroll = true;
        SelectedIndexChanged += (_, _) => AutoScrollPosition = Point.Empty;
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        var page = e.Control;
        // Botbase views can be removed and re-added. Retain their original size
        // rather than treating a previously expanded viewport as their baseline.
        _logicalSizes.GetValue(page, control => new PageSize(control));
        base.OnControlAdded(e);
        // SDUI sets Fill after raising ControlAdded. Use explicit bounds so AutoScroll
        // can expose the full page instead of shrinking its fixed-position controls.
        page.Dock = DockStyle.None;
        page.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        page.VisibleChanged += PageVisibilityChanged;
        PerformLayout();
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        e.Control.VisibleChanged -= PageVisibilityChanged;
        base.OnControlRemoved(e);
        PerformLayout();
    }

    private void PageVisibilityChanged(object sender, EventArgs e) => PerformLayout();

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (_layingOut) return;
        _layingOut = true;
        try
        {
            // Visibility changes during selection happen one page at a time. Only the
            // selected page should contribute to the scrollable extent.
            var page = SelectedIndex >= 0 && SelectedIndex < Count ? GetPage(SelectedIndex) : null;
            if (page != null && _logicalSizes.TryGetValue(page, out var pageSize))
            {
                var logicalSize = pageSize.LogicalSize;
                var scale = DeviceDpi / 96f;
                var minimum = new Size((int)Math.Ceiling(logicalSize.Width * scale),
                    (int)Math.Ceiling(logicalSize.Height * scale));
                if (AutoScrollMinSize != minimum) AutoScrollMinSize = minimum;
                base.OnLayout(e);
                page.Bounds = new Rectangle(AutoScrollPosition,
                    new Size(Math.Max(minimum.Width, ClientSize.Width),
                        Math.Max(minimum.Height, ClientSize.Height)));
            }
            else
            {
                AutoScrollMinSize = Size.Empty;
                base.OnLayout(e);
            }
        }
        finally
        {
            _layingOut = false;
        }
    }

    private sealed class PageSize
    {
        public SizeF LogicalSize { get; }

        public PageSize(Control page)
        {
            var scale = page.DeviceDpi / 96f;
            LogicalSize = new SizeF(page.Width / scale, page.Height / scale);
        }
    }
}
