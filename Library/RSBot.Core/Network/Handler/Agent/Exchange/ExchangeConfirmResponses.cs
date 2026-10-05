using RSBot.Core.Event;

namespace RSBot.Core.Network.Handler.Agent.Exchange;

/// <summary>
///     The exchange partner confirmed (locked) the exchange (0x3086).
/// </summary>
internal class ExchangePartnerConfirmedResponse : IPacketHandler
{
    /// <inheritdoc />
    public ushort Opcode => 0x3086;

    /// <inheritdoc />
    public PacketDestination Destination => PacketDestination.Client;

    /// <inheritdoc />
    public void Invoke(Packet packet)
    {
        EventManager.FireEvent("OnExchangePartnerConfirmed");
    }
}

/// <summary>
///     The answer to our own confirmation (0xB082).
/// </summary>
internal class ExchangeConfirmResponse : IPacketHandler
{
    /// <inheritdoc />
    public ushort Opcode => 0xB082;

    /// <inheritdoc />
    public PacketDestination Destination => PacketDestination.Client;

    /// <inheritdoc />
    public void Invoke(Packet packet)
    {
        var result = packet.ReadByte();
        if (result == 1)
        {
            EventManager.FireEvent("OnExchangeConfirmed");
            return;
        }

        // The wiki (AGENT_EXCHANGE_CONFIRM) documents a two-byte error code for result 2
        var errorCode = result == 2 && packet.Remaining >= 2 ? packet.ReadUShort() : (ushort)0;
        Log.Warn($"The exchange could not be confirmed (result {result}, code 0x{errorCode:X4}).");

        EventManager.FireEvent("OnExchangeConfirmFailed", errorCode);
    }
}
