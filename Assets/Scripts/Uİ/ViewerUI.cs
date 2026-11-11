using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;
using SatelliteViewer.Data;

using UIButton = UnityEngine.UI.Button;
using UISlider = UnityEngine.UI.Slider;
using UIDropdown = TMPro.TMP_Dropdown;

public class ViewerUI : MonoBehaviour
{
    [Header("Managers")]
    [SerializeField] private SatelliteOperationsManager operationsManager;
    [SerializeField] private SatellitePhysicsManager physicsManager;
    [SerializeField] private SatelliteMetadataLoader metadataLoader;
    [SerializeField] private VisibilityManager visibilityManager;
    [SerializeField] private ViewModeController viewModeController;
    [SerializeField] private ExplodeController explodeController;
    [SerializeField] private SelectionManager selectionManager;

    [Header("Legacy uGUI Optional")]
    [SerializeField] private UIDropdown satelliteDropdown;
    [SerializeField] private UISlider explodeSlider;
    [SerializeField] private UIButton activateButton;
    [SerializeField] private UIButton standbyButton;
    [SerializeField] private UIButton deactivateButton;
    [SerializeField] private UIButton faultButton;
    [SerializeField] private UIButton showAllButton;
    [SerializeField] private UIButton normalButton;
    [SerializeField] private UIButton assembleButton;

