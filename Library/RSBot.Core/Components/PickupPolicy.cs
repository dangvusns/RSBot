using System;

namespace RSBot.Core.Components;

[Flags]
public enum PickupCategories
{
    None = 0,
    Gold = 1,
    Rare = 2,
    Blue = 4,
    Equipment = 8,
    Quest = 16,
    Everything = 32,
}

public static class PickupPolicy
{
    public static bool Allows(PickupCategories categories, bool gold, bool quest, bool equipment, byte rarity)
    {
        if ((categories & PickupCategories.Everything) != 0)
            return true;
        if (gold)
            return (categories & PickupCategories.Gold) != 0;
        if (quest)
            return (categories & PickupCategories.Quest) != 0;
        return (equipment && (categories & PickupCategories.Equipment) != 0)
            || (rarity >= 2 && (categories & PickupCategories.Rare) != 0)
            || (rarity >= 1 && (categories & PickupCategories.Blue) != 0);
    }
}
