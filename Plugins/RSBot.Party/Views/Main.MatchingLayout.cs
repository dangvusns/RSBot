using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace RSBot.Party.Views;

public partial class Main
{
    private bool _autoJoinSettingsOpen;
    private bool _matchingLayoutInProgress;
    private int _matchingLayoutQueued;

    private void InitializeMatchingLayout()
    {
        // Keep the existing parents/names so the translation keys and event handlers still work.
        foreach (Control control in topPartyPanel.Controls)
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        foreach (Control control in bottomPartyPanel.Controls)
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        topPartyPanel.SizeChanged += (s, e) => UpdateMatchingLayout();
        bottomPartyPanel.SizeChanged += (s, e) => UpdateMatchingLayout();
        FontChanged += (s, e) => UpdateMatchingLayout();
        DpiChangedAfterParent += (s, e) => UpdateMatchingLayout();
        VisibleChanged += (s, e) => { if (Visible) UpdateMatchingLayout(); };
        foreach (var control in new Control[] { label3, label4, label5, label6,
            btnPartySearch, btnPartyRefresh, checkBoxJoinByName, checkBoxJoinByTitle,
            buttonConfirmJoinConfig, btnJoinFormedParty, btnWhisperPartyMaster,
            btnAutoMatchParty, lbl_partyPageRange, btnPartyMatchForm,
            btnPartyMatchChangeEntry, btnPartyMatchDeleteEntry })
        {
            control.TextChanged += (s, e) => UpdateMatchingLayout();
            control.FontChanged += (s, e) => UpdateMatchingLayout();
        }
        UpdateMatchingLayout();
    }

