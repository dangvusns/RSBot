using System;
using System.ComponentModel;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Components;
using SDUI.Controls;

namespace RSBot.Views.Controls;

public partial class Character : DoubleBufferedControl
{
    private readonly Timer _refreshTimer;

    public Character()
    {
        InitializeComponent();
        InitializeResponsiveLayout();
        components ??= new Container();
        _refreshTimer = new Timer(components) { Interval = 200 };
        _refreshTimer.Tick += (s, e) => RefreshCharacter();
        VisibleChanged += (s, e) => UpdateTimer();
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (IsDisposed || Disposing) return;
        _refreshTimer.Enabled = Visible;
        if (Visible) RefreshCharacter();
    }

    private void RefreshCharacter()
    {
        if (!Visible || FindForm()?.WindowState == FormWindowState.Minimized) return;
        var player = Game.Ready ? Game.Player : null;
        SetText(lblPlayerName, player?.Name ?? LanguageManager.GetLang("LabelPlayerName"));
        SetText(lblLevel, player == null ? "0" : $"lv.{player.Level}");
        SetText(lblInt, player?.Intelligence.ToString() ?? "0");
        SetText(lblStr, player?.Strength.ToString() ?? "0");
        SetText(lblGold, player?.Gold.ToString("#,#0") ?? "0");
        SetText(lblSP, player?.SkillPoints.ToString("#,#0") ?? "0");
        SetProgress(progressHP, player?.Health ?? 0, player?.MaximumHealth ?? 0);
        SetProgress(progressMP, player?.Mana ?? 0, player?.MaximumMana ?? 0);
        var maximumExperience = player == null ? 0 : Game.ReferenceManager.GetRefLevel(player.Level)?.Exp_C ?? 0;
        SetProgress(progressEXP, player?.Experience ?? 0, maximumExperience);
    }

    private static void SetText(Control control, string text)
    {
        if (control.Text != text) control.Text = text;
    }

    private static void SetProgress(SDUI.Controls.ProgressBar progress, long value, long maximum)
    {
        maximum = Math.Max(0, maximum);
        value = Math.Clamp(value, 0, maximum);
        if (progress.Maximum != maximum) progress.Maximum = maximum;
        if (progress.Value != value) progress.Value = value;
    }
}
