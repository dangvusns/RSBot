using System.Collections.Generic;
using RSBot.Core.Components;
using RSBot.Core.Network;
using RSBot.Core.Objects.Inventory;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Core.Objects.Exchange;

public class ExchangeInstance
{
    #region Fields

    private readonly uint _exchangePlayerUniqueId;

    #endregion Fields

    /// <summary>
    ///     Initializes a new instance of the <see cref="ExchangeInstance" /> class.
    /// </summary>
    /// <param name="exchangePlayerUniqueId">The exchange player unique identifier.</param>
    public ExchangeInstance(uint exchangePlayerUniqueId)
    {
        _exchangePlayerUniqueId = exchangePlayerUniqueId;
    }

    /// <summary>
    ///     Gets the receiving items.
    /// </summary>
    /// <value>
    ///     The receiving items.
    /// </value>
    public List<ExchangeItem> ReceivingItems { get; private set; }

    /// <summary>
    ///     Gets the sending items.
    /// </summary>
    /// <value>
    ///     The sending items.
    /// </value>
    public List<ExchangeItem> SendingItems { get; private set; }

    /// <summary>
    ///     Gets the exchange player.
    /// </summary>
    /// <value>
    ///     The exchange player.
    /// </value>
    public SpawnedPlayer ExchangePlayer => SpawnManager.GetEntity<SpawnedPlayer>(_exchangePlayerUniqueId);

    /// <summary>
    ///     Gets the unique id of the exchange partner.
    /// </summary>
    public uint ExchangePlayerUniqueId => _exchangePlayerUniqueId;

    /// <summary>
    ///     Gets the name of the exchange partner, or its unique id when it is not spawned.
    /// </summary>
    public string ExchangePlayerName => ExchangePlayer?.Name ?? $"#{_exchangePlayerUniqueId}";

    /// <summary>
    ///     Gets or sets the gold the player puts into the exchange.
    /// </summary>
    public ulong SendingGold { get; internal set; }

    /// <summary>
    ///     Gets or sets the gold the partner puts into the exchange.
    /// </summary>
    public ulong ReceivingGold { get; internal set; }

    /// <summary>
    ///     Gets a value indicating whether neither side offers items or gold; the server refuses to approve that.
    /// </summary>
    public bool IsEmpty =>
        (SendingItems?.Count ?? 0) == 0 && (ReceivingItems?.Count ?? 0) == 0 && SendingGold == 0 && ReceivingGold == 0;

    /// <summary>
    ///     The last item or gold operation sent, to explain an error answer of the server.
    /// </summary>
    internal InventoryOperation? PendingOperation { get; set; }

    internal int PendingTick { get; set; }

    /// <summary>
    ///     The gold amount of the last gold operation sent.
    /// </summary>
    internal ulong PendingGold { get; set; }

    private void SetPending(InventoryOperation operation)
    {
        PendingOperation = operation;
        PendingTick = Kernel.TickCount;
    }

    /// <summary>
    ///     Invites the specified player to an exchange (0x7081).
    /// </summary>
    /// <param name="playerUniqueId">The unique id of the player.</param>
    public static void Invite(uint playerUniqueId)
    {
        var packet = new Packet(0x7081);
        packet.WriteUInt(playerUniqueId);

        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    /// <summary>
    ///     Puts an inventory item into the exchange window (0x7034, SP_ADD_EXCHANGE).
    /// </summary>
    /// <param name="inventorySlot">The inventory slot of the item.</param>
    public void AddItem(byte inventorySlot)
    {
        var packet = new Packet(0x7034);
        packet.WriteByte(InventoryOperation.SP_ADD_EXCHANGE);
        packet.WriteByte(inventorySlot);

        SetPending(InventoryOperation.SP_ADD_EXCHANGE);

        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    /// <summary>
    ///     Takes an item back out of the exchange window (0x7034, SP_DEL_EXCHANGE).
    /// </summary>
    /// <param name="exchangeSlot">The slot of the item in the exchange window.</param>
    public void RemoveItem(byte exchangeSlot)
    {
        var packet = new Packet(0x7034);
        packet.WriteByte(InventoryOperation.SP_DEL_EXCHANGE);
        packet.WriteByte(exchangeSlot);

        SetPending(InventoryOperation.SP_DEL_EXCHANGE);

        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    /// <summary>
    ///     Sets the gold put into the exchange window (0x7034, SP_UPDATE_EXCHANGE_GOLD).
    /// </summary>
    /// <param name="gold">The amount of gold.</param>
    public void SetGold(ulong gold)
    {
        var packet = new Packet(0x7034);
        packet.WriteByte(InventoryOperation.SP_UPDATE_EXCHANGE_GOLD);
        packet.WriteULong(gold);

        SetPending(InventoryOperation.SP_UPDATE_EXCHANGE_GOLD);
        PendingGold = gold;

        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    /// <summary>
    ///     Confirms (locks) the exchange (0x7082).
    /// </summary>
    public void Confirm()
    {
        PacketManager.SendPacket(new Packet(0x7082), PacketDestination.Server);
    }

    /// <summary>
    ///     Approves the exchange after both sides confirmed (0x7083).
    /// </summary>
    public void Approve()
    {
        PacketManager.SendPacket(new Packet(0x7083), PacketDestination.Server);
    }

    /// <summary>
    ///     Cancels the exchange (0x7084).
    /// </summary>
    public void Cancel()
    {
        PacketManager.SendPacket(new Packet(0x7084), PacketDestination.Server);
    }

    /// <summary>
    ///     Updates the items.
    /// </summary>
    /// <param name="packet">The packet.</param>
    public void UpdateItems(Packet packet)
    {
        var ownerUniqueId = packet.ReadUInt();
        var playerIsSender = ownerUniqueId == Game.Player.UniqueId;

        if (playerIsSender)
            SendingItems = new List<ExchangeItem>(12);
        else
            ReceivingItems = new List<ExchangeItem>(12);

        var itemCount = packet.ReadByte();
        for (var i = 0; i < itemCount; i++)
        {
            var item = ExchangeItem.FromPacket(packet, playerIsSender);

            if (item.Item == null)
            {
                Log.Debug($"Could not detect item at exchange slot #{item.ExchangeSlot}");
                continue;
            }

            if (playerIsSender)
                SendingItems.Add(item);
            else
                ReceivingItems.Add(item);
        }
    }

    /// <summary>
    ///     Completes the exchange request. It updates the inventory item by the temporary stored information.
    /// </summary>
    public void Complete()
    {
        // A one-way exchange receives nothing, but the items given away still leave the inventory
        if (ReceivingItems != null)
            foreach (var item in ReceivingItems)
            {
                item.Item.Slot = Game.Player.Inventory.GetFreeSlot();
                Game.Player.Inventory.Add(item.Item);
            }

        if (SendingItems != null)
            foreach (var item in SendingItems)
                Game.Player.Inventory.RemoveAt(item.SourceSlot);
    }
}
