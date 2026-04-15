using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
using SatelliteViewer.Data;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class SelectionManager : MonoBehaviour
{
    public Camera sceneCamera;
    public SatelliteMetadataLoader metadata;
    public ViewerUI ui;

    [Header("UI Toolkit")]
    public UIDocument uiDocument;

    [Header("Highlight")]
    public Material highlightMaterial;

    [Header("Visibility / Focus")]
    public VisibilityManager visibility;
    public ViewModeController viewMode;

    [Header("Double Click")]
    public float doubleClickThreshold = 0.35f;

    public event Action<GameObject> OnPartSelected;
    public event Action<GameObject> OnPartFocused;

    private GameObject _selected;
    private Renderer[] _selectedRenderers;
    private Material[][] _originalMats;

    private float _lastClickTime = -999f;
    private GameObject _lastClickedRoot;
    private bool _suppressUiPush;

    private void Awake()
    {
        if (ui == null) ui = FindFirstObjectByType<ViewerUI>(FindObjectsInactive.Include);
        if (uiDocument == null) uiDocument = FindFirstObjectByType<UIDocument>(FindObjectsInactive.Include);
        if (visibility == null) visibility = FindFirstObjectByType<VisibilityManager>(FindObjectsInactive.Include);
        if (metadata == null) metadata = FindFirstObjectByType<SatelliteMetadataLoader>(FindObjectsInactive.Include);
        if (viewMode == null) viewMode = FindFirstObjectByType<ViewModeController>(FindObjectsInactive.Include);
    }

    private void Update()
    {
        if (!WasPrimaryClickPressedThisFrame()) return;

        if (IsPointerOverUIToolkit()) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (sceneCamera == null || visibility == null || visibility.satelliteRoot == null) return;

        Ray ray = sceneCamera.ScreenPointToRay(GetPointerScreenPosition());

        var partRoot = RaycastRendererRoot(ray);
        if (!partRoot) return;

        float now = Time.unscaledTime;
        bool isDoubleClick = (_lastClickedRoot == partRoot) && (now - _lastClickTime <= doubleClickThreshold);

        _lastClickTime = now;
        _lastClickedRoot = partRoot;

        if (isDoubleClick)
            FocusOn(partRoot);
        else
            Select(partRoot);
    }

    public void RegisterNewSatelliteRoot(Transform root)
    {
        if (visibility != null)
            visibility.SetSatelliteRoot(root, true);

        ClearHighlight();
    }

    public void SuppressUiPush(bool suppress)
    {
        _suppressUiPush = suppress;
    }

    public void SelectByUnityName(string unityName)
    {
        if (string.IsNullOrWhiteSpace(unityName))
            return;

        GameObject go = null;

        if (visibility != null)
            go = visibility.GetPartRootByUnityName(unityName);

        if (go == null && visibility != null)
            go = visibility.FindUnderSatelliteRootByName(unityName);

        if (go != null)
            Select(go);
    }

    public void SelectByPartId(string partId)
    {
        if (string.IsNullOrWhiteSpace(partId))
            return;

        GameObject go = null;

        if (visibility != null)
            go = visibility.GetPartRootByPartId(partId);

        if (go == null && metadata != null && metadata.TryGetPartByPartId(partId, out var part) && part != null)
        {
            if (visibility != null && !string.IsNullOrWhiteSpace(part.unityName))
                go = visibility.FindUnderSatelliteRootByName(part.unityName);
        }

        if (go != null)
            Select(go);
    }

    public void Select(GameObject partRoot)
    {
        if (!partRoot)
            return;

        ApplyHighlight(partRoot);
        PushToUI(partRoot);
        OnPartSelected?.Invoke(partRoot);
    }

    public void FocusOn(GameObject partRoot)
    {
        if (!partRoot)
            return;

        Select(partRoot);
        viewMode?.FocusPart(partRoot);
        OnPartFocused?.Invoke(partRoot);
    }

    public void ClearSelection()
    {
        ClearHighlight();
        _lastClickedRoot = null;
    }

    private GameObject RaycastRendererRoot(Ray ray)
    {
        float closest = float.MaxValue;
        Renderer closestRenderer = null;

        var root = visibility.satelliteRoot;
        var renderers = root.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (!r || !r.enabled) continue;

            if (r.bounds.IntersectRay(ray, out float dist) && dist < closest)
            {
                closest = dist;
                closestRenderer = r;
            }
        }

        if (!closestRenderer) return null;
        return ResolvePartRoot(closestRenderer.gameObject);
    }

    private GameObject ResolvePartRoot(GameObject hitGo)
    {
        if (!hitGo) return null;

        var tag = hitGo.GetComponentInParent<PartTag>();
        if (tag != null) return tag.gameObject;

        if (metadata != null && metadata.ByUnityName != null && metadata.ByUnityName.Count > 0)
        {
            Transform t = hitGo.transform;
            while (t != null)
            {
                if (metadata.ByUnityName.ContainsKey(t.name))
                    return t.gameObject;

                if (visibility != null && visibility.satelliteRoot != null && t == visibility.satelliteRoot) break;
                t = t.parent;
            }
        }

        if (visibility != null && visibility.satelliteRoot != null)
        {
            Transform t = hitGo.transform;
            Transform last = t;
            while (t != null && t != visibility.satelliteRoot)
            {
                last = t;
                t = t.parent;
            }
            return last != null ? last.gameObject : hitGo;
        }

        return hitGo;
    }

    private void ApplyHighlight(GameObject go)
    {
        ClearHighlight();

        _selected = go;
        _selectedRenderers = _selected.GetComponentsInChildren<Renderer>(true);
        _originalMats = new Material[_selectedRenderers.Length][];

        for (int r = 0; r < _selectedRenderers.Length; r++)
        {
            var rend = _selectedRenderers[r];
            if (!rend) continue;

            rend.enabled = true;
            _originalMats[r] = rend.sharedMaterials;

            if (highlightMaterial != null)
            {
                var mats = rend.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = highlightMaterial;

                rend.sharedMaterials = mats;
            }
        }
    }

    private void ClearHighlight()
    {
        if (_selectedRenderers != null && _originalMats != null)
        {
            for (int i = 0; i < _selectedRenderers.Length; i++)
            {
                var rend = _selectedRenderers[i];
                if (!rend) continue;

                if (i < _originalMats.Length && _originalMats[i] != null)
                    rend.sharedMaterials = _originalMats[i];
            }
        }

        _selected = null;
        _selectedRenderers = null;
        _originalMats = null;
    }

    private void PushToUI(GameObject go)
    {
        if (_suppressUiPush) return;
        if (go == null) return;

        if (metadata != null && metadata.ByUnityName != null && metadata.ByUnityName.TryGetValue(go.name, out var part))
            ui?.ShowPartFromSelection(part);
        else
            ui?.ShowRaw(go.name);
    }

    private bool IsPointerOverUIToolkit()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null)
            return false;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null)
            return false;

        Vector2 pos = Mouse.current.position.ReadValue();
#else
        Vector2 pos = Input.mousePosition;
#endif

        pos.y = Screen.height - pos.y;
        var picked = uiDocument.rootVisualElement.panel?.Pick(pos);
        return picked != null;
    }

    private bool WasPrimaryClickPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    private Vector2 GetPointerScreenPosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }
}