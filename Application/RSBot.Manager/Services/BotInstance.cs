using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Manager.Models;

namespace RSBot.Manager.Services;

/// <summary>
///     One RSBot process of an account and the pipe to its RSBot.ManagerLink plugin.
/// </summary>
public sealed class BotInstance : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    ///     RSBot writes the selected profile into the shared Profiles.rs on start, so instances start one by one.
    /// </summary>
    private static readonly SemaphoreSlim _launchLock = new(1, 1);

    private static readonly TimeSpan _launchSpacing = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _requestLock = new(1, 1);

    private NamedPipeClientStream _pipe;
    private StreamReader _reader;
    private StreamWriter _writer;
    private long _nextId;
    private DateTime _startedAt;
    private volatile bool _disposed;

    public BotInstance(ManagerAccount account)
    {
        Account = account;
    }

    public ManagerAccount Account { get; }

    public BotStatus Status { get; private set; }

    public bool IsConnected => _pipe?.IsConnected == true;

    /// <summary>
    ///     Set between "Mở Bot" and the first answer of the plugin.
    /// </summary>
    public bool IsStarting { get; private set; }

    public string LastError { get; private set; }

    private string PipeName => "RSBot.Manager." + Account.ProfileName;

    /// <summary>
    ///     Starts RSBot with the profile and character of this account and lets it open the client.
    /// </summary>
    public async Task LaunchAsync()
    {
        if (IsConnected || IsStarting)
            return;

        IsStarting = true;
        LastError = null;
        _startedAt = DateTime.Now;

        await _launchLock.WaitAsync();
        try
        {
            var startInfo = new ProcessStartInfo(ManagerStore.BotExecutable)
            {
                WorkingDirectory = ManagerStore.BotFolder,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("--profile");
            startInfo.ArgumentList.Add(Account.ProfileName);
            startInfo.ArgumentList.Add("--character");
            startInfo.ArgumentList.Add(Account.Character);
            startInfo.ArgumentList.Add("--launch-client");

            Process.Start(startInfo);

            await Task.Delay(_launchSpacing);
        }
        catch (Exception ex)
        {
            IsStarting = false;
            LastError = ex.Message;
        }
        finally
        {
            _launchLock.Release();
        }
    }

    /// <summary>
    ///     Connects when the bot is running and reads its status. Called by the manager every second.
    /// </summary>
    public async Task PollAsync()
    {
        if (_disposed)
            return;

        if (!IsConnected && !await TryConnectAsync())
        {
            Status = null;

            if (IsStarting && DateTime.Now - _startedAt > TimeSpan.FromSeconds(90))
            {
                IsStarting = false;
                LastError = "Không kết nối được bot, plugin RSBot.ManagerLink đã bật chưa?";
            }

            return;
        }

        try
        {
            var data = await SendAsync("status");
            Status = data.Deserialize<BotStatus>(_jsonOptions);
            IsStarting = false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Disconnect();
        }
    }

    /// <summary>
    ///     Sends one command and returns its "data". Throws when the bot answers with an error.
    /// </summary>
    public async Task<JsonElement> SendAsync(string command, object args = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BotInstance));

        await _requestLock.WaitAsync();
        try
        {
            if (_disposed || !IsConnected)
                throw new InvalidOperationException("Bot chưa chạy");

            var id = Interlocked.Increment(ref _nextId);
            await _writer.WriteLineAsync(JsonSerializer.Serialize(new { id, cmd = command, args }));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var line = await _reader.ReadLineAsync(timeout.Token)
                ?? throw new IOException("The bot closed the connection");

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (!root.GetProperty("ok").GetBoolean())
                throw new InvalidOperationException(root.GetProperty("error").GetString());

            return root.GetProperty("data").Clone();
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            Disconnect();
            throw;
        }
        finally
        {
            // A request that was running when the instance was disposed closes the pipe itself
            if (_disposed)
                Disconnect();

            _requestLock.Release();
        }
    }

    /// <summary>
    ///     Asks the bot to save and exit, and kills it when it is still running after 10 seconds.
    /// </summary>
    public async Task CloseAsync()
    {
        var processId = Status?.ProcessId ?? 0;

        try
        {
            await SendAsync("exit");
        }
        catch
        {
            // killed below if it is still there
        }

        Disconnect();
        Status = null;
        IsStarting = false;

        if (processId == 0)
            return;

        await Task.Run(() =>
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.WaitForExit(10000))
                    process.Kill();
            }
            catch (ArgumentException)
            {
                // already exited
            }
        });
    }

    private async Task<bool> TryConnectAsync()
    {
        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(100);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            pipe.Dispose();
            return false;
        }

        // The bot accepts one manager at a time, so a disposed instance must not keep the connection
        if (_disposed)
        {
            pipe.Dispose();
            return false;
        }

        _pipe = pipe;
        _reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
        _writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

        return true;
    }

    private void Disconnect()
    {
        try
        {
            _reader?.Dispose();
            _writer?.Dispose();
            _pipe?.Dispose();
        }
        catch (IOException)
        {
            // the pipe is already broken
        }

        _reader = null;
        _writer = null;
        _pipe = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Without waiting: blocking the UI thread here would stop the running request from finishing
        if (!_requestLock.Wait(0))
            return;

        Disconnect();
        _requestLock.Release();
    }
}
