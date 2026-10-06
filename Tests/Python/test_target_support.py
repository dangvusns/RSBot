"""Exercise the port with a fake RSBot API; no game or CLR required."""
import importlib.util
import json
from pathlib import Path
import struct
import sys
import tempfile
import types
import unittest
from unittest.mock import patch


PLUGIN = Path(__file__).resolve().parents[2] / "Plugins/RSBot.Python/Examples/TargetSupport.py"


class Control:
    def __init__(self, text=""):
        self.text = text
        self.checked = False
        self.items = []
        self.index = -1

    def set_checked(self, checked):
        self.checked = checked

    def set_text(self, text):
        self.text = text

    def get_text(self):
        return self.text

    def clear(self):
        self.items.clear()

    def add_item(self, text):
        self.items.append(text)

    def remove_at(self, index):
        self.items.pop(index)

    def selected_index(self):
        return self.index


class Gui:
    def __init__(self, name):
        pass

    def Label(self, text, *args, **kwargs):
        return Control(text)

    CheckBox = Label
    TextBox = Label
    Button = Label

    def ListBox(self, *args, **kwargs):
        return Control()


def attack_packet(attacker=100, target=200, prefix_byte=True, extra_uint=False, action=2):
    # result, action, optional marker, skill, executor, action ID,
    # optional unknown ID, target, action flag
    data = bytes([1, action]) + (b"\x30" if prefix_byte else b"")
    data += struct.pack("<III", 12345, attacker, 77)
    if extra_uint:
        data += struct.pack("<I", 999)
    return data + struct.pack("<I", target) + b"\x00"


class TargetSupportTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.directory = Path(directory.name)
        self.sent = []
        self.logs = []
        self.registrations = []
        self.ingame = True
        self.character = {"uid": 1, "name": "Follower", "dead": False}
        self.party = [{"uid": 100, "name": "Leader"}]
        self.selected = 0
        self.client = "Vietnam"
        api = types.ModuleType("RSBot")
        api.GUI = Gui
        api.log = lambda *args: self.logs.append(args)
        api.is_ingame = lambda: self.ingame
        api.get_character = lambda: self.character
        api.get_party = lambda: self.party
        api.get_selected_target = lambda: self.selected
        api.get_client_type = lambda: self.client
        api.get_config_dir = lambda: str(self.directory)
        api.send_server = lambda *args: self.sent.append(args)
        api.register_packet = lambda *args: self.registrations.append(args)
        api.unregister_packet = lambda *args: self.registrations.remove(args)
        with patch.dict(sys.modules, {"RSBot": api}):
            spec = importlib.util.spec_from_file_location("TargetSupport", PLUGIN)
            self.plugin = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(self.plugin)
        self.plugin._leaders = ["Leader"]
        self.plugin.lst_leaders.add_item("Leader")
        self.plugin._set_enabled(True)

    def queue(self, **kwargs):
        self.assertTrue(self.plugin.on_packet_from_server(0xB070, attack_packet(**kwargs)))

    def assert_target(self, uid):
        self.assertEqual([(0x7045, struct.pack("<I", uid), False)], self.sent)

    def test_vsro_attack_is_deferred_until_event_loop(self):
        # The hook must not read GUI/game state or send packets on the network thread.
        with patch.object(self.plugin, "get_party", side_effect=AssertionError("Network-thread API access")), \
             patch.object(self.plugin, "send_server", side_effect=AssertionError("Network-thread send")), \
             patch.object(self.plugin, "get_character", side_effect=AssertionError("Network-thread API access")):
            self.queue()
        self.assertEqual([], self.sent)
        self.plugin.event_loop()
        self.assert_target(200)

    def test_parser_matches_all_core_client_layouts(self):
        layouts = {
            "Japanese_Old": (False, False), "Thailand": (False, False),
            "Vietnam": (True, False), "Taiwan_Old": (True, False),
            "Vietnam193": (True, False), "Vietnam274": (True, False),
            "Chinese_Old": (True, False), "Chinese": (True, False),
            "Global": (True, True), "Turkey": (True, True),
            "VTC_Game": (True, True), "Taiwan": (True, True),
            "Korean": (True, True), "Japanese": (True, False),
            "RuSro": (True, True), "Rigid": (True, True),
        }
        for client, (prefix, extra) in layouts.items():
            with self.subTest(client=client):
                data = attack_packet(prefix_byte=prefix, extra_uint=extra)
                self.assertEqual((100, 200), self.plugin._parse_attack(data, client))

    def test_truncated_failed_and_nonattack_packets_are_ignored(self):
        packet = attack_packet()
        for size in range(19):
            self.assertTrue(self.plugin.on_packet_from_server(0xB070, packet[:size]))
        self.plugin.on_packet_from_server(0xB070, b"\x00\x05")
        self.queue(action=1)
        self.plugin.on_packet_from_server(0xB045, packet)
        self.plugin.event_loop()
        self.assertEqual([], self.sent)
        self.assertIsNone(self.plugin._parse_attack(packet, "Unknown"))

    def test_defensive_mode_selects_the_leaders_attacker(self):
        self.plugin._defensive = True
        self.queue(attacker=200, target=100)
        self.plugin.event_loop()
        self.assert_target(200)

    def test_defense_is_optional_and_unlisted_party_members_do_not_lead(self):
        self.party.append({"uid": 300, "name": "Other"})
        self.queue(attacker=200, target=100)
        self.queue(attacker=300)
        self.plugin.event_loop()
        self.assertEqual([], self.sent)

    def test_names_match_case_insensitively(self):
        self.party[0]["name"] = "LEADER"
        self.queue()
        self.plugin.event_loop()
        self.assert_target(200)

    def test_leader_chat_controls_support_only_with_exact_commands(self):
        self.plugin.on_chat(4, "Stranger", "TARGET OFF")
        self.assertTrue(self.plugin._enabled)
        self.plugin.on_chat(4, "leader", "TARGET OFF")
        self.assertFalse(self.plugin._enabled)
        self.plugin.on_chat(4, "Leader", "TARGET ON extra")
        self.assertFalse(self.plugin._enabled)
        self.plugin.on_chat(4, "LEADER", "TARGET ON")
        self.assertTrue(self.plugin._enabled)

    def test_latest_relevant_attack_wins_with_one_request(self):
        self.queue(target=200)
        self.queue(target=201)
        self.queue(attacker=300, target=202)
        self.plugin.event_loop()
        self.assert_target(201)

    def test_already_selected_target_is_not_requested_again(self):
        self.selected = 200
        self.queue()
        self.plugin.event_loop()
        self.assertEqual([], self.sent)

    def test_own_character_can_be_a_leader_but_is_never_selected(self):
        self.plugin._leaders.append("Follower")
        self.queue(attacker=1)
        self.plugin.event_loop()
        self.assert_target(200)
        self.sent.clear()
        self.queue(target=1)
        self.plugin.event_loop()
        self.assertEqual([], self.sent)

    def test_dead_character_drops_pending_attacks(self):
        self.queue()
        self.character["dead"] = True
        self.plugin.event_loop()
        self.assertEqual([], self.sent)
        self.assertEqual(0, len(self.plugin._attacks))

    def test_stale_attacks_are_dropped(self):
        with patch.object(self.plugin.time, "monotonic", return_value=10):
            self.queue()
        with patch.object(self.plugin.time, "monotonic", return_value=13):
            self.plugin.event_loop()
        self.assertEqual([], self.sent)

    def test_teleport_refreshes_layout_and_clears_pending_attacks(self):
        self.queue()
        self.client = "Global"
        self.plugin.on_teleported()
        self.assertEqual("Global", self.plugin._client_type)
        self.assertTrue(self.plugin._enabled)
        self.assertEqual(0, len(self.plugin._attacks))

    def test_disconnect_disables_support_and_unload_unregisters_hook(self):
        self.queue()
        self.plugin.on_disconnect()
        self.assertFalse(self.plugin._enabled)
        self.assertFalse(self.plugin.chk_enabled.checked)
        self.assertEqual(0, len(self.plugin._attacks))
        self.plugin.on_unload()
        self.assertEqual([], self.registrations)

    def test_settings_round_trip_but_enabled_is_not_persisted(self):
        self.plugin._defensive_changed(True)
        self.plugin._load_config()
        self.assertEqual(["Leader"], self.plugin._leaders)
        self.assertTrue(self.plugin._defensive)
        self.assertTrue(self.plugin.chk_defensive.checked)
        self.assertFalse(self.plugin._enabled)
        self.assertEqual({"Leaders": ["Leader"], "Defensive": True},
                         json.loads((self.directory / "TargetSupport.json").read_text()))

    def test_add_trims_names_and_prevents_case_duplicates_then_remove_saves(self):
        self.plugin.txt_leader.text = "  LEADER  "
        self.plugin._add_leader()
        self.assertEqual(["Leader"], self.plugin._leaders)
        self.plugin.txt_leader.text = "  Second  "
        self.plugin._add_leader()
        self.assertEqual(["Leader", "Second"], self.plugin._leaders)
        self.plugin.lst_leaders.index = 0
        self.plugin._remove_leader()
        self.assertEqual(["Second"], self.plugin._leaders)
        self.assertEqual(["Second"], json.loads(
            (self.directory / "TargetSupport.json").read_text())["Leaders"])

    def test_invalid_settings_are_logged_without_crashing_load(self):
        path = self.directory / "TargetSupport.json"
        for contents in ("{", "null", '{"Leaders": 42}'):
            with self.subTest(contents=contents):
                path.write_text(contents)
                self.plugin._load_config()
                self.assertEqual([], self.plugin._leaders)
                self.assertFalse(self.plugin._enabled)
        self.assertTrue(any("Could not load settings:" in message for message in self.logs))


if __name__ == "__main__":
    unittest.main()
