using RSBot.Core.Event;
using RSBot.Core.Objects.Exchange;

namespace RSBot.Core.Network.Handler.Agent.Exchange;

internal class ExchangeStartResponse : IPacketHandler
{
    /// <inheritdoc />
    public ushort Opcode => 0xB081;

    /// <inheritdoc />
    public PacketDestination Destination => PacketDestination.Client;

    /// <inheritdoc />
    public void Invoke(Packet packet)
    {
        var result = packet.ReadByte();
        if (result != 1)
        {
            var errorCode = result == 2 && packet.Remaining >= 2 ? packet.ReadUShort() : (ushort)0;
            Log.Warn($"The exchange could not be started (result {result}, code 0x{errorCode:X4}).");
            return;
        }

        var playerUniqueId = packet.ReadUInt();
        Game.Player.Exchange = new ExchangeInstance(playerUniqueId);

        Log.Notify($"Started exchanging with the player {Game.Player.Exchange.ExchangePlayerName}");

        EventManager.FireEvent("OnStartExchange");
    }
}
