using RSBot.Core;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using RSBot.Core.Components.Tracing;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace RSBot.Party.Bundle.Commands;

internal class CommandsBundle
{
    /// <summary>
    ///     Gets or sets the configuration.
    /// </summary>
    /// <value>
    ///     The configuration.
    /// </value>
    public CommandsConfig Config { get; set; }

    /// <summary>
    /// Stores the mapping of command names to their associated actions. The second parameter of an action is the
    /// argument text after the command name (empty if there is none).
    /// </summary>
    private readonly Dictionary<string, Action<SpawnedPlayer, string>> _commands;

    /// <summary>
    /// The prefix of the optional config keys that tune the trace (distances, intervals, debug logging).
    /// </summary>
    private const string TraceConfigPrefix = "RSBot.Party.Trace.";
    private readonly object _sentCommandLock = new();
    private Task _sentCommandTask = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance of the CommandsBundle class with a predefined set of command actions.
    /// </summary>
    /// <remarks>Commands are case-insensitive: "trace [name]" / "traceme [name]" (walks to the spots the
    /// commander clicks), "notrace", "sitdown", "start", "stop", "town"/"return", "teleport from,to", "radius r" and
    /// "area"/"setarea x,y,r". Failures are answered with a private message to the commander.</remarks>
    internal CommandsBundle()
    {
        _commands = new(StringComparer.InvariantCultureIgnoreCase);
        _commands["trace"] = StartSmartTrace;
        _commands["traceme"] = StartSmartTrace;
        _commands["notrace"] = StopTrace;
        _commands["sitdown"] = SendSitdownRequest;
        _commands["start"] = (p, m) => { Kernel.Bot.Start(); };
        _commands["stop"] = (p, m) => { Kernel.Bot.Stop(); };
        _commands["town"] = ReturnTown;
        _commands["return"] = ReturnTown;
        _commands["teleport"] = Teleport;
        _commands["radius"] = SetBotRadius;
        _commands["area"] = SetBotArea;
        _commands["setarea"] = SetBotArea;
    }

    /// <summary>
    ///     Applies the party leader's own management commands on a worker, in send order.
    ///     Incoming self echoes are ignored, so one outgoing message produces one local action.
    /// </summary>
    public void QueueSentChat(ChatType type, string message)
    {
        if (!Game.Ready || !Game.Party.IsInParty || !Game.Party.IsLeader
            || (type != ChatType.Party && type != ChatType.All && type != ChatType.AllGM && type != ChatType.Private)
            || string.IsNullOrWhiteSpace(message))
            return;

        var text = message.Trim();
        var separator = text.IndexOf(' ');
        var name = separator < 0 ? text : text.Substring(0, separator);
        var args = separator < 0 ? string.Empty : text.Substring(separator + 1).Trim();
        if (!(name.Equals("start", StringComparison.OrdinalIgnoreCase)
            || name.Equals("stop", StringComparison.OrdinalIgnoreCase)
            || name.Equals("radius", StringComparison.OrdinalIgnoreCase)
            || name.Equals("area", StringComparison.OrdinalIgnoreCase)
            || name.Equals("setarea", StringComparison.OrdinalIgnoreCase)))
            return;

        var character = Game.Player;
        lock (_sentCommandLock)
            _sentCommandTask = _sentCommandTask.ContinueWith(_ =>
            {
                try
                {
                    if (!Game.Ready || Game.Player != character || !Game.Party.IsLeader)
                        return;
                    _commands[name](null, args);
                }
                catch (Exception e)
                {
                    Log.Fatal(e);
                }
            }, TaskScheduler.Default);
    }

    /// <summary>
    /// Returns to town using a return scroll. Runs on a worker, because using an item waits for the answer of the
    /// server, which the packet thread could never deliver while it waits.
    /// </summary>
    /// <param name="player">The commander.</param>
    /// <param name="args">Not used.</param>
    private void ReturnTown(SpawnedPlayer player, string args)
    {
        Task.Run(() =>
        {
            try
            {
                if (!Game.Player.UseReturnScroll())
                    Reply(player, "Return scroll not found or not usable right now");
            }
            catch (Exception e)
            {
                Log.Fatal(e);
            }
        });
    }

