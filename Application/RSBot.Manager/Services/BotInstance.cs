using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
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

    /// <summary>
    ///     How long to wait before each auto restart, by the number of restarts in a row.
    /// </summary>
    private static readonly TimeSpan[] _restartDelays =
    {
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
    };

    /// <summary>
    ///     A running RSBot that does not answer for this long is treated as hung and killed by the auto restart.
    /// </summary>
    private static readonly TimeSpan _hungTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     Botting this long without a crash resets the restart delay to the shortest one.
    /// </summary>
    private static readonly TimeSpan _stableTime = TimeSpan.FromMinutes(10);

    private static int _queuedLaunches;

    private readonly SemaphoreSlim _requestLock = new(1, 1);

    private NamedPipeClientStream _pipe;
    private StreamReader _reader;
    private StreamWriter _writer;
    private long _nextId;
    private DateTime _startedAt;
    private volatile bool _disposed;

    private Process _process;
    private int _lastProcessId;

    /// <summary>
    ///     Set while the bot was botting, cleared when it is stopped or closed from the manager.
    /// </summary>
    private bool _restartArmed;

    private bool _resumeAfterRestart;
    private int _restartCount;
    private DateTime _runningSince;
    private DateTime? _unreachableSince;

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

    /// <summary>
    ///     When the auto restart opens the bot again, null when no restart is waiting.
    /// </summary>
    public DateTime? RestartAt { get; private set; }

    /// <summary>
    ///     The last auto restart, for the tooltip of the state column.
    /// </summary>
    public string RestartInfo { get; private set; }

    /// <summary>
    ///     Bots waiting for their turn to start.
    /// </summary>
    public static int QueuedLaunches => Volatile.Read(ref _queuedLaunches);

    /// <summary>
    ///     When the next queued bot may start.
    /// </summary>
    public static DateTime NextLaunchAt { get; private set; }

    private string PipeName => "RSBot.Manager." + Account.ProfileName;

    /// <summary>
    ///     Starts RSBot with the profile and character of this account. Does nothing when this bot is
    ///     already running, starting or waiting in the launch queue.
    /// </summary>
    public async Task LaunchAsync()
    {
        if (IsConnected || IsStarting || IsRunning())
            return;

        IsStarting = true;
        LastError = null;
        // Set again when the bot leaves the queue, so the connect timeout never ends while it waits
        _startedAt = DateTime.MaxValue;

        Interlocked.Increment(ref _queuedLaunches);
        await _launchLock.WaitAsync();
        Interlocked.Decrement(ref _queuedLaunches);
        try
        {
            if (_disposed)
            {
                IsStarting = false;
                return;
            }

            var startInfo = new ProcessStartInfo(ManagerStore.BotExecutable)
            {
                WorkingDirectory = ManagerStore.BotFolder,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("--profile");
            startInfo.ArgumentList.Add(Account.ProfileName);
            startInfo.ArgumentList.Add("--character");
            startInfo.ArgumentList.Add(Account.Character);
            startInfo.ArgumentList.Add(Account.Clientless ? "--launch-clientless" : "--launch-client");

            _process?.Dispose();
            _process = Process.Start(startInfo);

            // The connect timeout counts from the real start, not from the time in the queue
            _startedAt = DateTime.Now;

            var spacing = TimeSpan.FromSeconds(
                Math.Max(ManagerData.MinimumLaunchDelaySeconds, ManagerStore.Data.LaunchDelaySeconds)
            );
            NextLaunchAt = DateTime.Now + spacing;

            await Task.Delay(spacing);
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
    ///     True when an RSBot of this account runs without being connected to this manager: started by this
    ///     manager and still loading, or already serving the pipe (another manager, or a bot opened by hand).
    /// </summary>
    private bool IsRunning()
    {
        try
        {
            if (_process != null && !_process.HasExited)
                return true;
        }
        catch (InvalidOperationException)
        {
            // the process object is not associated with a process
        }

        return PipeExists();
    }

    /// <summary>
    ///     Lists the pipes instead of opening this one, which would take the one connection the bot accepts.
    /// </summary>
    private bool PipeExists()
    {
        try
        {
            return Directory
                .EnumerateFiles(@"\\.\pipe\")
                .Any(p => Path.GetFileName(p).Equals(PipeName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
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

            WatchForRestart();
            return;
        }

        try
        {
            var data = await SendAsync("status", new { resetTime = ManagerStore.Data.ResetTime });
            Status = data.Deserialize<BotStatus>(_jsonOptions);
            IsStarting = false;

            await TrackRunningAsync(Status);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Disconnect();
        }
    }

    /// <summary>
    ///     Remembers whether the bot was botting, so the auto restart knows a lost bot has to come back.
    /// </summary>
    private async Task TrackRunningAsync(BotStatus status)
    {
        _lastProcessId = status.ProcessId;
        _unreachableSince = null;

        switch (status.State)
        {
            case "Running":
                if (!_restartArmed)
                    _runningSince = DateTime.Now;

                _restartArmed = true;
                _resumeAfterRestart = false;

                if (DateTime.Now - _runningSince > _stableTime)
                    _restartCount = 0;
                break;

            case "InGame" when _resumeAfterRestart:
                // Back in game after an auto restart: continue botting
                _resumeAfterRestart = false;
                try
                {
                    await SendAsync("start");
                }
                catch (InvalidOperationException ex)
                {
                    LastError = ex.Message;
                }
                break;

            case "InGame":
                // Stopped while in game, by the user or the bot itself
                _restartArmed = false;
                break;
        }
    }

    /// <summary>
    ///     Auto restart: when a bot that was botting is gone (or alive but not answering for
    ///     <see cref="_hungTimeout" />), waits a growing delay and starts it again.
    /// </summary>
    private void WatchForRestart()
    {
        if (!Account.AutoRestart || !_restartArmed || IsStarting)
        {
            RestartAt = null;
            return;
        }

        if (RestartAt == null)
        {
            if (IsRSBotProcess(_lastProcessId))
            {
                _unreachableSince ??= DateTime.Now;
                if (DateTime.Now - _unreachableSince < _hungTimeout)
                    return;

                KillProcess(_lastProcessId);
            }

            var delay = _restartDelays[Math.Min(_restartCount, _restartDelays.Length - 1)];
            RestartAt = DateTime.Now + delay;
            RestartInfo = $"Bot bị tắt lúc {DateTime.Now:HH:mm:ss}, tự mở lại sau {delay.TotalSeconds:0} giây";
            return;
        }

        if (DateTime.Now < RestartAt)
            return;

        RestartAt = null;
        _unreachableSince = null;
        _restartCount++;
        _resumeAfterRestart = true;
        RestartInfo = $"Tự mở lại lần {_restartCount} lúc {DateTime.Now:HH:mm:ss}";

        // Not awaited: the launch queue would hold up the polling of all bots
        _ = LaunchAsync();
    }

    /// <summary>
    ///     The name check keeps a reused process id of another program from counting as the bot.
    /// </summary>
    private static bool IsRSBotProcess(int processId)
    {
        if (processId == 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(processId);

            return !process.HasExited && process.ProcessName.Equals("RSBot", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static void KillProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // already exited
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
        var processId = Status?.ProcessId ?? _lastProcessId;

        // Closed on purpose: the auto restart must not open it again
        _restartArmed = false;
        _resumeAfterRestart = false;
        RestartAt = null;

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

        // The saved id may be old: never wait for or kill another program that got it
        if (!IsRSBotProcess(processId))
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
        _process?.Dispose();

        // Without waiting: blocking the UI thread here would stop the running request from finishing
        if (!_requestLock.Wait(0))
            return;

        Disconnect();
        _requestLock.Release();
    }
}
