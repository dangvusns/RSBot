using System.ComponentModel;
using RSBot.Core;
using RSBot.Core.Event;

namespace RSBot.Views.Controls.Cos;

[ToolboxItem(false)]
public partial class Ability : CosControlBase
{
    private UiEventSubscriptions _uiEvents;
    public Ability()
    {
        InitializeComponent();
        _uiEvents = new UiEventSubscriptions(MiniCosControl);
        MiniCosControl.Satiety.Visible = false;
        MiniCosControl.Hgp.Visible = false;
        MiniCosControl.Level.Visible = false;
        progressHP.ShowAsPercent = true;
        SubscribeEvents();
    }

    /// <summary>
    ///     Subscribes the events.
    /// </summary>
    private void SubscribeEvents()
    {
        _uiEvents.Subscribe("OnAbilityPetNameChange", OnAbilityPetNameChange, coalesce: true);
    }

    private void OnAbilityPetNameChange()
    {
        if (Game.Player.AbilityPet == null)
            return;

        lblPetName.Text = Game.Player.AbilityPet.Name;
    }

    public override void Initialize()
    {
        base.Initialize();

        if (Game.Player.AbilityPet == null)
            return;

        progressHP.Value = 100;
        progressHP.Maximum = 100;

        MiniCosControl.Hp.Value = 100;
        MiniCosControl.Hp.Maximum = 100;

        lblPetName.Text = Game.Player.AbilityPet.Name;

        var icon = Game.Player.AbilityPet.Record?.GetIcon();
        if (icon != null)
        {
            var previousIcon = MiniCosControl.Icon.BackgroundImage;
            MiniCosControl.Icon.BackgroundImage = icon;
            previousIcon?.Dispose();
        }
    }
}
