using RSBot.Core;
using RSBot.Core.Extensions;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using RSBot.Party.Bundle;

namespace RSBot.Party.Bundle.Commands.Network;

/// <summary>Observes chat sent from the game client, after forwarding it to the server.</summary>
internal class ChatRequest : IPacketHandler
{
    public ushort Opcode => 0x7025;
    public PacketDestination Destination => PacketDestination.Server;

    public void Invoke(Packet packet)
    {
        var type = (ChatType)packet.ReadByte();
        packet.ReadByte(); // chatIndex
        if (Game.ClientType > GameClientType.Vietnam && packet.ReadByte() != 0)
            return; // Linked-item chat is not a command.
        if (Game.ClientType >= GameClientType.Chinese_Old)
            packet.ReadByte();
        if (type == ChatType.Private)
            packet.ReadString(); // receiver
        var message = packet.ReadConditonalString();
        Container.Commands?.QueueSentChat(type, message);
    }
}
