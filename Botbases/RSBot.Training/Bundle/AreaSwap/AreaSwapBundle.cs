using System;
using System.Collections.Generic;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;
using RSBot.Training.Bot;

namespace RSBot.Training.Bundle.AreaSwap;

/// <summary>
///     Moves to the next saved training area when the current one has no monsters to attack for a while.
///     Back in town the primary training area is used again, so the walkback works as usual.
/// </summary>
internal class AreaSwapBundle : IBundle
{
    /// <summary>
    ///     Areas farther away than this are walked to with an auto path instead of the movement bundle.
    /// </summary>
    private const int WALK_DIRECTLY_DISTANCE = 80;

    #region Fields

    /// <summary>
    ///     The last tick count the player had a monster to attack.
    /// </summary>
    private int _lastTargetTick;

    /// <summary>
    ///     The index of the saved area the player trains at, or -1 while it's the primary area.
    /// </summary>
    private int _currentIndex = -1;

    /// <summary>
    ///     The area that was set before the first swap.
    /// </summary>
    private Area? _primaryArea;

    #endregion Fields

    #region Methods

    /// <summary>
    ///     Invokes this instance.
    /// </summary>
    public void Invoke()
    {
        if (!PlayerConfig.Get("RSBot.Training.checkSwapArea", false))
            return;

        if (
            Game.SelectedEntity != null
            && SpawnManager.TryGetEntity<SpawnedMonster>(Game.SelectedEntity.UniqueId, out var target)
            && target.State.LifeState == LifeState.Alive
        )
        {
            _lastTargetTick = Kernel.TickCount;
            return;
        }

        if (_lastTargetTick == 0)
            _lastTargetTick = Kernel.TickCount;

        var timeoutSeconds = Math.Max(10, PlayerConfig.Get("RSBot.Training.numSwapAreaSeconds", 60));
        if (Kernel.TickCount - _lastTargetTick < timeoutSeconds * 1000)
            return;

        // Following the party master decides where the player trains
        if (PlayerConfig.Get("RSBot.Party.AlwaysFollowPartyMaster", false) && Game.Party.IsInParty && !Game.Party.IsLeader)
            return;

        _lastTargetTick = Kernel.TickCount;

        SwapToNextArea(timeoutSeconds);
    }

    /// <summary>
    ///     Uses the primary training area again, if the player swapped to another one.
    /// </summary>
    public void RestorePrimaryArea()
    {
        if (_primaryArea == null)
            return;

        Log.Notify($"[Area swap] Back to the primary training area [{_primaryArea.Value.Name}]");

        SetArea(_primaryArea.Value);

        _primaryArea = null;
        _currentIndex = -1;
    }

    /// <summary>
    ///     Refreshes this instance.
    /// </summary>
    public void Refresh()
    {
        //Nothing to do, the settings are read when needed
    }

    public void Stop()
    {
        _lastTargetTick = 0;

        RestorePrimaryArea();
    }

    private void SwapToNextArea(int timeoutSeconds)
    {
        var areas = GetSavedAreas();
        if (areas.Count < 2)
            return;

        var current = Container.Bot.Area;

        // Continue after the area the player trains at; the primary area may also be one of the saved ones
        var index = _currentIndex;
        if (index < 0)
            index = areas.FindIndex(a => a.Position.DistanceTo(current.Position) < 1);

        var nextIndex = (index + 1) % areas.Count;
        if (areas[nextIndex].Position.DistanceTo(current.Position) < 1)
            nextIndex = (nextIndex + 1) % areas.Count;

        var next = areas[nextIndex];
        var distance = Game.Player.Position.DistanceTo(next.Position);
        Log.Notify(
            $"[Area swap] No monsters for {timeoutSeconds}s, moving to training area [{next.Name}] ({Math.Round(distance)}m)"
        );

        _primaryArea ??= current;
        _currentIndex = nextIndex;
        SetArea(next);

        if (distance <= WALK_DIRECTLY_DISTANCE)
            return;

        // The walkscript leads to the primary area, walk to the new one with an auto path instead
        var walkScript = NavigationManager.TryBuildWalkScript(Game.Player.Movement.Source, next.Position);
        if (walkScript == null)
        {
            Log.Warn("[Area swap] Cannot find a path to the next training area, staying here.");
            RestorePrimaryArea();
            return;
        }

        ScriptManager.Load(walkScript);
        ScriptManager.RunScript(false);
    }

    /// <summary>
    ///     Gets the training areas saved in the training areas dialog.
    /// </summary>
    private static List<Area> GetSavedAreas()
    {
        var result = new List<Area>();

        foreach (var line in PlayerConfig.GetArray<string>("RSBot.Training.Areas"))
        {
            var split = line.Split("|", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (split.Length > 0 && Area.TryParse(split, out var area))
                result.Add(area);
        }

        return result;
    }

    private static void SetArea(Area area)
    {
        PlayerConfig.Set("RSBot.Area.Region", area.Position.Region);
        PlayerConfig.Set("RSBot.Area.X", area.Position.XOffset);
        PlayerConfig.Set("RSBot.Area.Y", area.Position.YOffset);
        PlayerConfig.Set("RSBot.Area.Z", area.Position.ZOffset);
        PlayerConfig.Set("RSBot.Area.Radius", area.Radius);

        EventManager.FireEvent("OnSetTrainingArea");
    }

    #endregion Methods
}
