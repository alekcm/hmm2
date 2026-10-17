"""Narrow source guards for compiler errors reported from Unity 6.6.

These catch reintroduction of the known bad expressions, NOT general C# type
errors. Actual behaviour tests are in UnityCompileRegressionTests.cs.
"""
from pathlib import Path
import unittest

RUNTIME = Path(__file__).resolve().parents[3] / "UnityProject/Assets/Scripts/HoMM5.Runtime"


class ReportedUnityCompileRegressionTest(unittest.TestCase):
    def test_garrison_is_a_list_not_an_object_with_stacks(self):
        for path in RUNTIME.rglob("*.cs"):
            with self.subTest(file=path.name):
                self.assertNotIn(".garrison.stacks", path.read_text())

    def test_known_invalid_and_obsolete_unity_calls_are_removed(self):
        self.assertNotIn("q.magnitude", (RUNTIME / "Anim/H5Anim.cs").read_text())
        self.assertNotIn("ScreenPointInRectangle(", (RUNTIME / "UI/H5Minimap.cs").read_text())
        self.assertIn("RectangleContainsScreenPoint(", (RUNTIME / "UI/H5Minimap.cs").read_text())
        effects = (RUNTIME / "Combat/CombatEffects.cs").read_text()
        self.assertNotIn("GetInstanceID(", effects)
        self.assertIn("GetEntityId()", effects)

    def test_ai_uses_existing_database_api_and_static_bank_method(self):
        source = (RUNTIME / "Map/H5AiController.cs").read_text()
        self.assertNotIn(".CreatureName(", source)
        self.assertNotIn("state.IsBank(", source)
        self.assertIn("AdventureMapGameState.IsBank(obj)", source)

    def test_rounding_has_explicit_float_rng_conversion(self):
        source = (RUNTIME / "Combat/CombatAbilities.cs").read_text()
        self.assertIn("RegenerationMin + (float)rng.NextDouble()", source)

    def test_combat_hud_uses_single_argument_list_add(self):
        source = (RUNTIME / "UI/CombatHud.cs").read_text()
        self.assertNotIn('lines.Add("", u.Def.Description)', source)
        self.assertIn("lines.Add(u.Def.Description)", source)
        self.assertEqual(source.count("var layout ="), 1)
        self.assertIn("var spellLayout =", source)

    def test_switch_and_nested_local_scopes_do_not_reintroduce_reported_clashes(self):
        visits = (RUNTIME / "Map/H5ObjectVisits.cs").read_text()
        self.assertRegex(visits, r'case "TREASURE_SEA_CHEST":\s*\{')
        battle = (RUNTIME / "Combat/CombatGameState.cs").read_text()
        self.assertNotIn("SiegeStructure target =", battle)
        self.assertIn("SiegeStructure structureTarget =", battle)

    def test_wav_u32_uses_unsigned_operands_before_shifting(self):
        source = (RUNTIME / "Audio/H5Wav.cs").read_text()
        self.assertIn("(uint)d[o] | ((uint)d[o + 1] << 8) | ((uint)d[o + 2] << 16) | ((uint)d[o + 3] << 24)", source)
        self.assertNotIn("((long)d[o + 3] << 24)", source)

    def test_runtime_only_view_fields_are_not_unity_serialized(self):
        self.assertIn("[System.NonSerialized] public CombatUnit Unit;",
                      (RUNTIME / "Combat/CombatSceneBuilder.cs").read_text())
        self.assertIn("[System.NonSerialized] public HoMM5.Data.GameDatabase Database;",
                      (RUNTIME / "UI/CombatHud.cs").read_text())
        self.assertIn("[System.Serializable]\n    public class PlayerResourceInventory",
                      (RUNTIME / "Data/GameDefs.cs").read_text())


if __name__ == "__main__":
    unittest.main()
