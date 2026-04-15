using System.Collections.Generic;
using UnityEngine;

public class VisibilityManager : MonoBehaviour
{
    public Transform satelliteRoot;

    private Renderer[] _allRenderers;
    private PartTag[] _allTags;

    private readonly Dictionary<string, List<PartTag>> _subsystemTags = new();
    private readonly Dictionary<string, GameObject> _unityNameToPartRoot = new();
    private readonly Dictionary<string, GameObject> _partIdToPartRoot = new();

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
        _subsystemTags.Clear();
        _unityNameToPartRoot.Clear();
        _partIdToPartRoot.Clear();

        if (!satelliteRoot)
        {
            _allRenderers = null;
            _allTags = null;
            return;
        }

        _allRenderers = satelliteRoot.GetComponentsInChildren<Renderer>(true);
        _allTags = satelliteRoot.GetComponentsInChildren<PartTag>(true);

        foreach (var tag in _allTags)
        {
            if (!tag) continue;

            tag.Cache();

            if (!string.IsNullOrWhiteSpace(tag.subsystemId))
            {
                if (!_subsystemTags.TryGetValue(tag.subsystemId, out var list))
                {
                    list = new List<PartTag>();
                    _subsystemTags.Add(tag.subsystemId, list);
                }
                list.Add(tag);
            }

            if (!string.IsNullOrWhiteSpace(tag.unityName) && !_unityNameToPartRoot.ContainsKey(tag.unityName))
                _unityNameToPartRoot.Add(tag.unityName, tag.gameObject);

            if (!string.IsNullOrWhiteSpace(tag.partId) && !_partIdToPartRoot.ContainsKey(tag.partId))
                _partIdToPartRoot.Add(tag.partId, tag.gameObject);
        }

        Debug.Log($"[VisibilityManager] Cache complete. Renderers={_allRenderers?.Length ?? 0}, PartTags={_allTags?.Length ?? 0}");
    }

    public IEnumerable<PartTag> GetAllPartTags()
    {
        if (_allTags == null || _allTags.Length == 0) Cache();
        return _allTags;
    }

    public GameObject GetPartRootByUnityName(string unityName)
    {
        if (string.IsNullOrWhiteSpace(unityName)) return null;
        if (_unityNameToPartRoot.Count == 0) Cache();
        return _unityNameToPartRoot.TryGetValue(unityName, out var go) ? go : null;
    }

    public GameObject GetPartRootByPartId(string partId)
    {
        if (string.IsNullOrWhiteSpace(partId)) return null;
        if (_partIdToPartRoot.Count == 0) Cache();
        return _partIdToPartRoot.TryGetValue(partId, out var go) ? go : null;
    }

    public GameObject FindUnderSatelliteRootByName(string objectName)
    {
        if (!satelliteRoot || string.IsNullOrWhiteSpace(objectName)) return null;

        var transforms = satelliteRoot.GetComponentsInChildren<Transform>(true);
        foreach (var t in transforms)
        {
            if (t != null && t.name == objectName)
                return t.gameObject;
        }

        return null;
    }

    public void HideAll()
    {
        if (!satelliteRoot) return;
        if (_allRenderers == null || _allRenderers.Length == 0) Cache();

        foreach (var r in _allRenderers)
            if (r) r.enabled = false;
    }

    public void ShowAll()
    {
        if (!satelliteRoot) return;
        if (_allRenderers == null || _allRenderers.Length == 0) Cache();

        foreach (var r in _allRenderers)
            if (r) r.enabled = true;
    }

    public void ShowOnly(GameObject root)
    {
        if (!satelliteRoot) return;
        HideAll();
        if (!root) return;

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r) r.enabled = true;
    }

    public void ShowOnly(HashSet<GameObject> keep)
    {
        if (!satelliteRoot) return;
        HideAll();
        if (keep == null || keep.Count == 0) return;

        foreach (var go in keep)
        {
            if (!go) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r) r.enabled = true;
        }
    }

    public void ShowOnlySubsystem(string subsystemId)
    {
        if (!satelliteRoot) return;
        if (_subsystemTags.Count == 0) Cache();

        if (string.IsNullOrWhiteSpace(subsystemId) ||
            !_subsystemTags.TryGetValue(subsystemId, out var tags) ||
            tags == null || tags.Count == 0)
        {
            Debug.LogWarning($"[VisibilityManager] No parts found for subsystem '{subsystemId}'. Showing all.");
            ShowAll();
            return;
        }

        HideAll();

        foreach (var tag in tags)
        {
            if (!tag) continue;

            var renderers = tag.CachedRenderers != null && tag.CachedRenderers.Length > 0
                ? tag.CachedRenderers
                : tag.GetComponentsInChildren<Renderer>(true);

            foreach (var r in renderers)
                if (r) r.enabled = true;
        }
    }

    public void ShowSubsystemWithContext(string subsystemId, IEnumerable<string> contextPartIds = null)
    {
        if (!satelliteRoot) return;

        var keep = new HashSet<GameObject>();

        if (!string.IsNullOrWhiteSpace(subsystemId) && _subsystemTags.TryGetValue(subsystemId, out var tags))
        {
            foreach (var tag in tags)
                if (tag) keep.Add(tag.gameObject);
        }

        if (contextPartIds != null)
        {
            foreach (var id in contextPartIds)
            {
                var go = GetPartRootByPartId(id);
                if (!go)
                    go = FindUnderSatelliteRootByName(id);

                if (go) keep.Add(go);
            }
        }

        if (keep.Count == 0)
        {
            ShowAll();
            return;
        }

        ShowOnly(keep);
    }
}