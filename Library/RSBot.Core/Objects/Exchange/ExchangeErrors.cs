namespace RSBot.Core.Objects.Exchange;

/// <summary>
///     Readable texts for the error codes of the exchange packets, as seen on the server so far.
/// </summary>
public static class ExchangeErrors
{
    public const ushort PartnerInventoryFull = 0x1836;
    public const ushort ItemNotTradable = 0x183A;
    public const ushort GoldRefused = 0x1829;
    public const ushort NothingToExchange = 0x1823;
    public const ushort CanceledByPlayer = 0x182C;
    public const ushort NoExchange = 0x181B;

    /// <summary>
    ///     Gets the text of the error code.
    /// </summary>
    public static string Describe(ushort code)
    {
        return code switch
        {
            PartnerInventoryFull => "the partner's inventory is full",
            ItemNotTradable => "this item can not be exchanged",
            GoldRefused => "the gold amount was refused",
            NothingToExchange => "there is nothing to exchange",
            CanceledByPlayer => "a player canceled the exchange",
            NoExchange => "no exchange is open",
            _ => $"error 0x{code:X4}",
        };
    }
}
