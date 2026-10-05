using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Network;

namespace RSBot.PacketAnalyzer.Components;

/// <summary>
///     Writes captured packets and bot events to <c>User/Logs/Packets/&lt;character&gt;/</c>.
///     Capturing only enqueues, all file I/O happens on a dedicated writer thread.
/// </summary>
internal sealed class PacketRecorder
{
    private const string ConfigKey = "RSBot.PacketAnalyzer.Record";
    private const int QueueCapacity = 200_000;

    private readonly PacketFilter _filter;
    private readonly BlockingCollection<Entry> _queue = new(new ConcurrentQueue<Entry>(), QueueCapacity);
    private readonly object _streamLock = new();

    private volatile bool _recording;
    private long _dropped;
    private long _size;

    private int _maxFileMB;
    private int _maxFolderMB;
    private int _keepDays;
    private bool _recordOnLaunch;
    private bool _recordOnBotStart;
    private volatile bool _includeHex;
    private volatile bool _includeLog;

    // Owned by the writer thread (guarded by _streamLock)
    private StreamWriter _writer;
    private string _basePath;
    private int _part;
    private DateTime _lastDate;

    public PacketRecorder(PacketFilter filter)
    {
        _filter = filter;

        _maxFileMB = GlobalConfig.Get($"{ConfigKey}.MaxFileMB", 50);
        _maxFolderMB = GlobalConfig.Get($"{ConfigKey}.MaxFolderMB", 1024);
        _keepDays = GlobalConfig.Get($"{ConfigKey}.KeepDays", 7);
        _recordOnLaunch = GlobalConfig.Get($"{ConfigKey}.OnLaunch", false);
        _recordOnBotStart = GlobalConfig.Get($"{ConfigKey}.OnBotStart", false);
        _includeHex = GlobalConfig.Get($"{ConfigKey}.IncludeHex", true);
        _includeLog = GlobalConfig.Get($"{ConfigKey}.IncludeLog", true);

        new Thread(WriterLoop) { IsBackground = true, Name = "PacketAnalyzer.Recorder" }.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CloseFile("# RSBot closed");
    }

    /// <summary>
    ///     Fired (on any thread) when recording started or stopped.
    /// </summary>
    public event Action StateChanged;

    public bool IsRecording => _recording;

    public string CurrentFile { get; private set; }

    public long CurrentFileSize => Interlocked.Read(ref _size);

    #region Settings

    public int MaxFileMB
    {
        get => _maxFileMB;
        set => GlobalConfig.Set($"{ConfigKey}.MaxFileMB", _maxFileMB = value);
    }

    public int MaxFolderMB
    {
        get => _maxFolderMB;
        set => GlobalConfig.Set($"{ConfigKey}.MaxFolderMB", _maxFolderMB = value);
    }

    public int KeepDays
    {
        get => _keepDays;
        set => GlobalConfig.Set($"{ConfigKey}.KeepDays", _keepDays = value);
    }

    public bool RecordOnLaunch
    {
        get => _recordOnLaunch;
        set => GlobalConfig.Set($"{ConfigKey}.OnLaunch", _recordOnLaunch = value);
    }

    public bool RecordOnBotStart
    {
        get => _recordOnBotStart;
        set => GlobalConfig.Set($"{ConfigKey}.OnBotStart", _recordOnBotStart = value);
    }

    public bool IncludeHex
    {
        get => _includeHex;
        set => GlobalConfig.Set($"{ConfigKey}.IncludeHex", _includeHex = value);
    }

    public bool IncludeLog
    {
        get => _includeLog;
        set => GlobalConfig.Set($"{ConfigKey}.IncludeLog", _includeLog = value);
    }

    #endregion Settings

    public static string RootDirectory => Path.Combine(Kernel.BasePath, "User", "Logs", "Packets");

