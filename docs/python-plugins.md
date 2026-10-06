# Python plugins

RSBot can run plugins written in Python. They live in `Build\Data\Python\Plugins\*.py` and are switched on and off in the **Python** tab, separately for each character. A plugin can read the game state, react to bot events and packets, add its own tab, and add commands to walk scripts.

The first build downloads Python 3.13 (embeddable, 32-bit) into `Build\Data\Python\PyRuntime`. No other Python installation is needed.

## A minimal plugin

```python
from RSBot import *

NAME = "Hello"
DESCRIPTION = "Says hello when the character dies"
VERSION = "1.0.0"

def on_player_died():
    log("I died at", get_position())
```

Save it in the plugins folder, press **Refresh list** in the Python tab, and tick it. After editing a running plugin, press **Reload enabled**.

The API is copied into the plugins folder as `RSBot.pyi` so that editors can autocomplete it. The bot always uses its own built-in copy. Don't name a plugin `RSBot.py`.

## Functions plugins can define

All of these are optional.

| Function | When it is called |
|---|---|
| `event_loop()` | Every 500 ms. A call is skipped while the previous one is still running |
| `on_enter_game()`, `on_disconnect()`, `on_teleported()` | Joining the game, losing the connection, after a teleport |
| `on_bot_started()`, `on_bot_stopped()` | The bot is started or stopped |
| `on_player_died()`, `on_kill()`, `on_level_up(level)` | |
| `on_party_changed(event, name)` | `event` is `"join"`, `"leave"`, `"update"` or `"dismiss"` |
| `on_chat(type, sender, message)` | A chat message is received (`CHAT_*` constants) |
| `on_packet_from_server(opcode, data)` / `on_packet_from_client(opcode, data)` | Only for opcodes registered with `register_packet()`. `data` is `bytes`. Return `False` to drop the packet |
| `on_unload()` | Before the plugin is switched off |
| `<name>(args)` | A walk script line `<name> arg1 arg2` (arguments separated by spaces) calls it with `['arg1', 'arg2']`. Return the milliseconds to wait, or `False` if it failed |

Events, the event loop and GUI handlers run one at a time on the bot's Python thread. Packet hooks run on the network thread and script commands on the script thread, so keep them short.

## API

| Area | Functions |
|---|---|
| Core | `log(*args)`, `get_version()`, `is_ingame()`, `start_bot()`, `stop_bot()`, `is_bot_running()` |
| Packets | `send_server(opcode, data=b"", encrypted=False)`, `send_client(...)`, `register_packet(opcode, "server" or "client")`, `unregister_packet(...)` |
| Character | `get_character()`: name, level, hp, max_hp, mp, max_mp, gold, exp, sp, x, y, region, dead, race. `get_position()`: x, y, z, region |
| Around you | `get_monsters()`, `get_players()`, `get_npcs()`: `{uid: {...}}` with name, servername, model, x, y, region, distance. Monsters also have type (rarity, `RARITY_*`), level, hp, max_hp, target, attacking_me. Players have guild |
| Party | `get_party()`: list of member_id, uid, name, level, guild, hp_percent, mp_percent, x, y, region. HP/MP come in steps of 10% |
| Items and skills | `get_inventory()`: slot, model, servername, name, quantity, plus, durability. `get_skills()`: id, servername, name, cooldown_ms. `get_active_buffs()`: id, servername, name, remaining_ms |
| Actions | `select_target(uid)`, `get_selected_target()`, `cast_skill(skill_id, target_uid=0)`, `use_item(slot)`, `use_return_scroll()`, `move_to(x, y, region=0)`, `chat(text, type=CHAT_ALL, receiver=None)` |
| Training area | `get_training_area()`: x, y, region, radius. `set_training_position(x, y, region=0, radius=None)` |
| Files | `get_config_dir()`: a folder for this character's plugin settings. `get_plugins_dir()` |

Coordinates are world coordinates; `region` is only needed in dungeons.

## GUI

```python
gui = GUI("My plugin")                       # adds a tab inside the Python tab
gui.Label("Text", x, y)
btn = gui.Button("Go", x, y, width, height, handler=on_go)          # handler()
chk = gui.CheckBox("Enabled", x, y, handler=on_toggle)              # handler(checked)
txt = gui.TextBox("", x, y, width, handler=on_text)                 # handler(text)
cmb = gui.ComboBox(x, y, width, handler=on_pick)                    # handler(index)
lst = gui.ListBox(x, y, width, height, handler=on_pick)             # handler(index)
```

Positions are pixels at 100% Windows scaling; they are scaled for the display. All controls have `set_text`, `get_text`, `set_enabled`, `set_visible` and `move`. Check boxes also have `is_checked` and `set_checked`. Lists and combo boxes have `add_item`, `remove_at`, `clear`, `get_items`, `item_count`, `get_item`, `selected_index`, `set_selected_index` and `get_selected_item`.

## Rewriting phBot plugins

phBot plugins use a different API (`phBot`, `QtBind`, `phBotChat`), so they have to be rewritten. `MobSelector.py`, a rewrite of JellyBitz's xMobSelector, is included as an example. The main changes:

| phBot | RSBot |
|---|---|
| `from phBot import *`, `import QtBind` | `from RSBot import *` |
| `QtBind.init(__name__, name)` | `gui = GUI(name)` |
| `QtBind.createButton(gui, 'func_name', text, x, y)` | `gui.Button(text, x, y, handler=func)` |
| `QtBind.createList(...)`, `QtBind.append(gui, lst, text)` | `lst = gui.ListBox(...)`, `lst.add_item(text)` |
| `inject_joymax(opcode, data, encrypted)` | `send_server(opcode, data, encrypted)` |
| `handle_joymax(opcode, data)` (every packet) | `register_packet(opcode, "server")` + `on_packet_from_server(opcode, data)` |
| `phBotChat.Party(text)` | `chat(text, CHAT_PARTY)` |
| `joined_game()` | `on_enter_game()` |

## Notes

- Plugins run with full rights on the PC. Only use plugins you trust.
- Each bot process runs its own Python; plugins of different bots don't share memory. Use files in `get_config_dir()` to keep settings.
