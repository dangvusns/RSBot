using System;
using System.Collections.Generic;
using System.Globalization;

namespace RSBot.Manager.Services;

/// <summary>
///     Works out the "Giờ Xanh" text from the daily server event windows ("HH:mm-HH:mm", may pass midnight).
/// </summary>
public static class BlueHours
{
    public static bool TryParseWindow(string text, out TimeSpan start, out TimeSpan end)
    {
        start = end = TimeSpan.Zero;

        var parts = (text ?? string.Empty).Split('-', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && TimeSpan.TryParseExact(parts[0], @"hh\:mm", CultureInfo.InvariantCulture, out start)
            && TimeSpan.TryParseExact(parts[1], @"hh\:mm", CultureInfo.InvariantCulture, out end)
            && start != end;
    }

    /// <summary>
    ///     "Đang diễn ra (còn mm:ss)" inside a window, "Còn hh:mm" before the next one, "—" without windows.
    /// </summary>
    public static string Describe(IEnumerable<string> windows, DateTime now)
    {
        TimeSpan? untilNext = null;

        foreach (var window in windows ?? Array.Empty<string>())
        {
            if (!TryParseWindow(window, out var start, out var end))
                continue;

            // Look at the window of yesterday too, for windows that pass midnight
            for (var day = -1; day <= 1; day++)
            {
                var from = now.Date.AddDays(day) + start;
                var to = now.Date.AddDays(day) + end;
                if (to <= from)
                    to = to.AddDays(1);

                if (now >= from && now < to)
                    return $"Đang diễn ra (còn {Format(to - now)})";

                if (from > now && (untilNext == null || from - now < untilNext))
                    untilNext = from - now;
            }
        }

        return untilNext.HasValue ? $"Còn {Format(untilNext.Value)}" : "—";
    }

    private static string Format(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}h"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }
}