    [Header("UI Toolkit")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Settings")]
    [SerializeField] private bool autoFindManagers = true;
    [SerializeField] private bool verboseLogs = true;
    [SerializeField] private bool allowSubsystemControlWhenNoPartSelected = true;
    [SerializeField] private string fallbackSubsystemForNoSelection = "ADCS";

    private VisualElement root;
    private ListView subsystemsList;
    private ListView partsList;
    private TextField searchInput;

    private UnityEngine.UIElements.Button toolkitActivateButton;
    private UnityEngine.UIElements.Button toolkitStandbyButton;
    private UnityEngine.UIElements.Button toolkitDeactivateButton;
    private UnityEngine.UIElements.Button toolkitFaultButton;
    private UnityEngine.UIElements.Button toolkitShowAllButton;
    private UnityEngine.UIElements.Button toolkitNormalButton;
    private UnityEngine.UIElements.Button toolkitAssembleButton;

    private Label satelliteTitleText;
    private Label selectedPartText;
    private Label selectedSubsystemText;
    private Label selectedPartStateText;
    private Label selectedSubsystemStateText;
    private Label selectedReasonText;
    private Label typeText;
    private Label statusText;
    private Label descriptionText;
    private Label rawInfoText;

    private Label batteryText;
    private Label tempText;
    private Label attitudeText;
    private Label signalText;
    private Label fuelText;
    private Label payloadText;
    private Label linkText;

    private readonly List<SubsystemData> subsystemItems = new();
    private readonly List<PartData> partItems = new();

    private string currentSelectedPart = string.Empty;
    private string currentSelectedSubsystem = string.Empty;
    private string lastReason = string.Empty;
    private string currentSatelliteTitle = "Satellite Viewer";
    private PartData currentPartData;

    private bool toolkitBound;
    private float nextTelemetryRefreshTime;
    private const float TelemetryRefreshInterval = 0.1f;

    private void Awake()
    {
        AutoWireManagers();
        BindLegacyButtons();
        BindLegacyInputs();
        BindUIToolkit();
    }

    private void OnEnable()
    {
        AutoWireManagers();

        if (operationsManager != null)
            operationsManager.OnOperationalStateChanged += RefreshUI;

        if (metadataLoader != null)
            metadataLoader.OnMetadataLoaded += OnMetadataLoaded;
    }

    private void OnDisable()
    {
        if (operationsManager != null)
            operationsManager.OnOperationalStateChanged -= RefreshUI;

        if (metadataLoader != null)
            metadataLoader.OnMetadataLoaded -= OnMetadataLoaded;
    }

    private void Start()
    {
        AutoWireManagers();
        BindUIToolkit();

        var mainMenu = GetComponent<SatelliteMainMenu>();
        if (mainMenu == null) mainMenu = gameObject.AddComponent<SatelliteMainMenu>();
        mainMenu.Initialize(uiDocument);

        if (metadataLoader == null)
        {
            Debug.LogError("[ViewerUI] metadataLoader is NULL!");
            return;
        }

        if (metadataLoader.Data != null && metadataLoader.Data.subsystems != null)
            OnMetadataLoaded(metadataLoader.Data);
        else if (verboseLogs)
            Debug.Log("[ViewerUI] Waiting for metadata...");
    }

    private void Update()
    {
        // Throttled: rebuilding 7 label strings every frame allocates and dirties UI layout.
        if (Time.unscaledTime < nextTelemetryRefreshTime)
            return;

        nextTelemetryRefreshTime = Time.unscaledTime + TelemetryRefreshInterval;
        RefreshTelemetryOnly();
    }

    private void AutoWireManagers()
    {
        if (!autoFindManagers)
            return;

        if (operationsManager == null)
            operationsManager = FindFirstObjectByType<SatelliteOperationsManager>(FindObjectsInactive.Include);

        if (physicsManager == null)
            physicsManager = FindFirstObjectByType<SatellitePhysicsManager>(FindObjectsInactive.Include);

        if (metadataLoader == null)
            metadataLoader = FindFirstObjectByType<SatelliteMetadataLoader>(FindObjectsInactive.Include);

        if (visibilityManager == null)
            visibilityManager = FindFirstObjectByType<VisibilityManager>(FindObjectsInactive.Include);

        if (viewModeController == null)
            viewModeController = FindFirstObjectByType<ViewModeController>(FindObjectsInactive.Include);

        if (explodeController == null)
            explodeController = FindFirstObjectByType<ExplodeController>(FindObjectsInactive.Include);

        if (selectionManager == null)
            selectionManager = FindFirstObjectByType<SelectionManager>(FindObjectsInactive.Include);

        if (uiDocument == null)
            uiDocument = FindFirstObjectByType<UIDocument>(FindObjectsInactive.Include);
    }

    private void BindUIToolkit()
    {
        if (uiDocument == null)
            return;

        // Called from both Awake and Start: bind only once, otherwise every button/list/search
        // callback is registered twice and each click runs its action twice.
        if (toolkitBound)
            return;

        root = uiDocument.rootVisualElement;
        if (root == null)
            return;

        toolkitBound = true;

        // "Root" and "CenterSpacer" fill the screen over the 3D view. With the default
        // PickingMode.Position every click over the viewport counted as "over UI", so
        // SelectionManager never picked parts. Children stay pickable.
        var layoutRoot = root.Q<VisualElement>("Root");
        if (layoutRoot != null)
            layoutRoot.pickingMode = PickingMode.Ignore;

        var centerSpacer = root.Q<VisualElement>("CenterSpacer");
        if (centerSpacer != null)
            centerSpacer.pickingMode = PickingMode.Ignore;

        subsystemsList = root.Q<ListView>("SubsystemsList");
        partsList = root.Q<ListView>("PartsList");
        searchInput = root.Q<TextField>("SearchInput");

        toolkitActivateButton = root.Q<UnityEngine.UIElements.Button>("ActivateButton");
        toolkitStandbyButton = root.Q<UnityEngine.UIElements.Button>("StandbyButton");
        toolkitDeactivateButton = root.Q<UnityEngine.UIElements.Button>("DeactivateButton");
        toolkitFaultButton = root.Q<UnityEngine.UIElements.Button>("FaultButton");
        toolkitShowAllButton = root.Q<UnityEngine.UIElements.Button>("ShowAllButton");
        toolkitNormalButton = root.Q<UnityEngine.UIElements.Button>("NormalButton");
        toolkitAssembleButton = root.Q<UnityEngine.UIElements.Button>("AssembleButton");

        satelliteTitleText = root.Q<Label>("SatelliteTitleText");
        selectedPartText = root.Q<Label>("SelectedPartText");
        selectedSubsystemText = root.Q<Label>("SelectedSubsystemText");
        selectedPartStateText = root.Q<Label>("SelectedPartStateText");
        selectedSubsystemStateText = root.Q<Label>("SelectedSubsystemStateText");
        selectedReasonText = root.Q<Label>("SelectedReasonText");
        typeText = root.Q<Label>("TypeText");
        statusText = root.Q<Label>("StatusText");
        descriptionText = root.Q<Label>("DescriptionText");
        rawInfoText = root.Q<Label>("RawInfoText");

        batteryText = root.Q<Label>("BatteryText");
        tempText = root.Q<Label>("TempText");
        attitudeText = root.Q<Label>("AttitudeText");
        signalText = root.Q<Label>("SignalText");
        fuelText = root.Q<Label>("FuelText");
        payloadText = root.Q<Label>("PayloadText");
        linkText = root.Q<Label>("LinkText");

        if (searchInput != null)
            searchInput.RegisterValueChangedCallback(_ => RebuildPartsList());

        BindToolkitButtons();
        SetupSubsystemsListView();
        SetupPartsListView();
    }

    private void BindToolkitButtons()
    {
        if (toolkitActivateButton != null)
            toolkitActivateButton.clicked += () => ApplyCurrentSelectionState(OperationalState.Active, "Manual activate");

        if (toolkitStandbyButton != null)
            toolkitStandbyButton.clicked += () => ApplyCurrentSelectionState(OperationalState.Standby, "Manual standby");

        if (toolkitDeactivateButton != null)
            toolkitDeactivateButton.clicked += () => ApplyCurrentSelectionState(OperationalState.Off, "Manual deactivate");

        if (toolkitFaultButton != null)
            toolkitFaultButton.clicked += () => ApplyCurrentSelectionState(OperationalState.Fault, "Manual fault");

        if (toolkitShowAllButton != null)
            toolkitShowAllButton.clicked += OnShowAllClicked;

        if (toolkitNormalButton != null)
            toolkitNormalButton.clicked += OnNormalClicked;

        if (toolkitAssembleButton != null)
            toolkitAssembleButton.clicked += OnAssembleClicked;
    }

    private void SetupSubsystemsListView()
    {
        if (subsystemsList == null)
            return;

        subsystemsList.selectionType = SelectionType.Single;
        subsystemsList.itemsSource = subsystemItems;

        subsystemsList.makeItem = () =>
        {
            var label = new Label();
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.paddingLeft = 8;
            label.style.paddingRight = 8;
            label.style.height = 32;
            label.style.color = Color.white;
            return label;
        };

        subsystemsList.bindItem = (element, index) =>
        {
            var label = element as Label;
            var item = subsystemItems[index];
            label.text = string.IsNullOrWhiteSpace(item.name) ? item.id : item.name;
        };

        subsystemsList.selectionChanged += selected =>
        {
            foreach (var obj in selected)
            {
                if (obj is SubsystemData subsystem)
                {
                    currentSelectedSubsystem = subsystem.id;
                    currentSelectedPart = string.Empty;
                    currentPartData = null;
                    lastReason = $"Selected subsystem: {subsystem.id}";

                    // Isolation was implemented (ViewModeController/VisibilityManager) but never
                    // triggered from the UI. Show/Normal buttons return to the full view.
                    if (subsystem.canIsolate && viewModeController != null)
                        viewModeController.IsolateSubsystem(subsystem.id);

                    RebuildPartsList();
                    RefreshUI();
                    break;
                }
            }
        };
    }

    private void SetupPartsListView()
    {
        if (partsList == null)
            return;

        partsList.selectionType = SelectionType.Single;
        partsList.itemsSource = partItems;

        partsList.makeItem = () =>
        {
            var label = new Label();
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.paddingLeft = 8;
            label.style.paddingRight = 8;
            label.style.height = 30;
            label.style.color = Color.white;
            return label;
        };

        partsList.bindItem = (element, index) =>
        {
            var label = element as Label;
            var item = partItems[index];
            label.text = !string.IsNullOrWhiteSpace(item.displayName) ? item.displayName : item.unityName;
        };

        partsList.selectionChanged += selected =>
        {
            foreach (var obj in selected)
            {
                if (obj is PartData part)
                {
                    ShowPart(part);
                    break;
                }
            }
        };
    }

    private void BindLegacyButtons()
    {
        SafeBindButton(activateButton, () => ApplyCurrentSelectionState(OperationalState.Active, "Manual activate"));
        SafeBindButton(standbyButton, () => ApplyCurrentSelectionState(OperationalState.Standby, "Manual standby"));
        SafeBindButton(deactivateButton, () => ApplyCurrentSelectionState(OperationalState.Off, "Manual deactivate"));
        SafeBindButton(faultButton, () => ApplyCurrentSelectionState(OperationalState.Fault, "Manual fault"));
        SafeBindButton(showAllButton, OnShowAllClicked);
        SafeBindButton(normalButton, OnNormalClicked);
        SafeBindButton(assembleButton, OnAssembleClicked);
    }

    private void BindLegacyInputs()
    {
        if (explodeSlider != null)
        {
            explodeSlider.onValueChanged.RemoveListener(OnExplodeChanged);
            explodeSlider.onValueChanged.AddListener(OnExplodeChanged);
        }
    }

    private void SafeBindButton(UIButton button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private void OnMetadataLoaded(SatelliteData data)
    {
        if (data == null || data.subsystems == null)
        {
            Debug.LogError("[ViewerUI] OnMetadataLoaded -> data is null");
            return;
        }

        currentPartData = null;
        currentSelectedPart = string.Empty;
        currentSelectedSubsystem = data.subsystems.Count > 0 ? data.subsystems[0].id : string.Empty;
        currentSatelliteTitle = string.IsNullOrWhiteSpace(data.satelliteName) ? "Satellite Viewer" : data.satelliteName;

        if (verboseLogs)
            Debug.Log($"[ViewerUI] OnMetadataLoaded -> subsystem count: {data.subsystems.Count}");

        RebuildSubsystemsList();
        RebuildPartsList();
        RefreshUI();
    }

    private void RebuildSubsystemsList()
    {
        subsystemItems.Clear();

        if (metadataLoader?.Data?.subsystems == null)
            return;

        foreach (var subsystem in metadataLoader.Data.subsystems)
        {
            if (subsystem != null)
                subsystemItems.Add(subsystem);
        }

        subsystemsList?.Rebuild();
    }

    private void RebuildPartsList()
    {
        partItems.Clear();

        if (metadataLoader?.Data?.subsystems == null)
            return;

        string filter = searchInput != null
            ? (searchInput.value ?? string.Empty).Trim().ToLowerInvariant()
            : string.Empty;

        foreach (var subsystem in metadataLoader.Data.subsystems)
        {
            if (subsystem == null || subsystem.parts == null)
                continue;

            if (!string.IsNullOrWhiteSpace(currentSelectedSubsystem) && subsystem.id != currentSelectedSubsystem)
                continue;

            foreach (var part in subsystem.parts)
            {
                if (part == null)
                    continue;

                string display = !string.IsNullOrWhiteSpace(part.displayName) ? part.displayName : part.unityName;
                string searchBlob = $"{display} {part.unityName} {part.partId} {part.type} {subsystem.id} {subsystem.name}".ToLowerInvariant();

                if (!string.IsNullOrWhiteSpace(filter) && !searchBlob.Contains(filter))
                    continue;

                partItems.Add(part);
            }
        }

        partsList?.Rebuild();
    }

    public void ShowPart(PartData part)
    {
        if (part == null)
        {
            ShowRaw("Part data is null.");
            return;
        }

        currentPartData = part;
        currentSelectedPart = !string.IsNullOrWhiteSpace(part.unityName) ? part.unityName : part.displayName;

        if (metadataLoader != null && !string.IsNullOrWhiteSpace(part.partId))
            currentSelectedSubsystem = metadataLoader.GetSubsystemIdForPart(part.partId);

        lastReason = $"Selected part: {part.displayName}";

        if (selectionManager != null)
        {
            selectionManager.SuppressUiPush(true);

            if (!string.IsNullOrWhiteSpace(part.partId))
                selectionManager.SelectByPartId(part.partId);
            else if (!string.IsNullOrWhiteSpace(part.unityName))
                selectionManager.SelectByUnityName(part.unityName);

            selectionManager.SuppressUiPush(false);
        }

        RefreshUI();
    }

    public void ShowPartFromSelection(PartData part)
    {
        if (part == null)
        {
            ShowRaw("Part data is null.");
            return;
        }

        currentPartData = part;
        currentSelectedPart = !string.IsNullOrWhiteSpace(part.unityName) ? part.unityName : part.displayName;

        if (metadataLoader != null && !string.IsNullOrWhiteSpace(part.partId))
            currentSelectedSubsystem = metadataLoader.GetSubsystemIdForPart(part.partId);

        lastReason = $"Selected part: {part.displayName}";
        RefreshUI();
    }

    public void ShowRaw(string rawText)
    {
        SetLabel(rawInfoText, rawText);
        lastReason = "Raw data shown";
        RefreshSelectionInfo();
    }

    public void ApplyCurrentSelectionState(OperationalState targetState, string reason = "")
    {
        if (operationsManager == null)
            return;

        string selectedSubsystem = GetCurrentSubsystem();

        if (!string.IsNullOrWhiteSpace(currentSelectedPart) && currentPartData != null && !currentPartData.canToggle)
        {
            // JSON canToggle:false (structure, mechanical, fuel brick) was ignored before.
            lastReason = $"Part '{currentSelectedPart}' cannot be toggled";
        }
        else if (!string.IsNullOrWhiteSpace(currentSelectedPart) && operationsManager.HasPart(currentSelectedPart))
        {
            operationsManager.SetPartState(currentSelectedPart, targetState, reason);
            lastReason = $"Part '{currentSelectedPart}' -> {targetState}";
        }
        else if (allowSubsystemControlWhenNoPartSelected && !string.IsNullOrWhiteSpace(selectedSubsystem))
        {
            operationsManager.SetSubsystemState(selectedSubsystem, targetState, reason);
            lastReason = $"Subsystem '{selectedSubsystem}' -> {targetState}";
        }
        else
        {
            lastReason = "No valid part or subsystem selected";
        }

        RefreshUI();
    }

    private void OnShowAllClicked()
    {
        visibilityManager?.ShowAll();
        viewModeController?.EnterNormalMode();
        subsystemsList?.ClearSelection(); // allow re-selecting the same subsystem to isolate again
        lastReason = "Show all";
        RefreshSelectionInfo();
    }

    private void OnNormalClicked()
    {
        viewModeController?.EnterNormalMode();
        visibilityManager?.ShowAll();
        subsystemsList?.ClearSelection(); // allow re-selecting the same subsystem to isolate again
        lastReason = "Normal mode";
        RefreshSelectionInfo();
    }

    private void OnAssembleClicked()
    {
        if (explodeSlider != null)
            explodeSlider.SetValueWithoutNotify(0f);

        explodeController?.Assemble();
        lastReason = "Assembled";
        RefreshSelectionInfo();
    }

    private void OnExplodeChanged(float value)
    {
        explodeController?.SetExplode(value);
    }

    public void SetSatelliteTitle(string title)
    {
        currentSatelliteTitle = string.IsNullOrWhiteSpace(title) ? "Satellite Viewer" : title;
        RefreshHeader();
    }

    public void RefreshAll()
    {
        RefreshUI();
    }

    private void RefreshUI()
    {
        RefreshHeader();
        RefreshSelectionInfo();
        RefreshTelemetryOnly();
        RefreshButtonInteractability();
    }

    private void RefreshHeader()
    {
        SetLabel(satelliteTitleText, currentSatelliteTitle);

        if (satelliteDropdown != null)
        {
            satelliteDropdown.ClearOptions();
            satelliteDropdown.AddOptions(new List<TMP_Dropdown.OptionData>
            {
                new TMP_Dropdown.OptionData(currentSatelliteTitle)
            });
            satelliteDropdown.value = 0;
            satelliteDropdown.RefreshShownValue();
        }
    }

    private void RefreshSelectionInfo()
    {
        if (operationsManager == null)
        {
            SetLabel(selectedPartText, "Select a part");
            SetLabel(selectedSubsystemText, "Subsystem: -");
            SetLabel(selectedPartStateText, "Type: -");
            SetLabel(selectedSubsystemStateText, "Status: -");
            SetLabel(selectedReasonText, "Reason: Operations manager missing");
            SetLabel(typeText, "Type: -");
            SetLabel(statusText, "Status: -");
            SetLabel(descriptionText, "");
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentSelectedPart) && currentPartData != null)
        {
            string shownTitle = !string.IsNullOrWhiteSpace(currentPartData.displayName)
                ? currentPartData.displayName
                : currentSelectedPart;

            string subsystem = GetCurrentSubsystem();
            OperationalState partState = operationsManager.GetPartState(currentSelectedPart);
            string type = !string.IsNullOrWhiteSpace(currentPartData.type) ? currentPartData.type : "-";
            string status = !string.IsNullOrWhiteSpace(currentPartData.statusText) ? currentPartData.statusText : partState.ToString();

            SetLabel(selectedPartText, shownTitle);
            SetLabel(selectedSubsystemText, $"Subsystem: {subsystem}");
            SetLabel(selectedPartStateText, $"Type: {type}");
            SetLabel(selectedSubsystemStateText, $"Status: {partState}");
            SetLabel(selectedReasonText, $"Reason: {lastReason}");
            SetLabel(typeText, $"Type: {type}");
            SetLabel(statusText, $"Status: {status}");
            SetLabel(descriptionText, currentPartData.description ?? "");

            SetLabel(rawInfoText,
                $"Selected Part: {shownTitle}\n" +
                $"Unity Name: {currentPartData.unityName}\n" +
                $"Part ID: {currentPartData.partId}\n" +
                $"Subsystem: {subsystem}\n" +
                $"State: {partState}");
        }
        else
        {
            SetLabel(selectedPartText, "Select a part");
            SetLabel(selectedSubsystemText, $"Subsystem: {(string.IsNullOrWhiteSpace(currentSelectedSubsystem) ? "-" : currentSelectedSubsystem)}");
            SetLabel(selectedPartStateText, "Type: -");
            SetLabel(selectedSubsystemStateText, "Status: -");
            SetLabel(selectedReasonText, $"Reason: {lastReason}");
            SetLabel(typeText, "Type: -");
            SetLabel(statusText, "Status: -");
            SetLabel(descriptionText, "");
            SetLabel(rawInfoText, "");
        }
    }

    private void RefreshTelemetryOnly()
    {
        if (physicsManager == null)
            return;

        SetLabel(batteryText, $"Battery: {physicsManager.batteryPercent:0.0}%");
        SetLabel(tempText, $"Temp: {physicsManager.temperatureC:0.0} C");
        SetLabel(attitudeText, $"Attitude Error: {physicsManager.attitudeErrorDeg:0.00} deg");
        SetLabel(signalText, $"Signal: {(physicsManager.signalQuality * 100f):0.0}%");
        SetLabel(fuelText, $"Fuel: {physicsManager.fuelPercent:0.0}%");
        SetLabel(payloadText, $"Payload Data: {physicsManager.payloadDataRate:0.00}");
        SetLabel(linkText, $"Cmd Link: {(physicsManager.commandLinkAvailable ? "AVAILABLE" : "LOST")}");
    }

    private void RefreshButtonInteractability()
    {
        bool hasOps = operationsManager != null;
        bool partSelected = !string.IsNullOrWhiteSpace(currentSelectedPart);
        bool partLocked = partSelected && currentPartData != null && !currentPartData.canToggle;
        bool canControlPart = hasOps && partSelected && !partLocked && operationsManager.HasPart(currentSelectedPart);
        bool canControlSubsystem = hasOps && !partLocked && allowSubsystemControlWhenNoPartSelected && !string.IsNullOrWhiteSpace(GetCurrentSubsystem());
        bool enable = canControlPart || canControlSubsystem;

        SetToolkitButtonEnabled(toolkitActivateButton, enable);
        SetToolkitButtonEnabled(toolkitStandbyButton, enable);
        SetToolkitButtonEnabled(toolkitDeactivateButton, enable);
        SetToolkitButtonEnabled(toolkitFaultButton, enable);

        if (activateButton != null) activateButton.interactable = enable;
        if (standbyButton != null) standbyButton.interactable = enable;
        if (deactivateButton != null) deactivateButton.interactable = enable;
        if (faultButton != null) faultButton.interactable = enable;
    }

    private void SetToolkitButtonEnabled(UnityEngine.UIElements.Button button, bool enabled)
    {
        if (button == null)
            return;

        button.SetEnabled(enabled);
    }

    private string GetCurrentSubsystem()
    {
        if (!string.IsNullOrWhiteSpace(currentSelectedSubsystem))
            return currentSelectedSubsystem;

        if (operationsManager == null)
            return fallbackSubsystemForNoSelection;

        if (!string.IsNullOrWhiteSpace(currentSelectedPart))
        {
            string subsystem = operationsManager.GetPartSubsystem(currentSelectedPart);
            if (!string.IsNullOrWhiteSpace(subsystem))
                return subsystem;
        }

        if (currentPartData != null && metadataLoader != null && !string.IsNullOrWhiteSpace(currentPartData.partId))
        {
            string subsystemId = metadataLoader.GetSubsystemIdForPart(currentPartData.partId);
            if (!string.IsNullOrWhiteSpace(subsystemId))
                return subsystemId;
        }

        return fallbackSubsystemForNoSelection;
    }

    private void SetLabel(Label label, string value)
    {
        if (label != null)
            label.text = value;
    }
}