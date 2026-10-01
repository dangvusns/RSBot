using RSBot.Core.Extensions;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Objects;

namespace RSBot.Core.Components;

/// <summary>
///     Sends chat messages to the server.
/// </summary>
public static class ChatManager
{
    /// <summary>
    ///     Sends a chat message.
    /// </summary>
    /// <param name="type">The chat type.</param>
    /// <param name="message">The message.</param>
    /// <param name="receiver">The receiver, only for private messages.</param>
    public static void Send(ChatType type, string message, string receiver = null)
    {
        var packet = new Packet(0x7025);

        packet.WriteByte(type);
        packet.WriteByte(1); //chatIndex

        if (Game.ClientType > GameClientType.Vietnam)
            packet.WriteByte(0); // has linking

        if (Game.ClientType >= GameClientType.Chinese_Old)
            packet.WriteByte(0);

        if (type == ChatType.Private)
            packet.WriteString(receiver);

        packet.WriteConditonalString(message);

        PacketManager.SendPacket(packet, PacketDestination.Server);
        EventManager.FireEvent("OnSendChat", type, message);
    }

    /// <summary>
    ///     Sends a private message (whisper).
    /// </summary>
    /// <param name="receiver">The name of the receiver.</param>
    /// <param name="message">The message.</param>
    public static void SendPrivate(string receiver, string message)
    {
        Send(ChatType.Private, message, receiver);
    }
}
