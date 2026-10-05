using System;
using System.Drawing;
using System.Windows.Forms;

namespace RSBot.Views.Controls;

public partial class Character
{
    private bool _updatingLayout;

    private void InitializeResponsiveLayout()
    {
        lblPlayerName.AutoSize = false;
        lblPlayerName.AutoEllipsis = true;
        foreach (var label in new Label[] { lblGold, lblSP, lblStr, lblInt })
        {
            label.AutoSize = false;
            label.AutoEllipsis = true;
        }
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        DpiChangedAfterParent += (_, _) => UpdateResponsiveLayout();
        foreach (var label in new Label[] { lblPlayerName, lblLevel, label3, label4, label9, label11 })
        {
            label.TextChanged += (_, _) => UpdateResponsiveLayout();
            label.FontChanged += (_, _) => UpdateResponsiveLayout();
        }
        UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        if (_updatingLayout || IsDisposed) return;
        _updatingLayout = true;
        try
        {
            int Pixels(int value) => (int)Math.Ceiling(value * DeviceDpi / 96f);
            var gap = Pixels(8);
            var edge = Pixels(10);
            var rowHeight = Math.Max(Pixels(22), lblPlayerName.Font.Height + Pixels(4));
            var available = Math.Max(1, ClientSize.Width - edge * 2);
            var compact = available < Pixels(640);
            var barsWidth = compact ? available : (int)(available * 0.6f);
            var statsTop = compact ? edge + (rowHeight + gap) * 3 : edge + rowHeight + gap;
            var statsLeft = compact ? edge : edge + barsWidth + gap;
            var statsWidth = compact ? available : Math.Max(1, available - barsWidth - gap);
            var levelWidth = Math.Max(Pixels(55), TextRenderer.MeasureText(lblLevel.Text, lblLevel.Font).Width);

            lblPlayerName.Bounds = new Rectangle(edge, edge, Math.Max(1, barsWidth - levelWidth - gap), rowHeight);
            lblLevel.Bounds = new Rectangle(edge + barsWidth - levelWidth, edge, levelWidth, rowHeight);
            var healthWidth = Math.Max(1, (barsWidth - gap) / 2);
            progressHP.Bounds = new Rectangle(edge, edge + rowHeight + gap, healthWidth, rowHeight);
            progressMP.Bounds = new Rectangle(edge + healthWidth + gap, progressHP.Top,
                Math.Max(1, barsWidth - healthWidth - gap), rowHeight);
            progressEXP.Bounds = new Rectangle(edge, progressHP.Bottom + gap, barsWidth, rowHeight);

            var attributesWidth = Math.Max(Pixels(90), statsWidth / 3);
            var valuesLeft = statsLeft + attributesWidth + gap;
            PlaceStat(label9, lblStr, statsLeft, statsTop, attributesWidth, rowHeight, gap);
            PlaceStat(label11, lblInt, statsLeft, statsTop + rowHeight + gap, attributesWidth, rowHeight, gap);
            PlaceStat(label3, lblGold, valuesLeft, statsTop,
                Math.Max(1, statsWidth - attributesWidth - gap), rowHeight, gap);
            PlaceStat(label4, lblSP, valuesLeft, statsTop + rowHeight + gap,
                Math.Max(1, statsWidth - attributesWidth - gap), rowHeight, gap);
            Height = (compact ? statsTop + rowHeight * 2 + gap : progressEXP.Bottom) + edge + separator1.Height;
        }
        finally
        {
            _updatingLayout = false;
        }
    }

    private static void PlaceStat(Label caption, Label value, int left, int top, int width, int height, int gap)
    {
        var captionWidth = TextRenderer.MeasureText(caption.Text, caption.Font).Width;
        caption.Location = new Point(left, top);
        value.Bounds = new Rectangle(left + captionWidth + gap, top,
            Math.Max(1, width - captionWidth - gap), height);
    }
}