    public void Initialize()
    {
        EventManager.SubscribeEvent("OnAddLog", new Action<string, LogLevel>(OnAddLog));
        EventManager.SubscribeEvent("OnStartBot", OnStartBot);
        EventManager.SubscribeEvent("OnStopBot", () => WriteEvent("OnStopBot"));
        EventManager.SubscribeEvent("OnLoadCharacter", () => WriteEvent($"OnLoadCharacter {Game.Player?.Name}"));
        EventManager.SubscribeEvent("OnGatewayServerConntected", () => WriteEvent("OnGatewayServerConnected"));
        EventManager.SubscribeEvent("OnGatewayServerDisconnected", () => WriteEvent("OnGatewayServerDisconnected"));
        EventManager.SubscribeEvent("OnAgentServerConnected", () => WriteEvent("OnAgentServerConnected"));
        EventManager.SubscribeEvent("OnAgentServerDisconnected", () => WriteEvent("OnAgentServerDisconnected"));

        if (RecordOnLaunch)
            Start();
    }

    public void Start()
    {
        if (_recording)
            return;

        // The header is queued before any packet, so the file always starts with it.
        _queue.Add(new Entry(EntryKind.Open, DateTime.Now, null, Game.Player?.Name));
        _recording = true;

        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (!_recording)
            return;

        _recording = false;
        _queue.Add(new Entry(EntryKind.Close, DateTime.Now, null, "# Recording stopped"));

        StateChanged?.Invoke();
    }

    /// <summary>
    ///     Called on the network thread for every captured packet.
    /// </summary>
    public void OnCapture(PacketCapture capture)
    {
        if (!_recording || !_filter.Matches(capture))
            return;

        if (!_queue.TryAdd(new Entry(EntryKind.Packet, capture.Time, capture, null)))
            Interlocked.Increment(ref _dropped);
    }

    public void AddMarker(string text)
    {
        WriteLine("#MARK", text);
    }

    private void OnAddLog(string message, LogLevel level)
    {
        // Our own messages are skipped so a failing recorder can not feed itself.
        if (_includeLog && !message.StartsWith("[PacketAnalyzer]"))
            WriteLine("#LOG ", $"[{level}] {message}");
    }

    private void OnStartBot()
    {
        if (_recordOnBotStart)
            Start();

        WriteEvent("OnStartBot");
    }

    private void WriteEvent(string text)
    {
        WriteLine("#EVT ", text);
    }

    private void WriteLine(string tag, string text)
    {
        if (!_recording)
            return;

        var now = DateTime.Now;
        if (!_queue.TryAdd(new Entry(EntryKind.Text, now, null, $"{now:HH:mm:ss.fff} {tag} {text}")))
            Interlocked.Increment(ref _dropped);
    }

    #region Writer thread

    private void WriterLoop()
    {
        while (true)
        {
            try
            {
                if (_queue.TryTake(out var entry, 250))
                    Process(entry);
                else
                    lock (_streamLock)
                        _writer?.Flush();
            }
            catch (Exception e)
            {
                // Stop first, otherwise the log line below would be recorded and fail again.
                CloseFile("# Recording stopped after an error");
                if (_recording)
                {
                    _recording = false;
                    StateChanged?.Invoke();
                }

                Log.Warn($"[PacketAnalyzer] Packet recording stopped: {e.Message}");
            }
        }
    }

    private void Process(Entry entry)
    {
        lock (_streamLock)
        {
            switch (entry.Kind)
            {
                case EntryKind.Open:
                    CloseFileUnsafe("# Recording restarted");
                    OpenFileUnsafe(entry.Text);
                    return;

                case EntryKind.Close:
                    CloseFileUnsafe(entry.Text);
                    return;
            }

            if (_writer == null)
                return;

            if (entry.Time.Date != _lastDate)
            {
                _lastDate = entry.Time.Date;
                Write($"# Date: {_lastDate:yyyy-MM-dd}{Environment.NewLine}");
            }

            var dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
                Write($"# WARNING: {dropped} entries were dropped because the recorder could not keep up{Environment.NewLine}");

            Write(
                entry.Kind == EntryKind.Packet
                    ? PacketFormatter.Format(entry.Capture, _includeHex)
                    : entry.Text + Environment.NewLine
            );

            if (_size >= (long)Math.Max(1, _maxFileMB) * 1024 * 1024)
            {
                _part++;
                CloseFileUnsafe($"# Continued in part {_part}");
                OpenStreamUnsafe($"{_basePath}_part{_part}.log", $"# Continuation of {Path.GetFileName(_basePath)}.log");
            }
        }
    }

