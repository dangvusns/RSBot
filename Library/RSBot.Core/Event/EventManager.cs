using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RSBot.Core.Event;

public class EventManager
{
    /// <summary>
    ///     A subscribed handler with its parameter count, taken once so firing needs no reflection.
    /// </summary>
    private readonly record struct Listener(Delegate Handler, int ParameterCount);

    /// <summary>
    ///     Handlers by event name. The arrays are never changed once published (copy-on-write),
    ///     so firing reads them without taking the lock.
    /// </summary>
    private static volatile Dictionary<string, Listener[]> _listeners = new();

    private static readonly object _listenerLock = new();

    /// <summary>
    ///     Registers the event.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="handler">The handler.</param>
    public static void SubscribeEvent(string name, Delegate handler)
    {
        if (handler == null)
            return;

        var listener = new Listener(handler, handler.Method.GetParameters().Length);

        lock (_listenerLock)
        {
            var listeners = new Dictionary<string, Listener[]>(_listeners);
            listeners[name] = listeners.TryGetValue(name, out var existing) ? [.. existing, listener] : [listener];
            _listeners = listeners;
        }
    }

    /// <summary>
    ///     Registers the event.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="handler">The handler.</param>
    public static void SubscribeEvent(string name, Action handler)
    {
        SubscribeEvent(name, (Delegate)handler);
    }

    /// <summary>
    ///     Removes a handler when its owner is disposed.
    /// </summary>
    public static void UnsubscribeEvent(string name, Delegate handler)
    {
        lock (_listenerLock)
        {
            if (!_listeners.TryGetValue(name, out var existing))
                return;

            var remaining = existing.Where(listener => listener.Handler != handler).ToArray();
            if (remaining.Length == existing.Length)
                return;

            var listeners = new Dictionary<string, Listener[]>(_listeners);
            if (remaining.Length == 0)
                listeners.Remove(name);
            else
                listeners[name] = remaining;

            _listeners = listeners;
        }
    }

    /// <summary>
    ///     Fires the event.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="parameters">The parameters.</param>
    public static void FireEvent(string name, params object[] parameters)
    {
        try
        {
            // Many events (e.g. one per packet) have no subscribers at all
            if (!_listeners.TryGetValue(name, out var listeners))
                return;

            var parameterCount = parameters?.Length ?? 0;
            var onPacketThread = Thread.CurrentThread.Name == "Network.PacketProcessor";

            foreach (var listener in listeners)
            {
                if (listener.ParameterCount != parameterCount)
                    continue;

                var target = listener.Handler;
                if (onPacketThread)
                    Task.Run(() => InvokeHandler(name, target, parameters));
                else
                    InvokeHandler(name, target, parameters);
            }
        }
        catch (Exception e)
        {
            Log.Fatal(e);
        }
    }

    /// <summary>
    ///     Invokes one handler; a failing handler must not keep the other handlers of the event from running.
    /// </summary>
    private static void InvokeHandler(string name, Delegate target, object[] parameters)
    {
        try
        {
            if (target is Action action)
                action();
            else
                target.DynamicInvoke(parameters);
        }
        catch (Exception e)
        {
            // Logging fires OnAddLog itself; a failing log handler would otherwise fail again forever
            if (name == "OnAddLog")
            {
                System.Diagnostics.Debug.WriteLine(e);
                return;
            }

            Log.Warn($"[Event] A handler of {name} failed: {target.Method.DeclaringType?.FullName}.{target.Method.Name}");
            Log.Fatal(e is TargetInvocationException { InnerException: not null } ? e.InnerException : e);
        }
    }
}
