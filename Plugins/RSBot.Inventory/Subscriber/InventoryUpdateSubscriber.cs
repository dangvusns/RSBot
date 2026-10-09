using System;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Event;

namespace RSBot.Inventory.Subscriber;

internal static class InventoryUpdateSubscriber
{
    private static int _queued;

    /// <summary>
    ///     Subscribes the events.
    /// </summary>
    public static void SubscribeEvents()
    {
        EventManager.SubscribeEvent("OnInventoryUpdate", OnInventoryUpdate);
    }

    private static void OnInventoryUpdate()
    {
        var autoSort = PlayerConfig.Get("RSBot.Inventory.AutoSort", false);
        var inventory = Game.Player?.Inventory;
        if (!autoSort || inventory == null || inventory.IsSorting
            || Interlocked.CompareExchange(ref _queued, 1, 0) != 0)
            return;

        // Keep the packet handler free to process the move acknowledgments.
        _ = Task.Run(() =>
        {
            try
            {
                if (Game.Player?.Inventory == inventory)
                    inventory.Sort();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex);
            }
            finally
            {
                Interlocked.Exchange(ref _queued, 0);
            }
        });
    }
}
