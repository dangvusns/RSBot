using System;
using System.IO;
using System.Linq;

namespace RSBot.Core.Components;

public class ProfileManager
{
    private const string ProfilesKey = "RSBot.Profiles";
    private const string SelectedProfileKey = "RSBot.SelectedProfile";
    private const string ShowProfileDialogKey = "RSBot.ShowProfileDialog";

    /// <summary>
    ///     The profile that always exists.
    /// </summary>
    public const string DefaultProfile = "Default";

    /// <summary>
    ///     Names that collide with files in the user folder.
    /// </summary>
    private static readonly string[] _reservedNames = { "Profiles", "Settings" };

    /// <summary>
    ///     The profile this process runs with. Kept in memory once chosen, so another RSBot window selecting a
    ///     different profile can not switch the config files of this one.
    /// </summary>
    private static string _currentProfile;

    /// <summary>
    ///     Gets the profiles. Read from disk every time, because other bot instances and the Manager add profiles
    ///     while this one runs. The default profile is always included.
    /// </summary>
    public static string[] Profiles
    {
        get
        {
            var profiles = ReadConfig().GetArray<string>(ProfilesKey, '|').Where(p => !string.IsNullOrWhiteSpace(p));

            return profiles.Prepend(DefaultProfile).Distinct(StringComparer.InvariantCultureIgnoreCase).ToArray();
        }
    }

    /// <summary>
    ///     If the selected profile loaded via program args <c>true</c>; otherwise <c>false</c>.
    /// </summary>
    public static bool IsProfileLoadedByArgs { get; set; }

    /// <summary>
    ///     The selected character
    /// </summary>
    public static string SelectedCharacter { get; set; }

    /// <summary>
    ///     The selected profile
    /// </summary>
    public static string SelectedProfile => _currentProfile ??= ReadConfig().Get(SelectedProfileKey, DefaultProfile);

    /// <summary>
    ///     Show the profile dialog <c>true</c>; otherwise <c>false</c>
    /// </summary>
    public static bool ShowProfileDialog
    {
        get => ReadConfig().Get(ShowProfileDialogKey, false);
        set => Config.Update(GetProfileConfigFileName(), config => config.Set(ShowProfileDialogKey, value));
    }

    /// <summary>
    ///     There have any value in the collection <c>true</c>; otherwise <c>false</c>
    /// </summary>
    public static bool Any()
    {
        return Profiles.Any();
    }

    /// <summary>
    ///     Set selected profile
    /// </summary>
    /// <param name="profile">The profile</param>
    public static bool SetSelectedProfile(string profile)
    {
        var existing = Profiles.FirstOrDefault(p => p.Equals(profile, StringComparison.InvariantCultureIgnoreCase));
        if (existing == null)
            return false;

        _currentProfile = existing;

        // A profile chosen by program args (e.g. by the Manager) is not remembered as the one to open next time.
        if (!IsProfileLoadedByArgs)
            Config.Update(GetProfileConfigFileName(), config => config.Set(SelectedProfileKey, existing));

        return true;
    }

    /// <summary>
    ///     Is profile exists <c>true</c>; otherwise <c>false</c>
    /// </summary>
    /// <param name="profile">The profile</param>
    public static bool ProfileExists(string profile)
    {
        return Profiles.Any(p => p.Equals(profile, StringComparison.InvariantCultureIgnoreCase));
    }

    /// <summary>
    ///     Create new profile and select it.
    /// </summary>
    /// <param name="profile">The profile</param>
    /// <param name="useAsBase">Copy the settings of the selected profile <c>true</c>; otherwise <c>false</c></param>
    /// <returns>Is created <c>true</c>; otherwise <c>false</c> (invalid or existing name)</returns>
    public static bool Add(string profile, bool useAsBase = false)
    {
        if (string.IsNullOrWhiteSpace(profile) || profile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        if (_reservedNames.Any(n => n.Equals(profile, StringComparison.InvariantCultureIgnoreCase)))
            return false;

        if (ProfileExists(profile))
            return false;

        // The directory must exist before the base profile's files are copied into it.
        Directory.CreateDirectory(GetProfileDirectory(profile));

        if (useAsBase)
            CopyOldProfileData(profile);

        Config.Update(
            GetProfileConfigFileName(),
            config =>
            {
                var profiles = config.GetArray<string>(ProfilesKey, '|').ToList();
                if (!profiles.Any(p => p.Equals(profile, StringComparison.InvariantCultureIgnoreCase)))
                    profiles.Add(profile);

                config.SetArray(ProfilesKey, profiles, "|");
            }
        );

        SetSelectedProfile(profile);

        return true;
    }

    /// <summary>
    ///     Remove the profile from the list. Its files are kept.
    /// </summary>
    /// <param name="profile">The profile</param>
    /// <returns>Is removed <c>true</c>; otherwise <c>false</c></returns>
    public static bool Remove(string profile)
    {
        if (profile == null || profile.Equals(DefaultProfile, StringComparison.InvariantCultureIgnoreCase))
            return false;

        var removed = false;

        Config.Update(
            GetProfileConfigFileName(),
            config =>
            {
                var profiles = config.GetArray<string>(ProfilesKey, '|').ToList();
                removed = profiles.RemoveAll(p => p.Equals(profile, StringComparison.InvariantCultureIgnoreCase)) > 0;

                config.SetArray(ProfilesKey, profiles, "|");
            }
        );

        return removed;
    }

    /// <summary>
    ///     Copies the old profile data to the new profile.
    /// </summary>
    /// <param name="profile">Name of the profile.</param>
    private static void CopyOldProfileData(string profile)
    {
        try
        {
            var oldProfileFilePath = GetProfileFile(SelectedProfile);
            var newProfileFilePath = GetProfileFile(profile);
            var oldAutoLoginFile = Path.Combine(GetProfileDirectory(SelectedProfile), "autologin.data");
            var newAutoLoginFile = Path.Combine(GetProfileDirectory(profile), "autologin.data");

            if (File.Exists(oldProfileFilePath) && !File.Exists(newProfileFilePath))
                File.Copy(oldProfileFilePath, newProfileFilePath);

            if (File.Exists(oldAutoLoginFile) && !File.Exists(newAutoLoginFile))
                File.Copy(oldAutoLoginFile, newAutoLoginFile);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not copy old profile data to the new profile: {ex.Message}");
        }
    }

    private static Config ReadConfig()
    {
        return new Config(GetProfileConfigFileName());
    }

    /// <summary>
    ///     Get profile config file name
    /// </summary>
    /// <returns></returns>
    public static string GetProfileConfigFileName()
    {
        return Path.Combine(Kernel.BasePath, "User", "Profiles.rs");
    }

    public static string GetProfileFile(string profileName)
    {
        return Path.Combine(Kernel.BasePath, "User", $"{profileName}.rs");
    }

    public static string GetProfileDirectory(string profileName)
    {
        return Path.Combine(Kernel.BasePath, "User", profileName);
    }
}
