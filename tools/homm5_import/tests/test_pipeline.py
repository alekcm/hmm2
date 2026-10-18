"""End-to-end checks against the real shipped data.

These are not unit tests of an idealised input: every assertion is measured
against files in the repository, and several are cross-checks between two
independent sources -- a geometry's decoded vertices against the bounding box its
own ``.xdb`` declares, a terrain plane's size against the map's ``<TileX>``, a
creature's name against the localization table it points at.
"""

from __future__ import annotations

import glob
import json
import math
import os
import re
import random
import struct
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

from homm5import import terrain  # noqa: E402
from homm5import.container import END_MARKER, parse_records  # noqa: E402
from homm5import.maps import iter_map_files, load_map  # noqa: E402
from homm5import.dds import DDSError, decode_dds, write_png  # noqa: E402
from homm5import.gamedata import (fill_dwelling_growth, fill_grail_values,
                                 load_arena_buildings,
                                 load_artifacts, load_campaigns, load_creature_enum_ids,
                                 load_creatures, load_game_rules,
                                 load_hero_classes, load_heroes, load_skills, load_spells,
                                 load_town_buildings, load_town_types,
                                 load_war_machines, town_building_key)  # noqa: E402
from homm5import.geometry import decode_file as decode_geometry  # noqa: E402
from homm5import.gltf import geometry_to_glb  # noqa: E402
from homm5import.localization import Localization  # noqa: E402
from homm5import.maps import load_map  # noqa: E402
from homm5import.terrain import FLAG_WATER, decode_file as decode_terrain  # noqa: E402
from homm5import.xdb import XDB  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
DATA = os.path.join(REPO, "data")
TEXTS = os.path.join(REPO, "texts")

_HAS_DATA = os.path.isdir(os.path.join(DATA, "bin", "Geometries"))
requires_data = unittest.skipUnless(_HAS_DATA, "game data not present")


def _vec3(text: str) -> tuple:
    import re
    return tuple(float(re.search(r"<%s>([-\d.e+]+)</%s>" % (c, c), text).group(1)) for c in "xyz")


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


@requires_data
class ContainerGrammarTest(unittest.TestCase):
    def test_grammar_accounts_for_every_byte(self):
        """A record stream must consume itself exactly, trailer and all."""
        path = sorted(glob.glob(os.path.join(DATA, "bin", "AIGeometries", "*")))[0]
        with open(path, "rb") as fh:
            data = fh.read()
        nodes = parse_records(data)
        self.assertTrue(nodes)
        # The trailer is the documented 6-byte end marker.
        self.assertTrue(data.endswith(END_MARKER), data[-8:].hex())

    def test_end_marker_present_in_every_geometry(self):
        for path in sorted(glob.glob(os.path.join(DATA, "bin", "Geometries", "*")))[:40]:
            with open(path, "rb") as fh:
                self.assertTrue(fh.read().endswith(END_MARKER), path)


@requires_data
class TerrainTest(unittest.TestCase):
    def test_every_terrain_file_decodes(self):
        files = (glob.glob(os.path.join(DATA, "Maps/**/GroundTerrain.bin"), recursive=True) +
                 glob.glob(os.path.join(DATA, "Maps/**/UndergroundTerrain.bin"), recursive=True))
        self.assertGreater(len(files), 200, "expected the shipped map set")
        for path in files:
            t = decode_terrain(path)
            v = t.vertex_count
            self.assertIsNotNone(t.height, path)
            self.assertEqual(t.height.width, v, path)
            self.assertEqual(t.height.count, v * v, path)

    def test_height_is_f32_and_planes_are_u8(self):
        """height = V^2 f32, flags = V^2 u8, river = (2V-1)^2 u8."""
        path = os.path.join(DATA, "Maps", "CombatArenas", "AbandonedMine", "GroundTerrain.bin")
        t = decode_terrain(path)
        v = t.vertex_count
        self.assertEqual(len(t.height.data), v * v * 4)
        self.assertEqual(t.flags.count, v * v)
        self.assertEqual(t.river.count, (2 * v - 1) ** 2)

    def test_default_ground_level_is_two(self):
        """A flat untouched map sits at 2.0, not at sea level."""
        path = os.path.join(DATA, "Maps", "CombatArenas", "AbandonedMine", "GroundTerrain.bin")
        t = decode_terrain(path)
        lo, hi, _mean = t.height_stats()
        self.assertAlmostEqual(lo, 2.0, places=5)
        self.assertAlmostEqual(hi, 2.0, places=5)

    def test_boat_arena_is_all_water(self):
        """The doc's own claim: BoatArena is 100% ground flag 0."""
        path = os.path.join(DATA, "Maps", "CombatArenas", "BoatArena", "GroundTerrain.bin")
        t = decode_terrain(path)
        self.assertEqual(t.water_vertex_count(), t.height.count)
        self.assertEqual(t.height_stats()[1], 0.0)

    def test_tile_count_matches_the_map_descriptor(self):
        import xml.etree.ElementTree as ET
        mapdir = os.path.join(DATA, "Maps", "CombatArenas", "AbandonedMine")
        root = ET.parse(os.path.join(mapdir, "AbandonedMine.xdb")).getroot()
        tile_x = int(root.find("TileX").text)
        t = decode_terrain(os.path.join(mapdir, "GroundTerrain.bin"))
        self.assertEqual(t.tile_x, tile_x)
        self.assertEqual(t.vertex_count, tile_x + 1)


@requires_data
class TerrainOutputIdTest(unittest.TestCase):
    """Output folder names, which must never collide.

    Map JSON ids come from unique_map_ids, and terrain has to agree with them.
    Arena folders add a second hazard: they ship ``GroundTerrain.bin`` under
    basenames like ``S1`` without holding any map descriptor, so a plain
    basename fallback overwrote the real multiplayer maps S1-S3 on disk.
    """

    def _files(self):
        return sorted(
            glob.glob(os.path.join(DATA, "Maps/**/GroundTerrain.bin"), recursive=True) +
            glob.glob(os.path.join(DATA, "Maps/**/UndergroundTerrain.bin"), recursive=True))

    def test_every_terrain_plane_gets_its_own_output_path(self):
        from homm5import.cli import _terrain_ids
        files = self._files()
        self.assertGreater(len(files), 200)
        ids = _terrain_ids(files, iter_map_files(XDB(DATA), only_map_descs=True), DATA)
        paths = set()
        for path in files:
            folder = os.path.normpath(os.path.abspath(os.path.dirname(path)))
            paths.add((ids[folder], os.path.basename(path)))
        self.assertEqual(len(paths), len(files), "two planes share an output path")

    def test_multiplayer_maps_keep_their_plain_ids(self):
        """S1-S3 must not be borrowed by the arena folders of the same name."""
        from homm5import.cli import _terrain_ids
        ids = _terrain_ids(self._files(), iter_map_files(XDB(DATA), only_map_descs=True), DATA)
        for name in ("S1", "S2", "S3"):
            folder = os.path.normpath(os.path.abspath(
                os.path.join(DATA, "Maps", "Multiplayer", name)))
            self.assertEqual(ids.get(folder), name)


@requires_data
class GeometryTest(unittest.TestCase):
    @staticmethod
    def _model_uid_index(limit=None):
        import re
        bins = {os.path.basename(p).upper(): p for p in glob.glob(os.path.join(DATA, "bin", "Geometries", "*"))}
        uid_re = re.compile(r"<uid>([0-9A-Fa-f-]{36})</uid>")
        pairs = {}
        for x in glob.glob(os.path.join(DATA, "_(Model)", "**", "*.xdb"), recursive=True):
            with open(x, encoding="utf-8", errors="replace") as fh:
                text = fh.read()
            for blk in re.findall(r"<Geometry[^>]*>(.*?)</Geometry>", text, re.S):
                m = uid_re.search(blk)
                if m and m.group(1).upper() in bins:
                    pairs.setdefault(m.group(1).upper(), (x, blk))
            if limit and len(pairs) >= limit:
                break
        return pairs, bins

    def test_positions_fit_the_declared_bounding_box(self):
        pairs, bins = self._model_uid_index()
        self.assertGreater(len(pairs), 100)
        random.seed(11)
        checked = 0
        for uid in random.sample(list(pairs), 40):
            _xdb_path, blk = pairs[uid]
            size = _vec3(re.search(r"<Size>(.*?)</Size>", blk, re.S).group(1))
            centre = _vec3(re.search(r"<Center>(.*?)</Center>", blk, re.S).group(1))
            geom = decode_geometry(bins[uid])
            bounds = geom.bounds()
            if bounds is None:
                continue
            lo, hi = bounds
            for i in range(3):
                self.assertGreaterEqual(lo[i], centre[i] - size[i] / 2 - 0.05, uid)
                self.assertLessEqual(hi[i], centre[i] + size[i] / 2 + 0.05, uid)
            checked += 1
        self.assertGreater(checked, 20)

    def test_declared_mesh_count_matches(self):
        pairs, bins = self._model_uid_index()
        random.seed(3)
        for uid in random.sample(list(pairs), 40):
            _xdb_path, blk = pairs[uid]
            m = re.search(r"<NumMeshes>(\d+)</NumMeshes>", blk)  # noqa: F821
            declared = int(m.group(1))
            geom = decode_geometry(bins[uid])
            self.assertEqual(geom.declared_mesh_count, declared, uid)
            self.assertEqual(len(geom.meshes), declared, uid)

    def test_triangle_indices_are_in_range(self):
        random.seed(5)
        files = sorted(glob.glob(os.path.join(DATA, "bin", "Geometries", "*")))
        for path in random.sample(files, 40):
            geom = decode_geometry(path)
            for mesh in geom.meshes:
                for group in mesh.groups:
                    n = len(group.positions)
                    for tri in group.triangles:
                        for index in tri:
                            self.assertLess(index, n, path)

    def test_authored_normals_agree_with_face_normals(self):
        """The byte triple at offset 8 is the normal, stored z, y, x.

        Measured against the face normal of the triangles that use it.  Reading
        the triple as x, y, z instead drops the agreement to ~0.27.
        """
        random.seed(4)
        files = sorted(glob.glob(os.path.join(DATA, "bin", "Geometries", "*")))
        agree = total = 0
        for path in random.sample(files, 60):
            geom = decode_geometry(path)
            for mesh in geom.meshes:
                for group in mesh.groups:
                    for tri in group.triangles:
                        a, b, c = (group.positions[i] for i in tri)
                        fn = _cross(_sub(b, a), _sub(c, a))
                        if _dot(fn, fn) < 1e-18:
                            continue
                        n = group.normals[tri[0]]
                        if _dot(fn, n) > 0:
                            agree += 1
                        total += 1
        self.assertGreater(total, 1000)
        self.assertGreater(agree / total, 0.9, "normal byte order looks wrong")

    def test_winding_needs_reversing_for_unity(self):
        """Z-up to Y-up swaps orientation, so Unity needs reversed winding.

        Verified the same way the Unity loader does it: transform by
        (x, y, z) -> (x, z, y), then compare the face normal against the
        transformed authored normal, with and without the reversal.
        """
        random.seed(4)
        files = sorted(glob.glob(os.path.join(DATA, "bin", "Geometries", "*")))

        def swap(v):
            return (v[0], v[2], v[1])

        kept = reversed_ok = total = 0
        for path in random.sample(files, 40):
            geom = decode_geometry(path)
            for mesh in geom.meshes:
                for group in mesh.groups:
                    for tri in group.triangles:
                        a, b, c = (swap(group.positions[i]) for i in tri)
                        n = swap(group.normals[tri[0]])
                        if _dot(_cross(_sub(b, a), _sub(c, a)), n) > 0:
                            kept += 1
                        if _dot(_cross(_sub(c, a), _sub(b, a)), n) > 0:
                            reversed_ok += 1
                        total += 1
        self.assertGreater(total, 1000)
        self.assertLess(kept / total, 0.05, "winding did not need reversing?")
        self.assertGreater(reversed_ok / total, 0.9, "reversed winding should face outward")


