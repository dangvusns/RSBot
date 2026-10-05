using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RSBot.Core.Event;

public class EventManager
{
    private static readonly List<(string name, Delegate handler)> _listeners = new();
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

        lock (_listenerLock)
            _listeners.Add((name, handler));
    }

    /// <summary>
    ///     Registers the event.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="handler">The handler.</param>
    public static void SubscribeEvent(string name, Action handler)
    {
        if (handler == null)
            return;

        lock (_listenerLock)
            _listeners.Add((name, handler));
    }

    /// <summary>
    ///     Removes a handler when its owner is disposed.
    /// </summary>
    public static void UnsubscribeEvent(string name, Delegate handler)
    {
        lock (_listenerLock)
            _listeners.RemoveAll(listener => listener.name == name && listener.handler == handler);
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
            Delegate[] targets;
            lock (_listenerLock)
                targets = (
                    from listener in _listeners
                    where listener.name == name && listener.handler.Method.GetParameters().Length == parameters.Length
                    select listener.handler
                ).ToArray();

            foreach (var target in targets)
                if (Thread.CurrentThread.Name == "Network.PacketProcessor")
                    Task.Run(() => InvokeHandler(name, target, parameters));
                else
                    InvokeHandler(name, target, parameters);
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
