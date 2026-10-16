using UnityEngine;

namespace HoMM5.Core
{
    /// <summary>
    /// One place that talks to the input devices.
    ///
    /// Unity 6.6 marks the Input Manager as deprecated, so the code is written
    /// against both backends and picks one at compile time from
    /// <c>ENABLE_INPUT_SYSTEM</c>.  Switching is a Project Settings toggle
    /// (Player ▸ Active Input Handling), not a code change -- and the default,
    /// Input Manager, keeps working exactly as before.
    ///
    /// Every accessor is null-safe: with the new Input System a device is null
    /// until it reports in, and dereferencing it on the first frame is a crash.
    /// </summary>
    public static class H5Input
    {
        /// <summary>WASD plus the arrow keys, as a -1..1 vector.</summary>
        public static Vector2 PanAxis()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return Vector2.zero;
            var v = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
            return v;
#else
            var v = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v.y -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) v.x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) v.x += 1f;
            return v;
#endif
        }

        /// <summary>Mouse wheel, positive away from the user.</summary>
        public static float Scroll()
        {
#if ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) return 0f;
            // The Input System reports ~120 units per notch where the Input
            // Manager reported 1; normalise so zoom speed is the same.
            return m.scroll.ReadValue().y / 120f;
#else
            return Input.GetAxisRaw("Mouse ScrollWheel");
#endif
        }

        public static bool SelectPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            return m != null && m.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        public static bool PanDragStarted()
        {
#if ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            return m != null && m.middleButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(2);
#endif
        }

        public static bool PanDragEnded()
        {
#if ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            return m != null && m.middleButton.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp(2);
#endif
        }

        /// <summary>Pointer position in pixels, screen space, origin bottom-left.</summary>
        public static Vector3 PointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            var p = UnityEngine.InputSystem.Pointer.current;
            if (p == null) return Vector3.zero;
            return p.position.ReadValue();
#else
            return Input.mousePosition;
#endif
        }

        /// <summary>F1, used to switch between the surface and the underground.</summary>
        public static bool FloorTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.f1Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F1);
#endif
        }

        /// <summary>
        /// Whether the pointer is over a uGUI element, so a click on the map
        /// list is not also treated as a click on the map.
        /// </summary>
        public static bool PointerOverUi
        {
            get
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es == null) return false;
#if ENABLE_INPUT_SYSTEM
                // With the Input System UI module the pointer id is not the
                // mouse's; the parameterless overload resolves the current one.
                return es.IsPointerOverGameObject();
#else
                return es.IsPointerOverGameObject();
#endif
            }
        }
    }
}