@requires_data
class GlbTest(unittest.TestCase):
    def test_glb_round_trips(self):
        random.seed(9)
        files = sorted(glob.glob(os.path.join(DATA, "bin", "Geometries", "*")))
        for path in random.sample(files, 20):
            geom = decode_geometry(path)
            if not any(g.triangles for m in geom.meshes for g in m.groups):
                continue
            blob = geometry_to_glb(geom, name="t")
            magic, version, total = struct.unpack_from("<III", blob, 0)
            self.assertEqual(magic, 0x46546C67)
            self.assertEqual(version, 2)
            self.assertEqual(total, len(blob))
            jlen, jtype = struct.unpack_from("<II", blob, 12)
            self.assertEqual(jtype, 0x4E4F534A)
            blen, btype = struct.unpack_from("<II", blob, 20 + jlen)
            self.assertEqual(btype, 0x004E4942)
            doc = json.loads(blob[20:20 + jlen].decode("utf-8").rstrip())
            self.assertEqual(doc["asset"]["version"], "2.0")
            for bv in doc["bufferViews"]:
                self.assertLessEqual(bv["byteOffset"] + bv["byteLength"], blen)
            self.assertTrue(doc["meshes"])

    def test_glb_positions_match_the_source(self):
        random.seed(10)
        files = sorted(glob.glob(os.path.join(DATA, "bin", "Geometries", "*")))
        for path in random.sample(files, 10):
            geom = decode_geometry(path)
            if not any(g.triangles for m in geom.meshes for g in m.groups):
                continue
            blob = geometry_to_glb(geom, name="t")
            jlen, _ = struct.unpack_from("<II", blob, 12)
            doc = json.loads(blob[20:20 + jlen].decode("utf-8").rstrip())
            bin_start = 20 + jlen + 8
            # First mesh's POSITION accessor.
            acc = doc["accessors"][doc["meshes"][0]["primitives"][0]["attributes"]["POSITION"]]
            bv = doc["bufferViews"][acc["bufferView"]]
            off = bin_start + bv["byteOffset"]
            first = struct.unpack_from("<3f", blob, off)
            src = geom.meshes[0].groups[0].positions[0]
            for i in range(3):
                self.assertAlmostEqual(first[i], src[i], places=4)


@requires_data
class DdsTest(unittest.TestCase):
    def test_all_layouts_present_are_decodable(self):
        """The shipped set is BGRA, RGB565, DXT1, DXT3, DXT4 and DXT5."""
        random.seed(1)
        files = sorted(glob.glob(os.path.join(DATA, "**", "*.dds"), recursive=True))
        self.assertGreater(len(files), 5000)
        seen = set()
        for path in random.sample(files, 250):
            with open(path, "rb") as fh:
                data = fh.read()
            fourcc = data[84:88]
            seen.add(fourcc)
            w, h, rgba = decode_dds(data)
            self.assertEqual(len(rgba), w * h * 4, path)

    def test_dxt1_block_decodes_to_its_endpoint_colours(self):
        """A 4x4 DXT1 block with index bits all zero is just colour0."""
        r, g, b = 0xF8, 0xFC, 0xF8  # white in RGB565
        c0 = ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3)
        body = struct.pack("<HH", c0, 0) + bytes(8)
        blob = bytearray(128)
        blob[0:4] = b"DDS "
        struct.pack_into("<IIII", blob, 4, 124, 0x1007, 4, 4)
        struct.pack_into("<I", blob, 76, 32)
        struct.pack_into("<I", blob, 80, 0x4)
        blob[84:88] = b"DXT1"
        struct.pack_into("<IIIII", blob, 88, 0, 0, 0, 0, 0)
        blob.extend(body)
        w, h, rgba = decode_dds(bytes(blob))
        self.assertEqual((w, h), (4, 4))
        self.assertEqual(tuple(rgba[0:4]), (255, 255, 255, 255))

    def test_write_png_is_a_valid_png(self):
        import tempfile
        import zlib
        w, h = 5, 3
        rgba = bytes([i % 256 for i in range(w * h * 4)])
        with tempfile.TemporaryDirectory() as tmp:
            out = os.path.join(tmp, "t.png")
            write_png(out, w, h, rgba)
            with open(out, "rb") as fh:
                data = fh.read()
        self.assertEqual(data[:8], b"\x89PNG\r\n\x1a\n")
        self.assertEqual(struct.unpack(">II", data[16:24]), (w, h))
        idat = data.index(b"IDAT")
        length = struct.unpack(">I", data[idat - 4:idat])[0]
        raw = zlib.decompress(data[idat + 4:idat + 4 + length])
        self.assertEqual(len(raw), h * (1 + w * 4))


@requires_data
class XdbTest(unittest.TestCase):
    def setUp(self):
        self.xdb = XDB(DATA)

    def test_absolute_href_resolves(self):
        node = self.xdb.load("/GameMechanics/Creature/Creatures/Academy/3rd/CombatMage.xdb")
        self.assertIsNotNone(node)
        self.assertEqual(node.tag, "Creature")

    def test_relative_href_resolves_against_its_own_file(self):
        """<Model href="T4_Combat_Mage.xdb"> is relative, not data-rooted."""
        char_href = "/Characters/Creatures/Academy/Alternative Upgrade/T4_Combat_Mage.(Character).xdb"
        node = self.xdb.load(char_href)
        self.assertIsNotNone(node, "character xdb missing")
        model, href = self.xdb.resolve(node.find("Model"), char_href)
        self.assertIsNotNone(model, "relative Model href did not resolve")
        self.assertTrue(href.startswith("/Characters/"), href)

    def test_missing_href_returns_none_rather_than_raising(self):
        self.assertIsNone(self.xdb.load("/Does/Not/Exist.xdb"))
        self.assertIsNone(self.xdb.resolve_field(None))

    def test_inline_values_are_the_element_own_child(self):
        node = self.xdb.load("/_(Model)/Arenas/EnvBoxBig.(Model).xdb")
        self.assertIsNotNone(node)
        geom = self.xdb.resolve_field(node.find("Geometry"))
        self.assertIsNotNone(geom, "inline Geometry did not resolve")
        self.assertEqual(geom.tag, "Geometry")


@requires_data
class LocalizationTest(unittest.TestCase):
    def test_texts_are_utf16_with_bom(self):
        loc = Localization(TEXTS)
        value = loc.read("/Text/Game/Creatures/Academy/3rd/CombatMage_Name.txt")
        self.assertIsNotNone(value)
        self.assertTrue(value)
        # The shipped localization is Russian.
        self.assertTrue(any("\u0400" <= ch <= "\u04FF" for ch in value), value)


