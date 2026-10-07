using RSBot.Core.Event;
using RSBot.Core.Objects;

namespace RSBot.Core.Network.Handler.Agent.Exchange;

/// <summary>
///     The partner changed its offer (AGENT_EXCHANGE_UPDATE): an item, or the gold.
///     The item lists come with <see cref="ExchangeUpdateItemsResponse" />, so only the gold is read here.
/// </summary>
internal class ExchangeUpdateResponse : IPacketHandler
{
    public ushort Opcode => 0x3089;

    public PacketDestination Destination => PacketDestination.Client;

    public void Invoke(Packet packet)
    {
        var exchange = Game.Player.Exchange;
        if (exchange == null)
            return;

        var type = (ExchangeUpdateType)packet.ReadByte();
        if (type == ExchangeUpdateType.Gold && packet.Remaining >= 8)
            exchange.ReceivingGold = packet.ReadULong();

        EventManager.FireEvent("OnUpdateExchangeItems");
    }
}

/// <summary>
///     The answer to the player's approve (AGENT_EXCHANGE_APPROVE).
/// </summary>
internal class ExchangeApproveResponse : IPacketHandler
{
    public ushort Opcode => 0xB083;

    public PacketDestination Destination => PacketDestination.Client;

    public void Invoke(Packet packet)
    {
        var result = packet.ReadByte();
        if (result == 1)
            return;

        var errorCode = result == 2 && packet.Remaining >= 2 ? packet.ReadUShort() : (ushort)0;
        Log.Warn($"The exchange could not be approved (result {result}, code 0x{errorCode:X4}).");

        EventManager.FireEvent("OnExchangeApproveFailed", errorCode);
    }
}

/// <summary>
///     The answer to the player's cancel (AGENT_EXCHANGE_CANCEL).
/// </summary>
internal class ExchangeCancelResponse : IPacketHandler
{
    public ushort Opcode => 0xB084;

    public PacketDestination Destination => PacketDestination.Client;

    public void Invoke(Packet packet)
    {
        var result = packet.ReadByte();
        if (result == 1)
            return;

        var errorCode = result == 2 && packet.Remaining >= 2 ? packet.ReadUShort() : (ushort)0;
        Log.Warn($"The exchange could not be canceled (result {result}, code 0x{errorCode:X4}).");
    }
}
