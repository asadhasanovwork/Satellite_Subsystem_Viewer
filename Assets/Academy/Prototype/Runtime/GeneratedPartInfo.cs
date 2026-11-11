using UnityEngine;

namespace Academy.Prototype
{
    /// <summary>Identity of a generated part, visible in the Inspector when the part is selected.</summary>
    public sealed class GeneratedPartInfo : MonoBehaviour
    {
        public string partId;
        public string partType;
        public string label;
        [TextArea(2, 6)] public string description;
    }
}
