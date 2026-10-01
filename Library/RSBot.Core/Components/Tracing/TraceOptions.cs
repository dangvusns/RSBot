using System;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     The tuning values of a trace. Distances are world units, times are milliseconds.
/// </summary>
public sealed class TraceOptions
{
    /// <summary>
    ///     The distance to the target the character walks to.
    /// </summary>
    public double FollowDistance { get; set; } = 10;

    /// <summary>Copies movement destinations without native trace. Used only by Party and Social.</summary>
    public bool DestinationFollow { get; set; }

    /// <summary>Creates the commander follow settings, preserving logging and timing configuration.</summary>
    public static TraceOptions Commander(string keyPrefix)
    {
        var options = Overlap().ApplyConfig(keyPrefix);
        options.DestinationFollow = true;
        options.FollowDistance = 0;
        options.StartMoveDistance = 0.5;
        options.StopMoveDistance = 0;
        options.ArrivalTolerance = 0.5;
        options.AimAtDestination = true;
        options.PredictionEnabled = false;
        options.DestinationChangeThreshold = 0.5;
        options.TargetMovementThreshold = 0.5;
        options.MinMoveInterval = Math.Max(100, options.MinMoveInterval);
        options.IdleRetryInterval = Math.Max(500, options.IdleRetryInterval);
        return options;
    }

    /// <summary>
    ///     Above this distance the character starts to follow.
    /// </summary>
    public double StartMoveDistance { get; set; } = 12;

    /// <summary>
    ///     Below this distance the character is in range and stops following.
    /// </summary>
    public double StopMoveDistance { get; set; } = 8;

    /// <summary>
    ///     How far beyond the follow distance a standing character still counts as arrived. Positions of the game are
    ///     not exact, so without it the character would never arrive and keep clicking its own position. It is also the
    ///     shortest move the trace issues.
    /// </summary>
    public double ArrivalTolerance { get; set; } = 1;

    /// <summary>
    ///     The time between two evaluations of a self driven trace.
    /// </summary>
    public int TraceUpdateInterval { get; set; } = 200;

    /// <summary>
    ///     The minimum time between two move commands.
    /// </summary>
    public int MinMoveInterval { get; set; } = 250;

    /// <summary>
    ///     The time after the last move command a standing character walks again while it is out of range.
    /// </summary>
    public int IdleRetryInterval { get; set; } = 400;

    /// <summary>
    ///     Gets or sets a value indicating whether the target position is predicted.
    /// </summary>
    public bool PredictionEnabled { get; set; }

    /// <summary>
    ///     The look-ahead time of the prediction.
    /// </summary>
    public int PredictionTime { get; set; } = 200;

    /// <summary>
    ///     The upper limit of the look-ahead time.
    /// </summary>
    public int MaxPredictionTime { get; set; } = 500;

    /// <summary>
    ///     The distance the target has to move to issue a new command.
    /// </summary>
    public double TargetMovementThreshold { get; set; } = 1.5;

    /// <summary>
    ///     The distance the follow point has to change to issue a new command.
    /// </summary>
    public double DestinationChangeThreshold { get; set; } = 2.5;

    /// <summary>
    ///     The time a lost target is reported as lost before the trace only waits for it.
    /// </summary>
    public int TargetLostTimeout { get; set; } = 5000;

    /// <summary>
    ///     The time after which the movement of a key walking target is not trusted anymore.
    /// </summary>
    public int MovementStaleTimeout { get; set; } = 1500;

    /// <summary>
    ///     A position change bigger than this within one tick is treated as teleport.
    /// </summary>
    public double TeleportDetectionDistance { get; set; } = 25;

    /// <summary>
    ///     The longest distance of a single move command (the game refuses more than 150).
    /// </summary>
    public double MaxMoveDistance { get; set; } = 140;

    /// <summary>
    ///     Gets or sets a value indicating whether the character heads for the clicked destination of the target
    ///     (the end of its trajectory) instead of its current position.
    /// </summary>
    public bool AimAtDestination { get; set; } = true;

    /// <summary>
    ///     The minimum time between two native trace requests when the backend switches quickly.
    /// </summary>
    public int MinGameTraceInterval { get; set; } = 500;

    /// <summary>
    ///     A character that stopped farther than this from its destination got stuck (for example blocked terrain).
    /// </summary>
    public double StuckDistance { get; set; } = 3;

