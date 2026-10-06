"""RSBot Python plugin API.

Plugins live in Data/Python/Plugins/*.py and use it with ``from RSBot import *``.
Optional constants NAME, DESCRIPTION, AUTHOR and VERSION describe the plugin.

Functions a plugin can define (all optional):
    event_loop()                          every 500 ms
    on_enter_game(), on_disconnect(), on_teleported()
    on_bot_started(), on_bot_stopped()
    on_player_died(), on_kill(), on_level_up(level)
    on_party_changed(event, name)         event: "join", "leave", "update", "dismiss";
                                          name of the member ("" for "dismiss")
    on_chat(type, sender, message)        type: see CHAT_* constants
    on_packet_from_server(opcode, data)   only for opcodes registered with register_packet()
    on_packet_from_client(opcode, data)   return False to drop the packet
    on_unload()                           before the plugin is unloaded
    <name>(args)                          a walk script line "<name> arg1 arg2" (separated by
                                          spaces) calls it with ['arg1', 'arg2']; return the
                                          milliseconds to wait, or False if it failed
"""

import json as _json
import sys as _sys
import threading as _threading
import traceback as _traceback

from _rsbot_bridge import bridge as _b

CHAT_ALL = 1
CHAT_PRIVATE = 2
CHAT_PARTY = 4
CHAT_GUILD = 5
CHAT_GLOBAL = 6
CHAT_NOTICE = 7
CHAT_STALL = 9
CHAT_UNION = 11
CHAT_ACADEMY = 16

# Monster rarity values used in get_monsters()['type']
RARITY_GENERAL = 0
RARITY_CHAMPION = 1
RARITY_UNIQUE = 3
RARITY_GIANT = 4
RARITY_TITAN = 5
RARITY_ELITE = 6
RARITY_GENERAL_PARTY = 16
RARITY_CHAMPION_PARTY = 17
RARITY_GIANT_PARTY = 20

_plugins = {}          # key -> module
_state = _threading.local()   # .current: key of the plugin whose code runs on this thread
_gui_handlers = {}     # control id -> callable
_packet_regs = {}      # key -> set of (direction, opcode)


# ------------------------------------------------------------------ helpers

def _load_json(text):
    return _json.loads(text) if text else None


def _by_uid(text):
    data = _load_json(text) or {}
    return {int(k): v for k, v in data.items()}


def _get_current():
    return getattr(_state, "current", None)


def _set_current(key):
    previous = _get_current()
    _state.current = key
    return previous


def _plugin_name():
    _current = _get_current()
    if _current is None:
        raise RuntimeError("This function can only be used while a plugin is loading or handling an event")
    return _current


def _add_plugins_path():
    path = _b.GetPluginsDir().rstrip("\\/")
    if path not in _sys.path:
        _sys.path.insert(0, path)


def _report(key, where):
    _b.Log(key or "", "Error in %s:\n%s" % (where, _traceback.format_exc()), 2)


# ------------------------------------------------------------------ core

def log(*args):
    """Writes to the plugin log (Python tab) and the bot log."""
    _b.Log(_get_current() or "", " ".join(str(a) for a in args), 0)


def get_version():
    return _b.GetVersion()


def start_bot():
    return bool(_b.StartBot())


def stop_bot():
    return bool(_b.StopBot())


def is_bot_running():
    return bool(_b.IsBotRunning())


def is_ingame():
    return bool(_b.IsIngame())


# ------------------------------------------------------------------ packets

def send_server(opcode, data=b"", encrypted=False):
    """Sends a packet to the game server. data is bytes."""
    _b.SendPacket(int(opcode), bytes(data).hex(), bool(encrypted), True)


def send_client(opcode, data=b"", encrypted=False):
    """Sends a packet to the game client. data is bytes."""
    _b.SendPacket(int(opcode), bytes(data).hex(), bool(encrypted), False)


def register_packet(opcode, direction="server"):
    """Calls on_packet_from_server / on_packet_from_client for this opcode.
    direction is "server" (packets coming from the server) or "client"."""
    key = _plugin_name()
    from_server = _direction(direction)
    _packet_regs.setdefault(key, set()).add((from_server, int(opcode)))
    _b.RegisterPacket(key, int(opcode), from_server)


def unregister_packet(opcode, direction="server"):
    key = _plugin_name()
    from_server = _direction(direction)
    _packet_regs.get(key, set()).discard((from_server, int(opcode)))
    _b.UnregisterPacket(key, int(opcode), from_server)


