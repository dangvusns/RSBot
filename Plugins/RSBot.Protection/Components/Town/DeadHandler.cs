using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Objects;

namespace RSBot.Protection.Components.Town;

public class DeadHandler : AbstractTownHandler
{
    private const int MaxResurrectAttempts = 5;

    /// <summary>
    ///     Increased on every death so an older, still waiting handler run stops.
    /// </summary>
    private static int _deathId;

    /// <summary>
    ///     Initializes this instance.
    /// </summary>
    public static void Initialize()
    {
        SubscribeEvents();
    }

    /// <summary>
    ///     Subscribes the events.
    /// </summary>
    private static void SubscribeEvents()
    {
        EventManager.SubscribeEvent("OnPlayerDied", OnPlayerDied);
    }

    /// <summary>
    ///     Cores the entity life state changed.
    /// </summary>
    /// <param name="uniqueId">The unique identifier.</param>
    private static async void OnPlayerDied()
    {
        if (!Kernel.Bot.Running)
            return;

        var deathId = Interlocked.Increment(ref _deathId);

        if (Game.Player.Level < 10)
        {
            await Task.Delay(5000);
            await Resurrect(2, deathId);
            return;
        }

        if (!PlayerConfig.Get<bool>("RSBot.Protection.checkDead"))
            return;

        if (Game.Player.State.LifeState != LifeState.Dead)
            return;

        var itemsToUse = PlayerConfig.GetArray<string>("RSBot.Inventory.AutoUseAccordingToPurpose");
        var inventoryItem = Game.Player.Inventory.GetItem(
            new TypeIdFilter(3, 3, 13, 6),
            p => itemsToUse.Contains(p.Record.CodeName)
        );
        if (inventoryItem != null)
        {
            inventoryItem.Use();
            return;
        }

        var timeOut = PlayerConfig.Get("RSBot.Protection.numDeadTimeout", 30);

        Log.WarnLang("ResurrectSPointSeconds", timeOut);

        await Task.Delay(timeOut * 1000);

        await Resurrect(1, deathId);
    }

    /// <summary>
    ///     Sends the resurrect request until the player is alive; a request sent while teleporting is lost.
    /// </summary>
    /// <param name="preferred">1 = specified (return) point, 2 = present point.</param>
    /// <param name="deathId">The death this run belongs to.</param>
    private static async Task Resurrect(byte preferred, int deathId)
    {
        for (var attempt = 1; attempt <= MaxResurrectAttempts; attempt++)
        {
            if (deathId != _deathId || !Kernel.Bot.Running || Game.Player.State.LifeState != LifeState.Dead)
                return;

            // Only PvP/CTF deaths (present point offered, normal not) forbid the usual choice.
            var options = Game.Player.ResurrectOptions;
            var type = options != 0 && (options & 4) == 0 && (options & preferred) == 0
                ? (byte)((options & 1) != 0 ? 1 : 2)
                : preferred;

            if (attempt > 1)
                Log.Warn($"Still dead, resending resurrect request ({attempt}/{MaxResurrectAttempts}).");

            var packet = new Packet(0x3053);
            packet.WriteByte(type);
            PacketManager.SendPacket(packet, PacketDestination.Server);

            await Task.Delay(5000);
        }

        if (deathId == _deathId && Kernel.Bot.Running && Game.Player.State.LifeState == LifeState.Dead)
            Log.Warn($"Could not resurrect after {MaxResurrectAttempts} attempts.");
    }
}
