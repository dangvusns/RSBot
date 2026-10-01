using System;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Objects;

namespace RSBot.Core.Components;

/// <summary>
///     Keeps the guild data of the character. It is not stored on the player, because the player object is
///     recreated on every map load while the guild data is only sent after joining the game.
/// </summary>
public static class GuildManager
{
    private static readonly object _lock = new();
    private static Packet _buffer;

    /// <summary>
    ///     Gets the guild of the character, <c>null</c> if it is unknown or the character has no guild.
    /// </summary>
    public static GuildInfo Guild { get; private set; }

    /// <summary>
    ///     Invites the specified player into the guild.
    /// </summary>
    /// <param name="playerUniqueId">The unique id of the player.</param>
    public static void Invite(uint playerUniqueId)
    {
        var packet = new Packet(0x70F3);
        packet.WriteUInt(playerUniqueId);

        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    /// <summary>
    ///     Starts receiving the guild data (0x34B3).
    /// </summary>
    internal static void BeginData()
    {
        lock (_lock)
        {
            _buffer = new Packet(0);
        }
    }

    /// <summary>
    ///     Adds a chunk of the guild data (0x3101).
    /// </summary>
    /// <param name="packet">The chunk.</param>
    internal static void AppendData(Packet packet)
    {
        lock (_lock)
        {
            _buffer?.WriteBytes(packet.GetBytes());
        }
    }

    /// <summary>
    ///     Reads the received guild data (0x34B4).
    /// </summary>
    internal static void EndData()
    {
        Packet packet;
        lock (_lock)
        {
            packet = _buffer;
            _buffer = null;
        }

        if (packet == null)
            return;

        try
        {
            packet.Lock();
            Guild = GuildInfo.FromPacket(packet);

            Log.Notify($"[Guild] {Guild.Name}: {Guild.Members.Count} member(s)");
            EventManager.FireEvent("OnGuildData");
        }
        catch (Exception e)
        {
            Log.Warn($"[Guild] The guild data could not be read (unsupported client version?): {e.Message}");
        }
    }
}