    internal void UpdateMatchingLayout()
    {
        if (IsDisposed || Disposing) return;
        if (IsHandleCreated && InvokeRequired)
        {
            // Party list updates can change captions on the packet thread.
            if (Interlocked.Exchange(ref _matchingLayoutQueued, 1) == 0)
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        Interlocked.Exchange(ref _matchingLayoutQueued, 0);
                        UpdateMatchingLayout();
                    }));
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Exchange(ref _matchingLayoutQueued, 0);
                }
            }
            return;
        }
        if (_matchingLayoutInProgress) return;
        _matchingLayoutInProgress = true;
        topPartyPanel.SuspendLayout();
        bottomPartyPanel.SuspendLayout();
        try
        {
            var scale = DeviceDpi / 96f;
            int Pixels(int value) => (int)Math.Ceiling(value * scale);
            var gap = Pixels(8);
            var padding = Pixels(8);
            var rowHeight = Math.Max(Pixels(30), Font.Height + Pixels(12));
            var available = Math.Max(Pixels(120), topPartyPanel.ClientSize.Width - padding * 2);

            void Caption(Control control, bool button = false)
            {
                control.AutoSize = false;
                var measured = TextRenderer.MeasureText(control.Text, control.Font);
                control.Size = new Size(measured.Width + (button ? Pixels(28) : 0),
                    Math.Max(rowHeight, measured.Height + Pixels(8)));
            }
            void Field(Control control, int width)
            {
                control.Size = new Size(Math.Max(control.MinimumSize.Width, Math.Min(Pixels(width), available)),
                    Math.Max(control.MinimumSize.Height, Math.Max(rowHeight, control.Font.Height + Pixels(12))));
            }
            // Place related controls together, wrapping whole groups to the next row.
            int WrapRows(int width, int start, params Control[][] groups)
            {
                var x = padding;
                var y = start;
                var height = 0;
                foreach (var group in groups)
                {
                    var groupWidth = group.Sum(control => control.Width) + gap * (group.Length - 1);
                    if (x > padding && x + groupWidth > padding + width)
                    {
                        y += height + gap;
                        x = padding;
                        height = 0;
                    }
                    foreach (var control in group)
                    {
                        // Very narrow windows can split a group rather than overlap its controls.
                        if (x > padding && x + control.Width > padding + width)
                        {
                            y += height + gap;
                            x = padding;
                            height = 0;
                        }
                        control.Location = new Point(x, y);
                        x += control.Width + gap;
                        height = Math.Max(height, control.Height);
                    }
                }
                return y + height;
            }

            foreach (var label in new Control[] { label3, label4, label5, label6 }) Caption(label);
            foreach (var button in new Control[] { btnPartySearch, btnPartyRefresh, buttonAutoJoinConfig }) Caption(button, true);
            Field(tbPartySearchName, 150);
            Field(cbPartySearchPurpose, 150);
            Field(nudPartySearchMin, 100);
            Field(nudPartySearchMax, 100);
            cbPartySearchPurpose.ItemHeight = Math.Max(Pixels(22), cbPartySearchPurpose.Font.Height + Pixels(8));
            var purposeWidth = cbPartySearchPurpose.Items.Cast<object>()
                .Select(item => TextRenderer.MeasureText(item.ToString(), cbPartySearchPurpose.Font).Width)
                .DefaultIfEmpty(0).Max() + Pixels(32);
            cbPartySearchPurpose.Width = Math.Min(available, Math.Max(cbPartySearchPurpose.Width, purposeWidth));
            cbPartySearchPurpose.DropDownWidth = Math.Max(cbPartySearchPurpose.Width, purposeWidth);

            var bottom = WrapRows(available, padding,
                new Control[] { label3, tbPartySearchName },
                new Control[] { label4, cbPartySearchPurpose },
                new Control[] { label5, nudPartySearchMin, label6, nudPartySearchMax },
                new Control[] { btnPartySearch, btnPartyRefresh, buttonAutoJoinConfig });

            foreach (var control in new Control[] { checkBoxJoinByName, textBoxJoinByName,
                checkBoxJoinByTitle, textBoxJoinByTitle, buttonConfirmJoinConfig, separator11 })
                control.Visible = _autoJoinSettingsOpen;

            if (_autoJoinSettingsOpen)
            {
                separator11.SetBounds(padding, bottom + gap, available, Pixels(8));
                bottom = separator11.Bottom + gap;
                foreach (var pair in new[] {
                    (Check: (Control)checkBoxJoinByName, Field: (Control)textBoxJoinByName),
                    (Check: (Control)checkBoxJoinByTitle, Field: (Control)textBoxJoinByTitle) })
                {
                    Caption(pair.Check);
                    pair.Check.Width += Pixels(32); // Room for the checkbox glyph.
                    var fieldWidth = available - pair.Check.Width - gap;
                    var sameRow = fieldWidth >= Pixels(140);
                    pair.Check.Location = new Point(padding, bottom);
                    pair.Field.Size = new Size(Math.Max(Pixels(80), sameRow ? fieldWidth : available), rowHeight);
                    pair.Field.Location = sameRow
                        ? new Point(pair.Check.Right + gap, bottom)
                        : new Point(padding, pair.Check.Bottom + gap);
                    bottom = Math.Max(pair.Check.Bottom, pair.Field.Bottom) + gap;
                }
                Caption(buttonConfirmJoinConfig, true);
                buttonConfirmJoinConfig.Location = new Point(
                    Math.Max(padding, padding + available - buttonConfirmJoinConfig.Width), bottom);
                bottom = buttonConfirmJoinConfig.Bottom;
            }
            topPartyPanel.Height = bottom + padding;

            foreach (var button in new Control[] { btnJoinFormedParty, btnWhisperPartyMaster,
                btnAutoMatchParty, btnPrev, btnNext, btnPartyMatchForm,
                btnPartyMatchChangeEntry, btnPartyMatchDeleteEntry }) Caption(button, true);
            Caption(lbl_partyPageRange);
            var footerWidth = Math.Max(Pixels(120), bottomPartyPanel.ClientSize.Width - padding * 2);
            bottomPartyPanel.Height = WrapRows(footerWidth, padding,
                new Control[] { btnJoinFormedParty, btnWhisperPartyMaster, btnAutoMatchParty },
                new Control[] { btnPrev, lbl_partyPageRange, btnNext },
                new Control[] { btnPartyMatchForm, btnPartyMatchChangeEntry, btnPartyMatchDeleteEntry }) + padding;
        }
        finally
        {
            topPartyPanel.ResumeLayout(false);
            bottomPartyPanel.ResumeLayout(false);
            _matchingLayoutInProgress = false;
        }
    }
}
