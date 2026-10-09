using RSBot.Core.Event;

namespace RSBot.Core.Network.Handler.Agent.Character;

internal class CharacterResurrectOptionResponse : IPacketHandler
{
    /// <summary>
    ///     Gets or sets the opcode.
    /// </summary>
    /// <value>
    ///     The opcode.
    /// </value>
    public ushort Opcode => 0x3011;

    /// <summary>
    ///     Gets or sets the destination.
    /// </summary>
    /// <value>
    ///     The destination.
    /// </value>
    public PacketDestination Destination => PacketDestination.Client;

    /// <summary>
    ///     Handles the packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    public void Invoke(Packet packet)
    {
        // 1 = specified point, 2 = present point, 4 = normal (2 = PvP/CTF, 6 = fortress)
        Game.Player.ResurrectOptions = packet.ReadByte();
        EventManager.FireEvent("OnResurrectOption");
    }
}
