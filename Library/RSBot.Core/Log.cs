using System;
using System.IO;
using RSBot.Core.Components;
using RSBot.Core.Event;

namespace RSBot.Core;

public class Log
{
    /// <summary>
    ///     Replaces the format item in a specified string with the string
    ///     representation of a corresponding object in a specified array
    /// </summary>
    /// <param name="logLevel">The message level</param>
    /// <param name="format">The format</param>
    /// <param name="args">The args</param>
    public static void AppendFormat(LogLevel logLevel, string format, params object[] args)
    {
        EventManager.FireEvent("OnAddLog", string.Format(format, args), logLevel);
    }

    /// <summary>
    ///     Appends the given message to the log using the provided log level.
    /// </summary>
    /// <param name="logLevel"></param>
    /// <param name="message"></param>
    public static void Append(LogLevel logLevel, string message)
    {
        EventManager.FireEvent("OnAddLog", message, logLevel);
    }

    /// <summary>
    ///     Appends the specified message.
    /// </summary>
    /// <param name="obj">The message.</param>
    /// <param name="level">The level.</param>
    public static void Notify(object obj)
    {
        EventManager.FireEvent("OnAddLog", obj.ToString(), LogLevel.Notify);
    }

    /// <summary>
    ///     Appends the specified language key.
    /// </summary>
    /// <param name="obj">The message.</param>
    /// <param name="level">The level.</param>
    public static void NotifyLang(string key, params object[] args)
    {
        EventManager.FireEvent("OnAddLog", LanguageManager.GetLang(key, args), LogLevel.Notify);
    }

    /// <summary>
    ///     Append specified debug message
    /// </summary>
    /// <param name="obj">The message</param>
    public static void Debug(object obj)
    {
        EventManager.FireEvent("OnAddLog", obj.ToString(), LogLevel.Debug);
    }

    /// <summary>
    ///     Append specified Warning message
    /// </summary>
    /// <param name="obj">The message</param>
    public static void Warn(object obj)
    {
        EventManager.FireEvent("OnAddLog", obj.ToString(), LogLevel.Warning);
    }

    /// <summary>
    ///     Appends the specified language key.
    /// </summary>
    /// <param name="obj">The message.</param>
    /// <param name="level">The level.</param>
    public static void WarnLang(string key, params object[] args)
    {
        EventManager.FireEvent("OnAddLog", LanguageManager.GetLang(key, args), LogLevel.Warning);
    }

    /// <summary>
    ///     Append specified Error message
    /// </summary>
    /// <param name="obj">The message</param>
    public static void Error(object obj)
    {
        EventManager.FireEvent("OnAddLog", obj.ToString(), LogLevel.Error);
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
    public static void StatusLang(string key, params object[] args)
    {
        EventManager.FireEvent("OnChangeStatusText", LanguageManager.GetLang(key, args));
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
