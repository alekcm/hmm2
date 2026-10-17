"""Checks for Unity project wiring that do not require the Unity Editor.

These deliberately do not claim to compile or run C#. Behaviour regressions
live in Assets/Tests/PlayMode and must run in the actual Unity Test Runner.
"""
import json
from pathlib import Path
import re
import unittest


PROJECT = Path(__file__).resolve().parents[3] / "UnityProject"


class UnityProjectWiringTest(unittest.TestCase):
    def test_bootstrap_script_reference_has_matching_metadata(self):
        scene = (PROJECT / "Assets/Scenes/AdventureMap.unity").read_text()
        meta = (PROJECT / "Assets/Scripts/HoMM5.Runtime/H5Bootstrap.cs.meta").read_text()
        guid = re.search(r"^guid: ([a-f0-9]{32})$", meta, re.M).group(1)
        self.assertIn("m_Script: {fileID: 11500000, guid: " + guid + ", type: 3}", scene)

    def test_bootstrap_scene_is_enabled_and_has_stable_guid(self):
        meta = (PROJECT / "Assets/Scenes/AdventureMap.unity.meta").read_text()
        guid = re.search(r"^guid: ([a-f0-9]{32})$", meta, re.M).group(1)
        build = (PROJECT / "ProjectSettings/EditorBuildSettings.asset").read_text()
        self.assertRegex(build, r"- enabled: 1\s+path: Assets/Scenes/AdventureMap\.unity\s+guid: " + guid)

    def test_runtime_explicitly_references_ugui(self):
        definition = json.loads((PROJECT / "Assets/Scripts/HoMM5.Runtime/HoMM5.Runtime.asmdef").read_text())
        self.assertIn("Unity.ugui", definition["references"])
        manifest = json.loads((PROJECT / "Packages/manifest.json").read_text())
        self.assertIn("com.unity.ugui", manifest["dependencies"])

    def test_playmode_regressions_are_a_test_assembly(self):
        definition = json.loads((PROJECT / "Assets/Tests/PlayMode/HoMM5.PlayMode.Tests.asmdef").read_text())
        self.assertIn("TestAssemblies", definition["optionalUnityReferences"])
        self.assertIn("HoMM5.Runtime", definition["references"])
        self.assertIn("UnityEngine.TestRunner", definition["references"])
        self.assertFalse(definition.get("includePlatforms"), "PlayMode tests must not be Editor-only")


if __name__ == "__main__":
    unittest.main()
