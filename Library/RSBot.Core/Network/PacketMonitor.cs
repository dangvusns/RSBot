using System;

namespace RSBot.Core.Network;

/// <summary>
///     The server the packet was exchanged with.
/// </summary>
public enum PacketContext : byte
{
    Gateway,
    Agent,
}

/// <summary>
///     Where the captured packet came from.
/// </summary>
public enum PacketOrigin : byte
{
    /// <summary>
    ///     Received from the client or the server and forwarded.
    /// </summary>
    Received,

    /// <summary>
    ///     Created and sent by the bot itself.
    /// </summary>
    BotInjected,

    /// <summary>
    ///     A packet hook replaced the received packet with this one.
    /// </summary>
    Replaced,

    /// <summary>
    ///     A packet hook dropped the received packet, it was never forwarded.
    /// </summary>
    Dropped,
}

/// <summary>
///     A snapshot of a packet passing through the proxy.
/// </summary>
public sealed class PacketCapture
{
    public PacketCapture(
        DateTime time,
        PacketContext context,
        PacketDestination destination,
        PacketOrigin origin,
        ushort opcode,
        bool encrypted,
        bool massive,
        byte[] payload
    )
    {
        Time = time;
        Context = context;
        Destination = destination;
        Origin = origin;
        Opcode = opcode;
        Encrypted = encrypted;
        Massive = massive;
        Payload = payload;
    }

    public DateTime Time { get; }
    public PacketContext Context { get; }
    public PacketDestination Destination { get; }
    public PacketOrigin Origin { get; }
    public ushort Opcode { get; }
    public bool Encrypted { get; }
    public bool Massive { get; }

    /// <summary>
    ///     A copy of the packet payload (without the 6 byte header).
    /// </summary>
    public byte[] Payload { get; }
}

/// <summary>
///     Lets plugins observe every packet that passes through the proxy, including packets sent by the bot.
/// </summary>
public static class PacketMonitor
{
    /// <summary>
    ///     Fired on the network thread for every captured packet. Subscribers must return quickly.
    /// </summary>
    public static event Action<PacketCapture> Captured;

    /// <summary>
    ///     Gets a value indicating whether anyone is listening.
    /// </summary>
    public static bool IsActive => Captured != null;

    internal static void Capture(Packet packet, PacketDestination destination, PacketOrigin origin)
    {
        var handler = Captured;
        if (handler == null || packet == null)
            return;

        try
        {
            var context =
                Kernel.Proxy?.IsConnectedToAgentserver == true ? PacketContext.Agent : PacketContext.Gateway;

            var capture = new PacketCapture(
                DateTime.Now,
                context,
                destination,
                origin,
                packet.Opcode,
                packet.Encrypted,
                packet.Massive,
                // A locked packet returns its internal buffer, an unlocked one a fresh snapshot.
                packet.Locked ? (byte[])packet.GetBytes().Clone() : packet.GetBytes()
            );

            handler(capture);
        }
        catch (Exception e)
        {
            Log.Debug($"[PacketMonitor] {e.Message}");
        }
    }
}
