using Academy.Builder;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Main navigation for the satellite demo's existing UI Toolkit document.</summary>
public sealed class SatelliteMainMenu : MonoBehaviour
{
    private VisualElement viewer, overlay, card;
    private Button menuButton;
    private CameraOrbitMMB orbitCamera;
    private SelectionManager selection;
    private SatelliteOrbitalMotion orbit;
    private SatellitePhysicsManager physics;
    private bool cameraEnabled, selectionEnabled, orbitRunning, physicsRunning;
    private bool isOpen;
    private string activeMode = "Satellite Viewer";
    private PanelSettings panelSettings;

    public void Initialize(UIDocument document)
    {
        if (overlay != null || document == null) return;
        var root = document.rootVisualElement;
        panelSettings = document.panelSettings;
        viewer = root.Q<VisualElement>("Root");
        if (viewer == null) return;

        orbitCamera = FindFirstObjectByType<CameraOrbitMMB>();
        selection = FindFirstObjectByType<SelectionManager>();
        orbit = FindFirstObjectByType<SatelliteOrbitalMotion>();
        physics = FindFirstObjectByType<SatellitePhysicsManager>();

        menuButton = MakeButton("Menu", ShowMenu);
        menuButton.style.position = Position.Absolute;
        menuButton.style.top = 16;
        menuButton.style.left = new Length(50, LengthUnit.Percent);
        menuButton.style.marginLeft = -70;
        menuButton.style.width = 140;
        root.Add(menuButton);

        overlay = new VisualElement { name = "SatelliteMainMenu" };
        overlay.style.position = Position.Absolute;
        overlay.style.left = overlay.style.right = overlay.style.top = overlay.style.bottom = 0;
        overlay.style.backgroundColor = new Color(0.055f, 0.102f, 0.169f, 0.985f);
        overlay.style.alignItems = Align.Center;
        overlay.style.justifyContent = Justify.Center;
        overlay.style.color = new Color(0.902f, 0.933f, 0.969f, 1f);
        root.Add(overlay);

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.width = new Length(92, LengthUnit.Percent);
        scroll.style.maxWidth = 540;
        scroll.style.maxHeight = new Length(95, LengthUnit.Percent);
        overlay.Add(scroll);
        card = new VisualElement();
        card.style.paddingLeft = card.style.paddingRight = 24;
        card.style.paddingTop = card.style.paddingBottom = 20;
        scroll.Add(card);
        ShowMenu();
    }

    public void ShowMenu()
    {
        if (overlay == null) return;
        if (!isOpen)
        {
            cameraEnabled = orbitCamera != null && orbitCamera.enabled;
            selectionEnabled = selection != null && selection.enabled;
            orbitRunning = orbit != null && orbit.simulate;
            physicsRunning = physics != null && physics.simulate;
            if (orbitCamera != null) orbitCamera.enabled = false;
            if (selection != null) selection.enabled = false;
            if (orbit != null) orbit.simulate = false;
            if (physics != null) physics.simulate = false;
            isOpen = true;
        }
        viewer.style.display = DisplayStyle.None;
        menuButton.style.display = DisplayStyle.None;
        overlay.style.display = DisplayStyle.Flex;
        Heading("SATELLITE", "Explore systems. Plan missions.");
        Add("Satellite Viewer", () => OpenWorkspace("Satellite Viewer", false, false));
        Add("Satellite Builder", ShowBuilder);
        Add("Mission Operations Training", OpenOperations);
        var orbitButton = Add("Satellite Orbit Simulation", OpenOrbitSimulation);
        orbitButton.tooltip = "Fly the satellite built in the Builder in its chosen orbit: plan manoeuvres, burn the real engine and propellant.";
        var operationsButton = Add("Satellite Operations Simulation",
            () => OpenWorkspace("Satellite Operations Simulation", true, true));
        operationsButton.SetEnabled(physics != null);
        if (physics == null) operationsButton.tooltip = "No physics simulation component is available in this scene.";
        Add("Options", ShowOptions);
        Add("Credits", () => Message("Credits", "Satellite Subsystem Viewer\nBuilt with Unity.\n\nProject team credits can be added here."));
        Add("Exit", ShowExit);
        card.Q<Button>()?.Focus();
    }

