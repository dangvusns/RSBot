using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using RSBot.Core;
using RSBot.Core.Network;

namespace RSBot.PacketAnalyzer.Components;

/// <summary>
///     Receives captures from <see cref="PacketMonitor" /> and hands them to the recorder and the live view.
///     It only subscribes while live capture or recording is on, so the bot pays nothing otherwise.
/// </summary>
internal static class PacketHub
{
    private const int MaxQueued = 20_000;

    private static readonly ConcurrentQueue<PacketCapture> _viewQueue = new();
    private static readonly object _lock = new();

    private static int _queued;
    private static volatile bool _liveCapture;
    private static volatile bool _viewActive;
    private static bool _subscribed;

    /// <summary>
    ///     Gets or sets a value indicating whether the live view is shown. Live capture only runs while it is,
    ///     a hidden view would only keep thousands of copied packets in memory.
    /// </summary>
    public static bool ViewActive
    {
        get => _viewActive;
        set
        {
            if (_viewActive == value)
                return;

            _viewActive = value;

            if (!value)
            {
                _viewQueue.Clear();
                Interlocked.Exchange(ref _queued, 0);
            }

            if (Recorder != null)
                UpdateSubscription();
        }
    }

    public static PacketFilter ViewFilter { get; } = new("View");
    public static PacketFilter RecordFilter { get; } = new("Record");
    public static PacketRecorder Recorder { get; private set; }

    public static bool LiveCapture
    {
        get => _liveCapture;
        set
        {
            _liveCapture = value;
            GlobalConfig.Set("RSBot.PacketAnalyzer.LiveCapture", value);

            if (!value)
            {
                _viewQueue.Clear();
                Interlocked.Exchange(ref _queued, 0);
            }

            UpdateSubscription();
        }
    }

    public static void Initialize()
    {
        if (Recorder != null)
            return;

        ViewFilter.Load(true);
        RecordFilter.Load(false);

        _liveCapture = GlobalConfig.Get("RSBot.PacketAnalyzer.LiveCapture", true);

        Recorder = new PacketRecorder(RecordFilter);
        Recorder.StateChanged += UpdateSubscription;
        Recorder.Initialize();

        UpdateSubscription();
    }

    /// <summary>
    ///     Moves up to <paramref name="max" /> queued captures into <paramref name="target" />.
    /// </summary>
    public static void Drain(List<PacketCapture> target, int max)
    {
        while (max-- > 0 && _viewQueue.TryDequeue(out var capture))
        {
            Interlocked.Decrement(ref _queued);
            target.Add(capture);
        }
    }

    private static void UpdateSubscription()
    {
        lock (_lock)
        {
            var required = (_liveCapture && _viewActive) || Recorder.IsRecording;

            if (required && !_subscribed)
                PacketMonitor.Captured += OnCaptured;
            else if (!required && _subscribed)
                PacketMonitor.Captured -= OnCaptured;

            _subscribed = required;
        }
    }

    /// <summary>
    ///     Runs on the network thread.
    /// </summary>
    private static void OnCaptured(PacketCapture capture)
    {
        Recorder.OnCapture(capture);

        // Filtered here instead of in the view, so noisy opcodes do not fill the queue.
        if (!_liveCapture || !_viewActive || !ViewFilter.Matches(capture))
            return;

        _viewQueue.Enqueue(capture);

        // The view could not keep up (e.g. the tab is hidden), forget the oldest captures.
        if (Interlocked.Increment(ref _queued) > MaxQueued && _viewQueue.TryDequeue(out _))
            Interlocked.Decrement(ref _queued);
    }
}
