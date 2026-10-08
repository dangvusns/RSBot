using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using RSBot.Core.Network;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Core.Objects.Party;

public class PartyMember
{
    /// <summary>
    ///     Gets or sets the guild.
    /// </summary>
    /// <value>
    ///     The guild.
    /// </value>
    public string Guild;

    /// <summary>
    ///     Gets or sets the health mana: the low nibble is HP, the high nibble MP, each in steps of 10%.
    /// </summary>
    /// <value>
    ///     The health mana.
    /// </value>
    public byte HealthMana;

    /// <summary>
    ///     The highest HP/MP step; the server sometimes sends 11 for full.
    /// </summary>
    public const int FullSteps = 10;

    /// <summary>
    ///     Gets the HP in steps of 10% (0-10).
    /// </summary>
    public int HealthSteps => ClampSteps(HealthMana & 0x0F);

    /// <summary>
    ///     Gets the MP in steps of 10% (0-10).
    /// </summary>
    public int ManaSteps => ClampSteps(HealthMana >> 4);

    private static int ClampSteps(int steps)
    {
        return steps > FullSteps ? FullSteps : steps;
    }

    /// <summary>
    ///     Gets or sets the level.
    /// </summary>
    public byte Level;

    /// <summary>
    ///     Gets or sets the mastery id1.
    /// </summary>
    /// <value>
    ///     The mastery id1.
    /// </value>
    public uint MasteryId1;

    /// <summary>
    ///     Gets or sets the mastery id2.
    /// </summary>
    /// <value>
    ///     The mastery id2.
    /// </value>
    public uint MasteryId2;

    /// <summary>
    ///     Gets or sets the unique identifier.
    /// </summary>
    public uint MemberId;

    /// <summary>
    ///     Gets or sets the name.
    /// </summary>
    /// <value>
    ///     The name.
    /// </value>
    public string Name;

    /// <summary>
    ///     Gets or sets the object identifier.
    /// </summary>
    public uint ObjectId;

    /// <summary>
    ///     Gets or sets the position.
    /// </summary>
    /// <value>
    ///     The position.
    /// </value>
    public Position Position;

    /// <summary>
    ///     Get the party member spawned info
    /// </summary>
    public SpawnedPlayer Player => SpawnManager.GetEntity<SpawnedPlayer>(p => p.Name == Name);

    /// <summary>
    ///     Gets the record.
    /// </summary>
    /// <value>
    ///     The record.
    /// </value>
    public RefObjChar Record => Game.ReferenceManager.GetRefObjChar(ObjectId);

    /// <summary>
    ///     Froms the packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    /// <returns></returns>
    public static PartyMember FromPacket(Packet packet)
    {
        var result = new PartyMember();

        packet.ReadByte(); //FF
        result.MemberId = packet.ReadUInt();
        result.Name = packet.ReadString();
        result.ObjectId = packet.ReadUInt();
        result.Level = packet.ReadByte();
        result.HealthMana = packet.ReadByte(); //MP|HP nibbles, 0-A -> 0%-100%
        result.Position = Position.FromPacketConditional(packet);
        result.Guild = packet.ReadString();

        packet.ReadByte(); //04

        result.MasteryId1 = packet.ReadUInt();
        result.MasteryId2 = packet.ReadUInt();

        return result;
    }

    /// <summary>
    ///     Bans this player from the party.
    /// </summary>
    public void Banish()
    {
        if (!Game.Party.IsLeader)
            return;

        var packet = new Packet(0x7063);
        packet.WriteUInt(MemberId);

        PacketManager.SendPacket(packet, PacketDestination.Server);
    }
}
