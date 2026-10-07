using System.Collections.Generic;
using RSBot.Core.Extensions;
using RSBot.Core.Objects.Item;

namespace RSBot.Core.Objects;

/// <summary>
///     The kind of an item description line, used to color it like the game tooltip.
/// </summary>
public enum ItemDescriptionLineKind : byte
{
    Title,
    Info,
    WhiteStat,
    BlueStat,
    Binding,
}

/// <summary>
///     A single line of an item description.
/// </summary>
public readonly record struct ItemDescriptionLine(string Text, ItemDescriptionLineKind Kind);

public static class ItemDescription
{
    /// <summary>
    ///     Gets the lines describing the item: name, white stats (%) and blue options.
    ///     The reference data has no base stat values, so white stats are shown as percentages.
    /// </summary>
    public static List<ItemDescriptionLine> GetDescriptionLines(this InventoryItem item)
    {
        var lines = new List<ItemDescriptionLine>();
        var record = item?.Record;
        if (record == null)
            return lines;

        var title = record.GetRealName();
        if (item.OptLevel > 0)
            title += $" (+{item.OptLevel})";

        lines.Add(new ItemDescriptionLine(title, ItemDescriptionLineKind.Title));

        if (record.IsEquip)
        {
            var rarity = record.GetRarityName();
            var degree = string.IsNullOrEmpty(rarity) ? $"Degree: {record.Degree}" : $"Degree: {record.Degree} ({rarity})";
            lines.Add(new ItemDescriptionLine(degree, ItemDescriptionLineKind.Info));

            if (record.ReqLevel1 > 0)
                lines.Add(new ItemDescriptionLine($"Required level: {record.ReqLevel1}", ItemDescriptionLineKind.Info));

            lines.Add(new ItemDescriptionLine($"Durability: {item.Durability}", ItemDescriptionLineKind.Info));

            AddWhiteStats(item, lines);
        }
        else if (record.MaxStack > 1)
        {
            lines.Add(new ItemDescriptionLine($"Quantity: {item.Amount}/{record.MaxStack}", ItemDescriptionLineKind.Info));
        }

        if (item.MagicOptions != null)
        {
            foreach (var magicOption in item.MagicOptions)
            {
                var option = magicOption.Record;
                if (option != null)
                    lines.Add(new ItemDescriptionLine(option.GetFusingTranslation(magicOption.Value), ItemDescriptionLineKind.BlueStat));
            }
        }

        if (item.BindingOptions != null)
        {
            foreach (var binding in item.BindingOptions)
            {
                var text = binding.Type == BindingOptionType.AdvancedElixir
                    ? $"Advanced elixir: +{binding.Value}"
                    : $"Socket {binding.Slot + 1}: {Game.ReferenceManager.GetMagicOption(binding.Id)?.GetFusingTranslation(binding.Value) ?? binding.Id.ToString()}";

                lines.Add(new ItemDescriptionLine(text, ItemDescriptionLineKind.Binding));
            }
        }

        return lines;
    }

    private static void AddWhiteStats(InventoryItem item, List<ItemDescriptionLine> lines)
    {
        var groups = ItemAttributesInfo.GetAvailableAttributeGroupsForItem(item.Record);
        if (groups == null)
            return;

        foreach (var group in groups)
        {
            var slot = ItemAttributesInfo.GetAttributeSlotForItem(group, item.Record);

            lines.Add(new ItemDescriptionLine($"{group.GetTranslation()}: {item.Attributes.GetPercentage(slot)}%", ItemDescriptionLineKind.WhiteStat));
        }
    }
}
