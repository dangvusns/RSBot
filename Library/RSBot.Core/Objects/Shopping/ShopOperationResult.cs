using System;
using RSBot.Core.Network;
using RSBot.Core.Objects.Inventory;

namespace RSBot.Core.Objects.Shopping;

public enum ShopOperationOutcome
{
    Confirmed,
    NotSent,
    Busy,
    Cancelled,
    TimedOut,
    // An inventory error has no request identity; it cannot prove this purchase was rejected.
    Unconfirmed,
}

public readonly record struct ShopOperationResult(ShopOperationOutcome Outcome, ushort? ErrorCode = null)
{
    public bool IsConfirmed => Outcome == ShopOperationOutcome.Confirmed;
}

/// <summary>Matches the fields shared with an NPC request without consulting mutable game state.</summary>
public sealed class ShopResponseMatcher
{
    private readonly InventoryOperation _operation;
    private readonly uint? _actorId;
    private readonly byte _slot;
    private readonly byte _tab;
    private readonly ushort _amount;
    private readonly uint _npcId;

    public ushort? ErrorCode { get; private set; }

    public ShopResponseMatcher(InventoryOperation operation, byte slot, ushort amount,
        byte tab = 0, uint npcId = 0, uint? actorId = null)
    {
        _operation = operation;
        _slot = slot;
        _amount = amount;
        _tab = tab;
        _npcId = npcId;
        _actorId = actorId;
    }

    public AwaitCallbackResult Match(Packet packet, GameClientType clientType)
    {
        if (packet.Opcode != 0xB034)
            return AwaitCallbackResult.ConditionFailed;

        if (packet.ReadByte() != 1)
        {
            ErrorCode = packet.Remaining >= 2 ? packet.ReadUShort() : packet.ReadByte();
            return AwaitCallbackResult.Fail;
        }

        if ((InventoryOperation)packet.ReadByte() != _operation)
            return AwaitCallbackResult.ConditionFailed;

        if (_actorId.HasValue && packet.ReadUInt() != _actorId.Value)
            return AwaitCallbackResult.ConditionFailed;

        if (_operation is InventoryOperation.SP_SELL_ITEM or InventoryOperation.SP_SELL_ITEM_COS)
        {
            var source = packet.ReadByte();
            var amount = packet.ReadUShort();
            var npc = packet.ReadUInt();
            packet.ReadByte(); // buyback slot
            return source == _slot && amount == _amount && npc == _npcId
                ? AwaitCallbackResult.Success : AwaitCallbackResult.ConditionFailed;
        }

        if (packet.ReadByte() != _tab || packet.ReadByte() != _slot)
            return AwaitCallbackResult.ConditionFailed;

        ushort quantity;
        byte count;
        if (clientType >= GameClientType.Chinese && clientType != GameClientType.Rigid)
        {
            quantity = packet.ReadUShort();
            count = packet.ReadByte();
            if (packet.ReadBytes(count).Length != count)
                return AwaitCallbackResult.Fail;
        }
        else
        {
            count = packet.ReadByte();
            if (packet.ReadBytes(count).Length != count)
                return AwaitCallbackResult.Fail;
            quantity = packet.ReadUShort();
        }

        // NPC package sizes can differ from the requested count. The caller must also verify
        // that the authoritative inventory actually changed for the expected item.
        return count > 0 && quantity > 0 ? AwaitCallbackResult.Success : AwaitCallbackResult.Fail;
    }
}
