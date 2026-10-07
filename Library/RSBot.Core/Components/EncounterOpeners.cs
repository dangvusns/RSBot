using System;
using System.Collections.Generic;

namespace RSBot.Core.Components;

/// <summary>Bounded history of confirmed openers, retained across temporary target changes.</summary>
public sealed class EncounterOpeners
{
    private sealed class Encounter
    {
        public readonly HashSet<uint> Accepted = new();
        public long LastSeen;
    }

    private readonly object _lock = new();
    private readonly Dictionary<uint, Encounter> _encounters = new();
    private readonly Func<long> _clock;
    private readonly int _capacity;
    private readonly long _lifetime;

    public EncounterOpeners(Func<long> clock = null, int capacity = 128, long lifetimeMilliseconds = 1_800_000)
    {
        if (capacity < 1 || lifetimeMilliseconds < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _clock = clock ?? (() => Environment.TickCount64);
        _capacity = capacity;
        _lifetime = lifetimeMilliseconds;
    }

    public bool WasAccepted(uint targetId, uint skillId)
    {
        lock (_lock)
        {
            Prune(_clock());
            if (!_encounters.TryGetValue(targetId, out var encounter))
                return false;
            encounter.LastSeen = _clock();
            return encounter.Accepted.Contains(skillId);
        }
    }

    public void Confirm(uint targetId, uint skillId)
    {
        if (targetId == 0 || skillId == 0)
            return;
        lock (_lock)
        {
            var now = _clock();
            Prune(now);
            if (!_encounters.TryGetValue(targetId, out var encounter))
            {
                if (_encounters.Count >= _capacity)
                {
                    uint oldestId = 0;
                    var oldest = long.MaxValue;
                    foreach (var entry in _encounters)
                        if (entry.Value.LastSeen < oldest)
                        {
                            oldest = entry.Value.LastSeen;
                            oldestId = entry.Key;
                        }
                    _encounters.Remove(oldestId);
                }
                _encounters[targetId] = encounter = new Encounter();
            }
            encounter.Accepted.Add(skillId);
            encounter.LastSeen = now;
        }
    }

    public void Forget(uint targetId)
    {
        lock (_lock) _encounters.Remove(targetId);
    }

    public void Clear()
    {
        lock (_lock) _encounters.Clear();
    }

    private void Prune(long now)
    {
        // ponytail: scan at most 128 encounters; index expirations if profiling shows a cost.
        List<uint> expired = null;
        foreach (var entry in _encounters)
            if (now - entry.Value.LastSeen >= _lifetime)
                (expired ??= new List<uint>()).Add(entry.Key);
        if (expired != null)
            foreach (var id in expired) _encounters.Remove(id);
    }
}