@requires_data
class GameDataTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.xdb = XDB(DATA)
        cls.loc = Localization(TEXTS)
        cls.creatures = load_creatures(cls.xdb, cls.loc)
        cls.spells = load_spells(cls.xdb, cls.loc)
        cls.artifacts = load_artifacts(cls.xdb, cls.loc)
        cls.campaigns = load_campaigns(cls.xdb, cls.loc)
        cls.rules = load_game_rules(cls.xdb, cls.loc)
        cls.town_buildings = load_town_buildings(cls.xdb, cls.loc)

    def test_campaign_records_link_localized_missions_to_maps(self):
        """Campaign selection must use the shipped XDB mission links, not a
        guessed first map; the ToTE overlay still leaves all four records
        readable and localizes their mission text.
        """
        self.assertEqual(set(self.campaigns), {"A2C0", "A2C1", "A2C2", "A2C3"})
        campaign = self.campaigns["A2C0"]
        self.assertEqual(campaign["name"], "Ярость племен")
        self.assertEqual(campaign["missions"][0]["mapId"], "A2C0M0")
        self.assertEqual(campaign["missions"][0]["tileX"], 136)
        self.assertTrue(campaign["missions"][0]["name"])
        self.assertEqual(campaign["missions"][0]["bonuses"][0]["resources"]["Gold"], 1000)

    def test_town_hall_income_matches_shipped_building_descriptions(self):
        halls = {b["upgrade"]: b["incomeGoldPerDay"] for b in self.town_buildings
                 if b["type"] == "TB_TOWN_HALL"}
        self.assertEqual(halls, {
            "BLD_UPG_1": 500, "BLD_UPG_2": 1000,
            "BLD_UPG_3": 2000, "BLD_UPG_4": 4000,
        })

    def test_adventure_rules_and_resource_tables_match_shipped_stats(self):
        self.assertEqual(self.rules["baseHeroMovement"], 2500)
        self.assertEqual(self.rules["visitFountainOfYouthMovementBonus"], 400)
        self.assertEqual(self.rules["visitOasisMovementBonus"], 800)
        self.assertEqual(self.rules["forWeekStablesMaxLandMovementBonus"], 600)
        self.assertEqual(self.rules["startHumanResources"][0]["wood"], 50)
        self.assertEqual(self.rules["startHumanResources"][0]["gold"], 50000)
        self.assertEqual(self.rules["mineDailyIncome"], {
            "GOLD": 1000, "ORE": 2, "WOOD": 2, "MERCURY": 1,
            "CRYSTAL": 1, "SULFUR": 1, "GEM": 1,
        })

    def test_combat_constants_match_shipped_stats(self):
        """The combat engine's own numbers: defending bonus, luck damage
        multipliers and the ATB morale values live in DefaultStats.xdb and
        must reach the runtime unchanged."""
        c = self.rules["combat"]
        self.assertEqual(c["defenceBonusOnDefend"], 0.3)
        self.assertEqual(c["goodLuckDamageMultiplier"], 2.0)
        self.assertEqual(c["badLuckDamageMultiplier"], 0.5)
        self.assertEqual(c["goodMoraleAtbValue"], 0.5)
        self.assertEqual(c["badMoraleAtbValue"], 0.5)
        self.assertEqual(c["heroInitiative"], 10)
        self.assertEqual(c["atbBaseTime"], 100000)

    def test_spell_damage_mana_and_mastery_rows_come_through(self):
        """DeepFreeze spells out its per-mastery damage rows and mana cost in
        the spell record itself; the runtime needs all of it to cast."""
        s = self.spells["DeepFreeze"]
        self.assertEqual(s["manaCost"], 11)
        self.assertTrue(s["damageIsElemental"])
        self.assertEqual(len(s["damage"]), 4, "damage rows are per mastery tier")
        self.assertEqual(s["damage"][0], {"base": 10.0, "perPower": 10.0})
        self.assertEqual(s["damage"][3], {"base": 30.0, "perPower": 30.0})
        self.assertEqual(s["duration"][0], {"base": 1.0, "perPower": 0.0})
        self.assertEqual(s["target"], "TARGET_HOSTILE")

    def test_war_machine_stats_match_the_reference_table(self):
        wm = load_war_machines(self.xdb)
        self.assertEqual(set(wm), {
            "WAR_MACHINE_BALLISTA", "WAR_MACHINE_CATAPULT",
            "WAR_MACHINE_FIRST_AID_TENT", "WAR_MACHINE_AMMO_CART"})
        b = wm["WAR_MACHINE_BALLISTA"]
        self.assertEqual(b["shots"], 100)
        self.assertEqual(b["health"], 250)
        self.assertEqual(b["initiative"], 10)
        self.assertEqual(b["offence"], [5, 10, 12, 15, 17])
        self.assertEqual(b["minDamageFactor"], [2, 2, 2, 5, 5])
        self.assertEqual(b["cost"]["Gold"], 1500)

    def test_siege_structure_health_matches_the_reference_table(self):
        ab = load_arena_buildings(self.xdb)
        self.assertEqual(ab["WALL"]["baseHealth"], 200)
        self.assertEqual(ab["WALL"]["healthFortBonuses"], [0, 0, 100])
        self.assertEqual(ab["GATE"]["healthFortBonuses"], [0, 100, 200])
        self.assertEqual(ab["BIG_TOWER"]["baseHealth"], 400)
        self.assertEqual(ab["MAGIC_WALL"]["baseHealth"], 500)

    def test_skill_table_supplies_ranks_perks_and_prerequisites(self):
        """Skills.xdb carries ranked skills plus four perk flavours; every
        perk points at its parent skill and ultimate perks spell out their
        cross-class feat requirements."""
        skills = load_skills(self.xdb, self.loc)
        self.assertGreaterEqual(len(skills), 220)
        kinds = {}
        for s in skills.values():
            kinds[s["kind"]] = kinds.get(s["kind"], 0) + 1
        self.assertEqual(kinds.get("skill"), 26)          # 12 common + racial + shatter/voice
        self.assertEqual(kinds.get("standardPerk"), 55)
        self.assertEqual(kinds.get("uniquePerk"), 8)

        offence = skills["HERO_SKILL_OFFENCE"]
        self.assertEqual(offence["kind"], "skill")
        self.assertEqual(offence["names"], ["Основы нападения", "Развитое нападение",
                                            "Искусное нападение"])
        self.assertIn("на 15%", offence["descriptions"][2])
        self.assertEqual(offence["spellBuffs"],
                         ["SPELL_SKILL_OFFENCE1", "SPELL_SKILL_OFFENCE2", "SPELL_SKILL_OFFENCE3"])

        archery = skills["HERO_SKILL_ARCHERY"]
        self.assertEqual(archery["kind"], "standardPerk")
        self.assertEqual(archery["skill"], "HERO_SKILL_OFFENCE")
        self.assertEqual(archery["prerequisites"], {})

        rage = skills["HERO_SKILL_ABSOLUTE_RAGE"]
        self.assertEqual(rage["kind"], "uniquePerk")
        self.assertEqual(rage["prerequisites"]["HERO_CLASS_BARBARIAN"],
                         ["HERO_SKILL_ENCOURAGE", "HERO_SKILL_CRITICAL_STRIKE",
                          "HERO_SKILL_TRIPLE_CATAPULT"])

        retribution = skills["HERO_SKILL_RETRIBUTION"]
        self.assertIn("HERO_CLASS_KNIGHT", retribution["prerequisites"])
        self.assertIn("HERO_SKILL_FRENZY", retribution["prerequisites"]["HERO_CLASS_KNIGHT"])

        # Racial skills rank four times (Basic..ExtraExpert).
        necro = skills["HERO_SKILL_NECROMANCY"]
        self.assertEqual(len(necro["names"]), 4)
        self.assertEqual(necro["heroClass"], "HERO_CLASS_NECROMANCER")

    def test_hero_classes_carry_skill_offer_weights_and_stat_probs(self):
        classes = load_hero_classes(self.xdb, self.loc)
        self.assertEqual(len(classes), 8)
        knight = classes["HERO_CLASS_KNIGHT"]
        self.assertEqual(knight["name"], "Рыцарь")
        self.assertEqual(knight["skillsProbs"]["HERO_SKILL_LEADERSHIP"], 15)
        self.assertEqual(knight["skillsProbs"]["HERO_SKILL_TRAINING"], 10)
        self.assertEqual(knight["attributeProbs"],
                         {"offence": 30, "defence": 45, "spellpower": 10, "knowledge": 15})
        self.assertIn("SPELL_RESURRECT", knight["preferredSpells"])
        # Every class knows its own racial skill with a positive weight.
        for cls_id, row in classes.items():
            racial = {"HERO_CLASS_KNIGHT": "HERO_SKILL_TRAINING",
                      "HERO_CLASS_DEMON_LORD": "HERO_SKILL_GATING",
                      "HERO_CLASS_NECROMANCER": "HERO_SKILL_NECROMANCY",
                      "HERO_CLASS_RANGER": "HERO_SKILL_AVENGER",
                      "HERO_CLASS_WIZARD": "HERO_SKILL_ARTIFICIER",
                      "HERO_CLASS_WARLOCK": "HERO_SKILL_INVOCATION",
                      "HERO_CLASS_RUNEMAGE": "HERO_SKILL_RUNELORE",
                      "HERO_CLASS_BARBARIAN": "HERO_SKILL_DEMONIC_RAGE"}[cls_id]
            self.assertGreater(row["skillsProbs"].get(racial, 0), 0, cls_id)

    def test_named_heroes_come_from_the_random_map_pools(self):
        heroes = load_heroes(self.xdb, self.loc)
        classes = load_hero_classes(self.xdb, self.loc)
        # RMGPresetTable lists eight heroes for each of the eight factions.
        self.assertEqual(len(heroes), 64)
        from collections import Counter
        per_town = Counter(h["townType"] for h in heroes.values())
        self.assertEqual(set(per_town.values()), {8})
        self.assertEqual(len(per_town), 8)

        axel = heroes["Haven/Axel"]
        self.assertEqual(axel["name"], "Фрида")
        self.assertEqual(axel["internalName"], "Axel")
        self.assertEqual(axel["townType"], "TOWN_HEAVEN")
        self.assertEqual(axel["heroClass"], "HERO_CLASS_KNIGHT")
        self.assertEqual(axel["specializationName"], "Бегущий как ветер")
        self.assertEqual(axel["stats"],
                         {"offence": 1, "defence": 2, "spellpower": 1, "knowledge": 1})
        # Mastery is the numeric rank the map hero records use, not the enum string.
        self.assertIn({"skill": "HERO_SKILL_OFFENCE", "mastery": 1}, axel["skills"])
        self.assertEqual(axel["perks"], ["HERO_SKILL_TACTICS"])
        self.assertEqual(axel["spells"], ["SPELL_HASTE"])
        self.assertEqual(axel["pools"], ["RACE_HEAVEN"])
        self.assertTrue(axel["face"].endswith(".dds"))

        # Stronghold heroes are numbered records; the key keeps the faction folder.
        self.assertEqual(heroes["Stronghold/Hero1"]["name"], "Краг")
        self.assertEqual(heroes["Stronghold/Hero1"]["townType"], "TOWN_STRONGHOLD")

        for key, hero in heroes.items():
            self.assertIn(hero["heroClass"], classes, key)
            self.assertTrue(hero["face"], key)
            self.assertTrue(hero["name"], key)
            self.assertGreaterEqual(hero["primarySkill"]["mastery"], 0, key)

    def test_hero_skill_constants_reach_the_rules_export(self):
        """The engine's skill coefficients stay data-driven: the runtime reads
        these instead of hardcoding effect sizes."""
        hero = self.rules["heroSkills"]
        self.assertEqual(hero["adventure"]["Scouting_VisibilityIncrease"], 4)
        self.assertEqual(hero["adventure"]["Intelligence_ManaPointsBonus"], 1.5)
        self.assertEqual(hero["adventure"]["WarMachines_HealthBonusPerSkillTrained"], 100)
        self.assertEqual(hero["combat"]["SkillOffence"],
                         {"BasicDamageBonus": 0.05, "AdvancedDamageBonus": 0.1,
                          "ExpertDamageBonus": 0.15, "Archery_DamageBonus": 0.2})
        self.assertEqual(hero["combat"]["SkillDefence"]["Evasion_DamageReduce"], 0.2)
        self.assertEqual(hero["combat"]["Knight"]["Retribution_DamageBonusPerMorale"], 0.05)
        self.assertEqual(hero["combat"]["Knight"]["HoldGround_DefendBonusMultiplier"], 2)
        self.assertEqual(hero["combat"]["Runemage"]["QuicknessOfMind_InitiativeBonus"], 0.25)
        self.assertEqual(hero["combat"]["Ranger"]["ElvenLuck_DamageMultiplier"], 2.25)
        self.assertEqual(hero["combat"]["Necromancer"]["Necromancy"]["RaisePercentPerSkillLevel"], 10)
        self.assertEqual(hero["combat"]["DemonLord"]["Gating"]["PortionOfSummonedByGatingLevel"]["Item"],
                         [0.3, 0.35, 0.4, 0.45])
        self.assertEqual(self.rules["necromancyTransform"]["CREATURE_PEASANT"], "CREATURE_SKELETON")
        self.assertEqual(self.rules["necromancyTransform"]["CREATURE_TITAN"], "CREATURE_WRAITH")
        se = self.rules["skillEffects"]
        self.assertEqual(se["offenceDamageBonus"], [0.05, 0.1, 0.15])
        self.assertEqual(se["defenceDamageReduce"], [0.1, 0.2, 0.3])
        self.assertEqual(se["gatingPortionByLevel"], [0.3, 0.35, 0.4, 0.45])
        self.assertEqual(se["trainingRetaliationBonus"], [0.05, 0.1, 0.2, 0.25])
        self.assertEqual(se["necromancyRaisePerLevel"], 10.0)
        self.assertEqual(se["quicknessOfMindInitiativeBonus"], 0.25)

    def test_creature_stats_match_the_xml(self):
        c = self.creatures["CombatMage"]
        self.assertEqual(c["AttackSkill"], 12)
        self.assertEqual(c["DefenceSkill"], 9)
        self.assertEqual(c["MinDamage"], 7)
        self.assertEqual(c["MaxDamage"], 7)
        self.assertEqual(c["Health"], 29)
        self.assertEqual(c["Speed"], 4)
        self.assertEqual(c["Initiative"], 10)
        self.assertEqual(c["cost"]["Gold"], 340)
        self.assertEqual(c["town"], "TOWN_ACADEMY")
        self.assertIn("ABILITY_MAGIC_ATTACK", c["abilities"])

    def test_creature_visual_name_is_localized(self):
        name = self.creatures["CombatMage"]["visual"]["name"]
        self.assertTrue(name)
        self.assertEqual(name, self.loc.read("/Text/Game/Creatures/Academy/3rd/CombatMage_Name.txt"))

    def test_creature_geometry_uid_resolves_to_a_real_file(self):
        uid = self.creatures["CombatMage"]["visual"]["geometryUid"]
        self.assertTrue(uid, "geometry uid did not resolve")
        self.assertTrue(os.path.isfile(os.path.join(DATA, "bin", "Geometries", uid)), uid)

    def test_spells_are_localized_and_levelled(self):
        s = self.spells["DimensionDoor"]
        self.assertEqual(s["level"], 4)
        self.assertEqual(s["school"], "MAGIC_SCHOOL_ADVENTURE")
        self.assertTrue(s["name"])

    def test_artifacts_come_from_the_reference_table(self):
        a = self.artifacts["ANGEL_WINGS"]
        self.assertEqual(a["artifactClass"], "ARTF_CLASS_RELIC")
        self.assertEqual(a["slot"], "SHOULDERS")
        self.assertEqual(a["price"], 40000)
        self.assertTrue(a["name"])
        self.assertTrue(os.path.isfile(os.path.join(DATA, "bin", "Geometries", a["geometryUid"])))