def _direction(direction):
    if direction not in ("server", "client"):
        raise ValueError('direction must be "server" or "client"')
    return direction == "server"


# ------------------------------------------------------------------ game state

def get_character():
    """dict: name, level, hp, max_hp, mp, max_mp, gold, exp, sp, x, y, region, dead, race.
    None when not in game."""
    return _load_json(_b.GetCharacter())


def get_position():
    """dict: x, y, z, region. None when not in game."""
    return _load_json(_b.GetPosition())


def get_monsters():
    """{uid: {name, servername, model, type, level, hp, max_hp, x, y, region,
    distance, target, attacking_me}}"""
    return _by_uid(_b.GetMonsters())


def get_players():
    """{uid: {name, guild, x, y, region, distance}}"""
    return _by_uid(_b.GetPlayers())


def get_npcs():
    """{uid: {name, servername, model, x, y, region, distance}}"""
    return _by_uid(_b.GetNpcs())


def get_party():
    """list of {member_id, uid, name, level, guild, hp_percent, mp_percent, x, y, region}"""
    return _load_json(_b.GetParty()) or []


def get_inventory():
    """list of {slot, model, servername, name, quantity, plus, durability}; slots 0-12 are equipment."""
    return _load_json(_b.GetInventory()) or []


def get_skills():
    """list of {id, servername, name, cooldown_ms}"""
    return _load_json(_b.GetSkills()) or []


def get_active_buffs():
    """list of {id, servername, name, remaining_ms}"""
    return _load_json(_b.GetActiveBuffs()) or []


def get_selected_target():
    """uid of the selected entity, or 0."""
    return int(_b.GetSelectedTarget())


def get_training_area():
    """dict: x, y, region, radius. None if not set."""
    return _load_json(_b.GetTrainingArea())


# ------------------------------------------------------------------ actions

def select_target(uid):
    """Selects an entity. Returns True if the server confirmed it."""
    return bool(_b.SelectTarget(int(uid)))


def cast_skill(skill_id, target_uid=0):
    return bool(_b.CastSkill(int(skill_id), int(target_uid)))


def use_item(slot):
    return bool(_b.UseItem(int(slot)))


def use_return_scroll():
    return bool(_b.UseReturnScroll())


def move_to(x, y, region=0):
    """Walks to world coordinates. region is only needed in dungeons."""
    return bool(_b.MoveTo(float(x), float(y), int(region)))


def chat(text, type=CHAT_ALL, receiver=None):
    """Sends a chat message. receiver is required for CHAT_PRIVATE."""
    return bool(_b.Chat(int(type), str(text), receiver or ""))


def set_training_position(x, y, region=0, radius=None):
    """Sets the training area center (world coordinates) and optionally its radius."""
    return bool(_b.SetTrainingPosition(float(x), float(y), int(region), -1 if radius is None else int(radius)))


# ------------------------------------------------------------------ config

def get_config_dir():
    """Folder for this character's plugin settings (created if missing)."""
    return _b.GetConfigDir()


def get_plugins_dir():
    return _b.GetPluginsDir()


# ------------------------------------------------------------------ GUI

class _Control:
    def __init__(self, cid):
        self._id = cid

    def set_text(self, text):
        _b.GuiSet(self._id, "text", str(text))

    def get_text(self):
        return _b.GuiGet(self._id, "text")

    def set_enabled(self, enabled):
        _b.GuiSet(self._id, "enabled", "1" if enabled else "0")

    def set_visible(self, visible):
        _b.GuiSet(self._id, "visible", "1" if visible else "0")

    def move(self, x, y):
        _b.GuiSet(self._id, "position", "%d,%d" % (x, y))


class _CheckBox(_Control):
    def is_checked(self):
        return _b.GuiGet(self._id, "checked") == "1"

    def set_checked(self, checked):
        _b.GuiSet(self._id, "checked", "1" if checked else "0")


class _ListControl(_Control):
    """ListBox and ComboBox."""

    def add_item(self, text):
        _b.GuiSet(self._id, "add", str(text))

    def remove_at(self, index):
        _b.GuiSet(self._id, "remove_at", str(int(index)))

    def clear(self):
        _b.GuiSet(self._id, "clear", "")

    def get_items(self):
        return _json.loads(_b.GuiGet(self._id, "items"))

    def item_count(self):
        return len(self.get_items())

    def get_item(self, index):
        return self.get_items()[index]

    def selected_index(self):
        return int(_b.GuiGet(self._id, "selected_index"))

    def set_selected_index(self, index):
        _b.GuiSet(self._id, "selected_index", str(int(index)))

    def get_selected_item(self):
        index = self.selected_index()
        items = self.get_items()
        return items[index] if 0 <= index < len(items) else None


