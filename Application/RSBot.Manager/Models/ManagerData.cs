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

    [JsonIgnore]
    public string Password
    {
        get
        {
            if (string.IsNullOrEmpty(PasswordProtected))
                return string.Empty;

            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(PasswordProtected),
                null,
                DataProtectionScope.CurrentUser
            );

            return Encoding.UTF8.GetString(bytes);
        }
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

    public float PosX { get; set; }

    public float PosY { get; set; }

    public bool Clientless { get; set; }

    public bool ClientRunning { get; set; }
}
