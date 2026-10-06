using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Extensions;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Python.Components;

/// <summary>
///     Passes received chat messages to on_chat(type, sender, message) of the Python plugins.
/// </summary>
internal class ChatReceivedHandler : IPacketHandler
{
    public ushort Opcode => 0x3026;

    public PacketDestination Destination => PacketDestination.Client;

    public void Invoke(Packet packet)
    {
        if (!PythonPluginManager.HasLoadedPlugins)
            return;

        var type = (ChatType)packet.ReadByte();
        string sender;
        string message;

        switch (type)
        {
            case ChatType.All:
            case ChatType.AllGM:
                var senderId = packet.ReadUInt();
                message = packet.ReadConditonalString();

                if (senderId == Game.Player?.UniqueId)
                    sender = Game.Player.Name;
                else if (SpawnManager.TryGetEntity<SpawnedPlayer>(senderId, out var player))
                    sender = player.Name;
                else
                    sender = string.Empty;

                break;

            case ChatType.Notice:
                sender = string.Empty;
                message = packet.ReadConditonalString();
                break;

            case ChatType.Npc:
                return;

            default:
                sender = packet.ReadString();
                message = packet.ReadConditonalString();
                break;
        }

        PythonPluginManager.PostAll("on_chat", (int)type, sender, message);
    }
}
