using RSBot.Core.Objects;

namespace RSBot.Core.Plugins;

public interface IBotbase : IExtension
{
    /// <summary>
    ///     Gets the area.
    /// </summary>
    /// <value>
    ///     The area.
    /// </value>
    public Area Area { get; }

    /// <summary>
    ///     Ticks this instance.
    /// </summary>
    void Tick();

    /// <summary>
    ///     Checks whether the botbase can start, before anything is changed for the start (e.g. party following stopped).
    /// </summary>
    /// <returns><c>true</c> if the botbase can start; otherwise, <c>false</c>.</returns>
    bool CanStart() => true;

    /// <summary>
    ///     Starts this instance.
    /// </summary>
    void Start();

    /// <summary>
    ///     Stops this instance.
    /// </summary>
    void Stop();
}
