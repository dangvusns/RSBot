using RSBot.Core.Objects.Spawn;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     The persistent logical player a trace follows. It is identified by the name; the visible entity (and its unique
///     id) is looked up again whenever the player spawns, so the trace survives despawns, teleports and relogs.
/// </summary>
public sealed class TraceTarget
{
    private readonly object _lock = new();
    private SpawnedPlayer _entity;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TraceTarget" /> class.
    /// </summary>
    /// <param name="name">The name of the player.</param>
    public TraceTarget(string name)
    {
        Name = name;
    }

    /// <summary>
    ///     Gets the name of the player.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the unique id the player had when it was seen the last time. Only a hint, it can change.
    /// </summary>
    public uint UniqueId { get; private set; }

    /// <summary>
    ///     Gets the currently visible entity of the player, <c>null</c> if the player is not visible.
    /// </summary>
    public SpawnedPlayer Entity
    {
        get
        {
            lock (_lock)
            {
                return _entity;
            }
        }
    }

    /// <summary>
    ///     Looks the player up in the spawned entities.
    /// </summary>
    /// <returns><c>true</c> if the player is visible; otherwise <c>false</c>.</returns>
    public bool Resolve()
    {
        if (!SpawnManager.TryGetEntity<SpawnedPlayer>(p => p.Name == Name, out var player))
            return false;

        Set(player);

        return true;
    }

    /// <summary>
    ///     Called when a player spawned.
    /// </summary>
    /// <param name="player">The player.</param>
    public void OnSpawn(SpawnedPlayer player)
    {
        if (player != null && player.Name == Name)
            Set(player);
    }

    /// <summary>
    ///     Called when an entity despawned.
    /// </summary>
    /// <param name="entity">The entity, can be <c>null</c>.</param>
    public void OnDespawn(SpawnedEntity entity)
    {
        lock (_lock)
        {
            if (_entity != null && entity != null && entity.UniqueId == _entity.UniqueId)
                _entity = null;
        }
    }

    /// <summary>
    ///     Forgets the visible entity, for example after all entities were cleared.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _entity = null;
        }
    }

    private void Set(SpawnedPlayer player)
    {
        lock (_lock)
        {
            _entity = player;
            UniqueId = player.UniqueId;
        }
    }
}
