using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;

namespace RSBot.ManagerLink.Components;

/// <summary>
///     Collects the numbers the manager shows for this bot (gold picked, item counters, online time).
/// </summary>
internal static class StatusTracker
{
    private static readonly object _lock = new();

    /// <summary>
    ///     Counter name to the wildcard patterns of the item code names it counts.
    /// </summary>
    private static Dictionary<string, Regex[]> _counterPatterns = new();

    private static readonly Dictionary<string, long> _itemCounts = new();

    private static ulong _goldPicked;

    private static DateTime? _enteredGameAt;

    public static void Initialize()
    {
        EventManager.SubscribeEvent("OnPickupGold", new Action<uint>(OnPickupGold));
        EventManager.SubscribeEvent("OnPickupItem", new Action<InventoryItem>(OnPickupItem));
        EventManager.SubscribeEvent("OnPartyPickItem", new Action<InventoryItem>(OnPickupItem));
        EventManager.SubscribeEvent("OnEnterGame", OnEnterGame);
        EventManager.SubscribeEvent("OnAgentServerDisconnected", OnAgentServerDisconnected);
    }

    /// <summary>
    ///     Replaces the item counters. Each pattern is a code name wildcard such as ITEM_ETC_ARCHEMY_*.
    /// </summary>
    public static void SetCounters(Dictionary<string, string[]> counters)
    {
        var compiled = new Dictionary<string, Regex[]>();
        foreach (var counter in counters ?? new Dictionary<string, string[]>())
            compiled[counter.Key] = (counter.Value ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(WildcardToRegex)
                .ToArray();

        lock (_lock)
        {
            _counterPatterns = compiled;

            foreach (var name in compiled.Keys)
                _itemCounts.TryAdd(name, 0);
        }
    }

    public static object CreateSnapshot()
    {
        var player = Game.Player;
        var ready = Game.Ready && player != null;

        Dictionary<string, long> itemCounts;
        ulong goldPicked;
        DateTime? enteredGameAt;
        lock (_lock)
        {
            itemCounts = new Dictionary<string, long>(_itemCounts);
            goldPicked = _goldPicked;
            enteredGameAt = _enteredGameAt;
        }

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
            goldPicked,
            itemCounts,
            onlineSeconds = ready && enteredGameAt.HasValue
                ? (long)(DateTime.Now - enteredGameAt.Value).TotalSeconds
                : 0,
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

    private static void OnPickupGold(uint amount)
    {
        lock (_lock)
            _goldPicked += amount;
    }

    private static void OnPickupItem(InventoryItem item)
    {
        var codeName = item?.Record?.CodeName;
        if (string.IsNullOrEmpty(codeName))
            return;

        var amount = Math.Max((int)item.Amount, 1);

        lock (_lock)
        {
            foreach (var counter in _counterPatterns)
                if (counter.Value.Any(p => p.IsMatch(codeName)))
                    _itemCounts[counter.Key] = _itemCounts.GetValueOrDefault(counter.Key) + amount;
        }
    }

    private static void OnEnterGame()
    {
        lock (_lock)
            _enteredGameAt = DateTime.Now;
    }

    private static void OnAgentServerDisconnected()
    {
        lock (_lock)
            _enteredGameAt = null;
    }

    private static Regex WildcardToRegex(string pattern)
    {
        var expression = "^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$";

        return new Regex(expression, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
