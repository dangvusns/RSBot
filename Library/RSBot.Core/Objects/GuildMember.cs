namespace RSBot.Core.Objects;

/// <summary>
///     A member of the guild of the character.
/// </summary>
public class GuildMember
{
    /// <summary>
    ///     The permission value of the guild master.
    /// </summary>
    public const uint MasterPermissions = uint.MaxValue;

    /// <summary>
    ///     Gets or sets the member id.
    /// </summary>
    public uint Id { get; set; }

    /// <summary>
    ///     Gets or sets the character name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     Gets or sets the level.
    /// </summary>
    public byte Level { get; set; }

    /// <summary>
    ///     Gets or sets the donated guild points.
    /// </summary>
    public uint GatheredPoints { get; set; }

    /// <summary>
    ///     Gets or sets the permission flags (join = 1, kick = 2, union chat = 4, storage = 8, notice = 16).
    /// </summary>
    public uint Permissions { get; set; }

    /// <summary>
    ///     Gets or sets the guild nickname.
    /// </summary>
    public string Nickname { get; set; }

    /// <summary>
    ///     Gets or sets the model id of the character.
    /// </summary>
    public uint ModelId { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the member is online.
    /// </summary>
    public bool IsOnline { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the member is the guild master.
    /// </summary>
    public bool IsMaster => Permissions == MasterPermissions;
}
