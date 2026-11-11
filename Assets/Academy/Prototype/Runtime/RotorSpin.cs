using UnityEngine;

namespace Academy.Prototype
{
    /// <summary>Spins a rotor (e.g. a reaction-wheel flywheel) in Play mode.</summary>
    public sealed class RotorSpin : MonoBehaviour
    {
        public static bool GlobalEnabled = true;

        [Tooltip("Visual spin speed. Real wheels run at thousands of rpm; this is slowed down so it can be seen.")]
        public float rpm = 90f;
        public Vector3 axis = Vector3.up;

        private void Update()
        {
            if (!GlobalEnabled)
                return;

            transform.Rotate(axis, rpm * 6f * Time.deltaTime, Space.Self);
        }
    }
}
