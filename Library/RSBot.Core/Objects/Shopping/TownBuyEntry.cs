namespace RSBot.Core.Objects.Shopping;

/// <summary>
///     An item the town loop keeps in stock, bought from any NPC that sells it.
/// </summary>
public class TownBuyEntry
{
    /// <summary>
    ///     Gets or sets the code name of the item (not the shop package).
    /// </summary>
    public string ItemCodeName { get; set; }

    /// <summary>
    ///     Gets or sets the amount to keep in the inventory.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the item is bought.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Parses a config value in the format <c>ITEM_CODE|quantity|0/1</c>.
    /// </summary>
    public static TownBuyEntry Parse(string value)
    {
        var parts = value.Split('|');
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || !int.TryParse(parts[1], out var quantity))
            return null;

        return new TownBuyEntry
        {
            ItemCodeName = parts[0],
            Quantity = quantity,
            Enabled = parts.Length < 3 || parts[2] == "1",
        };
    }

    public override string ToString()
    {
        return $"{ItemCodeName}|{Quantity}|{(Enabled ? 1 : 0)}";
    }
}