@requires_data
class MapTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.xdb = XDB(DATA)

    def test_objects_resolve_their_geometry(self):
        path = os.path.join(DATA, "Maps", "CombatArenas", "AbandonedMine", "AbandonedMine.xdb")
        md = load_map(self.xdb, path)
        self.assertEqual(md.tile_x, 96)
        self.assertTrue(md.objects)
        with_mesh = [o for o in md.objects if o.geometry_uid]
        self.assertEqual(len(with_mesh), len(md.objects), "every static should resolve a model")
        for o in with_mesh[:20]:
            self.assertTrue(os.path.isfile(os.path.join(DATA, "bin", "Geometries", o.geometry_uid)))

    def test_relative_geometry_href_is_resolved_against_model_file(self):
        """Terrain objects often use a model-local href such as Rail-geom.xdb.
        Treating it as data-rooted silently drops the model and its materials.
        """
        path = os.path.join(DATA, "Maps", "Multiplayer", "A2M1", "map.xdb")
        if not os.path.isfile(path):
            self.skipTest("multiplayer map not present")
        md = load_map(self.xdb, path)
        rail = [o for o in md.objects if o.shared_href and "/Subterra/Rail." in o.shared_href]
        self.assertTrue(rail, "reference map no longer contains the rail object")
        self.assertTrue(rail[0].geometry_uid)
        self.assertTrue(os.path.isfile(os.path.join(DATA, "bin", "Geometries", rail[0].geometry_uid)))

    def test_faction_town_resolves_exterior_model_and_type(self):
        """The shared town's direct Model is empty; the faction's exterior
        upgrade list supplies the actual adventure-map model.
        """
        path = os.path.join(DATA, "Maps", "Scenario", "A2C0M0", "map.xdb")
        if not os.path.isfile(path):
            self.skipTest("scenario map not present")
        md = load_map(self.xdb, path)
        towns = [o for o in md.objects if o.kind == "AdvMapTown"
                 and o.shared_href and "Heaven.(AdvMapTownShared)" in o.shared_href]
        self.assertTrue(towns)
        self.assertEqual(towns[0].extra.get("townType"), "TOWN_HEAVEN")
        self.assertTrue(towns[0].geometry_uid)
        self.assertTrue(os.path.isfile(os.path.join(DATA, "bin", "Geometries", towns[0].geometry_uid)))

    def test_shared_records_supply_hero_monster_mine_and_artifact_identity(self):
        """These values live in the referenced Shared XDB, not on each placed
        instance. Empty fields made the Unity inspector unable to identify them.
        """
        path = os.path.join(DATA, "Maps", "Scenario", "A2C0M0", "map.xdb")
        if not os.path.isfile(path):
            self.skipTest("scenario map not present")
        md = load_map(self.xdb, path, resolve_models=False)
        first = lambda kind: next(o for o in md.objects if o.kind == kind)
        self.assertEqual(first("AdvMapMonster").extra["creature"], "CREATURE_CHAMPION")
        self.assertEqual(first("AdvMapMine").extra["resource"], "GOLD")
        self.assertEqual(first("AdvMapArtifact").extra["artifact"], "GOLDEN_HORSESHOE")
        self.assertTrue(first("AdvMapHero").extra["hero"])
        self.assertTrue(first("AdvMapHero").object_id,
                        "hero map-object IDs are needed to persist movement across floor loads")

    def test_custom_resource_piles_preserve_authored_type_and_amount(self):
        path = os.path.join(DATA, "Maps", "Multiplayer", "A1M4", "map.xdb")
        md = load_map(self.xdb, path, resolve_models=False)
        piles = [o for o in md.objects if o.kind == "AdvMapTreasure" and o.extra.get("isCustom")]
        wood = next(o for o in piles if o.extra.get("sharedType") == "TREASURE_WOOD")
        self.assertEqual(wood.extra["amount"], 8)
        self.assertTrue(wood.object_id)

    def test_map_heroes_carry_their_level_up_history(self):
        """A map hero's Editable block lists ranked skills, perks and the
        spellbook -- the level-up history the runtime restores verbatim."""
        path = os.path.join(DATA, "Maps", "Scenario", "A2C1M5", "map.xdb")
        if not os.path.isfile(path):
            self.skipTest("scenario map not present")
        md = load_map(self.xdb, path, resolve_models=False)
        heroes = [o for o in md.objects if o.kind == "AdvMapHero" and o.extra.get("skills")]
        self.assertTrue(heroes, "no map hero ships a skill roster")
        hero = heroes[0]
        skills = {s["skill"]: s["mastery"] for s in hero.extra["skills"]}
        for mastery in skills.values():
            self.assertIn(mastery, (1, 2, 3, 4), "mastery ranks are numeric 1..4")
        self.assertTrue(any(v == 3 for v in skills.values()), "at least one Expert rank")
        perks = hero.extra.get("perks", [])
        self.assertGreater(len(perks), 3)
        for p in perks:
            self.assertTrue(p.startswith("HERO_SKILL_"))
        self.assertTrue(hero.extra.get("primarySkills") or hero.extra.get("experience", 0) >= 0)

    def test_shared_records_supply_visit_tiles_and_treasure_rolls(self):
        """The visit trigger tiles and the pickup roll range live on the shared
        record. Without them the runtime cannot tell which tile activates a
        building, nor how much a treasure pile pays out."""
        path = os.path.join(DATA, "Maps", "Scenario", "A2C0M0", "map.xdb")
        if not os.path.isfile(path):
            self.skipTest("scenario map not present")
        md = load_map(self.xdb, path, resolve_models=False)
        wood = next(o for o in md.objects if o.extra.get("sharedType") == "TREASURE_WOOD")
        self.assertEqual(wood.extra["activeTiles"], [[0, 0]])
        self.assertEqual(wood.extra["minResource"], 4)
        self.assertEqual(wood.extra["maxResource"], 7)
        gold = next(o for o in md.objects if o.extra.get("sharedType") == "TREASURE_GOLD")
        self.assertEqual((gold.extra["minResource"], gold.extra["maxResource"]), (5, 10))
        arena = next(o for o in md.objects if o.extra.get("sharedType") == "BUILDING_ARENA")
        self.assertTrue(arena.extra.get("activeTiles"))
        for tile in arena.extra["activeTiles"]:
            self.assertEqual(len(tile), 2, tile)

    def test_map_floor_indices_follow_shipped_lua_constants(self):
        """The source defines GROUND=0 and UNDERGROUND=1; don't swap values
        while importing map objects for Unity's layer selection.
        """
        script = os.path.join(DATA, "scripts", "advmap-startup.lua")
        with open(script, "r", encoding="utf-8", errors="replace") as fh:
            source = fh.read()
        self.assertRegex(source, r"(?m)^\s*GROUND\s*=\s*0\s*$")
        self.assertRegex(source, r"(?m)^\s*UNDERGROUND\s*=\s*1\s*$")
        path = os.path.join(DATA, "Maps", "Scenario", "A2C0M0", "map.xdb")
        if not os.path.isfile(path):
            self.skipTest("scenario map not present")
        md = load_map(self.xdb, path, resolve_models=False)
        self.assertTrue(md.objects)
        self.assertEqual({o.floor for o in md.objects}, {0})

    def test_blocked_tiles_are_tile_offsets(self):
        path = os.path.join(DATA, "Maps", "CombatArenas", "AbandonedMine", "AbandonedMine.xdb")
        md = load_map(self.xdb, path)
        for o in md.objects:
            for tile in o.blocked:
                self.assertEqual(len(tile), 2, tile)

    def test_positions_lie_inside_the_map(self):
        path = os.path.join(DATA, "Maps", "CombatArenas", "AbandonedMine", "AbandonedMine.xdb")
        md = load_map(self.xdb, path)
        for o in md.objects:
            self.assertGreaterEqual(o.pos[0], 0)
            self.assertLessEqual(o.pos[0], md.tile_x)
            self.assertGreaterEqual(o.pos[1], 0)
            self.assertLessEqual(o.pos[1], md.tile_y)

    def test_map_id_uses_the_folder_not_the_file_name(self):
        path = os.path.join(DATA, "Maps", "Scenario", "A2C1M5", "map.xdb")
        if not os.path.isfile(path):
            self.skipTest("scenario map not present")
        from homm5import.maps import map_id
        self.assertEqual(map_id(path), "A2C1M5")

    def test_only_advmapdesc_roots_are_maps(self):
        """data/Maps holds 1176 .xdb files; only 142 are maps.

        The rest are sounds, static object records, textures, AdvMapDescTag
        files, heroes, scripts, tents and fog volumes.  Treating every .xdb
        there as a map yields a thousand zero-tile empty maps.
        """
        from homm5import.maps import is_map_desc, iter_map_files
        xdb = XDB(DATA)
        everything = iter_map_files(xdb)
        maps = [p for p in everything if is_map_desc(p)]
        self.assertGreater(len(everything), 1000)
        self.assertGreater(len(maps), 100)
        self.assertLess(len(maps), len(everything) // 4,
                        "most files under data/Maps are not maps")
        for path in maps:
            with open(path, "rb") as fh:
                head = fh.read(200)
            self.assertIn(b"<AdvMapDesc", head, path)

    def test_every_map_gets_a_distinct_id(self):
        """Maps/Multiplayer/S1 and Maps/SingleMissions/S1 both reduce to 'S1'.

        Without disambiguation the second overwrites the first on disk.
        """
        from homm5import.maps import is_map_desc, iter_map_files, unique_map_ids
        xdb = XDB(DATA)
        maps = [p for p in iter_map_files(xdb, only_map_descs=True)]
        ids = unique_map_ids(maps)
        self.assertEqual(len(set(ids.values())), len(maps),
                         "map ids collide, files would overwrite each other")

    def test_no_imported_map_is_empty(self):
        """A real map has tiles; a mis-detected one comes out 0x0."""
        from homm5import.maps import iter_map_files
        xdb = XDB(DATA)
        empty = 0
        checked = 0
        for path in iter_map_files(xdb, only_map_descs=True):
            md = load_map(xdb, path, resolve_models=False)
            checked += 1
            if md.tile_x == 0 or not md.objects:
                empty += 1
        self.assertGreater(checked, 100)
        self.assertEqual(empty, 0, f"{empty} maps came out empty")

    def test_every_shipped_advmap_object_kind_is_imported(self):
        """Guard the completeness of the map-object union against the XDB corpus."""
        from xml.etree import ElementTree as ET
        from homm5import.maps import OBJECT_TYPES, iter_map_files
        xdb = XDB(DATA)
        observed = set()
        for path in iter_map_files(xdb, only_map_descs=True):
            root = ET.parse(path).getroot()
            base = xdb.href_from_path(path)
            for item in xdb.items(root.find("objects")):
                inner = list(item)[0] if len(item) else xdb.resolve_field(item, base)
                if inner is not None:
                    observed.add(inner.tag)
        self.assertGreaterEqual(len(observed), 20)
        self.assertEqual(observed - set(OBJECT_TYPES), set(),
                         "new map object kinds need an importer handler")


class AssetNamingTest(unittest.TestCase):
    """Unity rejects asset names that start with a space or hold ? < > : * | "."""

    def test_the_shipped_offending_file_is_renamed(self):
        from homm5import.cli import safe_relative_path
        src = "Textures/Effects/_Various/ Balor_swordClot_red_NoADD.(Texture).dds"
        out = safe_relative_path(src)
        self.assertFalse(any(part.startswith(" ") for part in out.split("/")), out)
        self.assertTrue(os.path.isfile(os.path.join(DATA, src)),
                        "the file this guards against is no longer in the data")

    def test_forbidden_characters_are_replaced(self):
        from homm5import.cli import safe_relative_path
        for ch in '?<>:*|"':
            out = safe_relative_path("Textures/a%sb.dds" % ch)
            self.assertNotIn(ch, out)

    def test_clean_paths_are_untouched(self):
        from homm5import.cli import safe_relative_path
        p = "Textures/Arenas/CombatArena/x-cTower.(Texture).dds"
        self.assertEqual(safe_relative_path(p), p)

    def test_no_shipped_dds_still_produces_a_bad_name(self):
        from homm5import.cli import safe_relative_path
        for path in glob.glob(os.path.join(DATA, "**", "*.dds"), recursive=True):
            rel = os.path.relpath(path, DATA).replace(os.sep, "/")
            out = safe_relative_path(rel)
            for part in out.split("/"):
                self.assertFalse(part.startswith(" "), path)
                self.assertFalse(any(c in part for c in '?<>:*|"'), path)

    def test_out_splits_forward_slashes_into_components(self):
        """A nested source path must not become one component with separators in it."""
        import tempfile
        from homm5import.cli import _out
        with tempfile.TemporaryDirectory() as tmp:
            got = _out(tmp, "Textures", "Textures/A/B.dds.png")
            self.assertTrue(os.path.isdir(os.path.join(tmp, "Textures", "Textures", "A")))
            self.assertEqual(os.path.basename(got), "B.dds.png")


class ObjectTextureTest(unittest.TestCase):
    """Object materials must resolve to a .dds the importer actually converts."""

    def setUp(self):
        from homm5import.xdb import XDB
        from homm5import.maps import iter_map_files, unique_map_ids, load_map
        self.xdb = XDB(DATA)
        self.maps = unique_map_ids(iter_map_files(self.xdb, only_map_descs=True))

    def test_texture_field_is_destname_not_filename(self):
        """The Texture XDB has no <FileName>; reading it left every object blank."""
        from homm5import.maps import load_map
        path, _ = sorted(self.maps.items())[0]
        m = load_map(self.xdb, path)
        with_tex = [o for o in m.objects if o.textures]
        self.assertGreater(len(with_tex), 0,
                           "no object resolved a texture -- DestName lookup is broken")

    def test_resolved_textures_are_keys_the_manifest_uses(self):
        """Keys must match Textures/_manifest.json exactly, or lookup misses."""
        from homm5import.maps import load_map
        checked = 0
        for path, _mid in sorted(self.maps.items())[:5]:
            for o in load_map(self.xdb, path).objects:
                for t in o.textures:
                    self.assertTrue(t.startswith("/"), t)
                    self.assertTrue(os.path.isfile(os.path.join(DATA, t.lstrip("/"))),
                                    "no such dds: %s" % t)
                    checked += 1
        self.assertGreater(checked, 100)

    def test_majority_of_objects_are_textured(self):
        from homm5import.maps import load_map
        total = with_tex = 0
        for path, _mid in sorted(self.maps.items())[:8]:
            for o in load_map(self.xdb, path).objects:
                total += 1
                if o.textures:
                    with_tex += 1
        self.assertGreater(with_tex / max(total, 1), 0.5,
                           "only %d/%d objects textured" % (with_tex, total))


