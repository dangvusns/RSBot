using System;
using RSBot.Core.Components;
using RSBot.Core.Event;

namespace RSBot.Statistics.Stats.Calculators.Static;

/// <summary>
///     The gold picked up from the ground. Unlike "Gold gained", spending gold does not lower it.
/// </summary>
internal class GoldPicked : IStatisticCalculator
{
    private ulong _currentValue;

    /// <inheritdoc />
    public string Name => "GoldPicked";

    /// <inheritdoc />
    public string Label => LanguageManager.GetLang("Calculators.GoldPicked.Label");

    /// <inheritdoc />
    public StatisticsGroup Group => StatisticsGroup.Loot;

    /// <inheritdoc />
    public string ValueFormat => "{0}";

    /// <inheritdoc />
    public UpdateType UpdateType => UpdateType.Static;

    /// <inheritdoc />
    public object GetValue()
    {
        return _currentValue;
    }

    /// <inheritdoc />
    public void Reset()
    {
        _currentValue = 0;
    }

    /// <inheritdoc />
    public void Initialize()
    {
        EventManager.SubscribeEvent("OnPickupGold", new Action<uint>(OnPickupGold));
    }

    private void OnPickupGold(uint amount)
    {
        _currentValue += amount;
    }
}
