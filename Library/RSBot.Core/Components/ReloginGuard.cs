using System;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core.Event;

namespace RSBot.Core.Components;

/// <summary>
///     Coordinates automatic relogins so a burst of disconnect events starts only one relogin,
///     and repeated failures back off instead of retrying at a fixed rate forever.
/// </summary>
public static class ReloginGuard
{
    private const int MaxDelayMs = 10 * 60 * 1000;

    private static int _sequence;
    private static int _attempts;

    /// <summary>
    ///     Subscribes the events.
    /// </summary>
    internal static void Initialize()
    {
        // A new connection supersedes any relogin that is still waiting.
        EventManager.SubscribeEvent("OnAgentServerConnected", Cancel);

        // Being in game again is the only real success; reset the backoff then.
        EventManager.SubscribeEvent("OnLoadCharacter", () => Interlocked.Exchange(ref _attempts, 0));
    }

    /// <summary>
    ///     Cancels any pending relogin.
    /// </summary>
    public static void Cancel()
    {
        Interlocked.Increment(ref _sequence);
    }

    /// <summary>
    ///     Starts a relogin attempt and waits for its backoff delay.
    /// </summary>
    /// <returns><c>true</c> if the caller should relogin now; <c>false</c> if a newer event superseded it or the attempt limit was reached.</returns>
    public static async Task<bool> WaitForAttemptAsync()
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var attempt = Interlocked.Increment(ref _attempts);

        var maxAttempts = GlobalConfig.Get("RSBot.General.MaxReloginAttempts", 0);
        if (maxAttempts > 0 && attempt > maxAttempts)
        {
            Log.Warn($"Relogin gave up after {maxAttempts} attempts.");
            return false;
        }

        var baseDelay = 10000;
        if (GlobalConfig.Get("RSBot.General.EnableWaitAfterDC", false))
            baseDelay = GlobalConfig.Get<int>("RSBot.General.WaitAfterDC") * 60 * 1000;

        // 10 s, 20 s, 40 s ... capped at 10 minutes (or the configured wait, if that is longer).
        baseDelay = Math.Max(baseDelay, 1000);
        var factor = 1L << Math.Min(attempt - 1, 16);
        var delay = (int)Math.Min(baseDelay * factor, Math.Max(MaxDelayMs, baseDelay));

        Log.Warn($"Attempting relogin in {delay / 1000} seconds (attempt {attempt})...");
        await Task.Delay(delay).ConfigureAwait(false);

        return Volatile.Read(ref _sequence) == sequence;
    }
}
