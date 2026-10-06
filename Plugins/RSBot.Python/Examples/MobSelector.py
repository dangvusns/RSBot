# Mob Selector - keeps a monster from your list selected.
# Rewrite of JellyBitz's phBot plugin xMobSelector for the RSBot Python API.
from RSBot import *
import json
import os

NAME = "Mob Selector"
DESCRIPTION = "Selects nearby monsters whose name and type are on your list"
AUTHOR = "RSBot (based on JellyBitz xMobSelector)"
VERSION = "1.0.0"

TYPE_NAMES = {
    RARITY_GENERAL: "General",
    RARITY_CHAMPION: "Champion",
    RARITY_UNIQUE: "Unique",
    RARITY_GIANT: "Giant",
    RARITY_TITAN: "Titan",
    RARITY_ELITE: "Elite",
    7: "Strong",
    RARITY_GENERAL_PARTY: "Party General",
    RARITY_CHAMPION_PARTY: "Party Champion",
    RARITY_GIANT_PARTY: "Party Giant",
}

nearby = []          # monsters shown in the left list
wanted = []          # [{"name": ..., "type": ...}]
selected_uid = 0
scanning = False
auto_refresh = False


def type_name(t):
    return TYPE_NAMES.get(t, "Unknown[%d]" % t)


def config_file():
    return os.path.join(get_config_dir(), "MobSelector.json")


def load_config():
    global wanted
    wanted = []
    lst_wanted.clear()
    try:
        with open(config_file(), "r", encoding="utf-8") as f:
            wanted = json.load(f).get("Mobs", [])
    except (OSError, ValueError):
        wanted = []
    for mob in wanted:
        lst_wanted.add_item("%s (%s)" % (mob["name"], type_name(mob["type"])))


def save_config():
    with open(config_file(), "w", encoding="utf-8") as f:
        json.dump({"Mobs": wanted}, f, indent=4, sort_keys=True)


def is_wanted(mob):
    return any(w["name"] == mob["name"] and w["type"] == mob["type"] for w in wanted)


# ---------------------------------------------------------------- buttons

def refresh_mobs():
    global nearby
    lst_nearby.clear()
    nearby = []
    for uid, mob in sorted(get_monsters().items(), key=lambda m: m[1]["distance"]):
        lst_nearby.add_item(
            "%s (%s) - HP %d/%d - %.0fm" % (mob["name"], type_name(mob["type"]), mob["hp"], mob["max_hp"], mob["distance"])
        )
        nearby.append(mob)


def add_mob():
    index = lst_nearby.selected_index()
    if 0 <= index < len(nearby):
        mob = nearby[index]
        if not is_wanted(mob):
            wanted.append({"name": mob["name"], "type": mob["type"]})
            lst_wanted.add_item("%s (%s)" % (mob["name"], type_name(mob["type"])))
            save_config()


def remove_mob():
    index = lst_wanted.selected_index()
    if 0 <= index < len(wanted):
        wanted.pop(index)
        lst_wanted.remove_at(index)
        save_config()


def toggle_scanner():
    global scanning, selected_uid
    scanning = not scanning
    selected_uid = 0
    btn_scan.set_text("Stop scanner" if scanning else "Start scanner")
    log("Scanner started" if scanning else "Scanner stopped")


def auto_refresh_changed(checked):
    global auto_refresh
    auto_refresh = checked


# ---------------------------------------------------------------- logic

def search_and_select():
    global selected_uid
    if not wanted:
        return

    mobs = get_monsters()

    # Keep the current target while it is still around
    if selected_uid in mobs:
        return
    selected_uid = 0

    candidates = [(uid, m) for uid, m in mobs.items() if is_wanted(m)]
    if candidates:
        uid, mob = min(candidates, key=lambda c: c[1]["distance"])
        select_target(uid)


def on_packet_from_server(opcode, data):
    # 0xB045: answer to a select request; byte 0 is 1 on success, followed by the uid
    global selected_uid
    if opcode == 0xB045 and scanning:
        if len(data) >= 5 and data[0] == 1:
            selected_uid = int.from_bytes(data[1:5], "little")
        else:
            selected_uid = 0
    return True


def event_loop():
    if not is_ingame():
        return
    if auto_refresh:
        refresh_mobs()
    if scanning:
        search_and_select()


def on_enter_game():
    load_config()


# ---------------------------------------------------------------- GUI

gui = GUI(NAME)

gui.Label("Monsters near you", 6, 10)
lst_nearby = gui.ListBox(6, 30, 510, 229)
gui.Button("Refresh", 6, 264, 80, 26, handler=refresh_mobs)
gui.CheckBox("Automatically", 92, 266, handler=auto_refresh_changed)
gui.Button("Add", 436, 264, 80, 26, handler=add_mob)

gui.Label("Select monsters from this list", 526, 10)
lst_wanted = gui.ListBox(526, 30, 220, 229)
gui.Button("Remove", 526, 264, 80, 26, handler=remove_mob)
btn_scan = gui.Button("Start scanner", 636, 264, 110, 26, handler=toggle_scanner)

register_packet(0xB045, "server")

if is_ingame():
    load_config()

log("Loaded. Pick monsters on the right and start the scanner.")
