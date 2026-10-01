using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RSBot.Core;
using RSBot.Core.Network.Protocol;
using RSBot.Manager.Models;

namespace RSBot.Manager.Services;

/// <summary>
///     Writes the RSBot profile of an account: Profiles.rs, User/&lt;profile&gt;.rs, the character config
///     and User/&lt;profile&gt;/autologin.data. Uses the same file formats as RSBot.Core and RSBot.General.
/// </summary>
public static class ProfileWriter
{
    private const string ProfilesKey = "RSBot.Profiles";
    private const string DefaultProfile = "Default";

    private static string UserDirectory => Path.Combine(ManagerStore.BotFolder, "User");

    private static string ProfilesFile => Path.Combine(UserDirectory, "Profiles.rs");

    private static string GetProfileFile(string profile) => Path.Combine(UserDirectory, profile + ".rs");

    private static string GetProfileDirectory(string profile) => Path.Combine(UserDirectory, profile);

    /// <summary>
    ///     The profiles that can be used as a template.
    /// </summary>
    public static string[] GetProfiles()
    {
        var profiles = new List<string>();

        if (File.Exists(GetProfileFile(DefaultProfile)))
            profiles.Add(DefaultProfile);

        if (File.Exists(ProfilesFile))
            profiles.AddRange(new Config(ProfilesFile).GetArray<string>(ProfilesKey, '|'));

        return profiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    ///     The server names saved in the autologin data of every profile, to suggest them in the add dialog.
    /// </summary>
    public static string[] GetKnownServers()
    {
        var servers = new List<string>();

        foreach (var profile in GetProfiles())
        {
            try
            {
                servers.AddRange(ReadAutologin(profile).Select(a => a.Servername));
            }
            catch
            {
                // a broken autologin file only means no suggestion from it
            }
        }

        return servers.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToArray();
    }

    /// <summary>
    ///     Creates or updates the RSBot profile of the account.
    /// </summary>
    public static void Write(ManagerAccount account)
    {
        var profile = account.ProfileName;
        var profileFile = GetProfileFile(profile);
        var profileDirectory = GetProfileDirectory(profile);

        Directory.CreateDirectory(profileDirectory);

        AddToProfileList(profile);
        CopyTemplate(account.TemplateProfile, profile, account.Character);

        var config = new Config(profileFile);
        config.Set("RSBot.General.AutoLoginAccountUsername", account.LoginId);
        config.Set("RSBot.General.EnableAutomatedLogin", true);
        config.Save();

        WriteAutologin(account);
    }

    /// <summary>
    ///     Removes the profile from Profiles.rs, and its files when <paramref name="deleteFiles" /> is set.
    /// </summary>
    public static void Remove(ManagerAccount account, bool deleteFiles)
    {
        var profile = account.ProfileName;

        if (File.Exists(ProfilesFile))
        {
            var config = new Config(ProfilesFile);
            var profiles = config
                .GetArray<string>(ProfilesKey, '|')
                .Where(p => !p.Equals(profile, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            config.SetArray(ProfilesKey, profiles, "|");
            config.Save();
        }

        if (!deleteFiles)
            return;

        if (File.Exists(GetProfileFile(profile)))
            File.Delete(GetProfileFile(profile));

        if (Directory.Exists(GetProfileDirectory(profile)))
            Directory.Delete(GetProfileDirectory(profile), true);
    }

    private static void AddToProfileList(string profile)
    {
        var config = new Config(ProfilesFile);
        var profiles = config.GetArray<string>(ProfilesKey, '|').ToList();

        if (profiles.Any(p => p.Equals(profile, StringComparison.OrdinalIgnoreCase)))
            return;

        profiles.Add(profile);
        config.SetArray(ProfilesKey, profiles, "|");
        config.Save();
    }

    /// <summary>
    ///     Copies the template settings once. Files that already exist are kept, so editing an account
    ///     does not reset settings made in RSBot afterwards.
    /// </summary>
    private static void CopyTemplate(string template, string profile, string character)
    {
        if (string.IsNullOrWhiteSpace(template) || template.Equals(profile, StringComparison.OrdinalIgnoreCase))
            return;

        var templateFile = GetProfileFile(template);
        var profileFile = GetProfileFile(profile);
        if (File.Exists(templateFile) && !File.Exists(profileFile))
            File.Copy(templateFile, profileFile);

        // The character config of the template (skills, items, training area) becomes the config of the new character
        var characterFile = Path.Combine(GetProfileDirectory(profile), character + ".rs");
        var templateDirectory = GetProfileDirectory(template);
        if (File.Exists(characterFile) || !Directory.Exists(templateDirectory))
            return;

        var templateCharacterFile = new DirectoryInfo(templateDirectory)
            .GetFiles("*.rs")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        templateCharacterFile?.CopyTo(characterFile);
    }

    private static void WriteAutologin(ManagerAccount account)
    {
        var accounts = new[]
        {
            new AutologinAccount
            {
                Username = account.LoginId,
                Password = account.Password,
                SecondaryPassword = string.Empty,
                Channel = 1,
                Servername = account.Server,
                SelectedCharacter = account.Character,
                Characters = new List<string> { account.Character },
            },
        };

        // Same encoding as RSBot.General Accounts.Save
        var buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(accounts));
        buffer = new Blowfish().Encode(buffer);

        File.WriteAllBytes(GetAutologinFile(account.ProfileName), buffer);
    }

    private static List<AutologinAccount> ReadAutologin(string profile)
    {
        var file = GetAutologinFile(profile);
        if (!File.Exists(file))
            return new List<AutologinAccount>();

        var buffer = File.ReadAllBytes(file);
        if (buffer.Length == 0)
            return new List<AutologinAccount>();

        buffer = new Blowfish().Decode(buffer);
        if (buffer == null)
            return new List<AutologinAccount>();

        var serialized = Encoding.UTF8.GetString(buffer).Trim('\0');

        return JsonSerializer.Deserialize<List<AutologinAccount>>(serialized) ?? new List<AutologinAccount>();
    }

    private static string GetAutologinFile(string profile) =>
        Path.Combine(GetProfileDirectory(profile), "autologin.data");

    /// <summary>
    ///     Same shape as RSBot.General.Models.Account.
    /// </summary>
    private sealed class AutologinAccount
    {
        public string Username { get; set; }

        public string Password { get; set; }

        public string SecondaryPassword { get; set; }

        public byte Channel { get; set; } = 1;

        public string Servername { get; set; }

        public string SelectedCharacter { get; set; }

        public List<string> Characters { get; set; }
    }
}
