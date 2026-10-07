using RSBot.Core.Event;

namespace RSBot.Core.Network.Handler.Agent.Exchange;

internal class ExchangeCanceledResponse : IPacketHandler
{
    /// <inheritdoc />
    public ushort Opcode => 0x3088;

    /// <inheritdoc />
    public PacketDestination Destination => PacketDestination.Client;

    /// <inheritdoc />
    public void Invoke(Packet packet)
    {
        Game.Player.Exchange = null;

        var errorCode = packet.Remaining >= 2 ? packet.ReadUShort() : (ushort)0;
        Log.Notify(errorCode == 0 ? "Exchange has been canceled." : $"Exchange has been canceled: {RSBot.Core.Objects.Exchange.ExchangeErrors.Describe(errorCode)}.");

        if (errorCode != 0)
            EventManager.FireEvent("OnExchangeCanceledReason", errorCode);

        EventManager.FireEvent("OnCancelExchange");
    }
}
