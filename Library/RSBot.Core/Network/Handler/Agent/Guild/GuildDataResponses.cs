using RSBot.Core.Components;

namespace RSBot.Core.Network.Handler.Agent.Guild;

/// <summary>
///     The guild data starts (0x34B3).
/// </summary>
internal class GuildDataBeginResponse : IPacketHandler
{
    /// <inheritdoc />
    public ushort Opcode => 0x34B3;

    /// <inheritdoc />
    public PacketDestination Destination => PacketDestination.Client;

    /// <inheritdoc />
    public void Invoke(Packet packet)
    {
        GuildManager.BeginData();
    }
}

/// <summary>
///     A chunk of the guild data (0x3101).
/// </summary>
internal class GuildDataResponse : IPacketHandler
{
    /// <inheritdoc />
    public ushort Opcode => 0x3101;

    /// <inheritdoc />
    public PacketDestination Destination => PacketDestination.Client;

    /// <inheritdoc />
    public void Invoke(Packet packet)
    {
        GuildManager.AppendData(packet);
    }
}

/// <summary>
///     The guild data is complete (0x34B4).
/// </summary>
internal class GuildDataEndResponse : IPacketHandler
{
    /// <inheritdoc />
    public ushort Opcode => 0x34B4;

    /// <inheritdoc />
    public PacketDestination Destination => PacketDestination.Client;

    /// <inheritdoc />
    public void Invoke(Packet packet)
    {
        GuildManager.EndData();
    }
}