    /// <summary>
    ///     The minimum time between two native trace requests.
    /// </summary>
    public int GameTraceResendInterval { get; set; } = 1500;

    /// <summary>
    ///     Gets or sets a value indicating whether debug messages are written.
    /// </summary>
    public bool Debug { get; set; }

    /// <summary>
    ///     Follows at a larger distance, as used for the party master.
    /// </summary>
    public static TraceOptions PartyMaster()
    {
        return new TraceOptions();
    }

    /// <summary>
    ///     Walks to exactly the spot the target clicked, so the character stands on the target. Used for the bot side
    ///     commander trace (traceme).
    /// </summary>
    public static TraceOptions Overlap()
    {
        return new TraceOptions
        {
            FollowDistance = 0,
            StartMoveDistance = 1,
            StopMoveDistance = 0,
            ArrivalTolerance = 0.5,
        };
    }

    /// <summary>
    ///     Follows very close to the target, as used for the native commander trace.
    /// </summary>
    public static TraceOptions Close()
    {
        return new TraceOptions
        {
            FollowDistance = 2.5,
            StartMoveDistance = 4,
            StopMoveDistance = 1.5,
        };
    }

    /// <summary>
    ///     Overrides the values with the ones stored in the player config. Only existing keys are read.
    /// </summary>
    /// <param name="keyPrefix">The prefix of the keys, for example "RSBot.Party.Trace.".</param>
    public TraceOptions ApplyConfig(string keyPrefix)
    {
        FollowDistance = Read(keyPrefix + "FollowDistance", FollowDistance);
        StartMoveDistance = Read(keyPrefix + "StartMoveDistance", StartMoveDistance);
        StopMoveDistance = Read(keyPrefix + "StopMoveDistance", StopMoveDistance);
        ArrivalTolerance = Read(keyPrefix + "ArrivalTolerance", ArrivalTolerance);
        TraceUpdateInterval = Read(keyPrefix + "TraceUpdateInterval", TraceUpdateInterval);
        MinMoveInterval = Read(keyPrefix + "MinMoveInterval", MinMoveInterval);
        IdleRetryInterval = Read(keyPrefix + "IdleRetryInterval", IdleRetryInterval);
        PredictionEnabled = Read(keyPrefix + "PredictionEnabled", PredictionEnabled);
        PredictionTime = Read(keyPrefix + "PredictionTime", PredictionTime);
        MaxPredictionTime = Read(keyPrefix + "MaxPredictionTime", MaxPredictionTime);
        TargetMovementThreshold = Read(keyPrefix + "TargetMovementThreshold", TargetMovementThreshold);
        DestinationChangeThreshold = Read(keyPrefix + "DestinationChangeThreshold", DestinationChangeThreshold);
        TargetLostTimeout = Read(keyPrefix + "TargetLostTimeout", TargetLostTimeout);
        MovementStaleTimeout = Read(keyPrefix + "MovementStaleTimeout", MovementStaleTimeout);
        TeleportDetectionDistance = Read(keyPrefix + "TeleportDetectionDistance", TeleportDetectionDistance);
        MaxMoveDistance = Read(keyPrefix + "MaxMoveDistance", MaxMoveDistance);
        AimAtDestination = Read(keyPrefix + "AimAtDestination", AimAtDestination);
        MinGameTraceInterval = Read(keyPrefix + "MinGameTraceInterval", MinGameTraceInterval);
        StuckDistance = Read(keyPrefix + "StuckDistance", StuckDistance);
        GameTraceResendInterval = Read(keyPrefix + "GameTraceResendInterval", GameTraceResendInterval);
        Debug = Read(keyPrefix + "Debug", Debug);

        Normalize();

        return this;
    }

    /// <summary>
    ///     Makes sure the distances are in a usable order (stop &lt;= follow &lt;= start).
    /// </summary>
    public void Normalize()
    {
        if (StartMoveDistance < FollowDistance)
            StartMoveDistance = FollowDistance;

        if (StopMoveDistance > FollowDistance)
            StopMoveDistance = FollowDistance;

        if (MaxMoveDistance > 148)
            MaxMoveDistance = 148;
    }

    private static T Read<T>(string key, T fallback)
    {
        try
        {
            return PlayerConfig.Exists(key) ? PlayerConfig.Get(key, fallback) : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
