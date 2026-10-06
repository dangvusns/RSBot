using System;

namespace RSBot.Core.Network;

/// <summary>
///     A packet hook backed by a delegate, for hooks registered at runtime (e.g. by scripts).
///     It lives in the core so that plugin assemblies don't have to declare an <see cref="IPacketHook" /> type,
///     which the extension loader would try to create with a parameterless constructor.
/// </summary>
public sealed class DelegatePacketHook(ushort opcode, PacketDestination destination, Func<Packet, Packet> replace)
    : IPacketHook
{
    /// <inheritdoc />
    public ushort Opcode { get; } = opcode;

    /// <inheritdoc />
    public PacketDestination Destination { get; } = destination;

    /// <summary>
    ///     Returns the packet to pass on, a replacement, or <c>null</c> to drop it.
    /// </summary>
    public Packet ReplacePacket(Packet packet)
    {
        return replace(packet);
    }
}