    /// <summary>
    /// Sets the radius of the training area.
    /// </summary>
    /// <remarks>If the radius value cannot be parsed to a positive floating-point number, the bot radius is
    /// not updated.</remarks>
    /// <param name="player">The commander.</param>
    /// <param name="radius">The radius. Must be a positive floating-point value.</param>
    private void SetBotRadius(SpawnedPlayer player, string radius)
    {
        if (!TryParseNumber(radius, out var r) || r <= 0)
        {
            Reply(player, "Usage: radius <number>");
            return;
        }

        PlayerConfig.Set("RSBot.Area.Radius", r);
        EventManager.FireEvent("OnSetTrainingArea");
    }

    /// <summary>
    /// Sets the training area to the specified coordinates.
    /// </summary>
    /// <param name="player">The commander.</param>
    /// <param name="coods">The coordinates and radius. Usage: area x,y,r</param>
    private void SetBotArea(SpawnedPlayer player, string coods)
    {
        try
        {
            var parts = coods.Split(',');
            if (parts.Length != 3)
            {
                Reply(player, "Usage: area x,y,radius");
                return;
            }

            if (!TryParseNumber(parts[0], out var x) || !TryParseNumber(parts[1], out var y)
                || !TryParseNumber(parts[2], out var radius) || radius <= 0)
            {
                Reply(player, "Usage: area x,y,radius (radius must be positive)");
                return;
            }

            var pos = new Position(x, y);
            PlayerConfig.Set("RSBot.Area.Region", pos.Region);
            PlayerConfig.Set("RSBot.Area.X", pos.XOffset);
            PlayerConfig.Set("RSBot.Area.Y", pos.YOffset);
            PlayerConfig.Set("RSBot.Area.Radius", radius);
            EventManager.FireEvent("OnSetTrainingArea");
        }
        catch (Exception e)
        {
            Log.Fatal(e);
        }
    }