class GUI:
    """A tab for the plugin inside the Python tab. Positions are in pixels at 100% scaling."""

    def __init__(self, title=None):
        self._key = _plugin_name()
        _b.GuiPage(self._key, str(title or self._key))

    def _create(self, kind, text, x, y, width, height, handler):
        cid = _b.GuiCreate(self._key, kind, str(text), int(x), int(y), int(width or 0), int(height or 0))
        if handler is not None:
            _gui_handlers[cid] = (self._key, handler)
        return cid

    def Label(self, text, x, y, width=None, height=None):
        return _Control(self._create("label", text, x, y, width, height, None))

    def Button(self, text, x, y, width=None, height=None, handler=None):
        """handler() is called on click."""
        return _Control(self._create("button", text, x, y, width, height, handler))

    def CheckBox(self, text, x, y, width=None, height=None, handler=None):
        """handler(checked) is called when it changes."""
        return _CheckBox(self._create("checkbox", text, x, y, width, height, handler))

    def TextBox(self, text, x, y, width=None, height=None, handler=None):
        """handler(text) is called when the text changes."""
        return _Control(self._create("textbox", text, x, y, width, height, handler))

    def ComboBox(self, x, y, width=None, height=None, handler=None):
        """handler(index) is called when the selection changes."""
        return _ListControl(self._create("combobox", "", x, y, width, height, handler))

    def ListBox(self, x, y, width=None, height=None, handler=None):
        """handler(index) is called when the selection changes."""
        return _ListControl(self._create("listbox", "", x, y, width, height, handler))


# ------------------------------------------------------------------ host side (called by RSBot)

def _load(key, path):
    import importlib.util
    spec = importlib.util.spec_from_file_location(key, path)
    module = importlib.util.module_from_spec(spec)
    previous = _set_current(key)
    try:
        _plugins[key] = module
        _sys.modules[key] = module
        spec.loader.exec_module(module)
        return True
    except Exception:
        _report(key, "loading")
        _unload(key)
        return False
    finally:
        _set_current(previous)


def _unload(key):
    _call(key, "on_unload", "[]")
    _plugins.pop(key, None)
    _sys.modules.pop(key, None)
    _packet_regs.pop(key, None)
    for cid in [c for c, (k, _) in _gui_handlers.items() if k == key]:
        del _gui_handlers[cid]


def _has(name):
    return any(_own_function(m, name) for m in _plugins.values())


def _own_function(module, name):
    """A function the plugin defines itself (not one imported with "from RSBot import *")."""
    func = getattr(module, name, None) if module else None
    if callable(func) and getattr(func, "__module__", None) == module.__name__:
        return func
    return None


def _call(key, name, args_json):
    func = _own_function(_plugins.get(key), name)
    if func is None:
        return None
    previous = _set_current(key)
    try:
        return func(*_json.loads(args_json))
    except Exception:
        _report(key, name)
        return None
    finally:
        _set_current(previous)


def _call_all(name, args_json):
    for key in list(_plugins):
        _call(key, name, args_json)


def _packet(key, from_server, opcode, data_hex):
    name = "on_packet_from_server" if from_server else "on_packet_from_client"
    func = _own_function(_plugins.get(key), name)
    if func is None:
        return True
    previous = _set_current(key)
    try:
        return func(opcode, bytes.fromhex(data_hex)) is not False
    except Exception:
        _report(key, name)
        return True
    finally:
        _set_current(previous)


def _script(name, args_json):
    """-1: no plugin has this command, -2: failed, otherwise milliseconds to wait."""
    # Event handlers are not script commands
    if name.startswith("_") or name.startswith("on_") or name == "event_loop":
        return -1
    for key, module in list(_plugins.items()):
        if _own_function(module, name):
            result = _call(key, name, _json.dumps([_json.loads(args_json)]))
            if result is False:
                return -2
            try:
                return max(0, int(result or 0))
            except (TypeError, ValueError):
                return 0
    return -1


def _gui_event(cid, value_json):
    entry = _gui_handlers.get(cid)
    if not entry:
        return
    key, handler = entry
    previous = _set_current(key)
    try:
        value = _json.loads(value_json)
        if value is None:
            handler()
        else:
            handler(value)
    except Exception:
        _report(key, "GUI handler")
    finally:
        _set_current(previous)


_add_plugins_path()
