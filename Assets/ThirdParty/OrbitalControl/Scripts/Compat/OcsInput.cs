using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drop-in replacement for the legacy UnityEngine.Input calls used by the
/// orbital-control simulator. The host project runs the Input System only.
/// </summary>
public static class OcsInput
{
    static Key Map(KeyCode k)
    {
        switch (k)
        {
            case KeyCode.Return: return Key.Enter;
            case KeyCode.KeypadEnter: return Key.NumpadEnter;
            case KeyCode.LeftControl: return Key.LeftCtrl;
            case KeyCode.RightControl: return Key.RightCtrl;
            case KeyCode.LeftCommand: return Key.LeftMeta;
            case KeyCode.RightCommand: return Key.RightMeta;
            case KeyCode.LeftAlt: return Key.LeftAlt;
            case KeyCode.RightAlt: return Key.RightAlt;
            case KeyCode.Alpha0: return Key.Digit0;
            case KeyCode.Alpha1: return Key.Digit1;
            case KeyCode.Alpha2: return Key.Digit2;
            case KeyCode.Alpha3: return Key.Digit3;
            case KeyCode.Alpha4: return Key.Digit4;
            case KeyCode.Alpha5: return Key.Digit5;
            case KeyCode.Alpha6: return Key.Digit6;
            case KeyCode.Alpha7: return Key.Digit7;
            case KeyCode.Alpha8: return Key.Digit8;
            case KeyCode.Alpha9: return Key.Digit9;
        }
        return System.Enum.TryParse(k.ToString(), out Key key) ? key : Key.None;
    }

    static UnityEngine.InputSystem.Controls.KeyControl Ctl(KeyCode k)
    {
        var kb = Keyboard.current;
        var key = Map(k);
        if (kb == null || key == Key.None) return null;
        return kb[key];
    }

    public static bool GetKey(KeyCode k) { var c = Ctl(k); return c != null && c.isPressed; }
    public static bool GetKeyDown(KeyCode k) { var c = Ctl(k); return c != null && c.wasPressedThisFrame; }
    public static bool GetKeyUp(KeyCode k) { var c = Ctl(k); return c != null && c.wasReleasedThisFrame; }

    static UnityEngine.InputSystem.Controls.ButtonControl Btn(int b)
    {
        var m = Mouse.current;
        if (m == null) return null;
        switch (b) { case 0: return m.leftButton; case 1: return m.rightButton; case 2: return m.middleButton; }
        return null;
    }

    public static bool GetMouseButton(int b) { var c = Btn(b); return c != null && c.isPressed; }
    public static bool GetMouseButtonDown(int b) { var c = Btn(b); return c != null && c.wasPressedThisFrame; }
    public static bool GetMouseButtonUp(int b) { var c = Btn(b); return c != null && c.wasReleasedThisFrame; }

    public static Vector3 mousePosition
    {
        get { var m = Mouse.current; return m == null ? Vector3.zero : (Vector3)m.position.ReadValue(); }
    }

    /// <summary>Legacy-style scroll: about 1 per wheel notch.</summary>
    public static Vector2 mouseScrollDelta
    {
        get
        {
            var m = Mouse.current;
            if (m == null) return Vector2.zero;
            var v = m.scroll.ReadValue();
            // Windows reports 120 per notch; newer settings normalise to 1.
            if (Mathf.Abs(v.y) >= 10f || Mathf.Abs(v.x) >= 10f) v /= 120f;
            return v;
        }
    }

    /// <summary>Legacy axes used by the simulator: Mouse X/Y and Mouse ScrollWheel.</summary>
    public static float GetAxis(string axis)
    {
        var m = Mouse.current;
        if (m == null) return 0f;
        switch (axis)
        {
            case "Mouse X": return m.delta.ReadValue().x * 0.1f;
            case "Mouse Y": return m.delta.ReadValue().y * 0.1f;
            case "Mouse ScrollWheel": return mouseScrollDelta.y * 0.1f;
            case "Horizontal":
            {
                var kb = Keyboard.current; if (kb == null) return 0f;
                return (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            }
            case "Vertical":
            {
                var kb = Keyboard.current; if (kb == null) return 0f;
                return (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
            }
        }
        return 0f;
    }
}
