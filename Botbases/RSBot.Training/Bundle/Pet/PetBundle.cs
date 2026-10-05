using System.Collections.Generic;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Cos;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Training.Bundle.Pet;

internal class PetBundle : IBundle
{
    /// <summary>
    ///     The pet is ordered again after this long, in case it lost the target.
    /// </summary>
    private const int REORDER_INTERVAL = 3_000;

    #region Fields

    /// <summary>
    ///     The last order per pet: pet unique id, (target unique id, tick).
    /// </summary>
    private readonly Dictionary<uint, (uint TargetId, int Tick)> _lastOrders = new(2);

    #endregion Fields

    #region Methods

    /// <summary>
    ///     Invokes this instance.
    /// </summary>
    public void Invoke()
    {
        if (!PlayerConfig.Get("RSBot.Training.checkBoxPetAttackTarget", false))
            return;

        if (
            Game.SelectedEntity == null
            || !SpawnManager.TryGetEntity<SpawnedMonster>(Game.SelectedEntity?.UniqueId ?? 0, out var target)
            || target.State.LifeState != LifeState.Alive
        )
            return;

        OrderAttack(Game.Player.Growth, target.UniqueId);
        OrderAttack(Game.Player.Fellow, target.UniqueId);
    }

    /// <summary>
    ///     Orders the pet to attack the target, if it isn't already attacking it.
    /// </summary>
    private void OrderAttack(Cos pet, uint targetId)
    {
        if (pet == null || !pet.HasHealth)
            return;

        var bionic = pet.Bionic;
        if (bionic != null && bionic.TargetId == targetId && Kernel.TickCount - bionic.TargetTick < REORDER_INTERVAL)
            return;

        if (
            _lastOrders.TryGetValue(pet.UniqueId, out var lastOrder)
            && lastOrder.TargetId == targetId
            && Kernel.TickCount - lastOrder.Tick < REORDER_INTERVAL
        )
            return;

        Log.Debug($"[PetBundle] Ordering pet [{pet.Name}] to attack {targetId}");

        pet.Attack(targetId);
        _lastOrders[pet.UniqueId] = (targetId, Kernel.TickCount);
    }

    /// <summary>
    ///     Refreshes this instance.
    /// </summary>
    public void Refresh()
    {
        _lastOrders.Clear();
    }

    public void Stop()
    {
        _lastOrders.Clear();
    }

    #endregion Methods
}
