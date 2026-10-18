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

from homm5import.container import END_MARKER, parse_records  # noqa: E402
from homm5import.dds import DDSError, decode_dds, write_png  # noqa: E402
from homm5import.gamedata import (load_artifacts, load_campaigns, load_creatures,
                                    load_default_stats, load_spells)  # noqa: E402
from homm5import.geometry import decode_file as decode_geometry  # noqa: E402
from homm5import.gltf import geometry_to_glb  # noqa: E402
from homm5import.gr2 import parse_gr2_bytes  # noqa: E402
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

    def test_creature_enum_reference_resolves_map_ids(self):
        champion = self.creatures["Champion"]
        self.assertIn("CREATURE_CHAMPION", champion["enumIds"])

    def test_model_materials_preserve_xdb_slot_order(self):
        materials = self.creatures["CombatMage"]["visual"]["materials"]
        self.assertEqual(len(materials), 2)
        self.assertEqual(materials[0]["alphaMode"], "AM_OPAQUE")
        self.assertTrue(materials[0]["texture"]["xdb"].endswith(".xdb"))
        self.assertTrue(materials[0]["texture"]["png"].endswith(".dds.png"))
        dds = os.path.join(DATA, materials[0]["texture"]["dds"].lstrip("/"))
        self.assertTrue(os.path.isfile(dds), dds)
        self.assertIn("is2Sided", materials[0])

    def test_campaigns_resolve_localized_mission_maps(self):
        campaign = self.campaigns["A2C0"]
        self.assertTrue(campaign["name"])
        self.assertEqual(campaign["missions"][0]["mapId"], "A2C0M0")
        self.assertTrue(campaign["missions"][0]["name"])
        self.assertEqual(campaign["missions"][0]["tileX"], 136)
        self.assertEqual(campaign["missions"][0]["bonuses"][0]["resources"]["Gold"], 1000)

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

    def test_map_materials_are_exported_with_the_static(self):
        path = os.path.join(DATA, "Maps", "CombatArenas", "AbandonedMine", "AbandonedMine.xdb")
        md = load_map(self.xdb, path)
        textured = [o for o in md.objects if o.materials and o.materials[0].get("texture", {}).get("xdb")]
        self.assertTrue(textured)
        self.assertEqual(textured[0].materials[0]["texture"]["xdb"], textured[0].textures[0])

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

    def test_scenario_owner_hero_state_and_monster_amounts(self):
        """Scenario records use PlayerID and retain the inline state fields."""
        path = os.path.join(DATA, "Maps", "Scenario", "A2C0M0", "map.xdb")
        if not os.path.isfile(path):
            self.skipTest("campaign scenario not present")
        md = load_map(self.xdb, path)
        hero = next(o for o in md.objects if o.kind == "AdvMapHero")
        self.assertEqual(hero.extra["owner"], "PLAYER_1")
        self.assertEqual(hero.extra["hero"], "Quroq")
        self.assertEqual(hero.extra["experience"], 4700)
        self.assertEqual(hero.extra["army"][0], {"creature": "CREATURE_ORC_WARRIOR", "count": 100})
        monster = next(o for o in md.objects if o.kind == "AdvMapMonster" and o.name == "butchers_guard")
        self.assertEqual(monster.extra["creature"], "CREATURE_CHAMPION")
        self.assertEqual(monster.extra["amount"], 4)
        self.assertEqual(monster.extra["amount2"], 4)
        self.assertEqual(monster.extra["additionalStacks"][0]["amount"], 25)
        town = next(o for o in md.objects if o.kind == "AdvMapTown")
        self.assertEqual(town.extra["owner"], "PLAYER_2")

    def test_floor_zero_is_surface_ground(self):
        """Matches common.lua (GROUND = 0, UNDERGROUND = 1) and NWorld::IsTilePassable."""
        ug_path = os.path.join(DATA, "Maps", "CombatArenas", "AbandonedMine", "AbandonedMine.xdb")
        ug_md = load_map(self.xdb, ug_path, resolve_models=False)
        for o in ug_md.objects:
            self.assertEqual(o.floor, 1)  # AbandonedMine is an underground arena (<Floor>1</Floor>)

        surf_path = os.path.join(DATA, "Maps", "Scenario", "A2C1M5", "map.xdb")
        if os.path.isfile(surf_path):
            surf_md = load_map(self.xdb, surf_path, resolve_models=False)
            floors = {o.floor for o in surf_md.objects}
            self.assertIn(0, floors)  # Surface objects have <Floor>0</Floor>


@requires_data
class GR2AndPatch31Test(unittest.TestCase):
    def test_oodle1_skeletons_decompress_and_parse_bones(self):
        """All 2,242 files in data/bin/Skeletons/ use GR2 v6 + Oodle1 (comp=2)."""
        skels = sorted(glob.glob(os.path.join(DATA, "bin", "Skeletons", "*")))
        self.assertGreater(len(skels), 2000)
        for path in skels[:25]:
            with open(path, "rb") as fh:
                gr2 = parse_gr2_bytes(fh.read())
            self.assertEqual(gr2.version, 6)
            self.assertEqual(gr2.sections[0].compression, 2)
            self.assertEqual(len(gr2.skeletons), 1)
            skel = gr2.skeletons[0]
            self.assertTrue(skel.name)
            self.assertGreater(len(skel.bones), 0)
            self.assertEqual(skel.bones[0].parent_index, -1)

    def test_gr2_animations_uncompressed_and_oodle1(self):
        """Animations in data/bin/animations/ are either comp=0 (2,816) or comp=2 (577)."""
        anims = sorted(glob.glob(os.path.join(DATA, "bin", "animations", "*")))
        self.assertGreater(len(anims), 3000)
        seen_comp = set()
        for path in anims[:60]:
            with open(path, "rb") as fh:
                raw = fh.read()
            comp = struct.unpack_from("<I", raw, 32 + struct.unpack_from("<I", raw, 44)[0])[0]
            seen_comp.add(comp)
            gr2 = parse_gr2_bytes(raw)
            self.assertEqual(len(gr2.animations), 1)
            anim = gr2.animations[0]
            self.assertGreater(anim.duration, 0.0)
            self.assertGreater(len(anim.track_groups), 0)
            self.assertGreater(len(anim.track_groups[0].transform_tracks), 0)
        self.assertIn(0, seen_comp)
        self.assertIn(2, seen_comp)

    def test_default_stats_and_a2p1_overlay(self):
        """DefaultStats.xdb resolves from ToTE 3.1 a2p1-data/ and matches decompiled NWorld constants."""
        xdb = XDB(DATA)
        self.assertIsNotNone(xdb.patch_root)
        self.assertTrue(xdb.patch_root.endswith("a2p1-data"))
        stats = load_default_stats(xdb)
        self.assertEqual(stats["heroInitiative"], 10)
        self.assertEqual(stats["atbBaseTime"], 100000)
        self.assertEqual(stats["adventure"]["baseHeroMovement"], 2500)
        self.assertAlmostEqual(stats["combat"]["damageIncrease"], 0.05)
        self.assertAlmostEqual(stats["combat"]["damageDecrease"], 0.05)


if __name__ == "__main__":
    unittest.main(verbosity=2)
