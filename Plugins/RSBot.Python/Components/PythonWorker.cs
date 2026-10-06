using System;
using System.Collections.Concurrent;
using System.Threading;
using RSBot.Core;

namespace RSBot.Python.Components;

/// <summary>
///     Runs Python work on one background thread, in order. Game events, UI clicks and the
///     event loop are queued here so that bot threads and the UI thread never wait for Python
///     (and the UI thread never takes the GIL, which avoids deadlocks with UI updates from Python).
/// </summary>
internal static class PythonWorker
{
    private static readonly BlockingCollection<Action> _queue = new();
    private static Thread _thread;
    private static int _eventLoopQueued;

    public static void Start()
    {
        if (_thread != null)
            return;

        _thread = new Thread(Run) { Name = "Python.Worker", IsBackground = true };
        _thread.Start();

        var timer = new Thread(EventLoopTimer) { Name = "Python.EventLoop", IsBackground = true };
        timer.Start();
    }

    /// <summary>
    ///     Queues work for the Python thread.
    /// </summary>
    public static void Post(Action action)
    {
        if (!_queue.IsAddingCompleted)
            _queue.Add(action);
    }

    private static void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Error($"[Python] {e.Message}");
            }
        }
    }

    /// <summary>
    ///     Calls event_loop() every 500 ms. A tick is skipped while the previous one is still queued
    ///     or running, so slow plugins never pile up calls.
    /// </summary>
    private static void EventLoopTimer()
    {
        while (true)
        {
            Thread.Sleep(500);

            if (!PythonPluginManager.HasLoadedPlugins)
                continue;

            if (Interlocked.Exchange(ref _eventLoopQueued, 1) == 1)
                continue;

            Post(() =>
            {
                try
                {
                    PythonPluginManager.CallAll("event_loop");
                }
                finally
                {
                    Interlocked.Exchange(ref _eventLoopQueued, 0);
                }
            });
        }
    }
}
