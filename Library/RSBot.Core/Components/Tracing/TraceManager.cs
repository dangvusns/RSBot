using System;
using RSBot.Core.Event;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     Owns the self driven trace of the character and feeds the game events to all trace sessions. The events are
///     subscribed only once for the lifetime of the manager.
/// </summary>
public static class TraceManager
{
    private static readonly object _sync = new();
    internal static readonly object CommandLock = new();
    private static volatile TraceSession[] _sessions = Array.Empty<TraceSession>();
    private static bool _initialized;

    /// <summary>
    ///     Gets the self driven trace that was started with <see cref="Start" />, <c>null</c> if there is none.
    /// </summary>
    public static TraceSession Current { get; private set; }

    /// <summary>
    ///     Starts to trace a player. A running self driven trace is replaced.
    /// </summary>
    /// <param name="targetName">The name of the player.</param>
    /// <param name="mode">The trace mode.</param>
    /// <param name="options">The options, <c>null</c> for the defaults.</param>
    /// <param name="seed">The visible entity of the player if it is already known.</param>
    public static TraceSession Start(
        string targetName,
        TraceMode mode,
        TraceOptions options = null,
        SpawnedPlayer seed = null
    )
    {
        lock (CommandLock)
        {
            Current?.Stop(false);

            var session = new TraceSession(targetName, mode, options, true);
            Current = session;
            session.Start(seed);

            return session;
        }
    }

    /// <summary>
    ///     Stops the self driven trace.
    /// </summary>
    public static void Stop()
    {
        StopCurrent(true);
    }

    /// <summary>
    ///     Adds a session to the event feed.
    /// </summary>
    internal static void Register(TraceSession session)
    {
        lock (_sync)
        {
            Initialize();

            if (Array.IndexOf(_sessions, session) >= 0)
                return;

            var sessions = new TraceSession[_sessions.Length + 1];
            _sessions.CopyTo(sessions, 0);
            sessions[sessions.Length - 1] = session;

            _sessions = sessions;
        }
    }

    /// <summary>
    ///     Removes a session from the event feed.
    /// </summary>
    internal static void Unregister(TraceSession session)
    {
        lock (_sync)
        {
            var index = Array.IndexOf(_sessions, session);
            if (index < 0)
                return;

            var sessions = new TraceSession[_sessions.Length - 1];
            Array.Copy(_sessions, 0, sessions, 0, index);
            Array.Copy(_sessions, index + 1, sessions, index, _sessions.Length - index - 1);

            _sessions = sessions;
        }
    }

    private static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;

        EventManager.SubscribeEvent("OnTick", new Action(OnTick));
        EventManager.SubscribeEvent("OnSpawnPlayer", new Action<SpawnedPlayer>(OnSpawnPlayer));
        EventManager.SubscribeEvent("OnDespawnEntity", new Action<SpawnedEntity>(OnDespawnEntity));
        EventManager.SubscribeEvent("OnLoadCharacter", new Action(OnWorldReset));
        EventManager.SubscribeEvent("OnAgentServerDisconnected", new Action(OnDisconnected));
        EventManager.SubscribeEvent("OnPartyDismiss", new Action(OnPartyDismiss));
    }

    private static void OnTick()
    {
        var sessions = _sessions;

        for (var i = 0; i < sessions.Length; i++)
            try
            {
                sessions[i].OnTick();
            }
            catch (Exception e)
            {
                Log.Fatal(e);
            }
    }

    private static void OnSpawnPlayer(SpawnedPlayer player)
    {
        foreach (var session in _sessions)
            session.OnSpawn(player);
    }

    private static void OnDespawnEntity(SpawnedEntity entity)
    {
        foreach (var session in _sessions)
            session.OnDespawn(entity);
    }

    private static void OnWorldReset()
    {
        foreach (var session in _sessions)
            session.OnWorldReset();
    }

    private static void StopCurrent(bool cancelAction)
    {
        lock (CommandLock)
        {
            var session = Current;
            Current = null;
            session?.Stop(cancelAction);
        }
    }

    private static void OnDisconnected()
    {
        OnWorldReset();

        StopCurrent(false);
    }

    private static void OnPartyDismiss()
    {
        StopCurrent(false);
    }
}
