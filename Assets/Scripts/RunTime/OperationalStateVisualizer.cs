using System.Collections.Generic;
using UnityEngine;
using SatelliteViewer.Data;

public class OperationalStateVisualizer : MonoBehaviour
{
    [Header("Optional materials")]
    public Material standbyMaterial;
    public Material activeMaterial;
    public Material faultMaterial;
    public bool hideWhenOff = true;

    [Header("Debug")]
    public bool verboseLogs = true;

    private readonly Dictionary<Renderer, Material[]> _originalMaterials = new();

    public void CacheOriginals(Transform satelliteRoot)
    {
        _originalMaterials.Clear();
        if (!satelliteRoot) return;

        var renderers = satelliteRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (!r) continue;
            if (!_originalMaterials.ContainsKey(r))
                _originalMaterials.Add(r, r.sharedMaterials);
        }

        if (verboseLogs)
            Debug.Log($"[OperationalStateVisualizer] Cached original materials for {renderers.Length} renderers.");
    }

    public void ApplyState(PartTag tag, OperationalState state)
    {
        if (!tag)
        {
            Debug.LogWarning("[OperationalStateVisualizer] ApplyState called with null PartTag.");
            return;
        }

        if (tag.CachedRenderers == null || tag.CachedRenderers.Length == 0)
            tag.Cache();

        if (verboseLogs)
            Debug.Log($"[OperationalStateVisualizer] ApplyState -> {tag.name}, subsystem={tag.subsystemId}, partId={tag.partId}, state={state}, renderers={tag.CachedRenderers.Length}");

        foreach (var r in tag.CachedRenderers)
        {
            if (!r) continue;

            if (!_originalMaterials.ContainsKey(r))
                _originalMaterials[r] = r.sharedMaterials;

            switch (state)
            {
                case OperationalState.Off:
                    if (hideWhenOff)
                    {
                        r.enabled = false;
                        if (verboseLogs)
                            Debug.Log($"[OperationalStateVisualizer] Renderer OFF -> {r.name} disabled");
                    }
                    else
                    {
                        r.enabled = true;
                        RestoreOriginal(r);
                    }
                    break;

                case OperationalState.Standby:
                    r.enabled = true;
                    if (standbyMaterial != null)
                    {
                        ReplaceAllMaterials(r, standbyMaterial);
                        if (verboseLogs)
                            Debug.Log($"[OperationalStateVisualizer] Renderer STANDBY -> {r.name} material replaced");
                    }
                    else
                    {
                        RestoreOriginal(r);
                        if (verboseLogs)
                            Debug.Log($"[OperationalStateVisualizer] Renderer STANDBY -> {r.name} original restored");
                    }
                    break;

                case OperationalState.Active:
                    r.enabled = true;
                    if (activeMaterial != null)
                    {
                        ReplaceAllMaterials(r, activeMaterial);
                        if (verboseLogs)
                            Debug.Log($"[OperationalStateVisualizer] Renderer ACTIVE -> {r.name} material replaced");
                    }
                    else
                    {
                        RestoreOriginal(r);
                        if (verboseLogs)
                            Debug.Log($"[OperationalStateVisualizer] Renderer ACTIVE -> {r.name} original restored");
                    }
                    break;

                case OperationalState.Fault:
                    r.enabled = true;
                    if (faultMaterial != null)
                    {
                        ReplaceAllMaterials(r, faultMaterial);
                        if (verboseLogs)
                            Debug.Log($"[OperationalStateVisualizer] Renderer FAULT -> {r.name} material replaced");
                    }
                    else
                    {
                        RestoreOriginal(r);
                        if (verboseLogs)
                            Debug.Log($"[OperationalStateVisualizer] Renderer FAULT -> {r.name} original restored");
                    }
                    break;
            }
        }
    }

    public void RestoreAll(Transform satelliteRoot)
    {
        if (!satelliteRoot) return;

        var tags = satelliteRoot.GetComponentsInChildren<PartTag>(true);
        foreach (var tag in tags)
        {
            if (!tag) continue;
            if (tag.CachedRenderers == null || tag.CachedRenderers.Length == 0) tag.Cache();

            foreach (var r in tag.CachedRenderers)
            {
                if (!r) continue;
                r.enabled = true;
                RestoreOriginal(r);
            }
        }

        if (verboseLogs)
            Debug.Log("[OperationalStateVisualizer] RestoreAll completed.");
    }

    private void ReplaceAllMaterials(Renderer renderer, Material material)
    {
        if (!renderer || !material) return;

        var mats = renderer.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
            mats[i] = material;

        renderer.sharedMaterials = mats;
    }

    private void RestoreOriginal(Renderer renderer)
    {
        if (!renderer) return;
        if (_originalMaterials.TryGetValue(renderer, out var mats) && mats != null)
            renderer.sharedMaterials = mats;
    }
}