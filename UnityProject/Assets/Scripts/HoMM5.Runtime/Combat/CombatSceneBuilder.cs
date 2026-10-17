using System.Collections;
using System.Collections.Generic;
using HoMM5.Core;
using HoMM5.Data;
using HoMM5.Map;
using UnityEngine;

namespace HoMM5.Combat
{
    /// <summary>
    /// One stack on the battlefield: its model, the count label and the side
    /// marker. The game-side identity is <see cref="CombatUnit"/>; this is only
    /// how it looks.
    /// </summary>
    public class CombatUnitView : MonoBehaviour
    {
        public CombatUnit Unit;
        TextMesh _countLabel;
        Coroutine _moveRoutine;

        public bool IsMoving => _moveRoutine != null;

        public void SetCountLabel(TextMesh label) => _countLabel = label;

        public void RefreshCount()
        {
            if (_countLabel != null && Unit != null)
                _countLabel.text = Unit.Count > 0 ? Unit.Count.ToString() : "";
        }

        public void MoveAlong(Vector3[] path, float unitsPerSecond)
        {
            if (_moveRoutine != null) StopCoroutine(_moveRoutine);
            _moveRoutine = StartCoroutine(FollowPath(path, unitsPerSecond));
        }

        IEnumerator FollowPath(Vector3[] path, float unitsPerSecond)
        {
            for (var i = 0; i < path.Length; i++)
            {
                var target = path[i];
                var flat = target - transform.position;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(flat);
                while ((transform.position - target).sqrMagnitude > 0.01f)
                {
                    transform.position = Vector3.MoveTowards(transform.position, target,
                        Mathf.Max(0.5f, unitsPerSecond) * Time.deltaTime);
                    yield return null;
                }
                transform.position = target;
            }
            _moveRoutine = null;
        }

        public void SnapTo(Vector3 position, float yaw)
        {
            if (_moveRoutine != null)
            {
                StopCoroutine(_moveRoutine);
                _moveRoutine = null;
            }
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }

    /// <summary>
    /// Turns a CombatGameState into a scene: real combat-arena scenery under
    /// the grid, one view per stack, and the movement/attack highlights.
    /// </summary>
    public class CombatSceneBuilder
    {
        public MapSceneBuilder Arena { get; private set; }
        public GameObject Root { get; private set; }
        public readonly Dictionary<int, CombatUnitView> Views = new Dictionary<int, CombatUnitView>();

        /// <summary>Grid origin in arena tiles (top-left cell).</summary>
        public int GridOriginX { get; private set; }
        public int GridOriginY { get; private set; }

        readonly MeshCache _meshes;
        readonly H5TextureCache _textures;
        readonly GameDatabase _db;
        readonly Material _objectMaterial;
        GameObject _highlight;
        MeshFilter _highlightFilter;

        public CombatSceneBuilder(MeshCache meshes, H5TextureCache textures, GameDatabase db,
                                  Material terrainMaterial, Material objectMaterial, Material waterMaterial)
        {
            _meshes = meshes;
            _textures = textures;
            _db = db;
            _objectMaterial = objectMaterial;
            Arena = new MapSceneBuilder(meshes, terrainMaterial, objectMaterial, waterMaterial,
                                        textures, db);
        }

        /// <summary>Build the arena scenery and lay the battle grid on it.</summary>
        public bool BuildArena(string arenaId)
        {
            Root = Arena.Build(arenaId, 0);
            if (Root == null) return false;

            GridOriginX = Mathf.Max(0, Arena.Map.tileX / 2 - CombatGameState.GridWidth / 2);
            GridOriginY = Mathf.Max(0, Arena.Map.tileY / 2 - CombatGameState.GridHeight / 2);

            _highlight = new GameObject("Highlights");
            _highlight.transform.SetParent(Root.transform, false);
            _highlightFilter = _highlight.AddComponent<MeshFilter>();
            var mr = _highlight.AddComponent<MeshRenderer>();
            mr.sharedMaterial = HighlightMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetHighlight(null, Color.clear);
            return true;
        }

        public Vector3 CellToWorld(int x, int y)
        {
            return Arena.TileToWorld(GridOriginX + x, GridOriginY + y);
        }

        /// <summary>Inverse of CellToWorld: which grid cell a world point lands in.</summary>
        public bool TryGetCell(Vector3 worldPoint, out int x, out int y)
        {
            var local = Root != null ? Root.transform.InverseTransformPoint(worldPoint) : worldPoint;
            x = Mathf.RoundToInt(local.x / TerrainMeshBuilder.TileSize) - GridOriginX;
            y = Mathf.RoundToInt(local.z / TerrainMeshBuilder.TileSize) - GridOriginY;
            return x >= 0 && y >= 0 && x < CombatGameState.GridWidth && y < CombatGameState.GridHeight;
        }

        // -- units ---------------------------------------------------------

        public void BuildUnits(CombatGameState state, Material objectMaterial = null)
        {
            if (state == null || Root == null) return;
            var container = new GameObject("Units");
            container.transform.SetParent(Root.transform, false);

            foreach (var unit in state.Units)
            {
                if (unit == null || !unit.IsAlive) continue;
                var go = new GameObject(unit.DisplayName);
                go.transform.SetParent(container.transform, false);
                var yaw = unit.Side == CombatSide.Attacker ? 90f : -90f;
                var view = go.AddComponent<CombatUnitView>();
                view.Unit = unit;

                BuildModel(go, unit);
                BuildSideMarker(go, unit);
                BuildCountLabel(go, view, unit);
                go.transform.position = CellToWorld(unit.X, unit.Y);
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

                // Picking needs a collider around the whole stack.
                var box = go.AddComponent<BoxCollider>();
                var bounds = WorldBounds(go);
                box.center = go.transform.InverseTransformPoint(bounds.center);
                box.size = go.transform.InverseTransformVector(bounds.size) +
                          new Vector3(0.2f, 0.2f, 0.2f);

                Views[unit.Id] = view;
            }
        }

