using RSBot.Core.Components;
using Xunit;

namespace RSBot.Core.Tests;

public class PickupPolicyTests
{
    [Theory]
    [InlineData(PickupCategories.Equipment, false, false, true, 0, true)]
    [InlineData(PickupCategories.Equipment, false, true, true, 0, false)]
    [InlineData(PickupCategories.Quest, false, true, false, 0, true)]
    [InlineData(PickupCategories.Rare, false, false, true, 1, false)]
    [InlineData(PickupCategories.Rare, false, false, true, 2, true)]
    [InlineData(PickupCategories.Blue, false, false, true, 2, true)]
    [InlineData(PickupCategories.None, true, false, false, 0, false)]
    [InlineData(PickupCategories.Gold, true, false, false, 0, true)]
    [InlineData(PickupCategories.Everything, false, true, false, 0, true)]
    public void CategoriesKeepQuestAndGoldSeparate(PickupCategories categories, bool gold, bool quest,
        bool equipment, byte rarity, bool expected)
    {
        Assert.Equal(expected, PickupPolicy.Allows(categories, gold, quest, equipment, rarity));
    }
}
