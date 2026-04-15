using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CameraOrbitMMB : MonoBehaviour
{
    public Transform target;
    public float distance = 12f;
    public float rotateSpeed = 0.25f;
    public float zoomSpeed = 2.5f;
    public float minDistance = 1.0f;
    public float maxDistance = 30f;

    [Header("Optional")]
    public bool allowAltLeftMouseOrbit = true;
    public bool smoothTargetChanges = true;
    public float smoothTime = 8f;

    private float yaw = 0f;
    private float pitch = 20f;
    private Vector3 _currentTargetPos;
    private bool _initialized;

    private void Start()
    {
        if (target != null)
            ReinitializeFromCurrentView();
    }

    private void LateUpdate()
    {
#if ENABLE_INPUT_SYSTEM
        if (target == null || Mouse.current == null)
            return;

        bool middleOrbit = Mouse.current.middleButton.isPressed;

        bool altLeftOrbit = false;
        if (allowAltLeftMouseOrbit)
        {
            bool altPressed = Keyboard.current != null &&
                              (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);

            altLeftOrbit = altPressed && Mouse.current.leftButton.isPressed;
        }

        if (middleOrbit || altLeftOrbit)
        {
            Vector2 d = Mouse.current.delta.ReadValue();
            yaw += d.x * rotateSpeed;
            pitch -= d.y * rotateSpeed;
            pitch = Mathf.Clamp(pitch, -80f, 80f);
        }

        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance -= scroll * 0.001f * zoomSpeed * distance;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        Vector3 desiredTarget = target.position;

        if (!_initialized)
        {
            _currentTargetPos = desiredTarget;
            _initialized = true;
        }

        if (smoothTargetChanges)
            _currentTargetPos = Vector3.Lerp(_currentTargetPos, desiredTarget, Time.unscaledDeltaTime * smoothTime);
        else
            _currentTargetPos = desiredTarget;

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pos = _currentTargetPos + (rot * new Vector3(0, 0, -distance));

        transform.SetPositionAndRotation(pos, rot);
        transform.LookAt(_currentTargetPos);
#endif
    }

    public void SetTarget(Transform newTarget, float? newDistance = null)
    {
        if (newTarget == null) return;

        target = newTarget;

        if (newDistance.HasValue)
            distance = Mathf.Clamp(newDistance.Value, minDistance, maxDistance);

        _currentTargetPos = target.position;
        ReinitializeFromCurrentView();
        _initialized = true;
    }

    public void ReinitializeFromCurrentView()
    {
        if (target == null) return;

        Vector3 dir = (transform.position - target.position).normalized;
        if (dir.sqrMagnitude < 0.0001f)
            dir = new Vector3(0f, 0.3f, -1f).normalized;

        pitch = Mathf.Asin(dir.y) * Mathf.Rad2Deg;
        yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        distance = Mathf.Clamp(Vector3.Distance(transform.position, target.position), minDistance, maxDistance);
        _currentTargetPos = target.position;
    }

    public void FrameBounds(Bounds bounds, float paddingMultiplier = 1.4f)
    {
        float radius = bounds.extents.magnitude;
        float wanted = Mathf.Clamp(radius * paddingMultiplier, minDistance, maxDistance);
        distance = wanted;
        _currentTargetPos = bounds.center;
        _initialized = true;
    }
}