        void BuildModel(GameObject host, CombatUnit unit)
        {
            Mesh[] meshes = null;
            var uid = unit.Def != null && unit.Def.visual != null ? unit.Def.visual.geometryUid : null;
            if (!string.IsNullOrEmpty(uid)) meshes = _meshes.Get(uid);

            if (meshes != null && meshes.Length > 0)
            {
                var mats = new List<Material>(meshes.Length);
                for (var m = 0; m < meshes.Length; m++)
                    mats.Add(UnitMaterial(unit, m) ?? _objectMaterial);
                for (var m = 0; m < meshes.Length; m++)
                {
                    var child = new GameObject(meshes[m].name);
                    child.transform.SetParent(host.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = meshes[m];
                    var mr = child.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mats[m];
                }
                // Adventure models are map-sized; shrink to roughly one cell.
                var bounds = WorldBounds(host);
                var extent = Mathf.Max(bounds.size.x, bounds.size.z);
                if (extent > 0.01f)
                {
                    var scale = Mathf.Min(3.2f / extent, 3.2f / Mathf.Max(0.01f, bounds.size.y));
                    host.transform.localScale = Vector3.one * scale;
                }
                return;
            }

            // Fallback marker so battles are playable without imported meshes.
            var marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            marker.name = "marker";
            marker.transform.SetParent(host.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            marker.transform.localScale = new Vector3(1.1f, 0.9f, 1.1f);
            var capsule = marker.GetComponent<MeshRenderer>();
            if (capsule != null)
            {
                var mat = new Material(Shader.Find("Standard"));
                mat.color = unit.Side == CombatSide.Attacker
                    ? new Color(0.75f, 0.30f, 0.22f) : new Color(0.25f, 0.38f, 0.72f);
                capsule.sharedMaterial = mat;
            }
            var primitiveCollider = marker.GetComponent<Collider>();
            if (primitiveCollider != null) Object.Destroy(primitiveCollider);
        }

        Material UnitMaterial(CombatUnit unit, int meshIndex)
        {
            if (_textures == null || unit.Def == null || unit.Def.visual == null) return null;
            var textures = unit.Def.visual.textures;
            if (textures == null || textures.Count == 0) return null;
            var idx = Mathf.Min(meshIndex, textures.Count - 1);
            return _textures.MaterialFor(textures[idx]);
        }

        void BuildSideMarker(GameObject host, CombatUnit unit)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "sideMarker";
            quad.transform.SetParent(host.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(2.2f, 2.2f, 1f);
            var col = quad.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var mr = quad.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.color = unit.Side == CombatSide.Attacker
                ? new Color(0.85f, 0.25f, 0.18f, 0.55f) : new Color(0.22f, 0.42f, 0.85f, 0.55f);
            SetTransparent(mat);
            mr.sharedMaterial = mat;
        }

        void BuildCountLabel(GameObject host, CombatUnitView view, CombatUnit unit)
        {
            var go = new GameObject("count");
            go.transform.SetParent(host.transform, false);
            go.transform.localPosition = new Vector3(0f, 2.4f, 0f);
            var label = go.AddComponent<TextMesh>();
            label.text = unit.Count.ToString();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 48;
            label.characterSize = 0.12f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = Color.white;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && label.font != null) mr.sharedMaterial = label.font.material;
            view.SetCountLabel(label);
        }

        // -- highlights ----------------------------------------------------

        /// <summary>Draw the reachable/attack cells as translucent quads.</summary>
        public void SetHighlight(IEnumerable<Vector2Int> cells, Color color)
        {
            if (_highlightFilter == null) return;
            var mesh = new Mesh { name = "highlight" };
            var pos = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            var colors = new List<Color>();
            const float s = TerrainMeshBuilder.TileSize * 0.46f;

            if (cells != null)
                foreach (var c in cells)
                {
                    var center = CellToWorld(c.x, c.y) + Vector3.up * 0.08f;
                    var baseIndex = pos.Count;
                    pos.Add(center + new Vector3(-s, 0f, -s));
                    pos.Add(center + new Vector3(-s, 0f, s));
                    pos.Add(center + new Vector3(s, 0f, s));
                    pos.Add(center + new Vector3(s, 0f, -s));
                    uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1));
                    uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
                    colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
                    tri.Add(baseIndex); tri.Add(baseIndex + 1); tri.Add(baseIndex + 2);
                    tri.Add(baseIndex); tri.Add(baseIndex + 2); tri.Add(baseIndex + 3);
                }

            mesh.SetVertices(pos);
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            var old = _highlightFilter.sharedMesh;
            _highlightFilter.sharedMesh = mesh;
            if (old != null) Object.Destroy(old);
        }

        // -- helpers -------------------------------------------------------

        static void SetTransparent(Material mat)
        {
            mat.SetFloat("_Mode", 3f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }

        static Material HighlightMaterial()
        {
            var mat = new Material(Shader.Find("Standard"));
            SetTransparent(mat);
            return mat;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
