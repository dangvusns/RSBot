using RSBot.Core.Client.ReferenceObjects;

namespace RSBot.Core.Objects;

/// <summary>
///     Describes which data the server expects after [slot][tid] in the item use packet (0x704C).
///     Sending a bare packet for an item that needs more data gets the character disconnected.
/// </summary>
public enum ItemUseKind : byte
{
    /// <summary>
    ///     The item can not be used.
    /// </summary>
    None,

    /// <summary>
    ///     No extra data, <see cref="InventoryItem.Use" />.
    /// </summary>
    Simple,

    /// <summary>
    ///     Reverse return scroll, <see cref="InventoryItem.UseTo" /> with the destination type (and map id).
    /// </summary>
    ReverseScroll,

    /// <summary>
    ///     Global chatting item, <see cref="InventoryItem.UseGlobalChat" />.
    /// </summary>
    GlobalChat,

    /// <summary>
    ///     Pet potion, <see cref="InventoryItem.UseFor" /> with the unique id of a summoned pet.
    /// </summary>
    OnActivePet,

    /// <summary>
    ///     Pet revival potion, <see cref="InventoryItem.UseTo" /> with the slot of a dead pet item.
    /// </summary>
    OnDeadPetItem,

    /// <summary>
    ///     Usable, but the required data is not known. Using it may disconnect the character.
    /// </summary>
    Unknown,
}

public static class ItemUseKindExtensions
{
    /// <summary>
    ///     Gets how the item has to be used.
    /// </summary>
    public static ItemUseKind GetUseKind(this RefObjItem record)
    {
        if (record == null || record.TypeID1 != 3 || record.CanUse == ObjectUseType.No)
            return ItemUseKind.None;

        switch (record.TypeID2)
        {
            case 2: // COS summon items
                return record.TypeID3 is 1 or 2 ? ItemUseKind.Simple : ItemUseKind.Unknown;

            case 3: // ETC
                switch (record.TypeID3)
                {
                    case 1: // Potions
                        if (record.IsCosHpPotion || record.IsFellowHpPotion || record.TypeID4 == 9)
                            return ItemUseKind.OnActivePet;

                        if (record.IsCosRevivalPotion)
                            return ItemUseKind.OnDeadPetItem;

                        return record.TypeID4 is 1 or 2 or 3 or 8 ? ItemUseKind.Simple : ItemUseKind.Unknown;

                    case 2: // Pills
                        return record.TypeID4 == 7 ? ItemUseKind.OnActivePet : ItemUseKind.Simple;

                    case 3: // Scrolls, event items
                        return record.TypeID4 switch
                        {
                            3 => ItemUseKind.ReverseScroll,
                            5 or 22 => ItemUseKind.GlobalChat,
                            1 or 2 or 6 or 7 or 9 or 10 or 11 or 12 => ItemUseKind.Simple,
                            _ => ItemUseKind.Unknown,
                        };

                    case 13: // Buff scrolls
                    case 15: // Monster scrolls
                        return ItemUseKind.Simple;
                }

                break;
        }

        return ItemUseKind.Unknown;
    }
}
