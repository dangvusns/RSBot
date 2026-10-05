using System;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core.Components;
using RSBot.Core.Components.Tracing;
using RSBot.Core.Event;
using RSBot.Core.Plugins;

namespace RSBot.Core;

public class Bot
{
    /// <summary>
    ///     Gets or sets a value indicating whether this <see cref="Bot" /> is running.
    /// </summary>
    /// <value>
    ///     <c>true</c> if running; otherwise, <c>false</c>.
    /// </value>
    public volatile bool Running;

    /// <summary>
    ///     Gets or sets to the <see cref="CancellationToken" />
    /// </summary>
    public CancellationTokenSource TokenSource;

    private readonly object _lock = new();

    /// <summary>
    ///     The task running the botbase ticks.
    /// </summary>
    private Task _workerTask;

    /// <summary>
    ///     Gets the base.
    /// </summary>
    /// <value>
    ///     The base.
    /// </value>
    public IBotbase Botbase { get; private set; }

    /// <summary>
    ///     Sets the botbase.
    /// </summary>
    /// <param name="botBase">The bot base.</param>
    public void SetBotbase(IBotbase botBase)
    {
        Botbase = botBase;
        Botbase.Initialize();

        EventManager.FireEvent("OnSetBotbase", botBase);
    }

    /// <summary>
    ///     Starts this instance.
    /// </summary>
    public void Start()
    {
        // A start that is refused must leave the player as it is, e.g. still following the party master
        if (Botbase != null && !Botbase.CanStart())
            return;

        // Stop self-driven Party/Social following before bot ticks can issue movement or attacks.
        // This also applies to the Start button and repeated start commands while already running.
        TraceManager.Stop();

        lock (_lock)
        {
            if (Running || Botbase == null || (_workerTask != null && !_workerTask.IsCompleted))
                return;

            var tokenSource = new CancellationTokenSource();
            TokenSource = tokenSource;

            // Set before the worker starts, so a second start request can not launch another worker
            Running = true;
            _workerTask = Task.Run(() => RunAsync(tokenSource));
        }
    }

    /// <summary>
    ///     Runs the botbase ticks until the given token source is cancelled.
    /// </summary>
    /// <param name="tokenSource">The token source of this run.</param>
    private async Task RunAsync(CancellationTokenSource tokenSource)
    {
        var token = tokenSource.Token;

        try
        {
            EventManager.FireEvent("OnStartBot");
            Botbase.Start();

            while (!token.IsCancellationRequested)
            {
                if (Game.Ready)
                    Botbase.Tick();

                // Always wait, otherwise the loop spins a CPU core while the game is not ready
                await Task.Delay(100, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Fatal(ex);
        }
        finally
        {
            if (ReferenceEquals(TokenSource, tokenSource))
                Running = false;
        }
    }

    /// <summary>
    ///     Stops this instance.
    /// </summary>
    public void Stop()
    {
        ScriptManager.Stop();
        ShoppingManager.Stop();
        PickupManager.Stop();

        CancellationTokenSource tokenSource;

        lock (_lock)
        {
            if (Botbase == null || !Running)
                return;

            Running = false;
            tokenSource = TokenSource;
        }

        if (tokenSource != null && !tokenSource.IsCancellationRequested)
            tokenSource.Cancel();

        CancelActionOnStop();

        EventManager.FireEvent("OnStopBot");
        Log.Notify($"Stopping bot {Botbase.Title}");

        Game.SelectedEntity = null;
        Botbase.Stop();

        Log.Notify($"Stoped bot {Botbase.Title}");
        Log.Status("Bot stopped");
    }

    /// <summary>
    ///     Cancels the current action of the player in the background, so stopping the bot does not
    ///     block while waiting for the server to confirm the cancellation.
    /// </summary>
    private void CancelActionOnStop()
    {
        var player = Game.Player;
        if (player == null)
            return;

        // Not gated on InAction: the worker tick may still send an attack after Stop was requested,
        // so the attack can start after this point and must be cancelled then.
        var workerTask = _workerTask;
        _ = CancelActionOnStopAsync();

        async Task CancelActionOnStopAsync()
        {
            const int watchDuration = 3000;
            const int pollDelay = 500;

            // Let the running tick finish so it can not start an attack after we stop watching.
            if (workerTask != null)
                await Task.WhenAny(workerTask, Task.Delay(watchDuration)).ConfigureAwait(false);

            for (var elapsed = 0; elapsed <= watchDuration; elapsed += pollDelay)
            {
                if (Running || !Game.Ready || !ReferenceEquals(Game.Player, player))
                    return;

                if (player.InAction)
                    SkillManager.CancelAction(0);

                await Task.Delay(pollDelay).ConfigureAwait(false);
            }
        }
    }
}
