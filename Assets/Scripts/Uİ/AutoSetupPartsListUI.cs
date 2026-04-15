using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AutoSetupPartsListUI : MonoBehaviour
{
    [Header("Targets")]
    public ViewerUI viewerUI;
    public Canvas targetCanvas;

    [Header("Root Names")]
    public string leftPanelName = "LeftPanel_Subsystems";
    public string rightPanelName = "RightPanel_Parts";

    [Header("Scroll Names")]
    public string subsystemsScrollName = "SubsystemsScrollView";
    public string partsScrollName = "PartsScrollView";
    public string viewportName = "Viewport";
    public string contentName = "Content";

    [Header("Layout")]
    public float leftPanelWidth = 300f;
    public float panelPadding = 10f;
    public float spacing = 5f;
    public bool createRuntimeItemPrefabIfMissing = true;

    [Header("Style")]
    public Color panelColor = new Color(0f, 0f, 0f, 0.18f);
    public Color viewportColor = new Color(1f, 1f, 1f, 0.001f);
    public Color itemColor = new Color(1f, 1f, 1f, 0.10f);

    [ContextMenu("Auto Setup Two Panel UI")]
    public void AutoSetup()
    {
        if (viewerUI == null)
            viewerUI = FindFirstObjectByType<ViewerUI>(FindObjectsInactive.Include);

        if (targetCanvas == null)
            targetCanvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);

        if (viewerUI == null)
        {
            Debug.LogError("[AutoSetupPartsListUI] ViewerUI tapılmadı.");
            return;
        }

        if (targetCanvas == null)
        {
            Debug.LogError("[AutoSetupPartsListUI] Canvas tapılmadı.");
            return;
        }

        GameObject leftPanel = GetOrCreatePanel(targetCanvas.transform, leftPanelName);
        GameObject rightPanel = GetOrCreatePanel(targetCanvas.transform, rightPanelName);

        SetupLeftPanelRect(leftPanel.GetComponent<RectTransform>());
        SetupRightPanelRect(rightPanel.GetComponent<RectTransform>());

        GameObject subsystemsScroll = GetOrCreateScrollView(leftPanel.transform, subsystemsScrollName);
        GameObject partsScroll = GetOrCreateScrollView(rightPanel.transform, partsScrollName);

        Transform subsystemsContent = SetupScrollView(subsystemsScroll);
        Transform partsContent = SetupScrollView(partsScroll);

        AssignPrivateField(viewerUI, "subsystemsListContainer", subsystemsContent);
        AssignPrivateField(viewerUI, "partsListContainer", partsContent);

        if (createRuntimeItemPrefabIfMissing)
        {
            TryAssignPrefab(viewerUI, "subsystemItemButtonPrefab", "RuntimeSubsystemItemPrefab");
            TryAssignPrefab(viewerUI, "partItemButtonPrefab", "RuntimePartItemPrefab");
        }

        Debug.Log("[AutoSetupPartsListUI] Two-panel Subsystems/Parts UI hazırlandı.");
    }

    private GameObject GetOrCreatePanel(Transform parent, string panelName)
    {
        Transform existing = parent.Find(panelName);
        GameObject panel = existing != null ? existing.gameObject : CreateUIObject(panelName, parent);

        Image img = GetOrAdd<Image>(panel);
        img.color = panelColor;
        img.raycastTarget = true;

        return panel;
    }

    private void SetupLeftPanelRect(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.offsetMin = new Vector2(panelPadding, panelPadding);
        rt.offsetMax = new Vector2(leftPanelWidth, -panelPadding);
        rt.localScale = Vector3.one;
    }

    private void SetupRightPanelRect(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(leftPanelWidth + panelPadding * 2f, panelPadding);
        rt.offsetMax = new Vector2(-panelPadding, -panelPadding);
        rt.localScale = Vector3.one;
    }

    private GameObject GetOrCreateScrollView(Transform parent, string scrollName)
    {
        Transform existing = parent.Find(scrollName);
        GameObject scrollRoot = existing != null ? existing.gameObject : CreateUIObject(scrollName, parent);

        RectTransform scrollRectTransform = scrollRoot.GetComponent<RectTransform>();
        StretchToParent(scrollRectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        Image scrollImage = GetOrAdd<Image>(scrollRoot);
        scrollImage.color = new Color(1f, 1f, 1f, 0.02f);
        scrollImage.raycastTarget = true;

        ScrollRect scrollRect = GetOrAdd<ScrollRect>(scrollRoot);
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 20f;

        GameObject scrollbarVertical = FindChild(scrollRoot.transform, "Scrollbar Vertical");
        if (scrollbarVertical == null)
            scrollbarVertical = CreateScrollbar(scrollRoot.transform, true);

        Scrollbar verticalScrollbar = scrollbarVertical.GetComponent<Scrollbar>();
        scrollRect.verticalScrollbar = verticalScrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        GameObject scrollbarHorizontal = FindChild(scrollRoot.transform, "Scrollbar Horizontal");
        if (scrollbarHorizontal != null)
            DestroyImmediate(scrollbarHorizontal);

        return scrollRoot;
    }

    private Transform SetupScrollView(GameObject scrollRoot)
    {
        ScrollRect scrollRect = GetOrAdd<ScrollRect>(scrollRoot);

        GameObject viewport = FindChild(scrollRoot.transform, viewportName);
        if (viewport == null)
            viewport = CreateUIObject(viewportName, scrollRoot.transform);

        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        StretchToParent(
            viewportRect,
            Vector2.zero,
            Vector2.one,
            new Vector2(0f, 0f),
            new Vector2(-20f, 0f)
        );

        Image viewportImage = GetOrAdd<Image>(viewport);
        viewportImage.color = viewportColor;

        GetOrAdd<Mask>(viewport).showMaskGraphic = false;

        GameObject content = FindChild(viewport.transform, contentName);
        if (content == null)
            content = CreateUIObject(contentName, viewport.transform);

        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 0f);
        contentRect.localScale = Vector3.one;

        VerticalLayoutGroup vlg = GetOrAdd<VerticalLayoutGroup>(content);
        vlg.spacing = spacing;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = GetOrAdd<ContentSizeFitter>(content);
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;

        return content.transform;
    }

    private void TryAssignPrefab(ViewerUI ui, string fieldName, string prefabName)
    {
        FieldInfo field = typeof(ViewerUI).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
            return;

        GameObject current = field.GetValue(ui) as GameObject;
        if (current != null)
            return;

        GameObject prefabLike = CreateRuntimeListItemTemplate(prefabName);
        field.SetValue(ui, prefabLike);
    }

    private GameObject CreateRuntimeListItemTemplate(string prefabName)
    {
        GameObject root = new GameObject(prefabName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        root.SetActive(false);

        RectTransform rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(220f, 34f);
        rt.localScale = Vector3.one;

        LayoutElement le = root.GetComponent<LayoutElement>();
        le.preferredHeight = 34f;
        le.minHeight = 34f;

        Image img = root.GetComponent<Image>();
        img.color = itemColor;
        img.raycastTarget = true;

        Button btn = root.GetComponent<Button>();
        btn.transition = Selectable.Transition.ColorTint;

        GameObject label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(root.transform, false);

        RectTransform lrt = label.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(8f, 4f);
        lrt.offsetMax = new Vector2(-8f, -4f);
        lrt.localScale = Vector3.one;

        TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
        tmp.text = "Item";
        tmp.fontSize = 18f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.enableWordWrapping = false;

        return root;
    }

    private void AssignPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
            field.SetValue(target, value);
    }

    private GameObject CreateScrollbar(Transform parent, bool vertical)
    {
        string name = vertical ? "Scrollbar Vertical" : "Scrollbar Horizontal";
        GameObject go = CreateUIObject(name, parent);

        RectTransform rt = go.GetComponent<RectTransform>();
        if (vertical)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(20f, 0f);
            rt.offsetMin = new Vector2(-20f, 0f);
            rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(0f, 20f);
        }

        Image bg = GetOrAdd<Image>(go);
        bg.color = new Color(1f, 1f, 1f, 0.08f);

        Scrollbar scrollbar = GetOrAdd<Scrollbar>(go);
        scrollbar.direction = vertical ? Scrollbar.Direction.BottomToTop : Scrollbar.Direction.LeftToRight;

        GameObject slidingArea = CreateUIObject("Sliding Area", go.transform);
        RectTransform saRt = slidingArea.GetComponent<RectTransform>();
        StretchToParent(saRt, Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));

        GameObject handle = CreateUIObject("Handle", slidingArea.transform);
        RectTransform handleRt = handle.GetComponent<RectTransform>();
        StretchToParent(handleRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        Image handleImage = GetOrAdd<Image>(handle);
        handleImage.color = new Color(1f, 1f, 1f, 0.35f);

        scrollbar.targetGraphic = handleImage;
        scrollbar.handleRect = handleRt;

        return go;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static GameObject FindChild(Transform parent, string childName)
    {
        Transform t = parent.Find(childName);
        return t != null ? t.gameObject : null;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        if (comp == null)
            comp = go.AddComponent<T>();
        return comp;
    }

    private static void StretchToParent(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = oMin;
        rt.offsetMax = oMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;
    }
}