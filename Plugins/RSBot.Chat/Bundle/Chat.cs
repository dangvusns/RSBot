using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Extensions;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using System.Collections.Generic;

namespace RSBot.Chat.Bundle;

internal class Chat
{
    internal static bool IgnoreChatResponsePacket;

    internal static Dictionary<ulong, InventoryItem> LinkedItems = [];

    /// <summary>
    ///     Sends the chat packet.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="message">The message.</param>
    /// <param name="reciever">The reciever.</param>
    internal static void SendChatPacket(ChatType type, string message, string reciever = null)
    {
        ChatManager.Send(type, message, reciever);
        IgnoreChatResponsePacket = true;
    }

    internal static void SendGlobalChatPacket(string message)
    {
        var inventoryItem =
            Game.Player.Inventory.GetItem(new TypeIdFilter(3, 3, 3, 5))
            ?? Game.Player.Inventory.GetItem(new TypeIdFilter(3, 3, 3, 22)); // VIP global

        inventoryItem?.UseGlobalChat(message);
    }
}
