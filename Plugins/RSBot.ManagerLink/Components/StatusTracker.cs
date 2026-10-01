using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;

namespace RSBot.ManagerLink.Components;

/// <summary>
///     Builds the status the manager shows for this bot. The loot numbers are the ones of the Statistics tab,
///     so both show the same values and its Reset button resets both.
/// </summary>
internal static class StatusTracker
{
    private static readonly DateTime _processStartedAt = Process.GetCurrentProcess().StartTime;

    private static IList _calculators;

    private static readonly object _lock = new();

    /// <summary>
    ///     The green (full experience) time left from the last fatigue update of the server, and when it came.
    /// </summary>
    private static long? _greenSecondsAtUpdate;

    private static DateTime _greenUpdatedAt;

    public static void Initialize()
    {
        EventManager.SubscribeEvent("OnFatigueTimeUpdate", OnFatigueTimeUpdate);
        EventManager.SubscribeEvent("OnAgentServerDisconnected", OnAgentServerDisconnected);
    }

    public static object CreateSnapshot()
    {
        var player = Game.Player;
        var ready = Game.Ready && player != null;

        return new
        {
            state = GetState(ready),
            profile = ProfileManager.SelectedProfile,
            processId = Environment.ProcessId,
            charName = ready ? player.Name : null,
            hp = ready ? player.Health : 0,
            maxHp = ready ? player.MaximumHealth : 0,
            mp = ready ? player.Mana : 0,
            maxMp = ready ? player.MaximumMana : 0,
            gold = ready ? player.Gold : 0,
            goldPicked = GetStatistic("GoldPicked"),
            elixirsPicked = GetStatistic("ElixirsPicked"),
            tabletsPicked = GetStatistic("TabletsPicked"),
            equipmentPicked = GetStatistic("EquipmentPicked"),
            // Since RSBot was opened, which is when "Mở Bot" was clicked in the manager
            uptimeSeconds = (long)(DateTime.Now - _processStartedAt).TotalSeconds,
            greenSecondsLeft = GetGreenSecondsLeft(),
            playedSecondsToday = OnlineTimeTracker.GetPlayedSeconds(),
            posX = ready ? player.Position.X : 0,
            posY = ready ? player.Position.Y : 0,
            region = ready ? (ushort)player.Position.Region : (ushort)0,
            clientless = Game.Clientless,
            clientRunning = ClientManager.IsRunning,
        };
    }

    private static string GetState(bool ready)
    {
        if (!ready)
            return Kernel.Proxy != null && Kernel.Proxy.IsConnectedToAgentserver ? "LoggingIn" : "NotLoggedIn";

        if (Kernel.Bot != null && Kernel.Bot.Running)
            return "Running";

        return "InGame";
    }

    /// <summary>
    ///     Counts down from the last fatigue update. Null when the server sent none (no fatigue system).
    /// </summary>
    private static long? GetGreenSecondsLeft()
    {
        lock (_lock)
        {
            if (_greenSecondsAtUpdate == null)
                return null;

            var elapsed = (long)(DateTime.Now - _greenUpdatedAt).TotalSeconds;

            return Math.Max(_greenSecondsAtUpdate.Value - elapsed, 0);
        }
    }

    /// <summary>
    ///     RSBot.Protection decodes the fatigue packet into FatigueHandler.ShardFatigueFullExpSeconds before it fires
    ///     the event. It has no reference to this plugin, so the value is read by reflection.
    /// </summary>
    private static void OnFatigueTimeUpdate()
    {
        try
        {
            var value = AppDomain
                .CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "RSBot.Protection")
                ?.GetType("RSBot.Protection.Components.Town.FatigueHandler")
                ?.GetProperty("ShardFatigueFullExpSeconds", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);

            if (value == null)
                return;

            lock (_lock)
            {
                _greenSecondsAtUpdate = Convert.ToInt64(value);
                _greenUpdatedAt = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"[Manager link] Could not read the fatigue time: {ex.Message}");
        }
    }

    private static void OnAgentServerDisconnected()
    {
        lock (_lock)
            _greenSecondsAtUpdate = null;
    }

    /// <summary>
    ///     Reads a value from the Statistics plugin's calculators by name. The plugin has no public API,
    ///     so its CalculatorRegistry is found by reflection. Returns 0 when the plugin is not loaded.
    /// </summary>
    private static long GetStatistic(string name)
    {
        try
        {
            var calculators = _calculators ??= FindCalculators();
            if (calculators == null)
                return 0;

            foreach (var calculator in calculators)
            {
                var type = calculator.GetType();
                if ((string)type.GetProperty("Name")?.GetValue(calculator) != name)
                    continue;

                return Convert.ToInt64(type.GetMethod("GetValue", Type.EmptyTypes)?.Invoke(calculator, null));
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"[Manager link] Could not read statistic [{name}]: {ex.Message}");
        }

        return 0;
    }

    private static IList FindCalculators()
    {
        var registry = AppDomain
            .CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "RSBot.Statistics")
            ?.GetType("RSBot.Statistics.Stats.CalculatorRegistry");

        return registry
            ?.GetProperty("Calculators", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) as IList;
    }
}
