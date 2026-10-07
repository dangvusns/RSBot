using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace RSBot.Core.Components;

/// <summary>
///     Writes log lines to <c>User/Logs/&lt;character&gt;/yyyy-MM-dd.txt</c> on a background thread.
///     The file stays open and is flushed in batches, so logging never waits on the disk.
/// </summary>
public static class LogFileWriter
{
    private const int QueueCapacity = 20000;
    private const int FlushIntervalMs = 250;
    private const int SettingsRefreshMs = 5000;

    private static readonly BlockingCollection<(DateTime Time, string Folder, LogLevel Level, string Message)> _queue =
        new(QueueCapacity);

    private static readonly object _startLock = new();
    private static readonly object _writeLock = new();
    private static Thread _thread;
    private static int _dropped;

    private static StreamWriter _writer;
    private static string _currentFolder;
    private static DateTime _currentDate;
    private static int _currentPart;

    private static long _settingsRefreshedAt = long.MinValue;
    private static LogLevel _minimumLevel = LogLevel.Warning;
    private static int _maxFileMB = 20;
    private static int _keepDays = 7;
    private static int _maxFolderMB = 500;

    /// <summary>
    ///     Gets the folder the log files are written to.
    /// </summary>
    public static string RootDirectory => Path.Combine(Kernel.BasePath, "User", "Logs");

    /// <summary>
    ///     Gets the lowest level written to the file. Debug is written in a debug environment.
    /// </summary>
    public static LogLevel MinimumLevel
    {
        get
        {
            RefreshSettings();
            return _minimumLevel;
        }
    }

    /// <summary>
    ///     Queues a log line when its level is written to the file.
    /// </summary>
    public static void Write(LogLevel level, string message)
    {
        if (!IsWritten(level))
            return;

        EnsureStarted();

        var folder = Game.Player?.Name ?? "Environment";
        if (!_queue.TryAdd((DateTime.Now, folder, level, message)))
            Interlocked.Increment(ref _dropped);
    }

    /// <summary>
    ///     Gets a value indicating whether lines of the given level are written to the file.
    /// </summary>
    public static bool IsWritten(LogLevel level)
    {
        return Rank(level) >= Rank(MinimumLevel);
    }

    /// <summary>
    ///     Re-reads the file settings on the next write, e.g. after they were changed in the UI.
    /// </summary>
    public static void ReloadSettings()
    {
        Interlocked.Exchange(ref _settingsRefreshedAt, long.MinValue);
    }

    private static int Rank(LogLevel level)
    {
        return level switch
        {
            LogLevel.Debug => 0,
            LogLevel.Notify => 1,
            LogLevel.Warning => 2,
            LogLevel.Error => 3,
            LogLevel.Fatal => 4,
            _ => 1,
        };
    }

    private static void RefreshSettings()
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _settingsRefreshedAt) < SettingsRefreshMs)
            return;

        Interlocked.Exchange(ref _settingsRefreshedAt, now);

        var configured = GlobalConfig.GetEnum("RSBot.Log.File.Level", LogLevel.Warning);
        _minimumLevel = Kernel.Debug ? LogLevel.Debug : configured;
        _maxFileMB = Math.Max(1, GlobalConfig.Get("RSBot.Log.File.MaxFileMB", 20));
        _keepDays = Math.Max(0, GlobalConfig.Get("RSBot.Log.File.KeepDays", 7));
        _maxFolderMB = Math.Max(1, GlobalConfig.Get("RSBot.Log.File.MaxFolderMB", 500));
    }

    private static void EnsureStarted()
    {
        if (_thread != null)
            return;

        lock (_startLock)
        {
            if (_thread != null)
                return;

            _thread = new Thread(WriterLoop) { IsBackground = true, Name = "Log.FileWriter" };
            _thread.Start();

            // Environment.Exit does not wait for background threads
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
        }
    }

    private static void WriterLoop()
    {
        CleanupOldFiles();

        while (true)
        {
            try
            {
                if (_queue.TryTake(out var entry, FlushIntervalMs))
                {
                    lock (_writeLock)
                    {
                        WriteEntry(entry);

                        // Drain what is already queued before flushing once
                        while (_queue.TryTake(out entry))
                            WriteEntry(entry);

                        _writer?.Flush();
                    }
                }
            }
            catch (Exception e)
            {
                // Do not log through Log, that would queue the failure again
                System.Diagnostics.Debug.WriteLine($"Log file could not be written: {e.Message}");
                lock (_writeLock)
                    CloseWriter();

                Thread.Sleep(1000);
            }
        }
    }

    private static void WriteEntry((DateTime Time, string Folder, LogLevel Level, string Message) entry)
    {
        EnsureWriter(entry.Time, entry.Folder);

        var dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
            _writer.WriteLine($"[{entry.Time:HH:mm:ss}]\t<Warning>\t{dropped} log lines were dropped, the log was too busy");

        _writer.WriteLine($"[{entry.Time:HH:mm:ss}]\t<{entry.Level}>\t{entry.Message}");
    }

    private static void EnsureWriter(DateTime time, string folder)
    {
        var date = time.Date;
        if (_writer != null && folder == _currentFolder && date == _currentDate)
        {
            if (_writer.BaseStream.Length < (long)_maxFileMB * 1024 * 1024)
                return;

            _currentPart++;
        }
        else
        {
            _currentPart = 0;
        }

        CloseWriter();

        if (date != _currentDate)
            CleanupOldFiles();

        _currentFolder = folder;
        _currentDate = date;

        var directory = Path.Combine(RootDirectory, SanitizeFolder(folder));
        Directory.CreateDirectory(directory);

        string path;
        do
        {
            var suffix = _currentPart == 0 ? string.Empty : $"_part{_currentPart}";
            path = Path.Combine(directory, $"{date:yyyy-MM-dd}{suffix}.txt");

            // Continue the existing file of today unless it is already full
            if (!File.Exists(path) || new FileInfo(path).Length < (long)_maxFileMB * 1024 * 1024)
                break;

            _currentPart++;
        } while (true);

        // Several bot processes may log for the same character
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream) { AutoFlush = false };
    }

    private static string SanitizeFolder(string folder)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            folder = folder.Replace(c, '_');

        return folder;
    }

    private static void CleanupOldFiles()
    {
        RefreshSettings();

        FileRetention.Cleanup(RootDirectory, "*.txt", _keepDays, _maxFolderMB);
        FileRetention.Cleanup(Path.Combine(Kernel.BasePath, "Data", "Logs", "Exceptions"), "*.txt", _keepDays, _maxFolderMB);
    }

    private static void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // the file is being closed anyway
        }

        _writer = null;
    }

    /// <summary>
    ///     Writes the queued lines now, e.g. when the process exits.
    /// </summary>
    public static void Flush()
    {
        try
        {
            lock (_writeLock)
            {
                while (_queue.TryTake(out var entry))
                    WriteEntry(entry);

                _writer?.Flush();
            }
        }
        catch
        {
            // the process is exiting
        }
    }
}
