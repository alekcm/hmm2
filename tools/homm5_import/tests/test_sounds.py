"""Sound, music and sound-binding checks.

Two kinds of test here. The normalisation rules are pure functions and need no
data. The rest run against the shipped data and assert things the runtime will
rely on: every key a creature or spell refers to exists in the catalog, every
catalogued file exists on disk, and the music keys the runtime asks for are
there. A missing key is a silent-music bug, so these fail loudly instead.
"""

from __future__ import annotations

import json
import os
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

from homm5import import sounds  # noqa: E402
from homm5import.gamedata import load_creatures, load_spells  # noqa: E402
from homm5import.xdb import XDB  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
DATA = os.path.join(REPO, "data")

_HAS_DATA = os.path.isdir(os.path.join(DATA, "bin", "Geometries"))
requires_data = unittest.skipUnless(_HAS_DATA, "game data not present")


class KeyNormalisationTest(unittest.TestCase):
    """Href-style references and file paths must agree on one key."""

    def test_href_with_pointer_and_namespace(self):
        self.assertEqual(
            sounds.sound_key_from_href("/Sounds/_(Sound)/Spells/Fireball.xdb#xpointer(/Sound)"),
            "spells/fireball")

    def test_music_suffix_is_stripped(self):
        self.assertEqual(
            sounds.sound_key_from_href("/Sounds/_(Music)/Towns/Haven.(Music).xdb#xpointer(/Music)"),
            "towns/haven")

    def test_suffix_inside_name_is_kept(self):
        self.assertEqual(
            sounds.sound_key_from_href("/Sounds/_(Sound)/Spells/LightningBolt_Special.(Sound).xdb"),
            "spells/lightningbolt_special")

    def test_case_is_folded(self):
        self.assertEqual(
            sounds.sound_key_from_href("/SOUNDS/_(sound)/Interface/Ingame/Buy.xdb"),
            sounds.sound_key_from_href("/Sounds/_(Sound)/Interface/Ingame/Buy.xdb"))

    def test_non_sound_hrefs_are_rejected(self):
        self.assertIsNone(sounds.sound_key_from_href(None))
        self.assertIsNone(sounds.sound_key_from_href(""))
        self.assertIsNone(sounds.sound_key_from_href("/Textures/Effects/Glow.xdb"))
        self.assertIsNone(sounds.sound_key_from_href("/Sounds/_(Sound)"))

    def test_event_name_strips_owner_and_frame_number(self):
        self.assertEqual(sounds._event_name("ArchMagi_hit", "ArchMagi"), "hit")
        self.assertEqual(sounds._event_name("attack00", "Genie"), "attack")
        self.assertEqual(sounds._event_name("Master_attack", "Genie"), "master_attack")

    def test_normalise_name_matches_folder_spellings(self):
        self.assertEqual(sounds.normalise_name("Arch_Magi"), sounds.normalise_name("ArchMagi"))
        self.assertEqual(sounds.normalise_name("Arch Magi"), "archmagi")


class AudioSniffingTest(unittest.TestCase):
    """SFX files have GUID names and no extension; the container decides the type."""

    def _sniff(self, header: bytes) -> str:
        with tempfile.NamedTemporaryFile(delete=False) as fh:
            fh.write(header + b"\x00" * 16)
            name = fh.name
        try:
            return sounds._audio_ext(name)
        finally:
            os.unlink(name)

    def test_riff_is_wav(self):
        self.assertEqual(self._sniff(b"RIFF"), ".wav")

    def test_ogg_is_ogg(self):
        self.assertEqual(self._sniff(b"OggS"), ".ogg")

    def test_unknown_keeps_its_extension_rather_than_guessing(self):
        with tempfile.NamedTemporaryFile(suffix=".mp3", delete=False) as fh:
            fh.write(b"ID3\x00")
            name = fh.name
        try:
            self.assertEqual(sounds._audio_ext(name), ".mp3")
        finally:
            os.unlink(name)


