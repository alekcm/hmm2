using HoMM5.Core;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>
    /// The adventure-map camera: pan with WASD or the middle mouse button, zoom
    /// with the wheel, click to select an object or provide a ground destination.
    /// HoMM5's own camera rides on a fixed pitch over the map, so the pitch here
    /// is locked rather than free-look.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class AdventureMapController : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Tiles per second at zoom 1.")]
        public float panSpeed = 24f;
        public float panSpeedMultiplierAtEdge = 2.5f;
        [Tooltip("Screen edge width, in pixels, that triggers edge scrolling.")]
        public int edgeScrollPixels = 12;

        [Header("Zoom")]
        public float minDistance = 8f;
        public float maxDistance = 90f;
        public float zoomSpeed = 6f;
        public float pitch = 52f;

        [Header("Bounds")]
        [Tooltip("Set from the map once it is built.")]
        public Rect limits = new Rect(-20, -20, 200, 200);
        public bool clampToLimits = true;

        Camera _cam;
        float _distance = 40f;
        Vector3 _focus;
        Vector3 _dragOrigin;
        Vector3 _dragFocus;
        bool _dragging;

        /// <summary>Where the camera looks, in world space (minimap, save files).</summary>
        public Vector3 Focus => _focus;

        /// <summary>Current zoom distance (minimap viewport rectangle).</summary>
        public float Distance => _distance;

        /// <summary>Vertical field of view in degrees (minimap viewport rectangle).</summary>
        public float ViewFov => _cam != null ? _cam.fieldOfView : 45f;

        public event System.Action<H5MapObject> SelectionChanged;
        public event System.Action<Vector3> MapClicked;
        public H5MapObject Selected { get; private set; }

        void Awake()
        {
            _cam = GetComponent<Camera>();
        }

        void Start()
        {
            _distance = Mathf.Clamp(_distance, minDistance, maxDistance);
            ApplyTransform();
        }

        public void SetMapBounds(int tileX, int tileY, float padding = 8f)
        {
            var worldWidth = tileX * TerrainMeshBuilder.TileSize;
            var worldHeight = tileY * TerrainMeshBuilder.TileSize;
            limits = new Rect(-padding, -padding, worldWidth + padding * 2f,
                              worldHeight + padding * 2f);
        }

        public void FocusOn(Vector3 worldPosition, float distance = -1f)
        {
            _focus = worldPosition;
            if (distance > 0f) _distance = Mathf.Clamp(distance, minDistance, maxDistance);
            Clamp();
            ApplyTransform();
        }

        void Update()
        {
            HandleZoom();
            HandlePan();
            HandleSelection();
            HandleMoveCommand();
            ApplyTransform();
        }

        void HandleZoom()
        {
            var wheel = H5Input.Scroll();
            if (Mathf.Approximately(wheel, 0f)) return;
            _distance = Mathf.Clamp(_distance - wheel * zoomSpeed, minDistance, maxDistance);
        }

        void HandlePan()
        {
            var move = H5Input.PanAxis();

            // Edge scrolling, unless the pointer is over the UI.
            if (move == Vector2.zero && !H5Input.PointerOverUi)
            {
                var p = H5Input.PointerPosition();
                if (p.x <= edgeScrollPixels) move.x -= 1f;
                else if (p.x >= Screen.width - edgeScrollPixels) move.x += 1f;
                if (p.y <= edgeScrollPixels) move.y -= 1f;
                else if (p.y >= Screen.height - edgeScrollPixels) move.y += 1f;
                if (move != Vector2.zero) move *= panSpeedMultiplierAtEdge;
            }

            // Middle mouse drag pans directly.
            if (H5Input.PanDragStarted())
            {
                _dragging = true;
                _dragOrigin = H5Input.PointerPosition();
                _dragFocus = _focus;
            }
            if (H5Input.PanDragEnded()) _dragging = false;
            if (_dragging)
            {
                var delta = _dragOrigin - H5Input.PointerPosition();
                var scale = _distance / Mathf.Max(1f, Screen.height) * 2f;
                _focus += (transform.right * delta.x + ProjectedForward() * delta.y) * scale;
            }
            else if (move != Vector2.zero)
            {
                var speed = panSpeed * (_distance / 40f) * Time.deltaTime;
                _focus += (transform.right * move.x + ProjectedForward() * move.y) * speed;
            }

            Clamp();
        }

        /// <summary>The map-plane forward direction, with the vertical part removed.</summary>
        Vector3 ProjectedForward()
        {
            var f = transform.forward;
            f.y = 0f;
            return f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
        }

        void Clamp()
        {
            if (!clampToLimits) return;
            _focus.x = Mathf.Clamp(_focus.x, limits.xMin, limits.xMax);
            _focus.z = Mathf.Clamp(_focus.z, limits.yMin, limits.yMax);
        }

        void ApplyTransform()
        {
            var offset = Quaternion.Euler(pitch, 0f, 0f) * new Vector3(0f, 0f, -_distance);
            transform.position = _focus + offset;
            transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void HandleSelection()
        {
            if (!H5Input.SelectPressed()) return;
            if (H5Input.PointerOverUi) return;
            if (_cam == null) return;

            var ray = _cam.ScreenPointToRay(H5Input.PointerPosition());
            if (!Physics.Raycast(ray, out var hit, 2000f)) return;

            var obj = hit.collider.GetComponentInParent<H5MapObject>();
            if (obj == null)
            {
                // Ground clicks are destinations for the currently selected
                // hero.  Do not clear that selection before subscribers have
                // received the destination.
                MapClicked?.Invoke(hit.point);
                return;
            }
            Select(obj);
        }

        void HandleMoveCommand()
        {
            if (!H5Input.MoveCommandPressed() || H5Input.PointerOverUi ||
                Selected == null || !Selected.IsHero || _cam == null) return;
            var ray = _cam.ScreenPointToRay(H5Input.PointerPosition());
            if (!Physics.Raycast(ray, out var hit, 2000f)) return;

            var builder = Selected.Builder;
            if (builder == null || builder.Root == null) return;
            // Route the same two-click command through the bootstrap as a
            // ground click. The native adventure map plots first and confirms
            // the plotted route on the second click.
            MapClicked?.Invoke(hit.point);
        }

        /// <summary>
        /// Map the clicked tile to the nearest tile a hero can actually stand
        /// on. Objects' footprints and water are impassable, and a right-click
        /// on a mine or a monster lands on exactly those -- the hero should
        /// walk up to it (and onto its activation tile), not refuse the order.
        /// </summary>
        public static Vector2Int ResolvePassableTile(H5Terrain terrain, Vector2Int tile)
        {
            if (terrain == null || terrain.IsPassable(tile.x, tile.y)) return tile;
            for (var r = 1; r <= 4; r++)
            {
                var best = tile;
                var bestDist = float.MaxValue;
                for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    var t = new Vector2Int(tile.x + dx, tile.y + dy);
                    if (!terrain.IsPassable(t.x, t.y)) continue;
                    var dist = dx * dx + dy * dy;
                    if (dist >= bestDist) continue;
                    bestDist = dist;
                    best = t;
                }
                if (bestDist < float.MaxValue) return best;
            }
            return tile;
        }

        public void Select(H5MapObject obj)
        {
            Selected = obj;
            SelectionChanged?.Invoke(obj);
        }
    }
}
