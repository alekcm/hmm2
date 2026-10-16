using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>
    /// The adventure-map camera: pan with WASD or the middle mouse button, zoom
    /// with the wheel, click to select.  HoMM5's own camera rides on a fixed
    /// pitch over the map, so the pitch here is locked rather than free-look.
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

        public event System.Action<H5MapObject> SelectionChanged;
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
            limits = new Rect(-padding, -padding, tileX + padding * 2f, tileY + padding * 2f);
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
            ApplyTransform();
        }

        void HandleZoom()
        {
            var wheel = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Approximately(wheel, 0f)) return;
            _distance = Mathf.Clamp(_distance - wheel * zoomSpeed, minDistance, maxDistance);
        }

        void HandlePan()
        {
            var move = Vector2.zero;

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move.y -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move.x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move.x += 1f;

            // Edge scrolling, unless the pointer is over the UI.
            if (move == Vector2.zero && !HoMM5.UI.HudInput.IsPointerOverUi)
            {
                var p = Input.mousePosition;
                if (p.x <= edgeScrollPixels) move.x -= 1f;
                else if (p.x >= Screen.width - edgeScrollPixels) move.x += 1f;
                if (p.y <= edgeScrollPixels) move.y -= 1f;
                else if (p.y >= Screen.height - edgeScrollPixels) move.y += 1f;
                if (move != Vector2.zero) move *= panSpeedMultiplierAtEdge;
            }

            // Middle mouse drag pans directly.
            if (Input.GetMouseButtonDown(2))
            {
                _dragging = true;
                _dragOrigin = Input.mousePosition;
                _dragFocus = _focus;
            }
            if (Input.GetMouseButtonUp(2)) _dragging = false;
            if (_dragging)
            {
                var delta = (Vector3)(_dragOrigin - Input.mousePosition);
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
            if (!Input.GetMouseButtonDown(0)) return;
            if (HoMM5.UI.HudInput.IsPointerOverUi) return;
            if (_cam == null) return;

            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 2000f)) return;

            var obj = hit.collider.GetComponentInParent<H5MapObject>();
            Select(obj);
        }

        public void Select(H5MapObject obj)
        {
            Selected = obj;
            SelectionChanged?.Invoke(obj);
        }
    }
}
