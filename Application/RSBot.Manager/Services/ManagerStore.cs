using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using RSBot.Manager.Models;

namespace RSBot.Manager.Services;

/// <summary>
///     Loads and saves the bot folder setting and the manager data of that folder.
/// </summary>
public static class ManagerStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string SettingsFile =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RSBot.Manager",
            "settings.json"
        );

    /// <summary>
    ///     The folder that contains RSBot.exe.
    /// </summary>
    public static string BotFolder { get; private set; }

    public static ManagerData Data { get; private set; } = new();

    public static string BotExecutable => Path.Combine(BotFolder ?? string.Empty, "RSBot.exe");

    public static bool HasValidBotFolder => !string.IsNullOrEmpty(BotFolder) && File.Exists(BotExecutable);

    private static string DataFile => Path.Combine(BotFolder, "User", "Manager", "manager.json");

    /// <summary>
    ///     Reads the saved bot folder. Falls back to the manager's own folder when RSBot.exe is next to it.
    /// </summary>
    public static void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(SettingsFile));
                if (document.RootElement.TryGetProperty("BotFolder", out var folder))
                    BotFolder = folder.GetString();
            }
        }
        catch
        {
            BotFolder = null;
        }

        if (!HasValidBotFolder && File.Exists(Path.Combine(AppContext.BaseDirectory, "RSBot.exe")))
            BotFolder = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        LoadData();
    }

    public static void SetBotFolder(string folder)
    {
        BotFolder = folder;

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new { BotFolder = folder }, _jsonOptions));

        LoadData();
    }

    public static void LoadData()
    {
        Data = new ManagerData();

        if (!HasValidBotFolder || !File.Exists(DataFile))
            return;

        Data = JsonSerializer.Deserialize<ManagerData>(File.ReadAllText(DataFile), _jsonOptions) ?? new ManagerData();
    }

    public static void SaveData()
    {
        if (!HasValidBotFolder)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(DataFile));
        File.WriteAllText(DataFile, JsonSerializer.Serialize(Data, _jsonOptions));
    }
}