    private void OpenWorkspace(string mode, bool runOrbit, bool runPhysics)
    {
        activeMode = mode;
        orbitRunning = runOrbit;
        physicsRunning = runPhysics;
        RestoreControls();
        viewer.style.display = DisplayStyle.Flex;
        overlay.style.display = DisplayStyle.None;
        menuButton.style.display = DisplayStyle.Flex;
        menuButton.tooltip = activeMode + " — return to main menu";
        menuButton.Focus();
    }

    private void RestoreControls()
    {
        if (!isOpen) return;
        if (orbitCamera != null) orbitCamera.enabled = cameraEnabled;
        if (selection != null) selection.enabled = selectionEnabled;
        if (orbit != null) orbit.simulate = orbitRunning;
        if (physics != null) physics.simulate = physicsRunning;
        isOpen = false;
    }

    private void ShowBuilder()
    {
        Heading("Satellite Builder",
            "Design your own satellite step by step: choose its type and orbit, then build every subsystem from real hardware.");

        Step(1, "Satellite type", "CubeSat, navigation satellite or communications satellite.");
        Step(2, "Orbit", "LEO, MEO or GEO. The orbit decides which hardware is mandatory.");
        Step(3, "Subsystems", "Structure, power, computer, communications, attitude control, thermal, propulsion, payload.");
        Step(4, "Hardware", "Fill each subsystem's mandatory slots from the parts catalogue.");
        Step(5, "Budgets", "Close the mass, power, link, thermal and delta-v budgets with margin.");
        Step(6, "Launch", "Deploy your satellite and fly it in the simulator.");

        Add("Start building", OpenBuilder).Focus();

        var note = new Label("You will choose a CubeSat (LEO), navigation satellite (MEO) or communications satellite (GEO), then build it subsystem by subsystem.");
        note.style.fontSize = 12;
        note.style.whiteSpace = WhiteSpace.Normal;
        note.style.color = new Color(0.557f, 0.639f, 0.737f, 1f);
        note.style.marginBottom = 12;
        card.Add(note);

        Add("Back", ShowMenu);
    }

    private void OpenBuilder()
    {
        // Loads the SatelliteBuilder scene (Build Settings or, in the editor, straight from the asset);
        // if it does not exist the builder scene is generated in code. No manual setup needed.
        RestoreControls();
        SatelliteBuilderSceneFactory.Open(panelSettings);
    }

    private void OpenOrbitSimulation()
    {
        // The orbital-control simulator (Assets/ThirdParty/OrbitalControl) with the Builder's satellite and orbit.
        RestoreControls();
        Academy.OrbitSim.OrbitSimBridge.Open(panelSettings);
    }

    private void OpenOperations()
    {
        // Flies the satellite built in the Builder inside OpenC3 COSMOS (full screen). If OpenC3 is not
        // available the start-up screen offers the Unity ground-station console instead.
        RestoreControls();
        Academy.OpsLink.OpsLinkFlow.Begin(panelSettings);
    }

    private void Step(int number, string title, string description)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.FlexStart;
        row.style.marginBottom = 10;

        var badge = new Label(number.ToString());
        badge.style.width = 28;
        badge.style.height = 28;
        badge.style.minWidth = 28;
        badge.style.marginRight = 12;
        badge.style.unityTextAlign = TextAnchor.MiddleCenter;
        badge.style.unityFontStyleAndWeight = FontStyle.Bold;
        badge.style.fontSize = 14;
        badge.style.color = new Color(0.902f, 0.933f, 0.969f, 1f);
        badge.style.backgroundColor = new Color(0.137f, 0.224f, 0.353f, 1f);
        badge.style.borderTopLeftRadius = badge.style.borderTopRightRadius = 14;
        badge.style.borderBottomLeftRadius = badge.style.borderBottomRightRadius = 14;
        row.Add(badge);

