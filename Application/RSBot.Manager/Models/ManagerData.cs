using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace RSBot.Manager.Models;

/// <summary>
///     Everything the manager saves in User/Manager/manager.json.
/// </summary>
public class ManagerData
{
    public List<ManagerAccount> Accounts { get; set; } = new();

    /// <summary>
    ///     Daily full experience hours of a character ("Giờ Xanh"), used when the server sends no fatigue time.
    /// </summary>
    public int GreenHours { get; set; } = 8;

    /// <summary>
    ///     Half experience hours after the green hours ("Cam").
    /// </summary>
    public int OrangeHours { get; set; } = 1;

    /// <summary>
    ///     When the daily hours start again, "HH:mm" in local time.
    /// </summary>
    public string ResetTime { get; set; } = "00:00";
}

public class ManagerAccount
{
    public string LoginId { get; set; }

    /// <summary>
    ///     The password encrypted with DPAPI for the current Windows user.
    /// </summary>
    public string PasswordProtected { get; set; }

    public string Character { get; set; }

    public string Server { get; set; }

    public string TemplateProfile { get; set; }

    /// <summary>
    ///     The RSBot profile of this account. One profile per account, named after the login id.
    /// </summary>
    [JsonIgnore]
    public string ProfileName => LoginId;

    /// <summary>
    ///     The password, or an empty string when it cannot be decrypted (see <see cref="CanReadPassword" />).
    /// </summary>
    [JsonIgnore]
    public string Password
    {
        get => TryReadPassword(out var password) ? password : string.Empty;
        set
        {
            var bytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value ?? string.Empty),
                null,
                DataProtectionScope.CurrentUser
            );

            PasswordProtected = Convert.ToBase64String(bytes);
        }
    }

    /// <summary>
    ///     False when the password was saved on another PC or by another Windows user: DPAPI only decrypts
    ///     for the user who encrypted it, so the password has to be entered again.
    /// </summary>
    [JsonIgnore]
    public bool CanReadPassword => TryReadPassword(out _);

    private bool TryReadPassword(out string password)
    {
        password = string.Empty;
        if (string.IsNullOrEmpty(PasswordProtected))
            return true;

        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(PasswordProtected),
                null,
                DataProtectionScope.CurrentUser
            );

            password = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }
}

/// <summary>
///     The answer of the "status" pipe command, see RSBot.ManagerLink StatusTracker.
/// </summary>
public class BotStatus
{
    public string State { get; set; }

    public string Profile { get; set; }

    public int ProcessId { get; set; }

    public string CharName { get; set; }

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public int Mp { get; set; }

    public int MaxMp { get; set; }

    public ulong Gold { get; set; }

    /// <summary>
    ///     The loot numbers of the bot's Statistics tab.
    /// </summary>
    public long GoldPicked { get; set; }

    public long ElixirsPicked { get; set; }

    public long TabletsPicked { get; set; }

    public long EquipmentPicked { get; set; }

    /// <summary>
    ///     Seconds since RSBot was opened.
    /// </summary>
    public long UptimeSeconds { get; set; }

    /// <summary>
    ///     Green (full experience) time left from the server's fatigue system, 0 in orange time,
    ///     null when the server sends no fatigue time.
    /// </summary>
    public long? GreenSecondsLeft { get; set; }

    /// <summary>
    ///     Seconds the character was in game since the daily reset, counted by the bot. Null when not in game.
    /// </summary>
    public long? PlayedSecondsToday { get; set; }

    public float PosX { get; set; }

    public float PosY { get; set; }

    public bool Clientless { get; set; }

    public bool ClientRunning { get; set; }
}
