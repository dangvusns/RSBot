using RSBot.Core.Event;

namespace RSBot.Core.Network.Handler.Agent.Inventory;

internal class InventoryItemUseResponse : IPacketHandler
{
    /// <summary>
    ///     Gets or sets the opcode.
    /// </summary>
    /// <value>
    ///     The opcode.
    /// </value>
    public ushort Opcode => 0xB04C;

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
        if (packet.ReadByte() != 0x01)
            return;

        var sourceSlot = packet.ReadByte();
        var newAmount = packet.ReadUShort();

        // Read before the amount update, the item is gone when the last one was used
        var usedItem = Game.Player.Inventory.GetItemAt(sourceSlot)?.Record;

        Game.Player.Inventory.UpdateItemAmount(sourceSlot, newAmount);
        Game.Player.OnPotionUsed(usedItem);

        EventManager.FireEvent("OnUseItem", sourceSlot);
    }
}
