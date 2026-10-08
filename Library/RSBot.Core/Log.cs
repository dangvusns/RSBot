using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using RSBot.Core.Components;
using RSBot.Core.Event;

namespace RSBot.Core;

public class Log
{
    /// <summary>
    ///     Identical lines within this window are counted instead of logged again.
    /// </summary>
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(2);

    private static readonly object _repeatLock = new();
    private static string _lastMessage;
    private static LogLevel _lastLevel;
    private static DateTime _lastTime;
    private static int _repeatCount;

    /// <summary>
    ///     Gets or sets a value indicating whether the Log tab shows debug lines.
    /// </summary>
    public static bool ShowDebug { get; set; }

    /// <summary>
    ///     Gets a value indicating whether debug lines are shown or written anywhere.
    ///     Debug calls return early when not, so their text is never built.
    /// </summary>
    public static bool IsDebugEnabled => ShowDebug || LogFileWriter.IsWritten(LogLevel.Debug);

    /// <summary>
    ///     Replaces the format item in a specified string with the string
    ///     representation of a corresponding object in a specified array
    /// </summary>
    /// <param name="logLevel">The message level</param>
    /// <param name="format">The format</param>
    /// <param name="args">The args</param>
    public static void AppendFormat(LogLevel logLevel, string format, params object[] args)
    {
        if (logLevel == LogLevel.Debug && !IsDebugEnabled)
            return;

        Write(logLevel, string.Format(format, args));
    }

    /// <summary>
    ///     Appends the given message to the log using the provided log level.
    /// </summary>
    /// <param name="logLevel"></param>
    /// <param name="message"></param>
    public static void Append(LogLevel logLevel, string message)
    {
        Write(logLevel, message);
    }

    /// <summary>
    ///     Appends the specified message.
    /// </summary>
    /// <param name="obj">The message.</param>
    /// <param name="level">The level.</param>
    public static void Notify(object obj)
    {
        Write(LogLevel.Notify, obj.ToString());
    }

    /// <summary>
    ///     Appends the specified language key.
    /// </summary>
    /// <param name="obj">The message.</param>
    /// <param name="level">The level.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NotifyLang(string key, params object[] args)
    {
        Write(LogLevel.Notify, LanguageManager.GetLangForAssembly(Assembly.GetCallingAssembly(), key, args));
    }

    /// <summary>
    ///     Append specified debug message
    /// </summary>
    /// <param name="obj">The message</param>
    public static void Debug(object obj)
    {
        if (!IsDebugEnabled)
            return;

        Write(LogLevel.Debug, obj.ToString());
    }

    /// <summary>
    ///     Append specified debug message. The message is only built when debug logging is enabled,
    ///     use this on hot paths with interpolated text.
    /// </summary>
    /// <param name="message">Builds the message</param>
    public static void Debug(Func<string> message)
    {
        if (!IsDebugEnabled)
            return;

        Write(LogLevel.Debug, message());
    }

    /// <summary>
    ///     Append specified Warning message
    /// </summary>
    /// <param name="obj">The message</param>
    public static void Warn(object obj)
    {
        Write(LogLevel.Warning, obj.ToString());
    }

    /// <summary>
    ///     Appends the specified language key.
    /// </summary>
    /// <param name="obj">The message.</param>
    /// <param name="level">The level.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void WarnLang(string key, params object[] args)
    {
        Write(LogLevel.Warning, LanguageManager.GetLangForAssembly(Assembly.GetCallingAssembly(), key, args));
    }

    /// <summary>
    ///     Append specified Error message
    /// </summary>
    /// <param name="obj">The message</param>
    public static void Error(object obj)
    {
        Write(LogLevel.Error, obj.ToString());
    }

    /// <summary>
    ///     Sends a line to the log file and the log views.
    /// </summary>
    private static void Write(LogLevel level, string message)
    {
        if (IsRepeat(level, message, out var summary))
            return;

        if (summary != null)
            Dispatch(summary.Value.Level, summary.Value.Message);

        Dispatch(level, message);
    }

    private static void Dispatch(LogLevel level, string message)
    {
        LogFileWriter.Write(level, message);
        EventManager.FireEvent("OnAddLog", message, level);
    }

    /// <summary>
    ///     Counts debug and warning lines that repeat within <see cref="RepeatWindow" /> instead of logging them,
    ///     e.g. the same failing operation retried by the bot. The count is logged with the next other line.
    /// </summary>
    private static bool IsRepeat(LogLevel level, string message, out (LogLevel Level, string Message)? summary)
    {
        summary = null;

        lock (_repeatLock)
        {
            var now = DateTime.Now;
            var repeatable = level is LogLevel.Debug or LogLevel.Warning;

            if (repeatable && level == _lastLevel && message == _lastMessage && now - _lastTime < RepeatWindow)
            {
                _repeatCount++;
                _lastTime = now;

                return true;
            }

            if (_repeatCount > 0)
                summary = (_lastLevel, $"(previous message repeated {_repeatCount} more times)");

            _lastMessage = message;
            _lastLevel = level;
            _lastTime = now;
            _repeatCount = 0;

            return false;
        }
    }

    /// <summary>
    ///     Change status message on ui
    /// </summary>
    /// <param name="obj">The message</param>
    public static void Status(object obj)
    {
        EventManager.FireEvent("OnChangeStatusText", obj.ToString());
    }

    /// <summary>
    ///     Change status message on ui by language key.
    /// </summary>
    /// <param name="obj">The message.</param>
    /// <param name="level">The level.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void StatusLang(string key, params object[] args)
    {
        EventManager.FireEvent("OnChangeStatusText", LanguageManager.GetLangForAssembly(Assembly.GetCallingAssembly(), key, args));
    }

    /// <summary>
    ///     Append specified fatal message
    /// </summary>
    /// <param name="obj">The message</param>
    public static void Fatal(Exception obj)
    {
        // A handler called through reflection is wrapped; its own exception tells what went wrong
        var cause = obj is System.Reflection.TargetInvocationException { InnerException: not null } ? obj.InnerException : obj;
        Warn($"{cause.GetType().Name}: {cause.Message}");

        var filePath = Path.Combine(Kernel.BasePath, "Data", "Logs", "Exceptions", $"{DateTime.Now:dd-MM-yyyy}.txt");

        // Several bot processes append to the same file
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Game.Player?.Name ?? "-"}] {obj}");

                return;
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(50 * attempt);
            }
        }
    }
}
