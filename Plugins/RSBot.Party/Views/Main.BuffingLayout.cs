using System;
using System.Windows.Forms;

namespace RSBot.Party.Views;

/// <summary>
///     Keeps the "Buffs" column of the Buffing tab readable at high DPI.
/// </summary>
public partial class Main
{
    private bool _buffingLayoutInProgress;

    private void InitializeBuffingLayout()
    {
        checkHideLowerLevelSkills.AutoSize = false;
        checkHideLowerLevelSkills.TextChanged += (s, e) => UpdateBuffingLayout();
        checkHideLowerLevelSkills.FontChanged += (s, e) => UpdateBuffingLayout();

        listPartyBuffSkills.ClientSizeChanged += (s, e) => UpdateBuffingLayout();
        DpiChangedAfterParent += (s, e) => UpdateBuffingLayout();

        UpdateBuffingLayout();
    }

    private void UpdateBuffingLayout()
    {
        if (_buffingLayoutInProgress || IsDisposed)
            return;

        _buffingLayoutInProgress = true;
        try
        {
            int Pixels(int value) => (int)Math.Ceiling(value * DeviceDpi / 96f);

            // SDUI check boxes keep a 30 px height at any DPI
            var rowHeight = Math.Max(Pixels(26), checkHideLowerLevelSkills.Font.Height + Pixels(10));
            checkHideLowerLevelSkills.Height = rowHeight;

            var height = rowHeight + Pixels(8);
            if (panel5.Height != height)
                panel5.Height = height;

            // The limit column stays narrow, the name takes the rest
            var limitWidth = Pixels(56);
            columnLimit.Width = limitWidth;
            columnName.Width = Math.Max(Pixels(120), listPartyBuffSkills.ClientSize.Width - limitWidth - Pixels(4));
        }
        finally
        {
            _buffingLayoutInProgress = false;
        }
    }
}