    private static bool TryParseNumber(string value, out float number)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && float.IsFinite(number);
    }

    /// <summary>
    ///     Handle the bundle
    /// </summary>
    public void Handle(SpawnedPlayer player, string message)
    {
        try
        {
            if (player == null || Config == null || Config.PlayerList == null || string.IsNullOrWhiteSpace(message))
                return;

            // "Listen party master" only obeys the master of the own party
            var isMaster = Game.Party.IsInParty && Game.Party.Leader?.Name == player.Name;

            var shouldExecute = (Config.ListenFromList && Config.PlayerList.Contains(player.Name)) ||
                                (Config.ListenOnlyMaster && isMaster);

            if (!shouldExecute)
                return;

            // The first word is the command, the rest are its arguments
            var text = message.Trim();
            var separator = text.IndexOf(' ');
            var name = separator < 0 ? text : text.Substring(0, separator);
            var args = separator < 0 ? string.Empty : text.Substring(separator + 1).Trim();

            if (_commands.TryGetValue(name, out var command))
                command?.Invoke(player, args);
        }
        catch (Exception e)
        {
            Log.Fatal(e);
        }
    }

    /// <summary>
    /// Traces the commander (or the named player) by the bot until "notrace" is received. The bot is stopped
    /// meanwhile, so it does not walk the character away.
    /// </summary>
    /// <param name="player">The commander.</param>
    /// <param name="args">An optional player name.</param>
    private void StartSmartTrace(SpawnedPlayer player, string args)
    {
        if (!TryResolveTarget(player, args, out var targetName, out var seed))
            return;

        if (Kernel.Bot.Running)
        {
            Kernel.Bot.Stop();
            Log.Notify("Bot stopped while tracing");
        }

        TraceManager.Start(
            targetName,
            TraceMode.Smart,
            TraceOptions.Commander(TraceConfigPrefix),
            seed
        );
    }

    /// <summary>
    /// Stops tracing the commander.
    /// </summary>
    /// <param name="player">The commander.</param>
    /// <param name="args">Not used.</param>
    private void StopTrace(SpawnedPlayer player, string args)
    {
        TraceManager.Stop();
    }

    /// <summary>
    /// Finds the player to trace: the commander without a name, otherwise a visible player or a party member with
    /// that name (case-insensitive).
    /// </summary>
    private static bool TryResolveTarget(
        SpawnedPlayer commander,
        string name,
        out string targetName,
        out SpawnedPlayer seed
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            targetName = commander.Name;
            seed = commander;

            return true;
        }

        if (SpawnManager.TryGetEntity<SpawnedPlayer>(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase), out seed))
        {
            targetName = seed.Name;

            return true;
        }

        seed = null;
        targetName = Game.Party?.Members?.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))?.Name;
        if (targetName != null)
            return true;

        Reply(commander, $"Player '{name}' not found");

        return false;
    }

    /// <summary>
    /// Uses a nearby teleporter. Usage: "teleport from,to" (or "teleport from to" for single word names), where the
    /// names are part of the teleporter name and of the destination zone, for example "teleport ferry,donwhang".
    /// </summary>
    /// <param name="player">The commander.</param>
    /// <param name="args">The source and the destination.</param>
    private void Teleport(SpawnedPlayer player, string args)
    {
        var parts = args.Contains(',')
            ? args.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2)
        {
            Reply(player, "Usage: teleport <from>,<to>");
            return;
        }

        var from = parts[0];
        var to = parts[1];

        // Selecting the teleporter waits for the server, so do not block the packet thread
        Task.Run(() =>
        {
            try
            {
                if (!TryFindTeleporter(from, out var npc, out var source))
                {
                    Reply(player, $"Teleporter '{from}' is not near");
                    return;
                }

                var link = source.GetLinks().FirstOrDefault(l => l.Target != null && MatchesTeleport(l.Target, to));
                if (link == null)
                {
                    Reply(player, $"No teleport from '{from}' to '{to}'");
                    return;
                }

                if (!npc.TrySelect())
                {
                    Reply(player, $"Could not select the teleporter '{from}'");
                    return;
                }

                var packet = new Packet(0x705A);
                packet.WriteUInt(npc.UniqueId);
                packet.WriteByte(2);
                packet.WriteUInt(link.Target.ID);

                PacketManager.SendPacket(packet, PacketDestination.Server);

                Log.Notify($"Teleporting to {link.Target.ZoneName}");
            }
            catch (Exception e)
            {
                Log.Fatal(e);
            }
        });
    }

    /// <summary>
    /// Finds a visible teleporter whose name (or code name) contains the specified text.
    /// </summary>
    private static bool TryFindTeleporter(string name, out SpawnedBionic npc, out RefTeleport source)
    {
        RefTeleport found = null;

        var visible = SpawnManager.TryGetEntity<SpawnedBionic>(
            e =>
            {
                var record = e.Record;
                if (record == null || !(Contains(record.GetRealName(), name) || Contains(record.CodeName, name)))
                    return false;

                found = Game.ReferenceManager.TeleportData.FirstOrDefault(t => t.AssocRefObjId == record.ID);

                return found != null;
            },
            out npc
        );

        source = found;

        return visible && found != null;
    }

    private static bool MatchesTeleport(RefTeleport teleport, string name)
    {
        return Contains(teleport.ZoneName, name)
            || Contains(teleport.CodeName, name)
            || Contains(teleport.Character?.GetRealName(), name);
    }

    private static bool Contains(string text, string part)
    {
        return text != null && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Sends a private message to the commander.
    /// </summary>
    /// <param name="player">The commander.</param>
    /// <param name="text">The message.</param>
    private static void Reply(SpawnedPlayer player, string text)
    {
        try
        {
            if (player == null)
            {
                Log.Warn("[Commands] " + text);
                return;
            }
            ChatManager.SendPrivate(player.Name, text);
        }
        catch (Exception e)
        {
            Log.Fatal(e);
        }
    }

    /// <summary>
    ///     Sends the sit down / stand up request.
    /// </summary>
    private void SendSitdownRequest(SpawnedPlayer player, string args)
    {
        var packet = new Packet(0x704F);
        packet.WriteByte(4);

        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    /// <summary>
    ///     Refreshes this instance.
    /// </summary>
    public void Refresh()
    {
        Config = new CommandsConfig
        {
            PlayerList = PlayerConfig.GetArray<string>("RSBot.Party.Commands.PlayersList"),
            ListenFromList = PlayerConfig.Get<bool>("RSBot.Party.Commands.ListenOnlyList"),
            ListenOnlyMaster = PlayerConfig.Get<bool>("RSBot.Party.Commands.ListenFromMaster"),
        };
    }
}