@requires_data
class SoundCatalogTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.xdb = XDB(DATA)
        cls.sounds, cls.music, cls.report = sounds.load_sound_catalog(cls.xdb, REPO)

    def test_most_sound_records_resolve_to_a_file(self):
        self.assertGreater(len(self.sounds), 1900)
        self.assertEqual(len(self.sounds) + self.report["soundMissingFile"]
                         + self.report["soundDuplicateKey"], self.report["soundRecords"])

    def test_missing_files_are_only_the_known_five(self):
        # The five unresolved records are ambient and animation leftovers whose
        # GUIDs are not shipped; anything more means a path rule broke.
        self.assertLessEqual(self.report["soundMissingFile"], 5)

    def test_every_catalogued_sound_points_at_a_real_source(self):
        missing = []
        for key, entry in self.sounds.items():
            src = sounds._sfx_file(REPO, entry["uid"])
            if src is None:
                missing.append(key)
        self.assertEqual(missing, [])

    def test_every_music_track_points_at_a_real_source(self):
        self.assertGreater(len(self.music), 150)
        for key, entry in self.music.items():
            rel = entry["file"][len("Audio/Music/"):]
            self.assertIsNotNone(sounds._find_ci(os.path.join(REPO, "music"), rel), key)

    def test_keys_the_runtime_asks_for_exist(self):
        for key in ("interface/click", "interface/ingame/buy", "interface/ingame/constr",
                    "interface/events/get_artf_res01", "interface/events/level up",
                    "spells/fireball", "spells/fireballhit"):
            self.assertIn(key, self.sounds, key)
        for key in ("mainmenu/mainmenu", "towns/haven", "towns/academy", "towns/a2_stronghold",
                    "battle-themes/haven", "battle-themes/neutral",
                    "advmap-terrain/grass", "advmap-terrain/dirt", "advmap-terrain/sand",
                    "advmap-terrain/taiga", "advmap-terrain/water", "advmap-terrain/lava",
                    "win-battle/haven", "lose-battle/haven",
                    "win-campaign/haven", "lose-campaign/haven"):
            self.assertIn(key, self.music, key)

    def test_pause_range_is_ordered(self):
        for key, entry in self.music.items():
            self.assertGreaterEqual(entry["maxPause"], entry["minPause"], key)


@requires_data
class BindingTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.xdb = XDB(DATA)
        cls.sounds, _, _ = sounds.load_sound_catalog(cls.xdb, REPO)
        cls.creatures = load_creatures(cls.xdb)
        cls.spells = load_spells(cls.xdb)

    def test_most_creatures_have_a_sound_table(self):
        non_hero = [r for r in self.creatures.values() if not r.get("isHero") and r.get("id") != "None"]
        with_sounds = [r for r in non_hero if r.get("sounds")]
        self.assertGreater(len(with_sounds), len(non_hero) * 0.85)

    def test_every_creature_sound_resolves_in_the_catalog(self):
        for rec in self.creatures.values():
            for event, key in (rec.get("sounds") or {}).items():
                self.assertIn(key, self.sounds, "%s/%s" % (rec["id"], event))

    def test_arch_magi_uses_its_own_clips(self):
        table = self.creatures["Arch_Magi"]["sounds"]
        self.assertEqual(table.get("attack"), "creatures/academy/archmagi/attack")
        self.assertIn("death", table)

    def test_creature_kind_names_strip_frame_numbers(self):
        for rec in self.creatures.values():
            for event in rec.get("sounds") or {}:
                self.assertFalse(event[-1:].isdigit(), "%s/%s" % (rec["id"], event))

    def test_fireball_visual_names_sound_and_particle_textures(self):
        visuals = self.spells["Fireball"]["visualInfo"]
        self.assertTrue(visuals)
        self.assertEqual(visuals[0]["sound"], "spells/fireball")
        self.assertTrue(visuals[0]["textures"])
        for key in visuals[0]["textures"]:
            self.assertTrue(key.startswith("/Textures/"), key)
            self.assertIsNotNone(sounds.manifest_key_on_disk(self.xdb, key), key)

    def test_spell_sounds_resolve_in_the_catalog(self):
        for rec in self.spells.values():
            for visual in rec.get("visualInfo") or []:
                if visual["sound"]:
                    self.assertIn(visual["sound"], self.sounds, rec["id"])


if __name__ == "__main__":
    unittest.main()
