using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Academy.Prototype
{
    /// <summary>
    /// Simple orbit camera for the prototype scene: left or right drag to orbit, scroll to zoom,
    /// slow auto-rotation when idle.
    /// </summary>
    public sealed class PrototypeOrbitCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 targetOffset;

        [Header("Distance")]
        public float distance = 0.9f;
        public float minDistance = 0.2f;
        public float maxDistance = 4f;

        [Header("Angles")]
        public float yaw = 35f;
        public float pitch = 18f;

        [Header("Speeds")]
        public float orbitSpeed = 0.25f;
        [Tooltip("Fraction of the distance removed per scroll notch.")]
        public float zoomStep = 0.1f;

        [Header("Idle auto-rotate")]
        public bool autoRotate = true;
        public float autoRotateDegPerSec = 6f;
        public float idleSecondsBeforeAutoRotate = 4f;

        /// <summary>Optional hook: return true when the screen position (pixels, bottom-left origin) is over UI,
        /// so drags and scrolls there don't move the camera. Set by UI Toolkit screens.</summary>
        public static System.Func<Vector2, bool> PointerOverUi;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PointerOverUi = null;
        }

        private float _idle;
        private bool _dragging;
        private ProceduralSatelliteBuilder _builder;
        private SatelliteAssemblyData _framedData;

        /// <summary>When the target's satellite changes, rescale distance limits to its size.</summary>
        private void AdaptToSatellite()
        {
            if (target == null)
                return;
            if (_builder == null)
                _builder = target.GetComponent<ProceduralSatelliteBuilder>();
            if (_builder == null || _builder.Data == null || _builder.Data == _framedData)
                return;

            _framedData = _builder.Data;
            float d = _framedData.cameraDistance > 0f ? _framedData.cameraDistance : 0.9f;
            distance = d;
            minDistance = d * 0.15f;
            maxDistance = d * 6f;
        }

        /// <summary>Set the viewing distance and zoom limits for an object of a given size class.</summary>
        public void Frame(float newDistance)
        {
            if (newDistance <= 0f)
                return;
            distance = newDistance;
            minDistance = newDistance * 0.15f;
            maxDistance = newDistance * 6f;
            _idle = 0f;
        }

        private void LateUpdate()
        {
            AdaptToSatellite();

            Vector2 mousePosition = Vector2.zero;
            Vector2 delta = Vector2.zero;
            bool pressed = false;
            bool held = false;
            float scrollNotches = 0f;

#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                mousePosition = mouse.position.ReadValue();
                pressed = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;
                held = mouse.leftButton.isPressed || mouse.rightButton.isPressed;
                delta = mouse.delta.ReadValue();
                scrollNotches = mouse.scroll.ReadValue().y / 120f;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            mousePosition = Input.mousePosition;
            pressed = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            held = Input.GetMouseButton(0) || Input.GetMouseButton(1);
            delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
            scrollNotches = Input.mouseScrollDelta.y;
#endif

            bool overUi = PointerOverUi != null && PointerOverUi(mousePosition);
            if (pressed)
            {
                // GUI rects use a top-left origin
                var guiPoint = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
                _dragging = !overUi && !ProceduralSatelliteBuilder.GuiRect.Contains(guiPoint);
            }
            if (overUi)
                scrollNotches = 0f;
            if (!held)
                _dragging = false;

            if (_dragging)
            {
                yaw += delta.x * orbitSpeed;
                pitch = Mathf.Clamp(pitch - delta.y * orbitSpeed, -85f, 85f);
                _idle = 0f;
            }

            if (Mathf.Abs(scrollNotches) > 0.001f)
            {
                distance = Mathf.Clamp(distance * Mathf.Pow(1f - zoomStep, scrollNotches), minDistance, maxDistance);
                _idle = 0f;
            }

            _idle += Time.unscaledDeltaTime;
            if (autoRotate && _idle > idleSecondsBeforeAutoRotate)
                yaw += autoRotateDegPerSec * Time.unscaledDeltaTime;

            Vector3 centre = (target != null ? target.position : Vector3.zero) + targetOffset;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(centre - rotation * Vector3.forward * distance, rotation);
        }
    }
}
