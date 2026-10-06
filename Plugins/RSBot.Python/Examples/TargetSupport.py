"""RSBot port of JellyBitz's xTargetSupport v1.2.3.

Original: https://github.com/JellyBitz/phBot-xPlugins/blob/master/xTargetSupport.py
"""
from RSBot import *
from collections import deque
import json
import os
import struct
import time

NAME = "Target Support"
DESCRIPTION = "Selects enemies and attackers of party leaders"
AUTHOR = "RSBot (based on JellyBitz xTargetSupport)"
VERSION = "1.0.0"

# Packet layout matches ActionSkillCastResponse.cs. Older clients omit the
# byte before the skill ID; newer clients add a uint before the target UID.
_NO_ACTION_BYTE = {"Japanese_Old", "Thailand"}
_EXTRA_TARGET_UINT = {
    "Global", "Turkey", "VTC_Game", "Taiwan", "Korean", "RuSro", "Rigid"
}
_CLIENT_TYPES = _NO_ACTION_BYTE | _EXTRA_TARGET_UINT | {
    "Vietnam", "Taiwan_Old", "Vietnam193", "Vietnam274", "Chinese_Old",
    "Chinese", "Japanese"
}
_attacks = deque(maxlen=64)
_client_type = ""
_enabled = False
_defensive = False
_leaders = []


def _config_file():
    return os.path.join(get_config_dir(), "TargetSupport.json")


def _save_config():
    if not is_ingame():
        return
    try:
        path = _config_file()
        with open(path + ".tmp", "w", encoding="utf-8") as stream:
            json.dump({"Leaders": _leaders, "Defensive": _defensive}, stream,
                      indent=4, ensure_ascii=False)
        os.replace(path + ".tmp", path)
    except OSError as error:
        log("Could not save settings:", error)


def _set_enabled(enabled):
    global _enabled
    _enabled = bool(enabled)
    _attacks.clear()
    chk_enabled.set_checked(_enabled)


def _enabled_changed(checked):
    global _enabled
    _enabled = bool(checked)
    _attacks.clear()


def _defensive_changed(checked):
    global _defensive
    _defensive = bool(checked)
    _attacks.clear()
    _save_config()


def _load_config():
    global _leaders, _defensive, _client_type
    _set_enabled(False)
    _leaders = []
    _defensive = False
    _client_type = get_client_type()
    lst_leaders.clear()
    if is_ingame():
        try:
            with open(_config_file(), "r", encoding="utf-8") as stream:
                data = json.load(stream)
            if not isinstance(data, dict) or not isinstance(data.get("Leaders", []), list):
                raise ValueError("Settings must contain a Leaders list")
            for name in data.get("Leaders", []):
                if isinstance(name, str):
                    name = name.strip()
                    if name and not _is_leader(name):
                        _leaders.append(name)
            _defensive = data.get("Defensive") is True
        except FileNotFoundError:
            pass
        except (OSError, ValueError) as error:
            log("Could not load settings:", error)
    for name in _leaders:
        lst_leaders.add_item(name)
    chk_defensive.set_checked(_defensive)
    if _client_type not in _CLIENT_TYPES:
        log("Unsupported client type:", _client_type)


def _is_leader(name):
    return bool(name) and any(name.casefold() == leader.casefold() for leader in _leaders)


def _add_leader():
    if not is_ingame():
        log("Enter the game before editing leaders.")
        return
    name = txt_leader.get_text().strip()
    if name and not _is_leader(name):
        _leaders.append(name)
        lst_leaders.add_item(name)
        txt_leader.set_text("")
        _save_config()
        log("Leader added:", name)


def _remove_leader():
    if not is_ingame():
        return
    index = lst_leaders.selected_index()
    if 0 <= index < len(_leaders):
        name = _leaders.pop(index)
        lst_leaders.remove_at(index)
        _attacks.clear()
        _save_config()
        log("Leader removed:", name)


def _parse_attack(data, client_type):
    if client_type not in _CLIENT_TYPES or len(data) < 2 or data[0] != 1 or data[1] != 2:
        return None
    attacker_offset = 6 if client_type in _NO_ACTION_BYTE else 7
    target_offset = attacker_offset + 8
    if client_type in _EXTRA_TARGET_UINT:
        target_offset += 4
    if len(data) < target_offset + 4:
        return None
    attacker = struct.unpack_from("<I", data, attacker_offset)[0]
    target = struct.unpack_from("<I", data, target_offset)[0]
    return (attacker, target) if attacker and target else None


def on_packet_from_server(opcode, data):
    # No GUI access, game-state reads or waiting for responses on this thread.
    if opcode == 0xB070 and _enabled:
        attack = _parse_attack(data, _client_type)
        if attack:
            _attacks.append((time.monotonic(), attack))
    return True


def event_loop():
    if not _enabled or not is_ingame():
        _attacks.clear()
        return
    character = get_character()
    if not character or character.get("dead"):
        _attacks.clear()
        return
    names = {member["uid"]: member.get("name", "") for member in get_party() if member.get("uid")}
    own_uid = character.get("uid", 0)
    if own_uid:
        names[own_uid] = character.get("name", "")
    now = time.monotonic()
    choice = None
    # Prefer the latest relevant attack and send at most one request per tick.
    while _attacks:
        timestamp, (attacker, target) = _attacks.popleft()
        if now - timestamp > 2:
            continue
        if _is_leader(names.get(attacker, "")):
            choice = (target, "enemy of", names[attacker])
        elif _defensive and _is_leader(names.get(target, "")):
            choice = (attacker, "attacker of", names[target])
    if choice and choice[0] != own_uid and choice[0] != get_selected_target():
        # Same nonblocking select request as the original phBot plugin.
        # select_target() waits for a reply and must not hold up packet hooks.
        send_server(0x7045, struct.pack("<I", choice[0]), False)
        log("Target requested:", choice[1], choice[2])


def on_chat(type, sender, message):
    if is_ingame() and _is_leader(sender):
        if message == "TARGET ON":
            _set_enabled(True)
            log("Enabled by", sender)
        elif message == "TARGET OFF":
            _set_enabled(False)
            log("Disabled by", sender)


def on_enter_game():
    _load_config()


def on_teleported():
    global _client_type
    _attacks.clear()
    _client_type = get_client_type()


def on_disconnect():
    _set_enabled(False)


def on_unload():
    _attacks.clear()
    unregister_packet(0xB070, "server")


gui = GUI(NAME)
chk_enabled = gui.CheckBox("Enabled", 6, 10, 90, 24, handler=_enabled_changed)
chk_defensive = gui.CheckBox("Defensive mode", 105, 10, 160, 24, handler=_defensive_changed)
gui.Label("Party leaders", 6, 42)
txt_leader = gui.TextBox("", 6, 65, 180, 24)
gui.Button("Add", 195, 65, 80, 26, handler=_add_leader)
lst_leaders = gui.ListBox(6, 100, 269, 140)
gui.Button("Remove", 195, 246, 80, 26, handler=_remove_leader)
gui.Label("Selects enemies attacked by listed party leaders.", 295, 65)
gui.Label("Defensive mode also selects enemies attacking them.", 295, 90)
gui.Label("Leaders can send TARGET ON or TARGET OFF in chat.", 295, 125)
gui.Label("Enabled resets when you join the game or reload this plugin.", 295, 150)

register_packet(0xB070, "server")
if is_ingame():
    _load_config()
log("Loaded", NAME, "v" + VERSION)
