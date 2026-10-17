using System.Diagnostics;
using System.IO;
using HoMM5.Core;
using HoMM5.Data;
using HoMM5.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace HoMM5.EditorTools
{
    /// <summary>
    /// Editor-side tools: run the importer, build a scene from scratch, and
    /// inspect what the import produced.
    /// </summary>
    public static class H5EditorMenu
    {
        const string MenuRoot = "Tools/HoMM5/";

        [MenuItem(MenuRoot + "Create Bootstrap Scene")]
        public static void CreateBootstrapScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("HoMM5");
            go.AddComponent<H5Bootstrap>();
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/AdventureMap.unity");
            Debug.Log("[HoMM5] bootstrap scene written to Assets/Scenes/AdventureMap.unity");
        }

        [MenuItem(MenuRoot + "Open Import Folder")]
        public static void OpenImportFolder()
        {
            var root = H5Paths.Root;
            if (!Directory.Exists(root))
            {
                EditorUtility.DisplayDialog("HoMM5", "No import found at:\n" + root, "OK");
                return;
            }
            EditorUtility.RevealInFinder(root);
        }

        [MenuItem(MenuRoot + "Run Importer (all)")]
        public static void RunImporterAll() => RunImporter("all");

        [MenuItem(MenuRoot + "Run Importer (game data only)")]
        public static void RunImporterGameData() => RunImporter("gamedata");

        [MenuItem(MenuRoot + "Run Importer (maps)")]
        public static void RunImporterMaps() => RunImporter("maps");

        [MenuItem(MenuRoot + "Run Importer (meshes)")]
        public static void RunImporterMeshes() => RunImporter("meshes");

        [MenuItem(MenuRoot + "Run Importer (original UI)")]
        public static void RunImporterUI() => RunImporter("ui");

        [MenuItem(MenuRoot + "Run Importer (textures)")]
        public static void RunImporterTextures() => RunImporter("textures");

        [MenuItem(MenuRoot + "Import Summary")]
        public static void ImportSummary()
        {
            var db = new GameDatabase();
            db.Load();
            var meshes = CountFiles(Path.Combine(H5Paths.Root, "Meshes"), "*.glb");
            var textures = CountFiles(Path.Combine(H5Paths.Root, "Textures"), "*.png");
            var terrain = CountFiles(Path.Combine(H5Paths.Root, "Terrain"), "*.heights");

            var report =
                $"Import root: {H5Paths.Root}\n\n" +
                $"creatures      {db.Creatures.Count}\n" +
                $"spells         {db.Spells.Count}\n" +
                $"artifacts      {db.Artifacts.Count}\n" +
                $"town buildings {db.TownBuildings.Count}\n" +
                $"strings        {db.Localization.Count}\n" +
                $"maps           {db.MapIndex.Count}\n" +
                $"meshes (.glb)  {meshes}\n" +
                $"textures (.png){textures}\n" +
                $"terrain planes {terrain}\n";
            EditorUtility.DisplayDialog("HoMM5 import summary", report, "OK");
        }

        /// <summary>Build one map into the currently open scene, for inspection.</summary>
        [MenuItem(MenuRoot + "Build Map Into Scene...")]
        public static void BuildMapIntoScene()
        {
            var ids = H5Paths.ListMapIds();
            if (ids.Length == 0)
            {
                EditorUtility.DisplayDialog("HoMM5", "No maps imported.", "OK");
                return;
            }

            var picked = EditorInputDialog.Pick("Build map into scene", "Map", ids, ids[0]);
            if (string.IsNullOrEmpty(picked)) return;

            var meshes = new MeshCache();
            var builder = new MapSceneBuilder(meshes,
                MakeMaterial("H5_Terrain", 0.05f), MakeMaterial("H5_Object", 0.25f), MakeMaterial("H5_Object", 0.25f));
            var definition = MapSceneBuilder.LoadMapDef(picked);
            builder.Build(picked, definition != null ? definition.initialFloor : 0);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        // -- helpers -------------------------------------------------------

        static int CountFiles(string dir, string pattern)
        {
            if (!Directory.Exists(dir)) return 0;
            var n = 0;
            foreach (var _ in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories)) n++;
            return n;
        }

        static Material MakeMaterial(string name, float gloss)
        {
            var m = new Material(Shader.Find("Standard")) { name = name };
            m.SetFloat("_Glossiness", gloss);
            return m;
        }

        /// <summary>
        /// Run the Python importer.  It writes into StreamingAssets, which is
        /// outside the AssetDatabase's generated-asset flow, so the only thing
        /// the editor has to do afterwards is refresh.
        /// </summary>
        static void RunImporter(string subcommand)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var repoRoot = Path.GetFullPath(Path.Combine(projectRoot, ".."));
            var importerDir = Path.Combine(repoRoot, "tools", "homm5_import");
            var runner = Path.Combine(repoRoot, "import_to_unity.py");

            if (!Directory.Exists(importerDir))
            {
                EditorUtility.DisplayDialog("HoMM5",
                    "Importer not found at:\n" + importerDir + "\n\nIs the Unity project still inside the repository?",
                    "OK");
                return;
            }

            // "python3" does not exist on Windows -- it is "python" or the "py"
            // launcher.  Try each in turn rather than assuming a platform.
            var output = Path.Combine(Application.streamingAssetsPath, "HoMM5");
            var candidates = new (string file, string[] prefix)[]
            {
                ("py", new[] { "-3" }),
                ("python3", new string[0]),
                ("python", new string[0]),
            };

            System.Exception lastError = null;
            foreach (var (file, prefix) in candidates)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    WorkingDirectory = importerDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                foreach (var p in prefix) psi.ArgumentList.Add(p);
                // The runner resolves its own paths, but pass them explicitly so
                // the output lands in this project regardless of cwd.
                psi.ArgumentList.Add(runner);
                psi.ArgumentList.Add(subcommand);
                psi.ArgumentList.Add("--data");
                psi.ArgumentList.Add(Path.Combine(repoRoot, "data"));
                psi.ArgumentList.Add("-o");
                psi.ArgumentList.Add(output);

                Process proc;
                try
                {
                    proc = Process.Start(psi);
                }
                catch (System.Exception e)
                {
                    lastError = e;      // not installed under this name; try the next
                    continue;
                }
                if (proc == null) continue;

                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode == 0)
                {
                    Debug.Log("[HoMM5] importer (" + file + ") finished.\n" + stdout);
                }
                else
                {
                    Debug.LogError("[HoMM5] importer (" + file + ") exit " + proc.ExitCode +
                                   "\n" + stdout + "\nstderr:\n" + stderr);
                }
                AssetDatabase.Refresh();
                return;
            }

            EditorUtility.DisplayDialog("HoMM5",
                "Could not start Python (tried: py, python3, python).\n\n" +
                (lastError != null ? lastError.Message + "\n\n" : "") +
                "Install Python 3 and re-run, or run the importer by hand:\n\n" +
                "  python \"" + runner + "\" " + subcommand,
                "OK");
        }
    }

    /// <summary>A one-field picker, since Unity has no built-in list dialog.</summary>
    static class EditorInputDialog
    {
        public static string Pick(string title, string label, string[] options, string initial)
        {
            var w = EditorWindow.GetWindow<PickerWindow>(true, title, true);
            w.minSize = new Vector2(420, 160);
            w.Init(label, options, initial);
            return PickerWindow.Result;
        }

        class PickerWindow : EditorWindow
        {
            public static string Result;
            string _label;
            string[] _options;
            int _index;

            public void Init(string label, string[] options, string initial)
            {
                _label = label;
                _options = options;
                _index = Mathf.Max(0, System.Array.IndexOf(options, initial));
                Result = null;
            }

            void OnGUI()
            {
                EditorGUILayout.LabelField(_label);
                _index = EditorGUILayout.Popup(_index, _options);
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Cancel"))
                    {
                        Result = null;
                        Close();
                    }
                    if (GUILayout.Button("Build"))
                    {
                        Result = _options[_index];
                        Close();
                    }
                }
            }
        }
    }
}
