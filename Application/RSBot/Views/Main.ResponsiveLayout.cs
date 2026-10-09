using System;
using System.Drawing;
using System.Windows.Forms;
using RSBot.Views.Controls;

namespace RSBot.Views;

public partial class Main
{
    private bool _updatingResponsiveLayout;

    private void InitializeResponsiveLayout()
    {
        var original = windowPageControl;
        var index = Controls.GetChildIndex(original);
        windowPageControl = new ScrollablePageControl
        {
            Name = original.Name,
            Dock = original.Dock,
            Margin = original.Margin,
            TabIndex = original.TabIndex,
            Bounds = original.Bounds,
        };
        WindowPageControl = windowPageControl;
        Controls.Remove(original);
        original.Dispose();
        Controls.Add(windowPageControl);
        Controls.SetChildIndex(windowPageControl, index);

        // An alternative to the compact SDUI title tabs on narrow/high-DPI screens.
        var pagesMenu = new ToolStripMenuItem("Pages");
        pagesMenu.DropDownOpening += (_, _) =>
        {
            while (pagesMenu.DropDownItems.Count > 0)
                pagesMenu.DropDownItems[0].Dispose();
            for (var i = 0; i < windowPageControl.Count; i++)
            {
                var pageIndex = i;
                var page = windowPageControl.GetPage(i);
                var item = new ToolStripMenuItem(page.Text)
                {
                    Checked = i == windowPageControl.SelectedIndex,
                };
                item.Click += (_, _) => windowPageControl.SelectedIndex = pageIndex;
                pagesMenu.DropDownItems.Add(item);
            }
        };
        viewToolStripMenuItem.DropDownItems.Add(pagesMenu);

        windowPageControl.SelectedIndexChanged += (_, _) => UpdateResponsiveLayout();
        // The page host gets its new DeviceDpi after the form's DpiChanged event.
        windowPageControl.DpiChangedAfterParent += (_, _) => UpdateResponsiveLayout();
        bottomPanel.Layout += (_, _) => UpdateResponsiveLayout();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        DpiChanged += (_, _) =>
        {
            UpdateResponsiveLayout();
            // Apply after WinForms has accepted the DPI message's suggested bounds.
            if (IsHandleCreated) BeginInvoke(new Action(FitWindowToWorkingArea));
        };
        Shown += (_, _) => FitWindowToWorkingArea();
        foreach (var control in new Control[] { btnSave, btnStartStop, buttonConfig })
            control.TextChanged += (_, _) => UpdateResponsiveLayout();
        UpdateResponsiveLayout();
    }

    private int LayoutPixels(int value) => (int)Math.Ceiling(value * DeviceDpi / 96f);

    private void FitWindowToWorkingArea()
    {
        if (IsDisposed || Disposing || WindowState != FormWindowState.Normal) return;
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(LayoutPixels(560), area.Width),
            Math.Min(LayoutPixels(360), area.Height));
        var size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        Bounds = new Rectangle(new Point(Math.Clamp(Left, area.Left, area.Right - size.Width),
            Math.Clamp(Top, area.Top, area.Bottom - size.Height)), size);
        UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        if (_updatingResponsiveLayout || IsDisposed || bottomPanel == null) return;
        _updatingResponsiveLayout = true;
        try
        {
            var gap = LayoutPixels(8);
            var edge = LayoutPixels(12);
            var buttonHeight = Math.Max(LayoutPixels(30), btnStartStop.Font.Height + LayoutPixels(12));
            var saveWidth = Math.Max(LayoutPixels(100),
                TextRenderer.MeasureText(btnSave.Text, btnSave.Font).Width + LayoutPixels(24));
            var startWidth = Math.Max(LayoutPixels(100),
                TextRenderer.MeasureText(btnStartStop.Text, btnStartStop.Font).Width + LayoutPixels(24));
            var configWidth = Math.Max(LayoutPixels(64),
                TextRenderer.MeasureText(buttonConfig.Text, buttonConfig.Font).Width + LayoutPixels(16));
            comboDivision.ItemHeight = Math.Max(LayoutPixels(20), comboDivision.Font.Height + LayoutPixels(4));
            comboServer.ItemHeight = Math.Max(LayoutPixels(20), comboServer.Font.Height + LayoutPixels(4));
            comboDivision.DropDownHeight = comboServer.DropDownHeight = LayoutPixels(200);
            var rowHeight = Math.Max(buttonHeight, Math.Max(comboDivision.Height, comboServer.Height));
            var leftWidth = LayoutPixels(100 + 140) + configWidth + gap * 2;
            var actionsWidth = saveWidth + startWidth + gap;
            var wrap = bottomPanel.ClientSize.Width < leftWidth + actionsWidth + gap + edge * 2;
            var actionTop = wrap ? edge + rowHeight + gap : edge;
            bottomPanel.Height = actionTop + rowHeight + edge;

            // Disable the original anchors/AutoSize: this routine owns footer bounds.
            foreach (var control in new Control[] { comboDivision, comboServer, buttonConfig, btnSave, btnStartStop })
                control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            buttonConfig.AutoSize = false;
            comboDivision.Location = new Point(edge, edge + (rowHeight - comboDivision.Height) / 2);
            comboDivision.Width = LayoutPixels(100);
            comboServer.Location = new Point(comboDivision.Right + gap, edge + (rowHeight - comboServer.Height) / 2);
            comboServer.Width = LayoutPixels(140);
            buttonConfig.Bounds = new Rectangle(comboServer.Right + gap, edge, configWidth, buttonHeight);
            var startLeft = Math.Max(edge, bottomPanel.ClientSize.Width - edge - startWidth);
            btnStartStop.Bounds = new Rectangle(startLeft, actionTop, startWidth, buttonHeight);
            btnSave.Bounds = new Rectangle(Math.Max(edge, startLeft - gap - saveWidth), actionTop, saveWidth, buttonHeight);

            // Retain the user's sidebar preference when temporarily hiding it for space:
            // the selected page must fit next to it without horizontal scrolling.
            pSidebar.Width = LayoutPixels(250);
            var page = windowPageControl is ScrollablePageControl pages ? pages.SelectedPageSize : Size.Empty;
            // Pages fill the width the sidebar leaves; Visible is false until the form is shown.
            var pageArea = ClientSize.Width - Padding.Horizontal;
            var pageWidth = page.Width;
            if (page.Height > windowPageControl.Height)
                pageWidth += SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi);
            pSidebar.Visible = menuSidebar.Checked && ClientSize.Width >= LayoutPixels(950)
                && pageArea - pSidebar.Width >= pageWidth;
        }
        finally
        {
            _updatingResponsiveLayout = false;
        }
    }
}