class TerrainLayerTest(unittest.TestCase):
    """Container tag 04: painted ground textures and their coverage planes."""

    def setUp(self):
        import glob
        from homm5import.terrain import decode_file
        self.decode_file = decode_file
        self.files = sorted(
            glob.glob(os.path.join(DATA, "Maps", "**", "GroundTerrain.bin"), recursive=True)
            + glob.glob(os.path.join(DATA, "Maps", "**", "UndergroundTerrain.bin"), recursive=True))

    def test_layer_count_matches_what_the_file_declares(self):
        """_layers raises if the declared count and the decoded count disagree."""
        for path in self.files[:40]:
            terr = self.decode_file(path)
            self.assertGreaterEqual(len(terr.layers), 1, path)
            for layer in terr.layers:
                self.assertEqual(len(layer.weights), layer.count, path)
                self.assertEqual(layer.width, terr.tile_x + 1, path)
                self.assertEqual(layer.height, terr.tile_y + 1, path)

    def test_base_layer_is_always_fully_opaque(self):
        """Layer 0 is a constant 255: the base covers everything, always."""
        for path in self.files[:40]:
            base = self.decode_file(path).layers[0]
            self.assertEqual(set(base.weights), {255}, path)

    def test_every_layer_href_resolves_to_a_real_dds(self):
        from homm5import.xdb import XDB, advmap_tile
        xdb = XDB(DATA)
        checked = 0
        for path in self.files[:20]:
            for layer in self.decode_file(path).layers:
                info = advmap_tile(xdb, layer.href)
                self.assertIsNotNone(info, "not an AdvMapTile: " + layer.href)
                self.assertTrue(info["texture"], "no texture for " + layer.href)
                self.assertTrue(
                    os.path.isfile(os.path.join(DATA, info["texture"].lstrip("/"))),
                    "missing dds: " + str(info["texture"]))
                checked += 1
        self.assertGreater(checked, 20)

    def test_lowercased_hrefs_still_resolve(self):
        """Some hrefs are lowercase on disk-cased folders; Windows folds case and
        the game never noticed, but a case-sensitive filesystem loses the layer."""
        from homm5import.xdb import XDB, advmap_tile
        xdb = XDB(DATA)
        lowered = 0
        for path in self.files[:60]:
            for layer in self.decode_file(path).layers:
                info = advmap_tile(xdb, layer.href)
                self.assertIsNotNone(info, "unresolved: " + layer.href)
                self.assertTrue(info["texture"], "no texture: " + layer.href)
                if layer.href != layer.href.replace(layer.href.split("/")[1],
                                                    layer.href.split("/")[1]):
                    pass
                if any(c.islower() for c in layer.href.split("/")[1][:3]):
                    lowered += 1
        self.assertGreater(lowered, 0,
                           "expected some lowercased hrefs in the data; sample changed?")

    def test_priorities_increase_with_specificity(self):
        """Base dirt/grass paints first; roads and variants sit on top."""
        for path in self.files[:15]:
            from homm5import.xdb import XDB, advmap_tile
            xdb = XDB(DATA)
            pris = [advmap_tile(xdb, L.href)["priority"]
                    for L in self.decode_file(path).layers]
            if len(pris) > 1:
                self.assertLess(pris[0], max(pris),
                                "base layer is not the lowest priority: %s" % pris)
                break


class MaterialExportTest(unittest.TestCase):
    """Model materials must carry the alpha mode, not just a texture path."""

    def setUp(self):
        from homm5import.xdb import XDB
        from homm5import.maps import iter_map_files, unique_map_ids, load_map
        self.xdb = XDB(DATA)
        self.maps = unique_map_ids(iter_map_files(self.xdb, only_map_descs=True))

    def _materials(self, n=6):
        out = []
        for path, _mid in sorted(self.maps.items())[:n]:
            for o in load_map(self.xdb, path).objects:
                out.extend(o.materials)
        return out

    def test_materials_are_exported_alongside_textures(self):
        mats = self._materials()
        self.assertGreater(len(mats), 500)
        for m in mats:
            self.assertIn("alphaMode", m)
            self.assertIn("twoSided", m)
            self.assertIn("texture", m)

    def test_alpha_test_materials_exist(self):
        """AM_ALPHA_TEST is the common case; if none appear the field is misread."""
        modes = set(m["alphaMode"] for m in self._materials())
        self.assertIn("AM_ALPHA_TEST", modes)
        self.assertIn("AM_OPAQUE", modes)

    def test_alpha_mode_is_never_empty(self):
        for m in self._materials():
            self.assertTrue(m["alphaMode"].startswith("AM_"), m["alphaMode"])

    def test_material_texture_matches_the_texture_list(self):
        """materials[i].texture and textures[i] must agree, or the runtime picks
        the wrong one depending on which field it reads."""
        from homm5import.maps import load_map
        checked = 0
        for path, _mid in sorted(self.maps.items())[:5]:
            for o in load_map(self.xdb, path).objects:
                if not o.materials:
                    continue
                from_mats = [m["texture"] for m in o.materials if m["texture"]]
                self.assertEqual(from_mats, o.textures)
                checked += 1
        self.assertGreater(checked, 100)


class Gr2Test(unittest.TestCase):
    """Granny GR2 reader, checked only against redundancies the data carries."""

    ANIM_DIR = os.path.join(DATA, "bin", "animations")

    def _files(self):
        import glob
        return sorted(glob.glob(os.path.join(self.ANIM_DIR, "*")))

    def test_every_header_satisfies_its_own_arithmetic(self):
        """headerSize == 88 + 44*sectionCount and fileSize == len(data). Both
        break if sectionArrayOffset is read file-absolute instead of relative to
        byte 32."""
        from homm5import.gr2 import GR2, GR2Error
        files = self._files()
        self.assertGreater(len(files), 3000)
        for path in files:
            g = GR2(open(path, "rb").read())
            self.assertEqual(g.header_size, 88 + 44 * g.section_count)
            self.assertEqual(g.file_size, os.path.getsize(path))
            self.assertEqual(g.version, 6)

    def test_stored_and_packed_census(self):
        """An animation file either stores everything or packs 7 of 8 sections
        with Oodle1; there is no third shape."""
        from homm5import.gr2 import GR2
        stored = packed = 0
        for path in self._files():
            g = GR2(open(path, "rb").read())
            if g.is_stored():
                self.assertEqual(g.section_count, 1)
                stored += 1
            else:
                # Eight sections, seven of them packed. Which one is stored
                # varies by file, so the shape is what is asserted, not indices.
                self.assertEqual(g.section_count, 8)
                self.assertEqual(len(g.compressed_sections()), 7)
                packed += 1
        self.assertEqual(stored, 2816)
        self.assertEqual(packed, 577)

    def _first_stored(self):
        from homm5import.gr2 import GR2
        for path in self._files():
            if GR2(open(path, "rb").read()).is_stored():
                return path
        self.fail("no stored animation in the library")

    def test_section_payload_is_sliced_once(self):
        """A section is cut out of the file once and the bytes reused.

        Slicing per read instead is quadratic: every knot in a curve copied the
        entire section, which is why a single animation took minutes. The
        identity check is what keeps the cache from being dropped again.
        """
        from homm5import.gr2 import GR2
        g = GR2(open(self._first_stored(), "rb").read())
        self.assertIs(g.section_bytes(0), g.section_bytes(0))

    def test_knots_use_the_scalar_fast_path(self):
        """Knots and controls are arrays of a one-field Real32 struct.

        ``_read_array`` reads a run of them with one ``struct.unpack`` instead
        of building a dict per knot; over the library that is the difference
        between minutes and hours. ``_curve`` flattens either shape, so nothing
        else would notice the slow one coming back -- this test does.
        """
        from homm5import import gr2
        g = gr2.GR2(open(self._first_stored(), "rb").read())

        def find(sec, off, seen):
            for m in g.members(sec, off):
                if m.name == "Knots" and m.kind == gr2.REFERENCE_TO_ARRAY:
                    return m
                if m.type_sec >= 0 and (m.type_sec, m.type_off) not in seen:
                    seen.add((m.type_sec, m.type_off))
                    hit = find(m.type_sec, m.type_off, seen)
                    if hit is not None:
                        return hit
            return None

        m = find(g.root_type[0], g.root_type[1], set())
        self.assertIsNotNone(m, "no Knots array in the type tree")
        self.assertEqual(g._single_scalar(m.type_sec, m.type_off), gr2.REAL32)

    def _extract(self, n=40):
        from homm5import.gr2 import GR2, extract
        out = []
        for path in self._files():
            g = GR2(open(path, "rb").read())
            if not g.is_stored():
                continue
            out.append(extract(g.root()))
            if len(out) >= n:
                break
        return out

    def test_exactly_one_root_bone(self):
        for data in self._extract():
            for sk in data["skeletons"]:
                roots = [b for b in sk["bones"] if b["parent"] == -1]
                self.assertEqual(len(roots), 1, sk["name"])
                for b in sk["bones"]:
                    self.assertTrue(-1 <= b["parent"] < len(sk["bones"]), b["name"])

    def test_curve_controls_match_knots_times_width(self):
        """3 per knot for a position, 4 for a quaternion, 9 for scale-shear.
        Measured over 17366 tracks in the reference library; every curve is one
        of the three."""
        checked = 0
        for data in self._extract():
            for clip in data["animations"]:
                for t in clip["tracks"]:
                    for key, width in (("position", 3), ("orientation", 4),
                                       ("scaleShear", 9)):
                        c = t[key]
                        if not c or not c["knots"]:
                            continue
                        self.assertEqual(len(c["controls"]),
                                         len(c["knots"]) * width,
                                         "%s %s" % (t["name"], key))
                        checked += 1
        self.assertGreater(checked, 500)

    def test_knots_lie_inside_the_clip(self):
        for data in self._extract():
            for clip in data["animations"]:
                dur = clip["duration"]
                self.assertGreater(dur, 0.0)
                for t in clip["tracks"]:
                    for key in ("position", "orientation", "scaleShear"):
                        c = t[key]
                        if not c or not c["knots"]:
                            continue
                        self.assertGreaterEqual(min(c["knots"]), -1e-4)
                        self.assertLessEqual(max(c["knots"]), dur + 1e-3,
                                             "%s %s" % (t["name"], key))

    def test_tracks_address_bones_by_name(self):
        """A track binds to a bone by name, so a clip must cover the rig it
        animates. The guard the reference library keeps is coverage: a clip
        tracking under half the bones is addressing some other scheme."""
        for data in self._extract(25):
            sk = data["skeletons"][0]
            clip = data["animations"][0]
            names = {b["name"] for b in sk["bones"]}
            tracked = {t["name"] for t in clip["tracks"]}
            self.assertGreater(len(tracked & names) / max(len(names), 1), 0.5,
                               "%s: %d of %d bones" % (clip["name"],
                                                       len(tracked & names),
                                                       len(names)))

    def test_the_exporter_identifies_itself(self):
        """A type-tree reader that had gone wrong would not recover this string."""
        from homm5import.gr2 import GR2
        seen = 0
        for path in self._files():
            g = GR2(open(path, "rb").read())
            if not g.is_stored():
                continue
            seen += 1
            r = g.root()
            self.assertEqual(r["ExporterInfo"]["ExporterName"],
                             "Granny Standard Exporter, SDK version 2.5.0.5")
            self.assertEqual(r["ArtToolInfo"]["FromArtToolName"], "Maya")
            if seen >= 5:
                break


class SkinBindingTest(unittest.TestCase):
    """Record tag 4 of bin/Geometries: the vertex-to-bone binding.

    24 bytes per POSITION: 4 float32 weights, the same 4 quantized to 0..255,
    then 4 bone indices. Reading the indices at 16 returns the quantized
    weights, which is what this guards against.
    """

    def _skinned(self, limit=40):
        import glob
        from homm5import.geometry import decode_file
        out = []
        for path in sorted(glob.glob(os.path.join(DATA, "bin", "Geometries", "*"))):
            try:
                geo = decode_file(path)
            except Exception:
                continue
            for mesh in geo.meshes:
                for grp in mesh.groups:
                    if grp.bone_weights:
                        out.append((path, grp))
                        break
            if len(out) >= limit:
                break
        return out

    def test_weights_sum_to_one(self):
        """The oracle: a bound position's four weights sum to exactly 1.000. An
        all-zero record is a position no bone claims, and is allowed -- but a
        partial sum means the stride or the offset is wrong."""
        groups = self._skinned()
        self.assertGreater(len(groups), 5)
        verts = unbound = bound = 0
        for _path, grp in groups:
            for w in grp.bone_weights:
                total = sum(w)
                # All-zero is a position no bone claims -- static meshes carry an
                # unused tag-4 array. Anything between zero and one means the
                # stride or the offset is wrong, which is what this catches.
                if total == 0.0:
                    unbound += 1
                else:
                    self.assertAlmostEqual(total, 1.0, places=3)
                    bound += 1
                verts += 1
        self.assertGreater(verts, 1000)
        self.assertGreater(bound, 1000, "no bound positions found at all")

    def test_quantized_slot_is_not_the_index_slot(self):
        """A vertex weighted 1.0 quantizes to 255. If the indices were read at
        the quantized offset, 255 would be the commonest index."""
        for _path, grp in self._skinned(15):
            flat = [i for t in grp.bone_indices for i in t]
            self.assertNotIn(255, flat,
                             "255 among bone indices: reading the quantized weights")

    def test_indices_are_plausible_bone_numbers(self):
        """Skeletons run to a few dozen bones; 87 was the largest seen."""
        for _path, grp in self._skinned(25):
            for t in grp.bone_indices:
                for i in t:
                    self.assertLess(i, 200, "implausible bone index %d" % i)

    def test_skin_arrays_line_up_with_render_vertices(self):
        """Skin is stored per position and must be remapped onto render vertices,
        or every triangle would be weighted by the wrong bone."""
        for _path, grp in self._skinned(25):
            self.assertEqual(len(grp.bone_weights), len(grp.bone_indices))
            self.assertEqual(len(grp.bone_weights), grp.vertex_count)

    def test_some_meshes_really_blend(self):
        """Not every vertex is bound to a single bone; if none blend, the index
        slot is probably still being misread."""
        blended = 0
        for _path, grp in self._skinned(40):
            for t, w in zip(grp.bone_indices, grp.bone_weights):
                if len(set(t)) > 1 and sum(1 for x in w if x > 0.01) > 1:
                    blended += 1
        self.assertGreater(blended, 0)


