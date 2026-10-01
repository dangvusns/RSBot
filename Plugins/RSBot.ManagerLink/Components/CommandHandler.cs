using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;

namespace RSBot.ManagerLink.Components;

/// <summary>
///     Runs the commands the manager sends through the pipe.
/// </summary>
internal static class CommandHandler
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    ///     Runs one command and returns the object to send back as "data". Throws when the command fails.
    /// </summary>
    public static object Handle(string command, JsonElement args)
    {
        switch (command)
        {
            case "status":
                // The manager sends its daily reset time of the green hours with every request
                if (args.ValueKind == JsonValueKind.Object
                    && args.TryGetProperty("resetTime", out var reset)
                    && TimeSpan.TryParse(reset.GetString(), out var resetTime))
                    OnlineTimeTracker.SetResetTime(resetTime);

                return StatusTracker.CreateSnapshot();

            case "start":
                RequireIngame();
                Kernel.Bot?.Start();
                return null;

            case "stop":
                Kernel.Bot?.Stop();
                return null;

            case "showBot":
                RunOnMainForm(form =>
                {
                    form.Show();
                    EventManager.FireEvent("OnShowBotWindow");
                });
                return null;

            case "hideBot":
                RunOnMainForm(form => form.Hide());
                return null;

            case "showClient":
            case "hideClient":
                if (!ClientManager.IsRunning)
                    throw new InvalidOperationException("The client is not running");

                // RSBot.General keeps the Hide/Show button text in sync with this event
                EventManager.FireEvent("OnManagerSetClientVisible", command == "showClient");
                return null;

            case "goClientless":
                RequireIngame();
                if (Game.Clientless)
                    throw new InvalidOperationException("Already clientless");

                EventManager.FireEvent("OnManagerGoClientless");
                return null;

            case "goClient":
                if (ClientManager.IsRunning)
                    throw new InvalidOperationException("The client is already running");

                EventManager.FireEvent("OnManagerGoClient");
                return null;

            case "setAreaHere":
                RequireIngame();
                SetTrainingArea(Game.Player.Position, null);
                return null;

            case "setArea":
                RequireIngame();
                var area = args.Deserialize<AreaArgs>(_jsonOptions)
                    ?? throw new ArgumentException("Missing x, y and radius");

                SetTrainingArea(new Position(area.X, area.Y), area.Radius);
                return null;

            case "exit":
                // Give the pipe time to send the answer before the process ends
                Task.Run(async () =>
                {
                    await Task.Delay(300);

                    GlobalConfig.Save();
                    PlayerConfig.Save();
                    ClientManager.Kill();

                    Environment.Exit(0);
                });
                return null;

            default:
                throw new ArgumentException($"Unknown command [{command}]");
        }
    }

    private static void RequireIngame()
    {
        if (!Game.Ready || Game.Player == null)
            throw new InvalidOperationException("The character is not in game");
    }

    /// <summary>
    ///     Writes the training area the same way as the Training tab and the "area" chat command.
    /// </summary>
    private static void SetTrainingArea(Position position, int? radius)
    {
        PlayerConfig.Set("RSBot.Area.Region", position.Region);
        PlayerConfig.Set("RSBot.Area.X", position.XOffset.ToString("0.0"));
        PlayerConfig.Set("RSBot.Area.Y", position.YOffset.ToString("0.0"));
        PlayerConfig.Set("RSBot.Area.Z", position.ZOffset.ToString("0.0"));

        if (radius.HasValue)
            PlayerConfig.Set("RSBot.Area.Radius", radius.Value);
        else
            PlayerConfig.Get("RSBot.Area.Radius", 50);

        PlayerConfig.Save();

        EventManager.FireEvent("OnSetTrainingArea");

        Log.Notify($"[Manager] Training area set to X={position.X:0} Y={position.Y:0}");
    }

    private static void RunOnMainForm(Action<Form> action)
    {
        var form = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().FullName == "RSBot.Views.Main")
            ?? throw new InvalidOperationException("The bot window was not found");

        if (form.InvokeRequired)
            form.Invoke(action, form);
        else
            action(form);
    }

    private sealed class AreaArgs
    {
        public float X { get; set; }

        public float Y { get; set; }

        public int Radius { get; set; } = 50;
    }
}
