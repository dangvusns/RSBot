using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Python.Components;

/// <summary>
///     What Python plugins can call (through RSBot.py). Only plain values cross the boundary:
///     numbers, strings, and JSON for structured data, so no Python objects are held by C#.
///     pythonnet releases the GIL while these methods run, so a call that waits (e.g. for a select
///     reply) does not block packet hooks or other Python code. They must not touch Python objects.
/// </summary>
public static class PythonBridge
{
    #region Core

    /// <param name="level">0 info, 1 warning, 2 error</param>
    public static void Log(string pluginKey, string message, int level)
    {
        var name = PythonPluginManager.GetDisplayName(pluginKey);
        var text = string.IsNullOrEmpty(name) ? $"[Python] {message}" : $"[Python] [{name}] {message}";

        Views.View.Instance.AppendLog(text);

        switch (level)
        {
            case 2:
                Core.Log.Error(text);
                break;

            case 1:
                Core.Log.Warn(text);
                break;

            default:
                Core.Log.Notify(text);
                break;
        }
    }

    public static string GetVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";
    }

    public static string GetClientType()
    {
        return Game.ClientType.ToString();
    }

    public static bool IsIngame()
    {
        return Game.Player != null && Kernel.Proxy != null && Kernel.Proxy.IsConnectedToAgentserver;
    }

    public static bool StartBot()
    {
        if (!IsIngame() || Kernel.Bot == null || Kernel.Bot.Running)
            return false;

        Kernel.Bot.Start();
        return true;
    }

    public static bool StopBot()
    {
        if (Kernel.Bot == null || !Kernel.Bot.Running)
            return false;

        Kernel.Bot.Stop();
        return true;
    }

    public static bool IsBotRunning()
    {
        return Kernel.Bot?.Running == true;
    }

    #endregion

    #region Packets

    public static void SendPacket(int opcode, string dataHex, bool encrypted, bool toServer)
    {
        var packet = new Packet((ushort)opcode, encrypted);
        if (!string.IsNullOrEmpty(dataHex))
            packet.WriteBytes(Convert.FromHexString(dataHex));

        PacketManager.SendPacket(packet, toServer ? PacketDestination.Server : PacketDestination.Client);
    }

    public static void RegisterPacket(string pluginKey, int opcode, bool fromServer)
    {
        PythonPluginManager.RegisterPacket(pluginKey, (ushort)opcode, fromServer);
    }

    public static void UnregisterPacket(string pluginKey, int opcode, bool fromServer)
    {
        PythonPluginManager.UnregisterPacket(pluginKey, (ushort)opcode, fromServer);
    }

    #endregion

    #region Game state

    public static string GetCharacter()
    {
        var player = Game.Player;
        if (player == null)
            return null;

        var position = player.Position;

        return Json(
            new Dictionary<string, object>
            {
                ["uid"] = player.UniqueId,
                ["name"] = player.Name,
                ["level"] = player.Level,
                ["hp"] = player.Health,
                ["max_hp"] = player.MaximumHealth,
                ["mp"] = player.Mana,
                ["max_mp"] = player.MaximumMana,
                ["gold"] = player.Gold,
                ["exp"] = player.Experience,
                ["sp"] = player.SkillPoints,
                ["x"] = position.X,
                ["y"] = position.Y,
                ["region"] = (ushort)position.Region,
                ["dead"] = player.State.LifeState == LifeState.Dead,
                ["race"] = player.Race.ToString(),
            }
        );
    }

    public static string GetPosition()
    {
        if (Game.Player == null)
            return null;

        var position = Game.Player.Position;

        return Json(
            new Dictionary<string, object>
            {
                ["x"] = position.X,
                ["y"] = position.Y,
                ["z"] = position.ZOffset,
                ["region"] = (ushort)position.Region,
            }
        );
    }

    public static string GetMonsters()
    {
        var result = new Dictionary<string, object>();
        if (!SpawnManager.TryGetEntities<SpawnedMonster>(out var monsters))
            return Json(result);

        foreach (var monster in monsters)
        {
            if (monster == null)
                continue;

            var info = Entity(monster);
            info["type"] = (int)monster.Rarity;
            info["level"] = monster.Record?.Level ?? 0;
            info["hp"] = monster.Health;
            info["max_hp"] = monster.MaxHealth;
            info["target"] = monster.TargetId;
            info["attacking_me"] = monster.AttackingPlayer;

            result[monster.UniqueId.ToString()] = info;
        }

        return Json(result);
    }

    public static string GetPlayers()
    {
        var result = new Dictionary<string, object>();
        if (!SpawnManager.TryGetEntities<SpawnedPlayer>(out var players))
            return Json(result);

        foreach (var player in players)
        {
            if (player == null)
                continue;

            var position = player.Position;
            result[player.UniqueId.ToString()] = new Dictionary<string, object>
            {
                ["name"] = player.Name,
                ["guild"] = player.Guild?.Name ?? string.Empty,
                ["x"] = position.X,
                ["y"] = position.Y,
                ["region"] = (ushort)position.Region,
                ["distance"] = Math.Round(player.DistanceToPlayer, 1),
            };
        }

        return Json(result);
    }

    public static string GetNpcs()
    {
        var result = new Dictionary<string, object>();
        if (!SpawnManager.TryGetEntities<SpawnedNpc>(npc => npc is not SpawnedMonster, out var npcs))
            return Json(result);

        foreach (var npc in npcs)
            if (npc != null)
                result[npc.UniqueId.ToString()] = Entity(npc);

        return Json(result);
    }

    public static string GetParty()
    {
        var result = new List<object>();
        var party = Game.Party;
        if (party?.Members == null)
            return Json(result);

        foreach (var member in party.Members.ToArray())
        {
            if (member == null)
                continue;

            var position = member.Position;
            result.Add(
                new Dictionary<string, object>
                {
                    ["member_id"] = member.MemberId,
                    ["uid"] = member.Player?.UniqueId ?? 0,
                    ["name"] = member.Name,
                    ["level"] = member.Level,
                    ["guild"] = member.Guild ?? string.Empty,
                    // The server sends HP and MP in tenths (0-10) packed in one byte
                    ["hp_percent"] = (member.HealthMana >> 4) * 10,
                    ["mp_percent"] = (member.HealthMana & 0x0F) * 10,
                    ["x"] = position.X,
                    ["y"] = position.Y,
                    ["region"] = (ushort)position.Region,
                }
            );
        }

        return Json(result);
    }

    public static string GetInventory()
    {
        var result = new List<object>();
        var inventory = Game.Player?.Inventory;
        if (inventory == null)
            return Json(result);

        foreach (var item in inventory.ToArray())
        {
            if (item?.Record == null)
                continue;

            result.Add(
                new Dictionary<string, object>
                {
                    ["slot"] = item.Slot,
                    ["model"] = item.ItemId,
                    ["servername"] = item.Record.CodeName,
                    ["name"] = item.Record.GetRealName(),
                    ["quantity"] = item.Amount,
                    ["plus"] = item.OptLevel,
                    ["durability"] = item.Durability,
                }
            );
        }

        return Json(result);
    }

    public static string GetSkills()
    {
        var result = new List<object>();
        var skills = Game.Player?.Skills?.KnownSkills;
        if (skills == null)
            return Json(result);

        foreach (var skill in skills.ToArray())
        {
            if (skill?.Record == null)
                continue;

            result.Add(
                new Dictionary<string, object>
                {
                    ["id"] = skill.Id,
                    ["servername"] = skill.Record.Basic_Code,
                    ["name"] = skill.Record.GetRealName(),
                    ["cooldown_ms"] = skill.HasCooldown ? skill.RemainingMilliseconds : 0,
                }
            );
        }

        return Json(result);
    }

    public static string GetActiveBuffs()
    {
        var result = new List<object>();
        var buffs = Game.Player?.State?.ActiveBuffs;
        if (buffs == null)
            return Json(result);

        foreach (var buff in buffs.ToArray())
        {
            if (buff?.Record == null)
                continue;

            result.Add(
                new Dictionary<string, object>
                {
                    ["id"] = buff.Id,
                    ["servername"] = buff.Record.Basic_Code,
                    ["name"] = buff.Record.GetRealName(),
                    ["remaining_ms"] = buff.RemainingMilliseconds,
                }
            );
        }

        return Json(result);
    }

    public static long GetSelectedTarget()
    {
        return Game.SelectedEntity?.UniqueId ?? 0;
    }

    public static string GetTrainingArea()
    {
        if (Game.Player == null || !PlayerConfig.Exists("RSBot.Area.Region"))
            return null;

        var position = new Position(
            PlayerConfig.Get<ushort>("RSBot.Area.Region"),
            PlayerConfig.Get<float>("RSBot.Area.X"),
            PlayerConfig.Get<float>("RSBot.Area.Y"),
            PlayerConfig.Get<float>("RSBot.Area.Z")
        );

        return Json(
            new Dictionary<string, object>
            {
                ["x"] = position.X,
                ["y"] = position.Y,
                ["region"] = (ushort)position.Region,
                ["radius"] = PlayerConfig.Get("RSBot.Area.Radius", 50),
            }
        );
    }

    #endregion

    #region Actions

    public static bool SelectTarget(long uniqueId)
    {
        return SpawnManager.TryGetEntity<SpawnedBionic>((uint)uniqueId, out var entity) && entity.TrySelect();
    }

    public static bool CastSkill(long skillId, long targetId)
    {
        var skill = Game.Player?.Skills?.GetSkillInfoById((uint)skillId);
        return skill != null && SkillManager.CastSkill(skill, (uint)targetId);
    }

    public static bool UseItem(int slot)
    {
        var item = Game.Player?.Inventory?.GetItemAt((byte)slot);
        return item != null && item.Use();
    }

    public static bool UseReturnScroll()
    {
        return Game.Player != null && Game.Player.UseReturnScroll();
    }

    public static bool MoveTo(double x, double y, int region)
    {
        if (Game.Player == null)
            return false;

        return Game.Player.MoveTo(new Position((float)x, (float)y, (ushort)region), false);
    }

    public static bool Chat(int type, string text, string receiver)
    {
        if (Game.Player == null || string.IsNullOrEmpty(text))
            return false;

        if ((ChatType)type == ChatType.Private)
        {
            if (string.IsNullOrEmpty(receiver))
                return false;

            ChatManager.SendPrivate(receiver, text);
            return true;
        }

        ChatManager.Send((ChatType)type, text);
        return true;
    }

    public static bool SetTrainingPosition(double x, double y, int region, int radius)
    {
        if (Game.Player == null)
            return false;

        var position = new Position((float)x, (float)y, (ushort)region);

        // Keep the height of the current spot when it is in the same region
        var z = Game.Player.Position.Region == position.Region ? Game.Player.Position.ZOffset : 0f;

        PlayerConfig.Set("RSBot.Area.Region", (ushort)position.Region);
        PlayerConfig.Set("RSBot.Area.X", position.XOffset);
        PlayerConfig.Set("RSBot.Area.Y", position.YOffset);
        PlayerConfig.Set("RSBot.Area.Z", z);
        if (radius > 0)
            PlayerConfig.Set("RSBot.Area.Radius", radius);

        EventManager.FireEvent("OnSetTrainingArea");
        return true;
    }

    #endregion

    #region Config

    public static string GetConfigDir()
    {
        var character = Game.Player?.Name ?? "_global";
        var directory = Path.Combine(Kernel.BasePath, "User", ProfileManager.SelectedProfile, "Python", character);
        Directory.CreateDirectory(directory);

        return directory + Path.DirectorySeparatorChar;
    }

    public static string GetPluginsDir()
    {
        return PythonPluginManager.PluginsDirectory + Path.DirectorySeparatorChar;
    }

    #endregion

    #region GUI

    public static void GuiPage(string pluginKey, string title)
    {
        PythonGui.CreatePage(pluginKey, title);
    }

    public static int GuiCreate(string pluginKey, string kind, string text, int x, int y, int width, int height)
    {
        return PythonGui.CreateControl(pluginKey, kind, text, x, y, width, height);
    }

    public static void GuiSet(int id, string property, string value)
    {
        PythonGui.Set(id, property, value);
    }

    public static string GuiGet(int id, string property)
    {
        return PythonGui.Get(id, property);
    }

    #endregion

    private static Dictionary<string, object> Entity(SpawnedBionic entity)
    {
        var position = entity.Position;

        return new Dictionary<string, object>
        {
            ["name"] = entity.Record?.GetRealName() ?? string.Empty,
            ["servername"] = entity.Record?.CodeName ?? string.Empty,
            ["model"] = entity.Id,
            ["x"] = position.X,
            ["y"] = position.Y,
            ["region"] = (ushort)position.Region,
            ["distance"] = Math.Round(entity.DistanceToPlayer, 1),
        };
    }

    private static string Json(object value)
    {
        return JsonSerializer.Serialize(value);
    }
}
