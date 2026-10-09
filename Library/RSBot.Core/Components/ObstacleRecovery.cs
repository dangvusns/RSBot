namespace RSBot.Core.Components;

/// <summary>Bounds collision recovery; damage and combat duration are deliberately not inputs.</summary>
public sealed class ObstacleRecovery
{
    public enum Step { Waiting, Attempt, Recovered, Exhausted }

    public bool Active { get; private set; }
    public int Attempts { get; private set; }
    private int _started, _lastAttempt, _lastFinished;
    private bool _hasFinished;
    private float _x, _y;

    public bool Start(int tick, float x, float y)
    {
        if (Active || (_hasFinished && Elapsed(tick, _lastFinished) < 15_000))
            return false;
        Active = true;
        Attempts = 0;
        _started = tick;
        _x = x;
        _y = y;
        return true;
    }

    public Step Update(int tick, float x, float y)
    {
        if (!Active)
            return Step.Waiting;
        var dx = x - _x;
        var dy = y - _y;
        if (dx * dx + dy * dy >= 1)
        {
            Finish(tick);
            return Step.Recovered;
        }
        if (Elapsed(tick, _started) >= 6_000)
        {
            Finish(tick);
            return Step.Exhausted;
        }
        if (Attempts >= 3 || (Attempts > 0 && Elapsed(tick, _lastAttempt) < 2_000))
            return Step.Waiting;
        Attempts++;
        _lastAttempt = tick;
        return Step.Attempt;
    }

    public void Finish(int tick)
    {
        Active = false;
        _lastFinished = tick;
        _hasFinished = true;
    }

    private static int Elapsed(int tick, int previous) => (tick - previous) & int.MaxValue;
}
