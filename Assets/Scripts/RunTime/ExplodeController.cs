using System.Collections.Generic;
using UnityEngine;

public class ExplodeController : MonoBehaviour
{
    public Transform satelliteRoot;
    public float maxDistance = 0.25f;

    private readonly List<Transform> _parts = new();
    private readonly Dictionary<Transform, Vector3> _originalLocalPos = new();
    private Vector3 _centerWorld;

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
        _parts.Clear();
        _originalLocalPos.Clear();

        if (!satelliteRoot) return;

        var renderers = satelliteRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);

            _centerWorld = b.center;
        }
        else
        {
            _centerWorld = satelliteRoot.position;
        }

        var tags = satelliteRoot.GetComponentsInChildren<PartTag>(true);
        foreach (var tag in tags)
        {
            if (!tag) continue;

            Transform t = tag.transform;
            _parts.Add(t);
            if (!_originalLocalPos.ContainsKey(t))
                _originalLocalPos.Add(t, t.localPosition);
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

            Vector3 dirWorld = (t.position - _centerWorld);
            if (dirWorld.sqrMagnitude < 1e-10f) dirWorld = Vector3.up;
            dirWorld.Normalize();

            Vector3 offsetWorld = dirWorld * (maxDistance * amount01);
            Vector3 offsetLocal = satelliteRoot.InverseTransformVector(offsetWorld);

            t.localPosition = orig + offsetLocal;
        }
    }

    public void Assemble()
    {
        SetExplode(0f);
    }
}