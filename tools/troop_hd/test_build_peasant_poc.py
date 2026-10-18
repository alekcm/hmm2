import unittest

import build_peasant_poc as poc


@unittest.skipUnless(poc.SOURCE_GEOMETRY.is_file(), "game geometry is not present")
class PeasantPrototypeGeometryTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.geometry = poc.decode_file(str(poc.SOURCE_GEOMETRY))
        cls.body = cls.geometry.meshes[0].groups[0]
        cls.fork = cls.geometry.meshes[1].groups[0]
        cls.fork_snapshot = (
            tuple(cls.fork.positions), tuple(cls.fork.uvs), tuple(cls.fork.normals),
            tuple(cls.fork.triangles), tuple(cls.fork.bone_indices), tuple(cls.fork.bone_weights),
        )
        cls.refined, cls.stats = poc.refine_body(cls.body)

    def test_only_local_soft_parts_gain_geometry(self):
        self.assertEqual(self.stats["source_triangles"], 1670)
        self.assertEqual(self.stats["refined_vertices"], 3049)
        self.assertEqual(self.stats["refined_triangles"], 4550)
        self.assertEqual(self.stats["moved_vertices"], 687)
        self.assertGreater(self.stats["max_vertex_displacement"], 0.03)
        self.assertEqual(set(self.stats["parts"]), {"hands", "hood", "legs_and_shins", "pouch"})
        for name in self.stats["parts"]:
            self.assertGreater(self.stats["parts"][name]["faces_mean_weight_over_0_2"], 0)

    def test_pitchfork_mesh_data_is_unchanged(self):
        fork = self.geometry.meshes[1].groups[0]
        self.assertEqual(
            self.fork_snapshot,
            (tuple(fork.positions), tuple(fork.uvs), tuple(fork.normals),
             tuple(fork.triangles), tuple(fork.bone_indices), tuple(fork.bone_weights)),
        )

    def test_glb_is_well_formed_and_keeps_both_mesh_blocks(self):
        self.geometry.meshes[0].groups[0] = self.refined
        blob = poc.write_glb(self.geometry, poc.UID)
        expected_triangles = self.geometry.total_triangles
        checked = poc.validate_glb(blob, expected_triangles=expected_triangles)
        self.assertEqual(checked["mesh_slices"], 2)
        self.assertEqual(checked["triangles"], expected_triangles)


if __name__ == "__main__":
    unittest.main()