class GlbSkinExportTest(unittest.TestCase):
    """A skinned mesh must carry JOINTS_0/WEIGHTS_0 or the rig has nothing to bind."""

    def _skinned_geometry(self):
        import glob
        from homm5import.geometry import decode_file
        for path in sorted(glob.glob(os.path.join(DATA, "bin", "Geometries", "*"))):
            try:
                geo = decode_file(path)
            except Exception:
                continue
            if any(gr.bone_indices for m in geo.meshes for gr in m.groups):
                return geo
        self.fail("no skinned geometry in the data")

    def _doc(self, blob):
        import struct
        self.assertEqual(struct.unpack_from("<I", blob, 0)[0], 0x46546C67)
        jlen = struct.unpack_from("<I", blob, 12)[0]
        return json.loads(blob[20:20 + jlen].decode("utf8"))

    def test_skinned_mesh_carries_joint_and_weight_attributes(self):
        from homm5import.gltf import geometry_to_glb
        doc = self._doc(geometry_to_glb(self._skinned_geometry()))
        with_skin = 0
        for mesh in doc["meshes"]:
            attrs = mesh["primitives"][0]["attributes"]
            if "JOINTS_0" not in attrs:
                continue
            with_skin += 1
            self.assertIn("WEIGHTS_0", attrs)
            j = doc["accessors"][attrs["JOINTS_0"]]
            w = doc["accessors"][attrs["WEIGHTS_0"]]
            self.assertEqual(j["type"], "VEC4")
            self.assertEqual(j["componentType"], 5121)   # UNSIGNED_BYTE
            self.assertEqual(w["type"], "VEC4")
            self.assertEqual(w["componentType"], 5126)   # FLOAT
            pos = doc["accessors"][attrs["POSITION"]]
            self.assertEqual(j["count"], pos["count"])
            self.assertEqual(w["count"], pos["count"])
        self.assertGreater(with_skin, 0)

    def test_an_unskinned_mesh_carries_neither(self):
        """Static props have an unused tag-4 array; writing empty skin attributes
        for them would make Unity expect a rig that does not exist."""
        import glob
        from homm5import.geometry import decode_file
        from homm5import.gltf import geometry_to_glb
        for path in sorted(glob.glob(os.path.join(DATA, "bin", "Geometries", "*"))):
            try:
                geo = decode_file(path)
            except Exception:
                continue
            groups = [gr for m in geo.meshes for gr in m.groups]
            if not groups or any(gr.bone_indices for gr in groups):
                continue
            doc = self._doc(geometry_to_glb(geo))
            for mesh in doc["meshes"]:
                attrs = mesh["primitives"][0]["attributes"]
                self.assertNotIn("JOINTS_0", attrs)
                self.assertNotIn("WEIGHTS_0", attrs)
            return
        self.skipTest("no unskinned geometry found")


class ModelAnimationTest(unittest.TestCase):
    """Character -> Model/AnimSet -> BasicSkelAnim uid -> bin/animations/<uid>."""

    @classmethod
    def setUpClass(cls):
        from homm5import.xdb import XDB
        from homm5import.gamedata import load_model_animations
        cls.map = load_model_animations(XDB(DATA))

    def test_models_carry_a_skeleton_uid(self):
        self.assertGreater(len(self.map), 300)
        for uid, entry in self.map.items():
            self.assertTrue(entry["skeleton"], uid)

    def test_clip_kinds_are_the_ones_the_game_uses(self):
        kinds = set()
        for entry in self.map.values():
            kinds.update(entry["arena"])
            kinds.update(entry["advmap"])
        for expected in ("idle00", "move", "death", "attack00", "hit"):
            self.assertIn(expected, kinds)

    def test_clip_uids_resolve_to_real_files(self):
        """Allowing a small tail: a couple of AnimSets name clips the shipped
        data does not carry, and dropping the whole entry over them would lose
        the 98% that do resolve."""
        present = missing = 0
        for entry in self.map.values():
            for group in ("arena", "advmap"):
                for clip in entry[group].values():
                    if os.path.isfile(os.path.join(DATA, "bin", "animations", clip)):
                        present += 1
                    else:
                        missing += 1
        self.assertGreater(present, 1000)
        self.assertLess(missing, present / 20)

    def test_keyed_by_geometry_uid(self):
        """The runtime looks an object up by the geometry uid it already has, so
        a key that is anything else is a dead lookup."""
        from homm5import.xdb import XDB
        xdb = XDB(DATA)
        checked = 0
        for uid in list(self.map)[:40]:
            self.assertTrue(
                os.path.isfile(os.path.join(DATA, "bin", "Geometries", uid)),
                "not a geometry uid: " + uid)
            checked += 1
        self.assertEqual(checked, min(40, len(self.map)))

    def test_adventure_idles_dominate(self):
        """An adventure-map model usually holds exactly one animation, idle00."""
        with_idle = sum(1 for e in self.map.values() if "idle00" in e["advmap"])
        self.assertGreater(with_idle, 100)


class WorldScaleTest(unittest.TestCase):
    """Pins the world-units-per-tile that TerrainMeshBuilder.TileSize must match.

    The Unity side cannot be executed here, so the fact it rests on is pinned
    instead: a tile is NOT one world unit. Two independent readings say two.
    """

    @classmethod
    def setUpClass(cls):
        from homm5import.xdb import XDB
        from homm5import.maps import iter_map_files, load_map
        cls.xdb = XDB(DATA)
        cls.files = sorted(iter_map_files(cls.xdb, only_map_descs=True))

    def test_object_positions_are_in_tiles_not_world_units(self):
        """Object Pos spans 0..tile_x, so it is a tile index. If it were world
        units it would span twice that, and sampling the height field with it
        would read the wrong cell."""
        import os
        checked = 0
        for f in self.files[::17]:
            tp = os.path.join(os.path.dirname(f), "GroundTerrain.bin")
            if not os.path.isfile(tp):
                continue
            t = terrain.decode_file(tp)
            md = load_map(self.xdb, f, resolve_models=False)
            xs = [o.pos[0] for o in md.objects]
            if not xs or t.height is None:
                continue
            self.assertLessEqual(max(xs), t.tile_x + 1)
            checked += 1
            if checked >= 5:
                break
        self.assertGreater(checked, 0)

    def test_a_two_tile_footprint_is_about_four_units_wide(self):
        """The engine halves an object's world point to get its tile. Objects
        blocked over exactly two tiles measure a median four units across, which
        is the same two-to-one from the model side."""
        import os, statistics
        from homm5import import geometry as G
        pairs = []
        cache = {}
        for f in self.files[::11]:
            md = load_map(self.xdb, f, resolve_models=True)
            for o in md.objects:
                if not o.blocked or not o.geometry_uid:
                    continue
                foot = max(len(o.blocked), max((len(r) for r in o.blocked), default=0))
                if foot != 2:
                    continue
                u = o.geometry_uid
                if u not in cache:
                    path = os.path.join(DATA, "bin", "Geometries", u)
                    cache[u] = G.decode_file(path).bounds() if os.path.isfile(path) else None
                b = cache[u]
                if not b:
                    continue
                (x0, y0, z0), (x1, y1, z1) = b
                pairs.append(max(x1 - x0, y1 - y0))
            if len(pairs) > 40:
                break
        self.assertGreater(len(pairs), 5)
        med = statistics.median(pairs)
        self.assertGreater(med, 3.0, "tile would be one world unit or less")
        self.assertLess(med, 5.5, "tile would be more than two world units")

    def test_heights_reach_tens_of_units(self):
        """Why the horizontal scale matters: whole maps sit far above the 2.0
        default ground, so relief is measured in tens of world units."""
        import struct
        worst = 0.0
        for p in sorted(glob.glob(os.path.join(DATA, "Maps", "**", "GroundTerrain.bin"),
                                  recursive=True))[::47]:
            t = terrain.decode_file(p)
            if t.height is None:
                continue
            _lo, hi, _mean = t.height_stats()
            worst = max(worst, hi)
        self.assertGreater(worst, 20.0)


