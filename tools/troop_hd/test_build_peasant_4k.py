import csv
import json
import struct
import tempfile
import unittest
from pathlib import Path

import build_peasant_4k as variant
import build_peasant_poc as poc


@unittest.skipUnless(poc.SOURCE_GEOMETRY.is_file(), "game geometry is not present")
@unittest.skipUnless(variant.ATLAS_SOURCE.is_file(), "Peasant diffuse atlas is not present")
class Peasant4KVariantTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp_dir = tempfile.TemporaryDirectory(prefix="peasant-4k-test-")
        cls.out_dir = Path(cls.temp_dir.name) / "HavenPeasant_4K"
        cls.default_model_path = (
            poc.REPO / "Troops_HD_Patch" / "HoMM5" / "Meshes" / f"{poc.UID}.glb"
        )
        cls.default_model_before = (cls.default_model_path.read_bytes()
                                    if cls.default_model_path.exists() else None)
        cls.manifest = variant.build(cls.out_dir)
        cls.default_model_after = (cls.default_model_path.read_bytes()
                                   if cls.default_model_path.exists() else None)

    @classmethod
    def tearDownClass(cls):
        cls.temp_dir.cleanup()

    def test_variant_is_separate_high_poly_mesh_with_4k_label(self):
        self.assertEqual(self.manifest["label"], "Haven Peasant 4K")
        self.assertTrue(self.manifest["is_separate_variant"])
        self.assertFalse(self.manifest["replaces_default_peasant"])
        self.assertGreater(self.manifest["glb"]["triangles"], 4732)
        self.assertEqual(self.manifest["glb"]["mesh_slices"], 2)
        self.assertFalse(self.manifest["refinement"]["pitchfork_changed"])
        self.assertEqual(self.default_model_before, self.default_model_after)
        self.assertFalse((self.out_dir / f"{poc.UID}.glb").exists())

    def test_triangle_uv_map_and_csv_cover_final_body_and_fork(self):
        body_triangles = self.manifest["refinement"]["body_shape_triangles"]
        fork_triangles = self.manifest["refinement"]["pitchfork_shape_triangles"]
        status_counts = self.manifest["uv_semantic_tracking"]["uv_mapping_status_counts"]
        self.assertEqual(sum(status_counts.values()), body_triangles)
        self.assertGreater(self.manifest["uv_semantic_tracking"]["region_counts"]["legs_and_shins"], 0)
        for name in ("uv_semantic_map.png", "uv_triangle_wireframe.png", "triangle_uv_regions.csv"):
            self.assertTrue((self.out_dir / name).is_file())
        with (self.out_dir / "triangle_uv_regions.csv").open(newline="", encoding="utf-8") as stream:
            rows = list(csv.DictReader(stream))
        self.assertEqual(len(rows), body_triangles + fork_triangles)
        self.assertEqual(sum(row["region"] == "pitchfork_preserved" for row in rows), fork_triangles)
        self.assertTrue(any(row["region"] == "legs_and_shins" for row in rows))

    def test_glb_is_labeled_and_indices_validate(self):
        path = self.out_dir / "HavenPeasant_4K.glb"
        blob = path.read_bytes()
        json_length, json_type = struct.unpack_from("<II", blob, 12)
        self.assertEqual(json_type, 0x4E4F534A)
        document = json.loads(blob[20:20 + json_length].decode("utf-8"))
        self.assertEqual(document["extras"]["variant_label"], "Haven Peasant 4K")
        checked = poc.validate_glb(blob, expected_triangles=self.manifest["glb"]["triangles"])
        self.assertEqual(checked["mesh_slices"], 2)


if __name__ == "__main__":
    unittest.main()
