using RSBot.Core;
using RSBot.Core.Components.Tracing;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Cos;
using RSBot.Core.Objects.Spawn;
using System;
using System.Collections.Generic;
using System.Linq;

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
    /// Stores the mapping of command names to their associated actions.
    /// </summary>
    /// <remarks>Each entry in the dictionary associates a string command identifier with an executable
    /// action. This collection is intended for internal use to manage available commands within the bundle.</remarks>
    private readonly Dictionary<string, Action<SpawnedPlayer, string>> _commands;

    /// <summary>
    /// The prefix of the optional config keys that tune the trace (distances, intervals, debug logging).
    /// </summary>
    private const string TraceConfigPrefix = "RSBot.Party.Trace.";

    /// <summary>
    /// Initializes a new instance of the CommandsBundle class with a predefined set of command actions.
    /// </summary>
    /// <remarks>This constructor sets up the internal command dictionary using case-insensitive string
    /// comparison. The available commands include "trace" (game follow), "traceme" (walks to the spots the commander
    /// clicks), "notrace" and "sitdown", each mapped to their respective action handlers.</remarks>
    internal CommandsBundle()
    {
        _commands = new(StringComparer.InvariantCultureIgnoreCase);
        _commands["trace"] = StartGameTrace;
        _commands["traceme"] = StartSmartTrace;
        _commands["notrace"] = StopTrace;
        _commands["sitdown"] = SendSitdownRequest;
        _commands["start"] = (p, m) => { Kernel.Bot.Start(); };
        _commands["stop"] = (p, m) => { Kernel.Bot.Stop(); };
        _commands["town"] = ReturnTown;
        _commands["radius"] = SetBotRadius;
        _commands["area"] = SetBotArea;
    }

    /// <summary>
    /// Returns the specified player to their home town using a return scroll.
    /// </summary>
    /// <param name="player">The player to be returned to their home town.</param>
    /// <param name="arg2">An additional argument for the return operation. The specific usage depends on the command context.</param>
    private void ReturnTown(SpawnedPlayer player, string arg2)
    {
        try
        {
            Game.Player.UseReturnScroll();
        }
        catch (Exception e)
        {
            Log.Fatal(e);
        }
    }

    /// <summary>
    /// Sets the bot's operating radius for the specified player if the provided radius value is valid.
    /// </summary>
    /// <remarks>If the radius value cannot be parsed to a positive floating-point number, the bot radius is
    /// not updated.</remarks>
    /// <param name="player">The player for whom the bot radius is being set.</param>
    /// <param name="radius">A string representation of the desired radius. Must be a positive floating-point value.</param>
    private void SetBotRadius(SpawnedPlayer player, string radius)
    {
        if (!float.TryParse(radius, out var r) || r <= 0)
            return;

        PlayerConfig.Set("RSBot.Area.Radius", r);
    }

    /// <summary>
    /// Sets the operational area for the specified bot player using the provided coordinates.
    /// </summary>
    /// <param name="player">The bot player whose area is to be set.</param>
    /// <param name="coods">A string representing the coordinates that define the bot's area. The format and validity requirements for this string depend on the implementation.</param>
    /// Usage: area x,y,r
    private void SetBotArea(SpawnedPlayer player, string coods)
    {
        try
        {
            var parts = coods.Split(',');
            if (parts.Length != 3)
                return;

            if (float.TryParse(parts[0], out var x) && float.TryParse(parts[1], out var y) && float.TryParse(parts[2], out var radius))
            {
                var pos = new Position(x, y);

                PlayerConfig.Set("RSBot.Area.Region", pos.Region);
                PlayerConfig.Set("RSBot.Area.X", pos.XOffset);
                PlayerConfig.Set("RSBot.Area.Y", pos.YOffset);
                PlayerConfig.Set("RSBot.Area.Radius", radius);

                EventManager.FireEvent("OnSetTrainingArea");
            }
        }
        catch (Exception e)
        {
            Log.Fatal(e);
        }
    }

    /// <summary>
    ///     Handle the bundle
    /// </summary>
    public void Handle(SpawnedPlayer player, string message)
    {
        try
        {
            if (player == null || Config == null || Config.PlayerList == null)
                return;

            var shouldExecute = (Config.ListenFromList && Config.PlayerList.Contains(player.Name)) ||
                                (Config.ListenOnlyMaster && Game.Party.IsInParty);

            if (!shouldExecute)
                return;

            foreach (var command in _commands)
                if (StringComparer.InvariantCultureIgnoreCase.Equals(message, command.Key))
                    command.Value?.Invoke(player, message);
        }
        catch (Exception e)
        {
            Log.Fatal(e);
        }
    }

    /// <summary>
    /// Traces the commander who sent the message with the native trace of the game until "notrace" is received.
    /// </summary>
    /// <param name="player">The commander.</param>
    /// <param name="message">The received message.</param>
    private void StartGameTrace(SpawnedPlayer player, string message)
    {
        TraceManager.Start(
            player.Name,
            TraceMode.GameTrace,
            TraceOptions.Close().ApplyConfig(TraceConfigPrefix),
            player
        );
    }

    /// <summary>
    /// Traces the commander who sent the message by the bot until "notrace" is received. The bot is stopped meanwhile,
    /// so it does not walk the character away.
    /// </summary>
    /// <param name="player">The commander.</param>
    /// <param name="message">The received message.</param>
    private void StartSmartTrace(SpawnedPlayer player, string message)
    {
        if (Kernel.Bot.Running)
        {
            Kernel.Bot.Stop();
            Log.Notify("Bot stopped while tracing");
        }

        TraceManager.Start(
            player.Name,
            TraceMode.Smart,
            TraceOptions.Close().ApplyConfig(TraceConfigPrefix),
            player
        );
    }

    /// <summary>
    /// Stops tracing the commander.
    /// </summary>
    /// <param name="player">The commander.</param>
    /// <param name="message">The received message.</param>
    private void StopTrace(SpawnedPlayer player, string message)
    {
        TraceManager.Stop();
    }

    /// <summary>
    ///     Send trace request by speficied uniqueId
    /// </summary>
    private void SendSitdownRequest(SpawnedPlayer player, string message)
    {
        var packet = new Packet(0x704F);
        packet.WriteByte(4);
        //packet.WriteUInt();

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
