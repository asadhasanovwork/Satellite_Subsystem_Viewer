using System.Collections.Generic;
using Academy.Prototype;
using UnityEngine;

namespace Academy.Builder
{
    /// <summary>
    /// Holds the procedurally generated 3D satellite in the builder. Each hardware slot owns its own
    /// group of generated parts, so changing one slot rebuilds only that slot, live, in Play mode.
    /// </summary>
    public sealed class BuilderSatelliteView : MonoBehaviour
    {
        [Range(0f, 1f)] public float deployFraction = 1f;
        [Range(0f, 1f)] public float explode;
        public bool showInside;
        public float deployDuration = 4f;
        public float highlightDuration = 1.4f;

        private sealed class SlotGroup
        {
            public Transform Root;
            public readonly List<GeneratedPart> Parts = new List<GeneratedPart>();
            public readonly List<Vector3> BasePositions = new List<Vector3>();
            public readonly List<Vector3> Directions = new List<Vector3>();
        }

        private readonly Dictionary<string, SlotGroup> _groups = new Dictionary<string, SlotGroup>();
        private float _explodeDistance = 0.1f;
        private float _deployTarget = 1f;
        private string _highlightSlot;
        private float _highlightTimer;

        public bool IsDeploying { get { return !Mathf.Approximately(deployFraction, _deployTarget); } }

        public void SetExplodeDistance(float distance)
        {
            _explodeDistance = Mathf.Max(0.01f, distance);
        }

        /// <summary>Replace the parts of one slot. An empty list removes the slot's hardware.</summary>
        public void RebuildSlot(string slotId, List<PartSpec> specs, bool highlight)
        {
            ClearSlot(slotId);
            if (specs == null || specs.Count == 0)
                return;

            var group = new SlotGroup();
            group.Root = new GameObject("Slot_" + slotId).transform;
            group.Root.SetParent(transform, false);

            foreach (PartSpec spec in specs)
            {
                GeneratedPart part = PartGenerators.Create(spec, group.Root);
                part.Root.transform.localPosition = spec.position;
                part.Root.transform.localRotation = Orientation(spec);
                group.Parts.Add(part);
                group.BasePositions.Add(spec.position);
                group.Directions.Add(ExplodeDirection(spec));
            }

            _groups[slotId] = group;
            if (highlight)
            {
                _highlightSlot = slotId;
                _highlightTimer = highlightDuration;
            }
            ApplyPose();
            ApplyShowInside();
        }

        /// <summary>Generated parts of one slot (empty if the slot has no hardware).</summary>
        public IList<GeneratedPart> GetSlotParts(string slotId)
        {
            SlotGroup group;
            return slotId != null && _groups.TryGetValue(slotId, out group) ? (IList<GeneratedPart>)group.Parts : new List<GeneratedPart>();
        }

        /// <summary>Jump to a deploy state without animating.</summary>
        public void SetDeployImmediate(bool deployed)
        {
            deployFraction = deployed ? 1f : 0f;
            _deployTarget = deployFraction;
            ApplyPose();
        }

        public void ClearSlot(string slotId)
        {
            SlotGroup group;
            if (!_groups.TryGetValue(slotId, out group))
                return;
            foreach (GeneratedPart part in group.Parts)
                foreach (Mesh mesh in part.Meshes)
                    if (mesh != null) Destroy(mesh);
            if (group.Root != null)
                Destroy(group.Root.gameObject);
            _groups.Remove(slotId);
        }

        public void ClearAll()
        {
            foreach (string id in new List<string>(_groups.Keys))
                ClearSlot(id);
        }

        /// <summary>Animate to stowed (false) or deployed (true).</summary>
        public void AnimateDeploy(bool deployed)
        {
            _deployTarget = deployed ? 1f : 0f;
        }

        /// <summary>Snap to stowed, then animate the full deployment sequence.</summary>
        public void PlayLaunchSequence()
        {
            deployFraction = 0f;
            _deployTarget = 1f;
            ApplyPose();
        }

        public void SetShowInside(bool value)
        {
            showInside = value;
            ApplyShowInside();
        }

        private void Update()
        {
            if (!Mathf.Approximately(deployFraction, _deployTarget))
                deployFraction = Mathf.MoveTowards(deployFraction, _deployTarget, Time.deltaTime / Mathf.Max(0.1f, deployDuration));

            if (_highlightTimer > 0f)
            {
                _highlightTimer -= Time.deltaTime;
                if (_highlightTimer <= 0f)
                    _highlightSlot = null;
            }

            ApplyPose();
        }

        private void ApplyPose()
        {
            foreach (KeyValuePair<string, SlotGroup> kv in _groups)
            {
                SlotGroup group = kv.Value;
                float pop = 0f;
                if (kv.Key == _highlightSlot && highlightDuration > 0f)
                {
                    // Pop the new hardware outward and back so the learner sees where it went
                    float t = 1f - _highlightTimer / highlightDuration;
                    pop = Mathf.Sin(t * Mathf.PI) * 0.35f;
                }

                for (int i = 0; i < group.Parts.Count; i++)
                {
                    GeneratedPart part = group.Parts[i];
                    if (part == null || part.Root == null)
                        continue;
                    part.Root.transform.localPosition = group.BasePositions[i] + group.Directions[i] * ((explode + pop) * _explodeDistance);
                    foreach (Deployable d in part.Deployables)
                        d.Apply(deployFraction);
                }
            }
        }

        private void ApplyShowInside()
        {
            foreach (SlotGroup group in _groups.Values)
            {
                foreach (GeneratedPart part in group.Parts)
                {
                    if (part == null || part.Root == null || part.Spec == null || part.Spec.type != "bus")
                        continue;
                    foreach (Renderer r in part.Root.GetComponentsInChildren<Renderer>(true))
                        r.enabled = !showInside;
                }
            }
        }

        private void OnDestroy()
        {
            ClearAll();
        }

        private static Quaternion Orientation(PartSpec spec)
        {
            Vector3 forward = spec.forward.sqrMagnitude < 1e-8f ? Vector3.forward : spec.forward;
            Vector3 up = spec.up.sqrMagnitude < 1e-8f ? Vector3.up : spec.up;
            if (Vector3.Cross(forward, up).sqrMagnitude < 1e-8f)
                up = Mathf.Abs(forward.normalized.y) < 0.9f ? Vector3.up : Vector3.right;
            return Quaternion.LookRotation(forward, up);
        }

        private static Vector3 ExplodeDirection(PartSpec spec)
        {
            if (spec.type == "cubesat_frame" || spec.type == "bus")
                return Vector3.zero;
            Vector3 p = spec.position;
            return p.sqrMagnitude < 1e-6f ? Vector3.up : p.normalized;
        }
    }
}
