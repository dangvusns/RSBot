using System;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;

namespace RSBot.Statistics.Stats.Calculators.Static;

/// <summary>
///     Counts the picked up items of one kind, by their amount.
/// </summary>
internal abstract class PickedItems : IStatisticCalculator
{
    private long _currentValue;

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public string Label => LanguageManager.GetLang($"Calculators.{Name}.Label");

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
        EventManager.SubscribeEvent("OnPickupItem", new Action<InventoryItem>(OnPickupItem));
        EventManager.SubscribeEvent("OnPartyPickItem", new Action<InventoryItem>(OnPickupItem));
    }

    /// <summary>
    ///     Whether the item is counted by this calculator.
    /// </summary>
    protected abstract bool Matches(RefObjItem record);

    private void OnPickupItem(InventoryItem item)
    {
        var record = item?.Record;
        if (record == null || !Matches(record))
            return;

        _currentValue += Math.Max((int)item.Amount, 1);
    }
}

/// <summary>
///     Elixirs (LKD): the same item type the alchemy botbase uses for elixirs.
/// </summary>
internal class ElixirsPicked : PickedItems
{
    /// <inheritdoc />
    public override string Name => "ElixirsPicked";

    /// <inheritdoc />
    protected override bool Matches(RefObjItem record)
    {
        return record.TypeID1 == 3 && record.TypeID2 == 3 && record.TypeID3 == 10 && record.TypeID4 == 1;
    }
}

/// <summary>
///     Tablets (Tấm lót): alchemy tablets, recognized by their code name.
/// </summary>
internal class TabletsPicked : PickedItems
{
    /// <inheritdoc />
    public override string Name => "TabletsPicked";

    /// <inheritdoc />
    protected override bool Matches(RefObjItem record)
    {
        return record.IsStackable
            && record.CodeName != null
            && record.CodeName.Contains("TABLET", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
///     Equipment (Trang bị): weapons, armor, shields and accessories, without avatars.
/// </summary>
internal class EquipmentPicked : PickedItems
{
    /// <inheritdoc />
    public override string Name => "EquipmentPicked";

    /// <inheritdoc />
    protected override bool Matches(RefObjItem record)
    {
        return record.IsEquip && !record.IsAvatar;
    }
}
