namespace RSBot.Core.Objects.Skill;

/// <summary>
///     The outcome of a buff cast request.
/// </summary>
public enum SkillCastResult
{
    /// <summary>
    ///     The request was not sent, e.g. the required weapon could not be equipped.
    /// </summary>
    NotSent,

    /// <summary>
    ///     Identified acceptance was not established: no wait, or an anonymous failure response.
    /// </summary>
    Unconfirmed,

    /// <summary>
    ///     The server started the cast.
    /// </summary>
    Accepted,

    /// <summary>
    ///     The server refused the cast, e.g. not enough MP.
    /// </summary>
    Refused,

    /// <summary>
    ///     The server did not answer in time.
    /// </summary>
    Timeout,

    /// <summary>Stop, a session transition or a competing manual action cancelled the wait.</summary>
    Cancelled,

    /// <summary>Another local cast owns equipment and the pending cast callbacks.</summary>
    Busy
}
