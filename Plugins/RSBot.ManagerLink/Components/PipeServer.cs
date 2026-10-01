using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Components;

namespace RSBot.ManagerLink.Components;

/// <summary>
///     Named pipe the manager connects to. One JSON object per line in both directions:
///     request {"id":1,"cmd":"start","args":{...}}, response {"id":1,"ok":true,"error":null,"data":{...}}.
/// </summary>
internal static class PipeServer
{
    /// <summary>
    ///     The pipe name is built from the profile so the manager can find a bot it did not start itself.
    /// </summary>
    public static string PipeName => "RSBot.Manager." + ProfileManager.SelectedProfile;

    private static CancellationTokenSource _cancellation;

    public static void Start()
    {
        if (_cancellation != null)
            return;

        _cancellation = new CancellationTokenSource();

        var token = _cancellation.Token;
        Task.Run(() => RunAsync(token));
    }

    public static void Stop()
    {
        _cancellation?.Cancel();
        _cancellation = null;
    }

    private static async Task RunAsync(CancellationToken token)
    {
        var name = PipeName;
        Log.Debug($"[Manager link] Listening on pipe [{name}]");

        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    name,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous
                );

                await pipe.WaitForConnectionAsync(token);

                try
                {
                    await ServeClientAsync(pipe, token);
                }
                catch (IOException)
                {
                    // The manager closed the pipe, wait for the next connection right away
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                // Another RSBot instance already uses this profile, or the manager went away mid-write
                Log.Warn($"[Manager link] Pipe [{name}] error: {ex.Message}");
                await Task.Delay(5000, token).ContinueWith(_ => { });
            }
            catch (Exception ex)
            {
                Log.Fatal(ex);
                await Task.Delay(5000, token).ContinueWith(_ => { });
            }
        }
    }

    private static async Task ServeClientAsync(Stream pipe, CancellationToken token)
    {
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

        while (!token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(token);
            if (line == null)
                return;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            await writer.WriteLineAsync(HandleLine(line));
        }
    }

    private static string HandleLine(string line)
    {
        long id = 0;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.TryGetProperty("id", out var idElement))
                id = idElement.GetInt64();

            var command = root.GetProperty("cmd").GetString();
            var args = root.TryGetProperty("args", out var argsElement) ? argsElement.Clone() : default;

            var data = CommandHandler.Handle(command, args);

            return JsonSerializer.Serialize(new { id, ok = true, error = (string)null, data });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { id, ok = false, error = ex.Message, data = (object)null });
        }
    }
}
