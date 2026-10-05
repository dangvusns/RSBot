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
    ///     The request was sent without waiting for the server's answer.
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
    Timeout
}
