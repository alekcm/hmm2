using UnityEngine;

namespace HoMM5.Core
{
    /// <summary>
    /// One place that talks to the input devices.
    ///
    /// Unity 6.6 marks the Input Manager as deprecated, so the code is written
    /// against both backends and picks one at compile time.  Both conditions are
    /// required:
    ///
    ///   H5_INPUT_SYSTEM      the package is present, so the asmdef reference to
    ///                        Unity.InputSystem resolves and the types compile.
    ///                        Set by versionDefines in HoMM5.Runtime.asmdef.
    ///   ENABLE_INPUT_SYSTEM  the project's Active Input Handling actually feeds
    ///                        the Input System, so devices are not null.  Unity
    ///                        sets this project-wide, regardless of whether this
    ///                        assembly can see the reference -- guarding on it
    ///                        alone is what produced CS0234.
    ///
    /// Switching backends is a Project Settings toggle, not a code change, and
    /// the default, Input Manager, keeps working exactly as before.
    ///
    /// Every accessor is null-safe: with the new Input System a device is null
    /// until it reports in, and dereferencing it on the first frame is a crash.
    /// </summary>
    public static class H5Input
    {
        /// <summary>WASD plus the arrow keys, as a -1..1 vector.</summary>
        public static Vector2 PanAxis()
        {
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
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
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) return 0f;
            // The Input System reports ~120 units per notch where the Input
            // Manager reported 1; normalise so zoom speed is the same.
            return m.scroll.ReadValue().y / 120f;
#else
            return Input.GetAxisRaw("Mouse ScrollWheel");
#endif
        }

        public static bool MoveCommandPressed()
        {
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            return m != null && m.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }

        public static bool SelectPressed()
        {
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            return m != null && m.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        public static bool PanDragStarted()
        {
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            return m != null && m.middleButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(2);
#endif
        }

        public static bool PanDragEnded()
        {
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            return m != null && m.middleButton.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp(2);
#endif
        }

        /// <summary>Pointer position in pixels, screen space, origin bottom-left.</summary>
        public static Vector3 PointerPosition()
        {
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
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
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.f1Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F1);
#endif
        }

        /// <summary>F5: quick save.</summary>
        public static bool SavePressed()
        {
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.f5Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F5);
#endif
        }

        /// <summary>F9: quick load.</summary>
        public static bool LoadPressed()
        {
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.f9Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F9);
#endif
        }

        /// <summary>H: hero screen (skills and perks).</summary>
        public static bool HeroScreenPressed()
        {
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.hKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.H);
#endif
        }

        /// <summary>M: mute or unmute all sound.</summary>
        public static bool MutePressed()
        {
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.mKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.M);
#endif
        }

        /// <summary>T: town screen of the selected (or first owned) town.</summary>
        public static bool TownScreenPressed()
        {
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.tKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.T);
#endif
        }

        /// <summary>
        /// Space: end the turn. Heroes 5 binds this to the turn hand-off, and it
        /// is the key a player reaches for most often in a match.
        /// </summary>
        public static bool EndTurnPressed()
        {
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.spaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Space);
#endif
        }

        /// <summary>Escape: close whatever modal panel is open.</summary>
        public static bool CancelPressed()
        {
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
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
                // Same call under both backends: the EventSystem resolves the
                // current pointer id itself, whichever module drives it.
                return es != null && es.IsPointerOverGameObject();
            }
        }
    }
}
