"""Native UI export and scene wiring; no claim of Unity rendering validation."""
from contextlib import ExitStack
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from homm5import import cli
from homm5import.ui import UiXDB, _placement, export_ui

REPO = Path(__file__).resolve().parents[3]
PROJECT = REPO / "UnityProject"


class NativeUiUnitTest(unittest.TestCase):
    def test_explicit_placement_overrides_shared_but_false_inherits(self):
        shared = ET.fromstring('''<Shared><Placement>
          <Position><First><x>3</x><y>4</y></First></Position>
          <Size><First><x>57</x><y>58</y></First></Size>
          <HorAllign><First>EPA_HIGH_END</First></HorAllign>
        </Placement></Shared>''')
        own = ET.fromstring('''<Window><Placement>
          <Position><First><x>-81</x><y>-71</y></First><Second>true</Second></Position>
          <Size><First><x>0</x><y>0</y></First><Second>false</Second></Size>
        </Placement></Window>''')
        result = _placement(own, shared)
        self.assertEqual(result["Position"], [-81, -71])
        self.assertEqual(result["Size"], [57, 58])
        self.assertEqual(result["HorAllign"], "EPA_HIGH_END")

    def test_overlay_resolution_and_base_fallback(self):
        with tempfile.TemporaryDirectory() as root:
            base = Path(root) / "data"
            expansion = Path(root) / "a2p1-data"
            base.mkdir(); expansion.mkdir()
            (base / "layout.xdb").write_text("<Base/>")
            (expansion / "layout.xdb").write_text("<Expansion/>")
            (base / "shared.xdb").write_text("<Shared/>")
            xdb = UiXDB(base)
            self.assertEqual(xdb.load("/layout.xdb").tag, "Expansion")
            self.assertEqual(xdb.load("/shared.xdb").tag, "Shared")

    def test_all_runs_ui_stage_and_standalone_ui_command_exists(self):
        args = cli.build_parser().parse_args(["ui"])
        self.assertIs(args.func, cli.cmd_ui)
        stages = ["gamedata", "terrain", "maps", "meshes", "textures", "animations", "sounds", "ui"]
        calls = []
        with ExitStack() as stack:
            for stage in stages:
                stack.enter_context(patch.object(cli, "cmd_" + stage,
                    side_effect=lambda _, name=stage: calls.append(name) or 0))
            cli._all(args)
        self.assertEqual(calls, stages)

    def test_separate_menu_scene_is_build_entry_and_uses_bootstrap(self):
        import re
        meta = (PROJECT / "Assets/Scenes/MainMenu.unity.meta").read_text()
        guid = re.search(r"^guid: (\w+)$", meta, re.M).group(1)
        build = (PROJECT / "ProjectSettings/EditorBuildSettings.asset").read_text()
        first = re.search(r"- enabled: 1\s+path: ([^\n]+)\s+guid: (\w+)", build)
        self.assertEqual(first.groups(), ("Assets/Scenes/MainMenu.unity", guid))
        scene = (PROJECT / "Assets/Scenes/MainMenu.unity").read_text()
        self.assertIn("startDirectlyOnMap: 0", scene)
        self.assertIn("9d37eff11f930fe404a92eed6c44c812", scene)
        entry = (PROJECT / "Assets/Scripts/HoMM5.Editor/H5PlayModeEntry.cs").read_text()
        self.assertIn("EditorSceneManager.playModeStartScene", entry)
        self.assertIn("SaveCurrentModifiedScenesIfUserWantsTo", entry)


@unittest.skipUnless((REPO / "data/UI/MainMenu2").is_dir(), "original game UI not present")
class NativeUiDataTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.output = tempfile.TemporaryDirectory()
        cls.skin = export_ui(REPO / "data", cls.output.name)

    @classmethod
    def tearDownClass(cls):
        cls.output.cleanup()

    def test_native_chrome_and_all_command_states_exist(self):
        self.assertEqual(self.skin["version"], 1)
        for key in ("endTurn", "Menu", "NextHero", "MoveHero", "Scenario", "Spellbook", "Underground"):
            for state in ("normal", "pushed", "disabled"):
                self.assertIn(self.skin["windows"][key][state], self.skin["assets"])
        for key in ("menuLogo", "menuPanel", "portrait:Haven/Godric", "portrait:Haven/Orrin", "town:TOWN_STRONGHOLD"):
            self.assertIn(key, self.skin["assets"])

    def test_campaign_portrait_resolves_by_shared_href(self):
        self.assertEqual(self.skin["heroPortraits"]["/mapobjects/haven/godric.(advmapheroshared).xdb"],
                         "portrait:Haven/Godric")

    def test_pngs_match_declared_dimensions_and_live_inside_output(self):
        for asset in self.skin["assets"].values():
            path = Path(self.output.name) / asset["png"]
            self.assertIn(Path(self.output.name).resolve(), path.resolve().parents)
            png = path.read_bytes()
            self.assertEqual(png[:8], b"\x89PNG\r\n\x1a\n")
            self.assertEqual(struct.unpack(">II", png[16:24]), (asset["width"], asset["height"]))
        saved = json.loads((Path(self.output.name) / "UI/skin.json").read_text())
        self.assertEqual(saved, self.skin)

    def test_padded_resource_bar_is_cropped_not_squeezed(self):
        asset = self.skin["assets"]["resources:normal"]
        self.assertEqual((asset["width"], asset["height"]), (697, 52))
        # Menu textures also contain large transparent padding in native DDSs.
        self.assertLess(self.skin["assets"]["menuPanel"]["width"], 512)
        self.assertLess(self.skin["assets"]["menuLogo"]["height"], 256)

    def test_expansion_hud_uses_correct_screen_edges(self):
        mini = self.skin["windows"]["minimap"]
        self.assertEqual((mini["x"], mini["y"], mini["width"], mini["height"]), (2, 0, 219, 221))
        self.assertEqual(mini["horizontal"], "EPA_LOW_END")
        self.assertEqual(mini["vertical"], "EPA_HIGH_END")
        end = self.skin["windows"]["endTurn"]
        self.assertEqual((end["x"], end["y"], end["width"]), (-81, -71, 82))
        self.assertEqual(end["horizontal"], "EPA_HIGH_END")
        self.assertEqual(end["vertical"], "EPA_HIGH_END")
        self.assertEqual(self.skin["windows"]["value:crystals"]["width"], 57)


if __name__ == "__main__":
    unittest.main()