        var text = new VisualElement();
        text.style.flexGrow = 1;
        text.style.flexShrink = 1;
        var titleLabel = new Label(title);
        titleLabel.style.fontSize = 15;
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleLabel.style.whiteSpace = WhiteSpace.Normal;
        text.Add(titleLabel);
        var descriptionLabel = new Label(description);
        descriptionLabel.style.fontSize = 12;
        descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
        descriptionLabel.style.color = new Color(0.557f, 0.639f, 0.737f, 1f);
        text.Add(descriptionLabel);
        row.Add(text);

        card.Add(row);
    }

    private void ShowOptions()
    {
        Heading("Options", "Settings apply immediately for this session.");
        var volume = new Slider("Master volume", 0, 1) { value = AudioListener.volume };
        volume.style.marginTop = volume.style.marginBottom = 16;
        volume.RegisterValueChangedCallback(evt => AudioListener.volume = evt.newValue);
        card.Add(volume);
        var fullscreen = new Toggle("Fullscreen") { value = Screen.fullScreen };
        fullscreen.style.marginBottom = 20;
        fullscreen.RegisterValueChangedCallback(evt => Screen.fullScreen = evt.newValue);
        card.Add(fullscreen);
        Add("Back", ShowMenu).Focus();
    }

    private void Message(string title, string message)
    {
        Heading(title, message);
        Add("Back", ShowMenu).Focus();
    }

    private void ShowExit()
    {
        Heading("Exit", "Close Satellite Subsystem Viewer?");
        Add("Cancel", ShowMenu).Focus();
        Add("Exit", () =>
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        });
    }

    private void Heading(string title, string subtitle)
    {
        card.Clear();
        var heading = new Label(title);
        heading.style.fontSize = 30;
        heading.style.unityFontStyleAndWeight = FontStyle.Bold;
        heading.style.whiteSpace = WhiteSpace.Normal;
        heading.style.marginBottom = 8;
        card.Add(heading);
        var description = new Label(subtitle);
        description.style.fontSize = 14;
        description.style.whiteSpace = WhiteSpace.Normal;
        description.style.color = new Color(0.557f, 0.639f, 0.737f, 1f);
        description.style.marginBottom = 24;
        card.Add(description);
    }

    private Button Add(string title, System.Action action)
    {
        var button = MakeButton(title, action);
        card.Add(button);
        return button;
    }

    private static Button MakeButton(string title, System.Action action)
    {
        var button = new Button(action) { text = title };
        button.style.minHeight = 48;
        button.style.marginBottom = 8;
        button.style.fontSize = 16;
        button.style.whiteSpace = WhiteSpace.Normal;
        button.style.color = new Color(0.902f, 0.933f, 0.969f, 1f);
        button.style.backgroundColor = new Color(0.102f, 0.176f, 0.275f, 1f);
        button.style.borderTopLeftRadius = button.style.borderTopRightRadius = 7;
        button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 7;
        button.RegisterCallback<FocusInEvent>(_ => button.style.backgroundColor = new Color(0.137f, 0.224f, 0.353f, 1f));
        button.RegisterCallback<FocusOutEvent>(_ => button.style.backgroundColor = new Color(0.102f, 0.176f, 0.275f, 1f));
        button.RegisterCallback<MouseEnterEvent>(_ => button.style.backgroundColor = new Color(0.137f, 0.224f, 0.353f, 1f));
        button.RegisterCallback<MouseLeaveEvent>(_ => button.style.backgroundColor = new Color(0.102f, 0.176f, 0.275f, 1f));
        return button;
    }

    private void OnDestroy()
    {
        RestoreControls();
        if (viewer != null) viewer.style.display = DisplayStyle.Flex;
        overlay?.RemoveFromHierarchy();
        menuButton?.RemoveFromHierarchy();
    }
}