    private void Write(string text)
    {
        _writer.Write(text);
        Interlocked.Add(ref _size, text.Length);
    }

    private void OpenFileUnsafe(string characterName)
    {
        var folderName = string.IsNullOrWhiteSpace(characterName) ? "Environment" : characterName;
        foreach (var c in Path.GetInvalidFileNameChars())
            folderName = folderName.Replace(c, '_');

        var directory = Path.Combine(RootDirectory, folderName);
        Directory.CreateDirectory(directory);
        CleanupOldFiles();

        var now = DateTime.Now;
        _basePath = Path.Combine(directory, $"{now:yyyy-MM-dd_HH-mm-ss}");
        for (var i = 2; File.Exists($"{_basePath}.log"); i++)
            _basePath = Path.Combine(directory, $"{now:yyyy-MM-dd_HH-mm-ss}_{i}");
        _part = 1;

        var header = new StringBuilder()
            .AppendLine("# RSBot packet log")
            .AppendLine($"# Started:   {now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"# RSBot:     {Assembly.GetEntryAssembly()?.GetName().Version}")
            .AppendLine($"# Character: {folderName}")
            .AppendLine(
                "# Format:    HH:mm:ss.fff [G=gateway|A=agent] C->S|S->C RECV|BOT|REPL|DROP 0xOPCODE Name len=<payload bytes> [E]ncrypted [M]assive"
            )
            .AppendLine(
                "#            RECV = received and forwarded, BOT = sent by the bot, REPL = replaced by a hook, DROP = dropped by a hook"
            )
            .AppendLine("#            The hex dump of the payload (without the 6 byte header) follows each packet line.")
            .AppendLine("#            #EVT = bot event, #LOG = bot log line, #MARK = user marker")
            .ToString()
            .TrimEnd();

        OpenStreamUnsafe($"{_basePath}.log", header);
    }

    private void OpenStreamUnsafe(string path, string firstLine)
    {
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(false)
        );
        _lastDate = DateTime.Now.Date;
        Interlocked.Exchange(ref _size, 0);
        CurrentFile = path;

        Write(firstLine + Environment.NewLine);
        Write($"# Date: {_lastDate:yyyy-MM-dd}{Environment.NewLine}");
    }

    private void CloseFile(string lastLine)
    {
        lock (_streamLock)
            CloseFileUnsafe(lastLine);
    }

    private void CloseFileUnsafe(string lastLine)
    {
        if (_writer == null)
            return;

        try
        {
            _writer.WriteLine(lastLine);
            _writer.Dispose();
        }
        catch
        {
            // ignore, the file is being closed anyway
        }

        _writer = null;
    }

    /// <summary>
    ///     Removes logs older than <see cref="KeepDays" /> and the oldest logs while the folder is above <see cref="MaxFolderMB" />.
    /// </summary>
    private void CleanupOldFiles()
    {
        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(RootDirectory)
                .GetFiles("*.log", SearchOption.AllDirectories)
                .OrderBy(f => f.LastWriteTime)
                .ToArray();
        }
        catch (Exception e)
        {
            Log.Debug($"[PacketAnalyzer] Could not list old packet logs: {e.Message}");
            return;
        }

        var maxBytes = (long)Math.Max(1, _maxFolderMB) * 1024 * 1024;
        var expiry = _keepDays > 0 ? DateTime.Now.AddDays(-_keepDays) : DateTime.MinValue;
        var total = files.Sum(f => f.Length);

        foreach (var file in files)
        {
            if (file.LastWriteTime >= expiry && total <= maxBytes)
                break;

            var length = file.Length;

            try
            {
                file.Delete();
                total -= length;
            }
            catch
            {
                // still in use, e.g. by another bot instance
            }
        }
    }

    #endregion Writer thread

    private enum EntryKind : byte
    {
        Packet,
        Text,
        Open,
        Close,
    }

    private readonly record struct Entry(EntryKind Kind, DateTime Time, PacketCapture Capture, string Text);
}