class TownPayloadTest(unittest.TestCase):
    """An AdvMapTown carries no TownType/Owner/TownName -- those names were read
    for a long time and produced an empty payload for every town on every map."""

    @classmethod
    def setUpClass(cls):
        from homm5import.xdb import XDB
        cls.xdb = XDB(DATA)
        cls.towns = []
        cls.chests = []
        for f in sorted(iter_map_files(cls.xdb, only_map_descs=True))[::11]:
            md = load_map(cls.xdb, f, resolve_models=True)
            for o in md.objects:
                if o.kind == "AdvMapTown":
                    cls.towns.append(o)
                elif o.kind == "AdvMapTreasure" and len(cls.chests) < 30:
                    cls.chests.append(o)

    def test_owner_is_read(self):
        self.assertGreater(len(self.towns), 5)
        owners = {t.extra.get("owner") for t in self.towns}
        self.assertTrue(any(str(o).startswith("PLAYER_") for o in owners),
                        f"no owner resolved from PlayerID: {owners}")

    def test_buildings_are_read(self):
        """A starting town has a hall, a fort and a marketplace at least."""
        withb = [t for t in self.towns if t.extra.get("buildings")]
        self.assertGreater(len(withb), len(self.towns) // 2)
        types = {b["type"] for t in withb for b in t.extra["buildings"]}
        for expected in ("TB_TOWN_HALL", "TB_FORT"):
            self.assertIn(expected, types)

    def test_only_random_towns_omit_the_faction(self):
        """A fixed AdvMapTownShared names its faction; RandomTown does not.
        Keep the former and do not guess the latter at import time."""
        random_towns = [t for t in self.towns if "RandomTown.xdb" in (t.shared_href or "")]
        self.assertTrue(random_towns)
        for t in random_towns:
            self.assertNotIn("townType", t.extra)
        for t in self.towns:
            if t not in random_towns:
                self.assertTrue(t.extra.get("townType"), t.shared_href)

    def test_chest_payload(self):
        self.assertGreater(len(self.chests), 0)
        for c in self.chests:
            self.assertIn("isCustom", c.extra)
            self.assertIsInstance(c.extra["amount"], int)


@requires_data
class TownScreenDataTest(unittest.TestCase):
    """The tables a town screen is built from.

    Every assertion is a cross-check between two shipped sources rather than a
    golden file: a building's dependencies against the building table itself, a
    dwelling's weekly number against the creature it breeds, a faction's
    building list against the files on disk, a town's ``TOWN_*`` enum against the
    ``AdvMapTownShared`` record that names its buildings.
    """

    @classmethod
    def setUpClass(cls):
        cls.xdb = XDB(DATA)
        cls.loc = Localization(TEXTS) if os.path.isdir(TEXTS) else None
        cls.creatures = load_creatures(cls.xdb, cls.loc)
        cls.raw_buildings = load_town_buildings(cls.xdb, cls.loc)
        cls.buildings = [dict(r) for r in cls.raw_buildings]
        cls.by_enum = {r["enumId"]: r for r in cls.creatures.values() if r.get("enumId")}
        cls.grown = fill_dwelling_growth(cls.buildings, cls.by_enum)
        fill_grail_values(cls.buildings)
        cls.by_key = {r["key"]: r for r in cls.buildings}
        cls.town_types = load_town_types(cls.xdb, cls.loc)
        # The eight factions a player can actually own; TOWN_SPECIAL /
        # TOWN_NO_TYPE / TOWN_RANDOM_TYPE are markers, not towns.
        cls.playable = {k: v for k, v in cls.town_types.items()
                        if v["faction"] and v["faction"] != "RandomTown"}

    def test_building_keys_are_faction_qualified_and_unique(self):
        """Every faction ships a Fort.xdb, so the folder has to be in the key."""
        self.assertEqual(len(self.buildings), 328)
        self.assertEqual(len(self.by_key), len(self.buildings))
        folders = {os.path.basename(p) for p in glob.glob(
            os.path.join(DATA, "GameMechanics", "TownBuildingSharedStats", "*"))}
        self.assertEqual({r["faction"] for r in self.buildings}, folders)
        for rec in self.buildings:
            self.assertEqual(rec["key"], rec["faction"] + "/" + rec["id"])

    def test_every_dependency_resolves_and_only_one_crosses_a_faction(self):
        """An upgraded dwelling names the building it replaces, by relative href.

        Exactly one record breaks the pattern: Necromancy's Ruined Tower depends
        on ``/Preserve/Fort/Fort.xdb`` -- an absolute href into another faction's
        folder, where every other dependency in the table is relative. A
        Necropolis can never hold a Sylvan Fort, so the shipped row is a
        copy-paste bug; the runtime drops cross-faction prerequisites rather
        than making the building unbuildable. Pinned here so a change shows up.
        """
        with_deps = [r for r in self.buildings if r["dependencies"]]
        self.assertGreater(len(with_deps), 100)
        cross = []
        for rec in with_deps:
            for dep in rec["dependencies"]:
                self.assertIn(dep, self.by_key, f"{rec['key']} -> {dep}")
                if self.by_key[dep]["faction"] != rec["faction"]:
                    cross.append((rec["key"], dep))
        self.assertEqual(cross, [("Necromancy/RuinedTower", "Preserve/Fort")])
        # The chain is a tree: upgrading never leads back to itself.
        for rec in with_deps:
            seen, stack = {rec["key"]}, list(rec["dependencies"])
            while stack:
                dep = stack.pop()
                self.assertNotIn(dep, seen, f"dependency cycle at {rec['key']}")
                seen.add(dep)
                stack.extend(self.by_key[dep]["dependencies"])

    def test_dwelling_growth_is_the_creature_weekly_growth(self):
        """No TownBuildingSharedStats record carries <Growth>; the creature does."""
        dwellings = [r for r in self.buildings if r["type"].startswith("TB_DWELLING")]
        self.assertEqual(len(dwellings), 126)
        self.assertGreaterEqual(self.grown, 110)
        for rec in dwellings:
            if rec["creature"] == "CREATURE_UNKNOWN":
                continue                        # RandomTown's empty placeholders
            creature = self.by_enum.get(rec["creature"])
            self.assertIsNotNone(creature, rec["key"])
            self.assertEqual(rec["growth"], int(creature["WeeklyGrowth"]), rec["key"])
            self.assertGreater(rec["growth"], 0)
        self.assertEqual(self.by_key["Haven/Peasant_Huts"]["growth"], 22)
        self.assertEqual(self.by_key["Haven/Peasant_Huts"]["cost"]["Gold"], 500)

    def test_every_faction_ships_a_complete_town(self):
        """Seven dwellings, four halls, three fort tiers, five guild levels."""
        for faction in sorted({r["faction"] for r in self.buildings} - {"RandomTown"}):
            rows = [r for r in self.buildings if r["faction"] == faction]
            types = {}
            for r in rows:
                types[r["type"]] = types.get(r["type"], 0) + 1
            for tier in range(1, 8):
                self.assertEqual(types.get("TB_DWELLING_%d" % tier), 2,
                                 f"{faction} dwelling {tier}")
            self.assertEqual(types.get("TB_TOWN_HALL"), 4, faction)
            self.assertEqual(types.get("TB_FORT"), 3, faction)
            self.assertEqual(types.get("TB_MAGIC_GUILD"), 5, faction)
            self.assertEqual(types.get("TB_GRAIL"), 1, faction)
            # A dwelling pair is the base building and its upgrade; the upgrade
            # costs more, depends on the base, and may open a second creature
            # (Academy's Manufactory recruits Master Gremlins *or* Saboteurs).
            for tier in range(1, 8):
                pair = sorted((r for r in rows if r["type"] == "TB_DWELLING_%d" % tier),
                              key=lambda r: r["upgrade"])
                base, upgraded = pair
                self.assertEqual([base["upgrade"], upgraded["upgrade"]],
                                 ["BLD_UPG_1", "BLD_UPG_2"])
                self.assertEqual(upgraded["dependencies"], [base["key"]])
                self.assertGreaterEqual(upgraded["cost"]["Gold"], base["cost"]["Gold"])
                self.assertNotEqual(upgraded["creature"], base["creature"])
                if upgraded["creature2"]:
                    self.assertIn(upgraded["creature2"], self.by_enum)

    def test_town_types_list_every_building_of_their_faction(self):
        """AdvMapTownShared.buildings is the link from TOWN_* to a building folder.

        Neither TownTypesInfo (whose textFort for TOWN_STRONGHOLD points at
        Inferno's texts) nor the creature folders (Orcs, Dwarf, Necropolis) can
        produce the building folder, so this list is what the town screen's
        contents come from -- and it has to be exactly the folder on disk.
        """
        self.assertEqual(len(self.playable), 8)
        for town_id, rec in sorted(self.playable.items()):
            folder = os.path.join(DATA, "GameMechanics", "TownBuildingSharedStats",
                                  rec["faction"])
            on_disk = len(glob.glob(os.path.join(folder, "*", "*.xdb")))
            self.assertEqual(len(rec["buildings"]), on_disk, town_id)
            self.assertEqual({k.split("/")[0] for k in rec["buildings"]}, {rec["faction"]})
            for key in rec["buildings"]:
                self.assertIn(key, self.by_key, f"{town_id} -> {key}")
            # The whole folder, no more and no less.
            self.assertEqual(set(rec["buildings"]),
                             {r["key"] for r in self.buildings
                              if r["faction"] == rec["faction"]}, town_id)
        self.assertEqual(self.playable["TOWN_HEAVEN"]["faction"], "Haven")
        self.assertEqual(self.playable["TOWN_FORTRESS"]["faction"], "Dwarves")
        self.assertEqual(self.playable["TOWN_STRONGHOLD"]["faction"], "Stronghold")

    def test_town_type_numbers_match_the_shipped_table(self):
        heaven = self.playable["TOWN_HEAVEN"]
        self.assertEqual(heaven["siloIncome"]["Crystal"], 1)
        self.assertEqual(heaven["siloIncome"]["Gold"], 0)
        self.assertEqual(heaven["nativeWarMachine"], "WAR_MACHINE_BALLISTA")
        self.assertEqual(heaven["moatMaxDamage"], 204)
        self.assertEqual(self.playable["TOWN_ACADEMY"]["moatMaxDamage"], 340)
        self.assertEqual(self.playable["TOWN_NECROMANCY"]["nativeWarMachine"],
                         "WAR_MACHINE_FIRST_AID_TENT")
        # Dungeon's moat poisons; the numbers are the record's own.
        moat = self.playable["TOWN_DUNGEON"]["moatSpells"]
        self.assertEqual(len(moat), 1)
        self.assertEqual(moat[0]["spell"], "SPELL_ABILITY_POISONOUS_ATTACK")
        self.assertEqual((moat[0]["chance"], moat[0]["power"]), (100, 28))
        for rec in self.playable.values():
            self.assertGreater(rec["moatMaxDamage"], 0)
            self.assertNotEqual(rec["nativeWarMachine"], "WAR_MACHINE_NONE")
            self.assertTrue(any(rec["siloIncome"].values()), rec["id"])

    def test_growth_multipliers_are_read_from_the_building_descriptions(self):
        """Citadel +50%, Castle doubling, Grail +50% -- in all eight factions.

        The numbers exist only as sentences in the localized descriptions, so the
        importer parses them. Asserting every faction's rows catches a wording
        change that would otherwise silently drop a town's growth bonus.
        """
        for faction in sorted({r["faction"] for r in self.buildings} - {"RandomTown"}):
            rows = {r["key"]: r for r in self.buildings if r["faction"] == faction}
            citadel = [r for r in rows.values() if r["type"] == "TB_FORT" and r["upgrade"] == "BLD_UPG_2"]
            castle = [r for r in rows.values() if r["type"] == "TB_FORT" and r["upgrade"] == "BLD_UPG_3"]
            self.assertEqual(len(citadel), 1, faction)
            self.assertEqual(len(castle), 1, faction)
            self.assertEqual(citadel[0]["growthMultiplier"], 1.5, citadel[0]["key"])
            self.assertEqual(castle[0]["growthMultiplier"], 2.0, castle[0]["key"])
        # Nothing else in the table claims a growth bonus.
        odd = [r["key"] for r in self.buildings
               if r["growthMultiplier"] != 1.0
               and r["type"] not in ("TB_FORT", "TB_GRAIL")]
        self.assertEqual(odd, [])

    def test_grails_that_omit_their_numbers_take_the_ones_that_state_them(self):
        """Eight Grails, one building, four descriptions with the figures in.

        Haven's Sentinel of Elrath says "+50% weekly growth, 5000 gold daily";
        the Academy's Skyship says only "increases the weekly growth, brings
        gold". The four silent rows take the four explicit rows' numbers, which
        is the one inferred value in the town tables -- pinned here so the
        fallback is visible rather than buried.
        """
        grails = [r for r in self.buildings if r["type"] == "TB_GRAIL"]
        self.assertEqual(len(grails), 8)
        for rec in grails:
            self.assertEqual(rec["growthMultiplier"], 1.5, rec["key"])
            self.assertEqual(rec["incomeGoldPerDay"], 5000, rec["key"])
        # Four rows state the numbers, four do not: the fill touches two fields
        # on each of the silent ones.
        self.assertEqual(fill_grail_values([dict(r) for r in self.raw_buildings]), 8)
        self.assertEqual(fill_grail_values([dict(r) for r in self.buildings]), 0)

    def test_creature_enum_ids_come_from_the_reference_table(self):
        """Maps, spells and dwellings all name creatures CREATURE_*, never by file."""
        table = load_creature_enum_ids(self.xdb)
        with open(os.path.join(DATA, "GameMechanics", "RefTables", "Creatures.xdb"),
                  encoding="utf-8") as fh:
            declared = re.findall(r"<ID>([A-Z_0-9]+)</ID>", fh.read())
        self.assertEqual(sorted(table), sorted(set(declared)))
        self.assertEqual(len(table), 180)
        for cid, href in table.items():
            self.assertTrue(os.path.isfile(os.path.join(DATA, href.lstrip("/"))), href)
        self.assertTrue(table["CREATURE_PEASANT"].endswith("Haven/Peasant.xdb"))
        # Every creature a town dwelling breeds resolves through the enum.
        for rec in self.buildings:
            if rec["creature"] and rec["creature"] != "CREATURE_UNKNOWN":
                self.assertIn(rec["creature"], table, rec["key"])

    def test_map_town_upgrades_resolve_against_the_building_table(self):
        """A map authors TB_* + BLD_UPG_*; the town screen resolves both."""
        towns = []
        for path in sorted(iter_map_files(self.xdb, only_map_descs=True)):
            if len(towns) >= 6:
                break
            md = load_map(self.xdb, path, resolve_models=False)
            towns.extend(o for o in md.objects if o.kind == "AdvMapTown")
        self.assertGreaterEqual(len(towns), 6)
        checked = 0
        for town in towns:
            for state in town.extra["buildings"]:
                self.assertIn("maxUpgrade", state)
                upgrade, cap = state["upgrade"], state["maxUpgrade"]
                if upgrade == "BLD_UPG_NONE":
                    continue
                matches = [r for r in self.buildings
                           if r["type"] == state["type"] and r["upgrade"] == upgrade]
                self.assertTrue(matches, f"{state['type']} {upgrade} in {town.shared_href}")
                # One record per faction: the map says what tier, the town's
                # faction says which building that is.
                self.assertEqual(len({m["faction"] for m in matches}), len(matches))
                self.assertLessEqual(_upgrade_rank(upgrade), _upgrade_rank(cap),
                                     f"{state['type']} starts above its cap")
                checked += 1
        self.assertGreater(checked, 20)


@requires_data
class DtoContractTest(unittest.TestCase):
    """Every JSON field the importer writes has a field in the C# DTO.

    There is no C# compiler in this environment, so the Unity side cannot be
    built here -- but the contract between the two halves can still be checked.
    A key the runtime does not declare is not an error at load time: Newtonsoft
    drops it silently and the value arrives as 0 or null. That is exactly how a
    town screen ends up breeding nothing, so the two sides are compared field by
    field instead.
    """

    DTO_FILE = os.path.join(REPO, "UnityProject", "Assets", "Scripts",
                            "HoMM5.Runtime", "Data", "GameDefs.cs")

    @classmethod
    def setUpClass(cls):
        cls.xdb = XDB(DATA)
        cls.loc = Localization(TEXTS) if os.path.isdir(TEXTS) else None
        cls.dto = _parse_dto_fields(cls.DTO_FILE)

    def test_the_dto_file_declares_the_classes_checked(self):
        for name in ("CreatureDef", "TownBuildingDef", "TownTypeDef", "MapObjectDef",
                     "MapBuildingStateDef", "ArtifactDef", "SkillDef", "HeroClassDef",
                     "WarMachineDef", "SpellDef"):
            self.assertIn(name, self.dto, f"{name} is missing from GameDefs.cs")

    def test_town_building_fields_match_the_dto(self):
        produced = _union_of_keys(load_town_buildings(self.xdb, self.loc))
        self.assertLessEqual(produced, self.dto["TownBuildingDef"])

    def test_town_type_fields_match_the_dto(self):
        produced = _union_of_keys(list(load_town_types(self.xdb, self.loc).values()))
        self.assertLessEqual(produced, self.dto["TownTypeDef"])
        spells = _union_of_keys([s for t in load_town_types(self.xdb, self.loc).values()
                                 for s in t["moatSpells"]])
        self.assertLessEqual(spells, self.dto["TownMoatSpellDef"])

    def test_creature_fields_match_the_dto(self):
        produced = _union_of_keys(list(load_creatures(self.xdb, self.loc).values()))
        self.assertLessEqual(produced, self.dto["CreatureDef"])
        self.assertIn("enumid", produced)
        self.assertIn("enumid", self.dto["CreatureDef"])

    def test_map_object_fields_match_the_dto(self):
        """Towns carry the most payload of any object kind; check one of each."""
        seen = set()
        produced = set()
        for path in sorted(iter_map_files(self.xdb, only_map_descs=True)):
            md = load_map(self.xdb, path, resolve_models=False)
            for o in md.objects:
                if o.kind in seen:
                    continue
                seen.add(o.kind)
                produced |= {k.lower() for k in o.to_json()}
                for b in o.extra.get("buildings") or []:
                    self.assertLessEqual({k.lower() for k in b},
                                         self.dto["MapBuildingStateDef"], o.kind)
                for stack in (o.extra.get("garrison") or []) + (o.extra.get("army") or []):
                    self.assertLessEqual({k.lower() for k in stack},
                                         self.dto["MapArmyStackDef"], o.kind)
            if len(seen) >= 12:
                break
        self.assertIn("AdvMapTown", seen)
        self.assertLessEqual(produced, self.dto["MapObjectDef"])


def _union_of_keys(records) -> set:
    """Every key any record carries, lower-cased for a case-insensitive compare."""
    out = set()
    for rec in records:
        if isinstance(rec, dict):
            out |= {k.lower() for k in rec}
    return out


def _parse_dto_fields(path: str) -> dict:
    """``{class name: {lower-cased field names}}`` from the C# DTO file.

    Only ``public <type> <name>;`` fields are collected -- which is what
    Newtonsoft binds -- so properties and methods are correctly ignored.
    """
    with open(path, encoding="utf-8") as fh:
        text = fh.read()
    out = {}
    for match in re.finditer(r"\bclass\s+(\w+)\b(.*?)(?=\n    (?:public|internal)\s+(?:sealed\s+)?class\s|\Z)",
                             text, re.S):
        name, body = match.group(1), match.group(2)
        fields = set()
        # `= ...;` is a field initializer; `=> ...;` is an expression-bodied
        # property, which Newtonsoft does not bind and must not be counted.
        for field in re.finditer(r"^\s*public\s+(?!class|enum|struct|const)(?:readonly\s+)?"
                                 r"[\w.<>\[\],\s?]+?\s+(\w+)\s*(?:=\s*(?!=)[^;]*)?;",
                                 body, re.M):
            fields.add(field.group(1).lower())
        out[name] = fields
    return out


class ScenarioSetupDataTest(unittest.TestCase):
    """The data the main menu needs to offer a real 1v1.

    A menu that lists nothing is not a menu, and a faction picker with no
    factions is a dead end. These assert the shipped corpus can actually
    populate the new-game flow: scenarios that author two opposing players,
    eight playable factions, and a hero class for each.
    """

    def test_multiplayer_scenarios_author_owners(self):
        """38 of the 40 Multiplayer maps put a PlayerID on their towns.

        Without an authored owner every town is neutral and there is nobody to
        play against, so this is what makes a 1v1 possible at all.
        """
        xdb = XDB(DATA)
        paths = iter_map_files(xdb, only_map_descs=True)
        multiplayer = [p for p in paths if "Multiplayer" in p]
        self.assertGreater(len(multiplayer), 0)

        owned = 0
        two_player = []
        owners_seen = set()
        for path in multiplayer:
            try:
                desc = load_map(xdb, path)
            except Exception:
                continue
            owners = set()
            for obj in desc.objects or []:
                if obj.kind != "AdvMapTown":
                    continue
                owner = obj.extra.get("owner")
                # PLAYER_NONE is the shipped spelling of "neutral", not a player.
                if owner and owner != "PLAYER_NONE":
                    owners.add(owner)
            if owners:
                owned += 1
                owners_seen |= owners
                if len(owners) == 2:
                    two_player.append(path)
        self.assertGreaterEqual(owned, 30, "too few scenarios author town owners")
        self.assertIn("PLAYER_1", owners_seen)
        self.assertIn("PLAYER_2", owners_seen)
        self.assertGreaterEqual(len(two_player), 1,
                                "no scenario is written for exactly two players")

    def test_eight_playable_factions(self):
        """The menu's faction list: eight towns with buildings to run."""
        xdb = XDB(DATA)
        loc = Localization(TEXTS) if os.path.isdir(TEXTS) else None
        town_types = load_town_types(xdb, loc)
        playable = []
        for town_type in _values(town_types):
            faction = town_type.get("faction") or ""
            buildings = town_type.get("buildings") or []
            if not faction or faction == "RandomTown" or not buildings:
                continue
            playable.append(town_type)
        self.assertEqual(len(playable), 8,
                         "expected the eight playable factions, got %d" % len(playable))
        factions = {t["faction"] for t in playable}
        for expected in ("Haven", "Academy", "Necromancy", "Inferno",
                         "Preserve", "Dungeon", "Dwarves", "Stronghold"):
            self.assertIn(expected, factions)

    def test_every_faction_maps_to_a_hero_class(self):
        """The hero list must not be empty for any faction the menu offers."""
        # Mirrors AdventureMapGameState.ClassForTown, which the menu calls to
        # put the faction's own class at the top of the list.
        mapping = {
            "TOWN_HEAVEN": "HERO_CLASS_KNIGHT",
            "TOWN_INFERNO": "HERO_CLASS_DEMON_LORD",
            "TOWN_NECROMANCY": "HERO_CLASS_NECROMANCER",
            "TOWN_PRESERVE": "HERO_CLASS_RANGER",
            "TOWN_ACADEMY": "HERO_CLASS_WIZARD",
            "TOWN_DUNGEON": "HERO_CLASS_WARLOCK",
            "TOWN_FORTRESS": "HERO_CLASS_RUNEMAGE",
            "TOWN_STRONGHOLD": "HERO_CLASS_BARBARIAN",
        }
        xdb = XDB(DATA)
        loc = Localization(TEXTS) if os.path.isdir(TEXTS) else None
        town_types = load_town_types(xdb, loc)
        playable = [t for t in _values(town_types)
                    if (t.get("faction") or "") and t["faction"] != "RandomTown"
                    and (t.get("buildings") or [])]
        self.assertEqual(len(playable), 8)
        for town_type in playable:
            self.assertIn(town_type["id"], mapping,
                          "no hero class for %s" % town_type["id"])



class CSharpNameCheckTest(unittest.TestCase):
    """Undeclared simple names are CS0103, which neither the call checker nor
    the access checker sees. A method that uses a local from another method
    compiled in no environment we had, so this runs the name checker over the
    sources and over a planted corpus, and must catch both kinds of error.
    """

    TOOL = os.path.join(REPO, "tools", "csharp_name_check.py")
    UNITY = os.path.join(REPO, "UnityProject", "Assets", "Scripts")

    def _run(self, root):
        import subprocess
        import sys
        proc = subprocess.run([sys.executable, self.TOOL, root],
                              capture_output=True, text=True, timeout=600)
        return proc.returncode, proc.stdout + proc.stderr

    @unittest.skipUnless(os.path.isdir(os.path.join(REPO, "UnityProject")),
                         "no UnityProject checkout")
    def test_repository_sources_have_no_undeclared_names(self):
        rc, out = self._run(self.UNITY)
        self.assertEqual(rc, 0, out)
        self.assertIn("0 undeclared name(s)", out)

    def test_planted_out_of_scope_names_are_reported(self):
        import tempfile
        code = (
            "public class Planted {\n"
            "    void Helper(int mapObj) { var local = 1; }\n"
            "    public void Uses(int x) {\n"
            "        Helper(mapObj);\n"
            "        var a = local + x;\n"
            "    }\n"
            "    public bool Ok(int y, out int z) { z = y; return Sum(z, y); }\n"
            "    int Sum(int a, int b) => a + b;\n"
            "}\n")
        with tempfile.TemporaryDirectory() as d:
            with open(os.path.join(d, "Planted.cs"), "w", encoding="utf8") as f:
                f.write(code)
            rc, out = self._run(d)
        self.assertEqual(rc, 1, out)
        self.assertIn("'mapObj' is used but never declared", out)
        self.assertIn("'local' is used but never declared", out)
        self.assertNotIn("'x'", out)
        self.assertNotIn("'z'", out)


class CSharpCallCheckTest(unittest.TestCase):
    """The C# side cannot be compiled here, so the call checker is the only
    thing standing between a wrong argument count and a Unity compile error
    found by hand. This test runs it over the real sources and over its own
    self-test corpus, so a checker that silently stops resolving calls fails
    here rather than quietly blessing broken code.
    """

    TOOLS = os.path.join(REPO, "tools")
    UNITY = os.path.join(REPO, "UnityProject")

    def _run(self, *args):
        import subprocess
        import sys
        proc = subprocess.run(
            [sys.executable, os.path.join(self.TOOLS, "csharp_call_check.py"), *args],
            capture_output=True, text=True, timeout=600)
        return proc.returncode, proc.stdout + proc.stderr

    @unittest.skipUnless(os.path.isdir(os.path.join(REPO, "UnityProject")),
                         "no UnityProject checkout")
    def test_repository_sources_have_no_arity_errors(self):
        rc, out = self._run(self.UNITY)
        self.assertEqual(rc, 0, out)
        self.assertIn("every resolved call matches a declaration's arity", out)

    @unittest.skipUnless(os.path.isdir(os.path.join(REPO, "UnityProject")),
                         "no UnityProject checkout")
    def test_checker_actually_resolves_calls(self):
        """A checker that resolves nothing also reports nothing. The count is
        the assertion: if resolution breaks, the repo goes quiet and this fails.
        """
        rc, out = self._run(self.UNITY)
        match = re.search(r"resolved (\d+) calls", out)
        self.assertIsNotNone(match, out)
        self.assertGreater(int(match.group(1)), 1000,
                           "resolution collapsed: " + out)

    def test_checker_self_test_catches_planted_violations(self):
        rc, out = self._run("--self-test")
        self.assertEqual(rc, 0, out)
        self.assertIn("self-test PASSED", out)



def _values(container):
    """Iterate the records of a table that may be a list or an id-keyed dict."""
    if isinstance(container, dict):
        return list(container.values())
    return list(container or [])


def _upgrade_rank(upgrade: str) -> int:
    """BLD_UPG_NONE < BLD_UPG_1 < ... (a map caps a building at a tier)."""
    return 0 if upgrade in ("", "BLD_UPG_NONE") else int(upgrade.rsplit("_", 1)[1])


if __name__ == "__main__":
    unittest.main(verbosity=2)
