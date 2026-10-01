using System;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Party.Bundle.Commands;

/// <summary>
///     Traces a commander on the bot side: the character walks to the same spots the commander clicks.
/// </summary>
internal class TraceController
{
    /// <summary>
    ///     The minimum time between two checks in milliseconds
    /// </summary>
    private const int CheckInterval = 100;

    /// <summary>
    ///     The distance to a standing commander in which the character does not move closer
    /// </summary>
    private const double CatchUpDistance = 2;

    /// <summary>
    ///     The minimum difference between two commander destinations to walk to the new one
    /// </summary>
    private const double MirrorThreshold = 0.5;

    /// <summary>
    ///     The maximum distance to the party member position the character moves to without sight of the commander
    /// </summary>
    private const double MaxMoveDistance = 150;

    /// <summary>
    ///     The tick of the last check
    /// </summary>
    private int _lastTick;

    /// <summary>
    ///     A value indicating whether a check is currently running (0 = idle, 1 = busy)
    /// </summary>
    private int _busy;

    /// <summary>
    ///     The name of the commander that is traced
    /// </summary>
    private volatile string _targetName;

    /// <summary>
    ///     The last commander destination the character walked to
    /// </summary>
    private Position _lastMirrored;

    /// <summary>
    ///     A value indicating whether <see cref="_lastMirrored" /> is set
    /// </summary>
    private bool _hasMirrored;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TraceController" /> class.
    /// </summary>
    public TraceController()
    {
        EventManager.SubscribeEvent("OnTick", OnTick);
        EventManager.SubscribeEvent("OnPartyDismiss", Reset);
        EventManager.SubscribeEvent("OnAgentServerDisconnected", Reset);
    }

    /// <summary>
    ///     Gets a value indicating whether the character is tracing a commander.
    /// </summary>
    public bool Active => _targetName != null;

    /// <summary>
    ///     Starts tracing the specified commander and stops the bot, so it does not walk the character away.
    /// </summary>
    /// <param name="commander">The commander.</param>
    public void Start(SpawnedPlayer commander)
    {
        if (Kernel.Bot.Running)
        {
            Kernel.Bot.Stop();
            Log.Notify("Bot stopped while tracing");
        }

        _hasMirrored = false;
        _lastTick = Kernel.TickCount;
        _targetName = commander.Name;

        Log.Notify($"Tracing [{commander.Name}]");
    }

    /// <summary>
    ///     Stops tracing and cancels the current action.
    /// </summary>
    public void Stop()
    {
        if (!Active)
            return;

        _targetName = null;

        Task.Run(() =>
        {
            try
            {
                SkillManager.CancelAction();
            }
            catch (Exception e)
            {
                Log.Fatal(e);
            }
        });

        Log.Notify("Trace stopped");
    }

    /// <summary>
    ///     Stops tracing without sending anything to the server.
    /// </summary>
    private void Reset()
    {
        _targetName = null;
    }

    /// <summary>
    ///     Called on every tick, checks periodically what the commander does.
    /// </summary>
    private void OnTick()
    {
        if (!Active || Kernel.TickCount - _lastTick < CheckInterval)
            return;

        // The checks send packets and wait for the response, so they must not block the update loop
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;

        _lastTick = Kernel.TickCount;

        Task.Run(() =>
        {
            try
            {
                Follow();
            }
            catch (Exception e)
            {
                Log.Fatal(e);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    /// <summary>
    ///     Walks to the spot the commander clicked or moves closer to the standing commander.
    /// </summary>
    private void Follow()
    {
        var targetName = _targetName;
        if (targetName == null || Game.Player == null || Game.Player.State.LifeState == LifeState.Dead)
            return;

        if (SpawnManager.TryGetEntity<SpawnedPlayer>(p => p.Name == targetName, out var commander))
        {
            var movement = commander.Movement;

            // The commander clicked a spot, walk to the same spot
            if (movement.Moving && movement.HasDestination)
            {
                if (!_hasMirrored || movement.Destination.DistanceTo(_lastMirrored) > MirrorThreshold)
                {
                    _lastMirrored = movement.Destination;
                    _hasMirrored = true;

                    Game.Player.MoveTo(movement.Destination, false);
                }

                return;
            }

            // The commander stands or walks with the keys, get closer
            _hasMirrored = false;

            if (
                !Game.Player.Movement.Moving
                && commander.Position.DistanceTo(Game.Player.Position) > CatchUpDistance
            )
                Game.Player.MoveTo(commander.Position, false);

            return;
        }

        // The commander is out of sight, use the position of the party member instead
        var member = Game.Party.GetMemberByName(targetName);
        if (member == null || Game.Player.Movement.Moving)
            return;

        var distance = member.Position.DistanceTo(Game.Player.Position);
        if (distance > CatchUpDistance && distance <= MaxMoveDistance)
            Game.Player.MoveTo(member.Position, false);
    }
}
