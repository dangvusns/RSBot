using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RSBot.Core.Components;

namespace RSBot.Core.Network;

public class PacketManager
{
    /// <summary>
    ///     <inheritdoc />
    /// </summary>
    private static readonly object _lock = new();
    private static readonly object _hookLock = new();

    /// <summary>
    ///     Gets the handlers.
    /// </summary>
    /// <value>
    ///     The handlers.
    /// </value>
    internal static List<IPacketHandler> Handlers;

    /// <summary>
    ///     Gets the hooks.
    /// </summary>
    internal static List<IPacketHook> Hooks;

    /// <summary>
    ///     The callbacks
    /// </summary>
    private static readonly List<AwaitCallback> _callbacks = new();

    public static int PendingCallbackCount
    {
        get { lock (_lock) return _callbacks.Count; }
    }

    internal static void RemoveCallback(AwaitCallback callback)
    {
        lock (_lock)
            _callbacks.Remove(callback);
    }

    internal static void CancelCallbacks()
    {
        AwaitCallback[] callbacks;
        lock (_lock)
        {
            callbacks = _callbacks.ToArray();
            _callbacks.Clear();
        }
        foreach (var callback in callbacks)
            callback.Cancel();
    }

    /// <summary>
    ///     Registers the handler.
    /// </summary>
    /// <param name="handler">The handler.</param>
    public static void RegisterHandler(IPacketHandler handler)
    {
        if (Handlers == null)
            Handlers = new List<IPacketHandler>();

        Handlers.Add(handler);
    }

    /// <summary>
    ///     Removes the handler.
    /// </summary>
    /// <param name="handler">The handler.</param>
    public static void RemoveHandler(IPacketHandler handler)
    {
        Handlers?.Remove(handler);
    }

    /// <summary>
    ///     Registers the hook.
    /// </summary>
    /// <param name="hook">The hook.</param>
    public static void RegisterHook(IPacketHook hook)
    {
        // Copy on write: hooks can be added while packets are being dispatched on other threads
        lock (_hookLock)
            Hooks = new List<IPacketHook>(Hooks ?? new List<IPacketHook>()) { hook };
    }

    /// <summary>
    ///     Removes the hook.
    /// </summary>
    /// <param name="hook">The hook.</param>
    public static void RemoveHook(IPacketHook hook)
    {
        lock (_hookLock)
        {
            if (Hooks == null || !Hooks.Contains(hook))
                return;

            var hooks = new List<IPacketHook>(Hooks);
            hooks.Remove(hook);
            Hooks = hooks;
        }
    }

    /// <summary>
    ///     Calls the specified packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    /// <param name="destination">The destination.</param>
    internal static void CallHandler(Packet packet, PacketDestination destination)
    {
        if (Handlers == null)
            return;

        foreach (
            var handler in Handlers.Where(handler =>
                handler != null && handler.Opcode == packet.Opcode && handler.Destination == destination
            )
        )
        {
            handler.Invoke(packet);
            packet.SeekRead(0, SeekOrigin.Begin);
        }
    }

    /// <summary>
    ///     Calls the registered hooks and returns a replaced packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    /// <param name="destination">The destination.</param>
    /// <returns></returns>
    internal static Packet CallHook(Packet packet, PacketDestination destination)
    {
        var hooks = Hooks?.Where(hook =>
            packet != null && hook.Opcode == packet.Opcode && hook.Destination == destination
        );
        foreach (var hook in hooks)
            packet = hook.ReplacePacket(packet);

        return packet;
    }

    /// <summary>
    ///     Calls the callback.
    /// </summary>
    /// <param name="packet">The packet.</param>
    internal static void CallCallback(Packet packet)
    {
        AwaitCallback[] callbacks;
        lock (_lock)
        {
            if (_callbacks.Count == 0)
                return;
            callbacks = _callbacks.Where(c => c.ResponseOpcode == packet.Opcode && !c.IsClosed).ToArray();
        }

        try
        {
            foreach (var callback in callbacks)
            {
                packet.SeekRead(0, SeekOrigin.Begin);
                callback.Invoke(packet);
            }

        }
        finally
        {
            packet.SeekRead(0, SeekOrigin.Begin);
        }
    }

    /// <summary>
    ///     Sends the packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    /// <param name="destination">The destination.</param>
    public static void SendPacket(Packet packet, PacketDestination destination)
    {
        SendPacket(packet, destination, false);
    }

    /// <summary>
    ///     Sends the packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    /// <param name="destination">The destination.</param>
    /// <param name="forwarded">Whether the packet was received by the proxy and is only being forwarded.</param>
    internal static void SendPacket(Packet packet, PacketDestination destination, bool forwarded)
    {
        TrySendPacket(packet, destination, forwarded);
    }

    private static bool TrySendPacket(Packet packet, PacketDestination destination, bool forwarded)
    {
        var proxy = Kernel.Proxy;
        if (proxy == null)
            return false;

        if (!packet.Locked)
            packet.Lock();

        if (!forwarded && PacketMonitor.IsActive && !(destination == PacketDestination.Client && Game.Clientless))
            PacketMonitor.Capture(packet, destination, PacketOrigin.BotInjected);

        try
        {
            switch (destination)
            {
                case PacketDestination.Client:
                    if (Game.Clientless || proxy.Client?.IsConnected != true)
                        return false;
                    proxy.Client.Send(packet);
                    break;

                case PacketDestination.Server:
                    if (proxy.Server?.IsConnected != true)
                        return false;
                    if (packet.Opcode == 0x7034)
                        ShoppingManager.ObserveInventoryRequest(packet);
                    proxy.Server.Send(packet);
                    break;
                default:
                    return false;
            }
            return true;
        }
        catch (Exception e)
        {
            Log.Fatal(e);
            return false;
        }
    }

    /// <summary>
    ///     Sends the packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    /// <param name="destination">The destination.</param>
    /// <param name="callback">The callback.</param>
    public static void SendPacket(Packet packet, PacketDestination destination, params AwaitCallback[] callbacks)
    {
        callbacks ??= Array.Empty<AwaitCallback>();
        if (callbacks.Any(c => c?.IsClosed == true))
        {
            foreach (var callback in callbacks)
                callback?.Cancel();
            return;
        }
        lock (_lock)
        {
            _callbacks.AddRange(callbacks.Where(c => c != null && !c.IsClosed));
        }

        if (!TrySendPacket(packet, destination, false))
            foreach (var callback in callbacks)
                callback?.NotSent();
    }

    /// <summary>
    ///     Gets the handlers by the specified opcode. If none specified, all handlers will be returned.
    /// </summary>
    /// <param name="opcode">The opcode.</param>
    /// <returns></returns>
    public static List<IPacketHandler> GetHandlers(ushort? opcode = null)
    {
        if (opcode == null)
            return Handlers;

        return Handlers.Where(h => h.Opcode == opcode).ToList();
    }

    /// <summary>
    ///     Gets the hooks by the specified opcode. If none specified, all hooks will be returned.
    /// </summary>
    /// <param name="opcode">The opcode.</param>
    /// <returns></returns>
    public static List<IPacketHook> GetHooks(ushort? opcode = null)
    {
        var hooks = Hooks;
        if (hooks == null)
            return new List<IPacketHook>();

        return opcode == null ? new List<IPacketHook>(hooks) : hooks.Where(h => h.Opcode == opcode).ToList();
    }
}
