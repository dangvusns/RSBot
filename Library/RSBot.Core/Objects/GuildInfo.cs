using System.Collections.Generic;
using RSBot.Core.Network;

namespace RSBot.Core.Objects;

/// <summary>
///     The guild of the character, as sent by the server after joining the game.
/// </summary>
public class GuildInfo
{
    /// <summary>
    ///     Gets or sets the guild id.
    /// </summary>
    public uint Id { get; set; }

    /// <summary>
    ///     Gets or sets the guild name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     Gets or sets the guild level.
    /// </summary>
    public byte Level { get; set; }

    /// <summary>
    ///     Gets or sets the gathered guild points.
    /// </summary>
    public uint GatheredPoints { get; set; }

    /// <summary>
    ///     Gets or sets the title of the notice.
    /// </summary>
    public string NoticeTitle { get; set; }

    /// <summary>
    ///     Gets or sets the text of the notice.
    /// </summary>
    public string NoticeMessage { get; set; }

    /// <summary>
    ///     Gets the members.
    /// </summary>
    public List<GuildMember> Members { get; } = new();

    /// <summary>
    ///     Reads the guild data (vSRO layout).
    /// </summary>
    /// <param name="packet">The assembled guild data packet.</param>
    public static GuildInfo FromPacket(Packet packet)
    {
        var guild = new GuildInfo
        {
            Id = packet.ReadUInt(),
            Name = packet.ReadString(),
            Level = packet.ReadByte(),
            GatheredPoints = packet.ReadUInt(),
            NoticeTitle = packet.ReadString(),
            NoticeMessage = packet.ReadString(),
        };

        packet.ReadUInt(); // unknown
        packet.ReadByte(); // unknown

        var memberCount = packet.ReadByte();
        for (var i = 0; i < memberCount; i++)
        {
            var member = new GuildMember { Id = packet.ReadUInt(), Name = packet.ReadString() };

            packet.ReadByte(); // unknown
            member.Level = packet.ReadByte();
            member.GatheredPoints = packet.ReadUInt();
            member.Permissions = packet.ReadUInt();
            packet.ReadUInt(); // unknown
            packet.ReadUInt(); // unknown
            packet.ReadUInt(); // unknown
            member.Nickname = packet.ReadString();
            member.ModelId = packet.ReadUInt();
            packet.ReadBool(); // is master, also known from the permissions
            member.IsOnline = !packet.ReadBool(); // the packet says "offline"

            guild.Members.Add(member);
        }

        return guild;
    }
}
