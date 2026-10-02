using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace RSBot.Core;

public class Config
{
    /// <summary>
    ///     The object that stores the configuration
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _config;

    /// <summary>
    ///     Gets the path.
    /// </summary>
    private readonly string _path;

    /// <summary>
    ///     Loads the specified file.
    /// </summary>
    /// <param name="file">The file.</param>
    public Config(string file)
    {
        _path = file;

        CheckPath();

        _config = new ConcurrentDictionary<string, string>();
        var malformed = 0;
        foreach (var line in File.ReadAllLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // A line without "{" used to throw and made the whole file fail to load; skip it instead
            var open = line.IndexOf('{');
            if (open < 0)
            {
                malformed++;
                continue;
            }

            var key = line.Substring(0, open);

            // Same result as before: the text up to the next "{", cut at the first "}"
            var value = line.Substring(open + 1);
            var nextOpen = value.IndexOf('{');
            if (nextOpen >= 0)
                value = value.Substring(0, nextOpen);

            var close = value.IndexOf('}');
            if (close >= 0)
                value = value.Substring(0, close);

            if (!_config.ContainsKey(key))
                _config.TryAdd(key, value);
        }

        if (malformed > 0)
            BackupMalformedFile(malformed);
    }

    /// <summary>
    ///     Keeps a copy of a file with broken lines, because the next save writes only the lines that could be read.
    /// </summary>
    /// <param name="malformed">The number of broken lines.</param>
    private void BackupMalformedFile(int malformed)
    {
        try
        {
            File.Copy(_path, _path + ".bkp", true);
            Log.Warn($"[Config] {malformed} broken line(s) ignored in {Path.GetFileName(_path)}, a copy was saved as .bkp");
        }
        catch (Exception e)
        {
            Log.Warn($"[Config] {malformed} broken line(s) ignored in {Path.GetFileName(_path)}: {e.Message}");
        }
    }

    /// <summary>
    ///     gets is loaded
    /// </summary>
    private bool _isLoaded => _config != null;

    /// <summary>
    ///     Existses the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns></returns>
    public bool Exists(string key)
    {
        if (!_isLoaded)
            return false;

        return _config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    ///     Gets the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The default value.</param>
    public T Get<T>(string key, T defaultValue = default)
    {
        if (!_isLoaded)
            return (T)Convert.ChangeType(false, typeof(T));

        if (!_config.ContainsKey(key))
        {
            Set(key, defaultValue);

            return defaultValue;
        }

        var value = _config[key];
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        return (T)Convert.ChangeType(value, typeof(T));
    }

    /// <summary>
    ///     Gets the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The default value.</param>
    public TEnum GetEnum<TEnum>(string key, TEnum defaultValue)
        where TEnum : struct
    {
        if (!_isLoaded)
            return default;

        if (!_config.TryGetValue(key, out var value))
        {
            Set(key, defaultValue);
            value = defaultValue.ToString();
        }

        TEnum result;
        if (!Enum.TryParse(value, out result))
            return default;

        return result;
    }

    /// <summary>
    ///     Sets the specified key inside the config.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public void Set<T>(string key, T value)
    {
        var setValue = value == null ? string.Empty : value.ToString();
        _config.AddOrUpdate(key, setValue, (k, v) => setValue);
    }

    /// <summary>
    ///     Check directories
    /// </summary>
    private void CheckPath()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        if (!File.Exists(_path))
            File.Create(_path).Dispose();
    }

    /// <summary>
    ///     Saves the specified file.
    /// </summary>
    /// <param name="file">The file.</param>
    public void Save()
    {
        if (!_isLoaded || string.IsNullOrWhiteSpace(_path))
            return;

        CheckPath();

        var serializedConfig = new string[_config.Count];
        var index = 0;

        foreach (var element in _config.OrderBy(c => c.Key))
        {
            serializedConfig[index] = element.Key + "{" + element.Value + "}";
            index++;
        }

        // Write a temporary file and swap it in, so a crash mid-save can not leave a half-written config.
        var tempPath = _path + ".tmp";
        File.WriteAllLines(tempPath, serializedConfig);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tempPath, _path, true);
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                // Another process may be reading the file right now.
                Thread.Sleep(50);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"[Config] Could not replace {Path.GetFileName(_path)} ({e.Message}), writing it directly");
                File.WriteAllLines(_path, serializedConfig);
                File.Delete(tempPath);
                return;
            }
        }
    }

    /// <summary>
    ///     Re-reads a config file, applies <paramref name="change" /> and saves it, while holding a lock shared by
    ///     all processes. Use it for files that several bot instances (and the Manager) write, like Profiles.rs,
    ///     so one process does not overwrite what another one just saved.
    /// </summary>
    /// <param name="path">The config file.</param>
    /// <param name="change">The change to apply.</param>
    public static void Update(string path, Action<Config> change)
    {
        var name = "RSBot.Config." + Convert.ToHexString(
            SHA1.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()))
        );

        using var mutex = new Mutex(false, name);
        var owned = false;

        try
        {
            try
            {
                owned = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                // The previous owner exited without releasing; the lock is ours now.
                owned = true;
            }

            if (!owned)
                Log.Warn($"[Config] Timed out waiting for {Path.GetFileName(path)}, saving anyway");

            var config = new Config(path);
            change(config);
            config.Save();
        }
        finally
        {
            if (owned)
                mutex.ReleaseMutex();
        }
    }

    /// <summary>
    ///     Sets the array.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <param name="delimiter">The delimiter.</param>
    public void SetArray<T>(string key, IEnumerable<T> values, string delimiter = ",")
    {
        if (values == null)
            return;

        Set(key, string.Join(delimiter, values));
    }

    /// <summary>
    ///     Gets the array.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="delimiter">The delimiter.</param>
    /// <returns></returns>
    public T[] GetArray<T>(
        string key,
        char delimiter = ',',
        StringSplitOptions options = StringSplitOptions.RemoveEmptyEntries
    )
    {
        if (!_isLoaded)
            return new T[] { };

        var data = Get<string>(key)?.Split(new[] { delimiter }, options);
        if (data == null || data.Length == 0)
            return new T[] { };

        return data?.Select(p => (T)Convert.ChangeType(p, typeof(T))).ToArray();
    }

    /// <summary>
    ///     Get array the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The default value.</param>
    public TEnum[] GetEnums<TEnum>(string key, char delimiter = ',')
        where TEnum : struct
    {
        if (!_isLoaded)
            return new TEnum[] { };

        var data = Get<string>(key)?.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries);
        if (data == null || data.Length == 0)
            return new TEnum[] { };

        return data?.Select(p => Enum.Parse<TEnum>(p)).ToArray();
    }
}
