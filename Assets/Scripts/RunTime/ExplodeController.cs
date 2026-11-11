using System.Collections.Generic;
using UnityEngine;

public class ExplodeController : MonoBehaviour
{
    public Transform satelliteRoot;
    public float maxDistance = 0.25f;

    private readonly List<Transform> _parts = new();
    private readonly Dictionary<Transform, Vector3> _originalLocalPos = new();

    // Explode direction of each part, expressed in satelliteRoot local space.
    // Cached once while assembled so it stays valid while the satellite orbits/rotates
    // (a world-space centre cached at load time goes stale as soon as the root moves).
    private readonly Dictionary<Transform, Vector3> _dirRootLocal = new();

    private void Awake()
    {
        Cache();
    }

    public void SetSatelliteRoot(Transform root, bool recache = true)
    {
        satelliteRoot = root;
        if (recache) Cache();
    }

    public void Cache()
    {
        // Restore any exploded offsets before re-reading the assembled layout.
        foreach (var kv in _originalLocalPos)
            if (kv.Key) kv.Key.localPosition = kv.Value;

        _parts.Clear();
        _originalLocalPos.Clear();
        _dirRootLocal.Clear();

        if (!satelliteRoot) return;

        Vector3 centerWorld;
        var renderers = satelliteRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);

            centerWorld = b.center;
        }
        else
        {
            centerWorld = satelliteRoot.position;
        }

        Vector3 centerLocal = satelliteRoot.InverseTransformPoint(centerWorld);

        var tags = satelliteRoot.GetComponentsInChildren<PartTag>(true);
        foreach (var tag in tags)
        {
            if (!tag) continue;

            Transform t = tag.transform;
            if (t == satelliteRoot || _originalLocalPos.ContainsKey(t)) continue;

            _parts.Add(t);
            _originalLocalPos.Add(t, t.localPosition);

            Vector3 dir = satelliteRoot.InverseTransformPoint(PartCenterWorld(tag)) - centerLocal;
            if (dir.sqrMagnitude < 1e-10f) dir = Vector3.up;
            _dirRootLocal.Add(t, dir.normalized);
        }
    }

    public void SetExplode(float amount01)
    {
        if (!satelliteRoot) return;
        if (_parts.Count == 0) Cache();

        amount01 = Mathf.Clamp01(amount01);

        foreach (var t in _parts)
        {
            if (!t) continue;
            if (!_originalLocalPos.TryGetValue(t, out var orig)) continue;
            if (!_dirRootLocal.TryGetValue(t, out var dirRootLocal)) continue;

            Vector3 offsetWorld = satelliteRoot.TransformDirection(dirRootLocal) * (maxDistance * amount01);

            // localPosition is in the parent's space, which is not necessarily satelliteRoot.
            Transform space = t.parent != null ? t.parent : satelliteRoot;
            Vector3 offsetLocal = space.InverseTransformVector(offsetWorld);

            t.localPosition = orig + offsetLocal;
        }
    }

    public void Assemble()
    {
        SetExplode(0f);
    }

    private static Vector3 PartCenterWorld(PartTag tag)
    {
        var rs = tag.CachedRenderers;
        if (rs == null || rs.Length == 0)
            rs = tag.GetComponentsInChildren<Renderer>(true);

        bool any = false;
        Bounds b = default(Bounds);
        foreach (var r in rs)
        {
            if (!r) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }

        return any ? b.center : tag.transform.position;
    }
}