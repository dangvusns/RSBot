namespace RSBot.Core.Components.Tracing;

/// <summary>
///     Tick arithmetic for <see cref="Kernel.TickCount" />, which wraps around at <see cref="int.MaxValue" />.
/// </summary>
public static class TraceTime
{
    /// <summary>
    ///     Gets the elapsed milliseconds between two tick counts.
    /// </summary>
    /// <param name="now">The current tick count.</param>
    /// <param name="then">The earlier tick count.</param>
    public static int Elapsed(int now, int then)
    {
        return (now - then) & int.MaxValue;
    }
}
