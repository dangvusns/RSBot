using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using RSBot.Core;
using RSBot.Core.Components;

namespace RSBot.ManagerLink.Components;

/// <summary>
///     Counts how long each character was in game since the daily reset, for the "Giờ Xanh" column
///     on servers that do not send their fatigue time. Saved in User/&lt;profile&gt;/online-time.json
///     so restarting the bot or the manager keeps the time.
/// </summary>
internal static class OnlineTimeTracker
{
    private static readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     A longer gap between two ticks (sleep, freeze) is not counted.
    /// </summary>
    private static readonly TimeSpan _maxCountedGap = TimeSpan.FromSeconds(30);

    private static readonly object _lock = new();

    private static Dictionary<string, Entry> _entries;
    private static Timer _timer;
    private static DateTime? _lastTick;
    private static string _lastCharacter;

    /// <summary>
    ///     The daily reset time, sent by the manager with every status request.
    /// </summary>
    private static TimeSpan _resetTime = TimeSpan.Zero;

    private static string FilePath =>
        Path.Combine(Kernel.BasePath, "User", ProfileManager.SelectedProfile, "online-time.json");

    public static void Initialize()
    {
        _timer ??= new Timer(_ => Tick(), null, _tickInterval, _tickInterval);
    }

    public static void SetResetTime(TimeSpan resetTime)
    {
        lock (_lock)
            _resetTime = resetTime;
    }

    /// <summary>
    ///     Seconds the current character was in game since the last daily reset, or null when not in game.
    /// </summary>
    public static long? GetPlayedSeconds()
    {
        var name = Game.Ready ? Game.Player?.Name : null;
        if (string.IsNullOrEmpty(name))
            return null;

        lock (_lock)
        {
            var entry = GetEntry(name, DateTime.Now);

            return (long)entry.Seconds;
        }
    }

    private static void Tick()
    {
        try
        {
            var now = DateTime.Now;
            var name = Game.Ready ? Game.Player?.Name : null;

            lock (_lock)
            {
                if (string.IsNullOrEmpty(name))
                {
                    _lastTick = null;
                    return;
                }

                var entry = GetEntry(name, now);

                if (_lastTick.HasValue && _lastCharacter == name)
                {
                    var elapsed = now - _lastTick.Value;
                    if (elapsed > TimeSpan.Zero && elapsed <= _maxCountedGap)
                        entry.Seconds += elapsed.TotalSeconds;
                }

                _lastTick = now;
                _lastCharacter = name;

                Save();
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"[Manager link] Could not update the online time: {ex.Message}");
        }
    }

    /// <summary>
    ///     The entry of the character for the current day, starting again from 0 after the daily reset.
    /// </summary>
    private static Entry GetEntry(string name, DateTime now)
    {
        _entries ??= Load();

        var periodStart = now.Date + _resetTime;
        if (now < periodStart)
            periodStart = periodStart.AddDays(-1);

        if (!_entries.TryGetValue(name, out var entry) || entry.PeriodStart != periodStart)
        {
            entry = new Entry { PeriodStart = periodStart, Seconds = 0 };
            _entries[name] = entry;
        }

        return entry;
    }

    private static Dictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath))
                    ?? new Dictionary<string, Entry>();
        }
        catch (Exception ex)
        {
            Log.Debug($"[Manager link] Could not read {FilePath}: {ex.Message}");
        }

        return new Dictionary<string, Entry>();
    }

    private static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_entries));
    }

    private sealed class Entry
    {
        public DateTime PeriodStart { get; set; }

        public double Seconds { get; set; }
    }
}
