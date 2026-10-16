using System.Collections.Generic;
using HoMM5.Core;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>
    /// Loads and caches imported meshes by geometry UID.
    ///
    /// Map objects share models heavily -- a 200-object arena reuses a couple of
    /// dozen meshes -- so the cache is the difference between a map that loads
    /// and one that does not.  Meshes are shared, never instanced per object.
    /// </summary>
    public class MeshCache
    {
        readonly Dictionary<string, Mesh[]> _cache = new Dictionary<string, Mesh[]>();
        readonly HashSet<string> _missing = new HashSet<string>();

        public int CachedCount => _cache.Count;
        public int MissingCount => _missing.Count;
        public int TriangleCount
        {
            get
            {
                var t = 0;
                foreach (var kv in _cache)
                    if (kv.Value != null)
                        foreach (var m in kv.Value)
                            if (m != null) t += (int)(m.triangles.LongLength / 3);
                return t;
            }
        }

        /// <summary>Returns null when the geometry was not imported.</summary>
        public Mesh[] Get(string geometryUid)
        {
            if (string.IsNullOrEmpty(geometryUid)) return null;
            if (_cache.TryGetValue(geometryUid, out var cached)) return cached;
            if (_missing.Contains(geometryUid)) return null;

            var path = H5Paths.Meshes(geometryUid);
            var meshes = GlbMeshLoader.Load(path, geometryUid);
            if (meshes == null || meshes.Count == 0)
            {
                _missing.Add(geometryUid);
                return null;
            }
            _cache[geometryUid] = meshes.ToArray();
            return _cache[geometryUid];
        }

        public bool Has(string geometryUid) => Get(geometryUid) != null;

        public void Clear()
        {
            _cache.Clear();
            _missing.Clear();
        }

        public void DestroyMeshes()
        {
            foreach (var kv in _cache)
            {
                if (kv.Value == null) continue;
                foreach (var m in kv.Value)
                    if (m != null) Object.Destroy(m);
            }
            Clear();
        }
    }
}
