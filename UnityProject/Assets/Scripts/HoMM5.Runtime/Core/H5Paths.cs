using System.IO;
using UnityEngine;

namespace HoMM5.Core
{
    /// <summary>
    /// Where the offline importer puts its output, and how the runtime finds it.
    ///
    /// Everything the game needs lives under
    /// <c>Assets/StreamingAssets/HoMM5</c> as plain files (JSON, PNG, GLB and
    /// the raw terrain planes).  StreamingAssets means the runtime never touches
    /// the AssetDatabase, so a partial import is still a runnable build -- and
    /// the same code path works in the editor and in a player.
    /// </summary>
    public static class H5Paths
    {
        public const string RootFolderName = "HoMM5";

        public static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(_override)) return _override;
                return Path.Combine(Application.streamingAssetsPath, RootFolderName);
            }
        }

        static string _override;

        /// <summary>Point the runtime at an import output directory outside the project.</summary>
        public static void SetOverride(string path) => _override = string.IsNullOrEmpty(path) ? null : path;

        public static string Game(string file) => Path.Combine(Root, "Game", file);
        public static string Maps(string file) => Path.Combine(Root, "Maps", file);
        public static string Terrain(string mapId, string file) => Path.Combine(Root, "Terrain", mapId, file);
        public static string Meshes(string uid) => Path.Combine(Root, "Meshes", uid + ".glb");
        public static string Textures(string pngRelPath) => Path.Combine(Root, "Textures", pngRelPath);

        public static bool HasImport => Directory.Exists(Root) && Directory.Exists(Path.Combine(Root, "Game"));

        /// <summary>
        /// Read a whole file.  StreamingAssets is a real directory on standalone
        /// platforms and on the editor; inside an Android APK it would need
        /// UnityWebRequest, which is why every caller goes through here.
        /// </summary>
        public static byte[] ReadAllBytes(string path)
        {
            if (!File.Exists(path)) return null;
            return File.ReadAllBytes(path);
        }

        public static string ReadAllText(string path)
        {
            var bytes = ReadAllBytes(path);
            return bytes == null ? null : System.Text.Encoding.UTF8.GetString(bytes);
        }

        public static bool Exists(string path) => File.Exists(path);

        public static string[] ListMapIds()
        {
            var dir = Maps("");
            if (!Directory.Exists(dir)) return new string[0];
            var files = Directory.GetFiles(dir, "*.json");
            var ids = new System.Collections.Generic.List<string>(files.Length);
            foreach (var f in files)
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (name != "_index") ids.Add(name);
            }
            ids.Sort(System.StringComparer.OrdinalIgnoreCase);
            return ids.ToArray();
        }
    }
}
