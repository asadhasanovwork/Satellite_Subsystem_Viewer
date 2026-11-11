using System;
using System.Collections.Generic;
using System.IO;
using Academy.Prototype;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Academy.Builder
{
    /// <summary>
    /// Satellite Builder screen. Flow: choose satellite type (which fixes the orbit) -> build each
    /// subsystem by filling its hardware slots -> review budgets -> launch (deployment) and save.
    /// Every hardware change is generated procedurally in the 3D view immediately.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SatelliteBuilderUI : MonoBehaviour
    {
        [Header("References")]
        public BuilderSatelliteView view;
        public PrototypeOrbitCamera orbitCamera;

        [Header("Data")]
        [Tooltip("Relative to Assets/StreamingAssets.")]
        public string indexFile = "Academy/Builder/builder_index.json";
        public string mainMenuScene = "Test_Demo";

        // Palette (matches the main menu)
        private static readonly Color Bg = new Color(0.055f, 0.102f, 0.169f, 0.96f);
        private static readonly Color PanelBg = new Color(0.075f, 0.133f, 0.212f, 0.95f);
        private static readonly Color CardBg = new Color(0.102f, 0.176f, 0.275f, 1f);
        private static readonly Color CardSelected = new Color(0.122f, 0.231f, 0.322f, 1f);
        private static readonly Color Accent = new Color(0.102f, 0.176f, 0.275f, 1f);
        private static readonly Color AccentHover = new Color(0.137f, 0.224f, 0.353f, 1f);
        private static readonly Color TextColor = new Color(0.902f, 0.933f, 0.969f, 1f);
        private static readonly Color Muted = new Color(0.557f, 0.639f, 0.737f, 1f);
        private static readonly Color Green = new Color(0.235f, 0.784f, 0.706f, 1f);
        private static readonly Color Red = new Color(0.898f, 0.282f, 0.302f, 1f);
        private static readonly Color Amber = new Color(0.949f, 0.710f, 0.267f, 1f);
        private static readonly Color Grey = new Color(0.365f, 0.455f, 0.569f, 1f);

        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _typeScreen;
        private VisualElement _workspace;
        private ScrollView _subsystemList;
        private ScrollView _detail;
        private Label _title;
        private Label _subtitle;
        private Label _progressLabel;
        private Label _statusLine;
        private Label _massLabel;
        private Label _powerLabel;
        private Label _generationLabel;
        private Label _marginLabel;
        private Button _previousButton;
        private Button _nextButton;
        private Button _deployButton;

        private readonly List<BuilderTemplate> _templates = new List<BuilderTemplate>();
        private readonly Dictionary<string, string> _builtKeys = new Dictionary<string, string>();
        private SatelliteDesign _design;
        private int _currentSubsystem;
        private bool _deployed = true;
        private bool _inReview;
        private string _lastSavedPath;

        // ================================================================== lifecycle

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            if (view == null) view = FindFirstObjectByType<BuilderSatelliteView>();
            if (orbitCamera == null) orbitCamera = FindFirstObjectByType<PrototypeOrbitCamera>();
        }

        private void OnEnable()
        {
            PrototypeOrbitCamera.PointerOverUi = IsPointerOverUi;
            // Static state left over from the prototype scenes (or kept when domain reload is off).
            RotorSpin.GlobalEnabled = true;
            if (_design != null)
            {
                _design.Changed -= OnDesignChanged;
                _design.Changed += OnDesignChanged;
            }
        }

        private void OnDisable()
        {
            if (PrototypeOrbitCamera.PointerOverUi == (Func<Vector2, bool>)IsPointerOverUi)
                PrototypeOrbitCamera.PointerOverUi = null;
            if (_design != null)
                _design.Changed -= OnDesignChanged;
        }

        private void Start()
        {
            LoadTemplates();
            BuildLayout();
            ShowTypeSelection();
        }

        // ================================================================== data

        private void LoadTemplates()
        {
            _templates.Clear();
            string indexPath = Path.Combine(Application.streamingAssetsPath, indexFile);
            if (!File.Exists(indexPath))
            {
                Debug.LogError("[SatelliteBuilder] Index not found: " + indexPath);
                return;
            }

            BuilderIndex index = JsonUtility.FromJson<BuilderIndex>(File.ReadAllText(indexPath));
            string folder = Path.GetDirectoryName(indexPath);
            foreach (string file in index.templates)
            {
                string path = Path.Combine(folder, file);
                if (!File.Exists(path))
                {
                    Debug.LogError("[SatelliteBuilder] Template not found: " + path);
                    continue;
                }
                try
                {
                    BuilderTemplate template = JsonUtility.FromJson<BuilderTemplate>(File.ReadAllText(path));
                    if (template != null)
                        _templates.Add(template);
                }
                catch (Exception e)
                {
                    Debug.LogError("[SatelliteBuilder] Failed to parse " + path + ": " + e.Message);
                }
            }
        }

        // ================================================================== layout

        private void BuildLayout()
        {
            _root = _document.rootVisualElement;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.flexGrow = 1;

            _workspace = new VisualElement();
            Stretch(_workspace);
            _workspace.style.flexDirection = FlexDirection.Column;
            _workspace.pickingMode = PickingMode.Ignore;
            _root.Add(_workspace);

            // ---- top bar
            VisualElement top = Row();
            top.style.height = 60;
            top.style.paddingLeft = top.style.paddingRight = 16;
            top.style.alignItems = Align.Center;
            top.style.backgroundColor = Bg;
            var titles = new VisualElement();
            titles.style.flexGrow = 1;
            _title = MakeLabel("Satellite Builder", 20, true, TextColor);
            _subtitle = MakeLabel(string.Empty, 12, false, Muted);
            titles.Add(_title);
            titles.Add(_subtitle);
            top.Add(titles);
            top.Add(MakeButton("Change satellite type", ShowTypeSelection, 190));
            top.Add(MakeButton("Main menu", GoToMainMenu, 120));
            _workspace.Add(top);

            // ---- body: subsystem list | 3D view | hardware panel
            VisualElement body = Row();
            body.style.flexGrow = 1;
            body.pickingMode = PickingMode.Ignore;
            _workspace.Add(body);

            var left = new VisualElement();
            left.style.width = 250;
            left.style.backgroundColor = PanelBg;
            left.style.paddingLeft = left.style.paddingRight = 10;
            left.style.paddingTop = left.style.paddingBottom = 10;
            left.Add(MakeLabel("Subsystems", 15, true, TextColor));
            _progressLabel = MakeLabel(string.Empty, 11, false, Muted);
            _progressLabel.style.marginBottom = 8;
            left.Add(_progressLabel);
            _subsystemList = new ScrollView(ScrollViewMode.Vertical);
            _subsystemList.style.flexGrow = 1;
            left.Add(_subsystemList);
            left.Add(MakeButton("Review & launch", ShowReview, 0));
            body.Add(left);

            var centre = new VisualElement();
            centre.style.flexGrow = 1;
            centre.pickingMode = PickingMode.Ignore;
            centre.style.justifyContent = Justify.FlexEnd;
            centre.style.alignItems = Align.Center;
            _statusLine = MakeLabel(string.Empty, 13, false, TextColor);
            _statusLine.pickingMode = PickingMode.Ignore;
            _statusLine.style.marginBottom = 14;
            _statusLine.style.maxWidth = 520;
            _statusLine.style.paddingLeft = _statusLine.style.paddingRight = 12;
            _statusLine.style.paddingTop = _statusLine.style.paddingBottom = 6;
            _statusLine.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            Round(_statusLine, 6);
            centre.Add(_statusLine);
            body.Add(centre);

            var right = new VisualElement();
            right.style.width = 420;
            right.style.backgroundColor = PanelBg;
            right.style.flexDirection = FlexDirection.Column;
            _detail = new ScrollView(ScrollViewMode.Vertical);
            _detail.style.flexGrow = 1;
            _detail.style.paddingLeft = _detail.style.paddingRight = 12;
            _detail.style.paddingTop = 12;
            right.Add(_detail);
            VisualElement nav = Row();
            nav.style.paddingLeft = nav.style.paddingRight = 12;
            nav.style.paddingTop = nav.style.paddingBottom = 10;
            _previousButton = MakeButton("< Previous", () =>
            {
                if (_design == null) return;
                GoToSubsystem(_inReview ? _design.Template.subsystems.Count - 1 : _currentSubsystem - 1);
            }, 0);
            _nextButton = MakeButton("Next >", () => GoToSubsystem(_inReview ? _currentSubsystem : _currentSubsystem + 1), 0);
            _previousButton.style.flexGrow = 1;
            _nextButton.style.flexGrow = 1;
            nav.Add(_previousButton);
            nav.Add(_nextButton);
            right.Add(nav);
            body.Add(right);

            // ---- bottom bar: budgets + view controls
            VisualElement bottom = Row();
            bottom.style.height = 54;
            bottom.style.paddingLeft = bottom.style.paddingRight = 16;
            bottom.style.alignItems = Align.Center;
            bottom.style.backgroundColor = Bg;
            _massLabel = StatLabel(bottom);
            _powerLabel = StatLabel(bottom);
            _generationLabel = StatLabel(bottom);
            _marginLabel = StatLabel(bottom);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            bottom.Add(spacer);

            var inside = new Toggle("Show inside");
            inside.style.color = TextColor;
            inside.style.marginRight = 16;
            inside.RegisterValueChangedCallback(e => { if (view != null) view.SetShowInside(e.newValue); });
            bottom.Add(inside);

            var explode = new Slider("Explode", 0f, 1f);
            explode.style.width = 220;
            explode.style.color = TextColor;
            explode.style.marginRight = 16;
            explode.RegisterValueChangedCallback(e => { if (view != null) view.explode = e.newValue; });
            bottom.Add(explode);

            _deployButton = MakeButton("Stow for launch", ToggleDeploy, 150);
            bottom.Add(_deployButton);
            _workspace.Add(bottom);

            // ---- satellite type selection (full-screen overlay)
            _typeScreen = new VisualElement();
            Stretch(_typeScreen);
            _typeScreen.style.backgroundColor = new Color(0.055f, 0.102f, 0.169f, 0.985f);
            _typeScreen.style.alignItems = Align.Center;
            _typeScreen.style.justifyContent = Justify.Center;
            _root.Add(_typeScreen);
        }

        // ================================================================== screen 1: satellite type

        private void ShowTypeSelection()
        {
            _typeScreen.Clear();
            _typeScreen.style.display = DisplayStyle.Flex;
            _workspace.style.display = DisplayStyle.None;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = new Length(96, LengthUnit.Percent);
            scroll.style.width = new Length(96, LengthUnit.Percent);
            scroll.style.maxWidth = 1100;
            _typeScreen.Add(scroll);

            var heading = MakeLabel("Choose your satellite type", 30, true, TextColor);
            heading.style.unityTextAlign = TextAnchor.MiddleCenter;
            scroll.Add(heading);
            var sub = MakeLabel("The type decides the orbit, and the orbit decides which hardware is mandatory. You will then build the satellite subsystem by subsystem.", 14, false, Muted);
            sub.style.unityTextAlign = TextAnchor.MiddleCenter;
            sub.style.marginBottom = 24;
            sub.style.marginTop = 6;
            scroll.Add(sub);

            VisualElement cards = Row();
            cards.style.flexWrap = Wrap.Wrap;
            cards.style.justifyContent = Justify.Center;
            scroll.Add(cards);

            if (_templates.Count == 0)
            {
                cards.Add(MakeLabel("No satellite templates found in StreamingAssets/" + indexFile, 14, false, Red));
            }

            foreach (BuilderTemplate template in _templates)
            {
                BuilderTemplate t = template;
                var card = new VisualElement();
                card.style.width = 320;
                card.style.marginLeft = card.style.marginRight = 10;
                card.style.marginBottom = 16;
                card.style.paddingLeft = card.style.paddingRight = 18;
                card.style.paddingTop = card.style.paddingBottom = 16;
                card.style.backgroundColor = CardBg;
                Round(card, 10);

                Label badge = Badge(t.orbitClass, OrbitColor(t.orbitClass));
                badge.style.alignSelf = Align.FlexStart;
                card.Add(badge);
                Label name = MakeLabel(t.displayName, 20, true, TextColor);
                name.style.marginTop = 8;
                card.Add(name);
                card.Add(MakeLabel(t.satelliteClass, 13, false, Muted));
                Label summary = MakeLabel(t.orbitSummary, 12, false, TextColor);
                summary.style.marginTop = 10;
                summary.style.marginBottom = 10;
                card.Add(summary);

                int slots = 0;
                foreach (BuilderSubsystem s in t.subsystems) slots += s.slots.Count;
                card.Add(MakeLabel(t.subsystems.Count + " subsystems · " + slots + " hardware slots", 12, false, Muted));

                Button build = MakeButton("Build this satellite", () => StartDesign(t), 0);
                build.style.marginTop = 14;
                card.Add(build);
                cards.Add(card);
            }

            VisualElement footer = Row();
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 12;
            if (_design != null)
            {
                footer.Add(MakeButton("Continue: " + _design.Template.displayName, () =>
                {
                    _typeScreen.style.display = DisplayStyle.None;
                    _workspace.style.display = DisplayStyle.Flex;
                }, 260));
            }
            footer.Add(MakeButton("Main menu", GoToMainMenu, 160));
            scroll.Add(footer);
        }

        private void StartDesign(BuilderTemplate template)
        {
            if (template == null || template.subsystems == null || template.subsystems.Count == 0)
            {
                Debug.LogError("[SatelliteBuilder] Template has no subsystems: " + (template != null ? template.id : "null"));
                return;
            }
            if (_design != null)
                _design.Changed -= OnDesignChanged;

            if (view != null)
            {
                view.ClearAll();
                view.SetExplodeDistance(template.explodeDistance);
                view.deployFraction = 1f;
                view.AnimateDeploy(true);
            }
            _deployed = true;
            _deployButton.text = "Stow for launch";
            _builtKeys.Clear();
            _lastSavedPath = null;

            _design = new SatelliteDesign(template);
            _design.Changed += OnDesignChanged;

            if (orbitCamera != null)
                orbitCamera.Frame(template.cameraDistance);

            _title.text = "Satellite Builder  ·  " + template.displayName;
            _subtitle.text = template.satelliteClass + "  ·  " + template.orbitClass + " orbit (fixed by the satellite type)";

            _typeScreen.style.display = DisplayStyle.None;
            _workspace.style.display = DisplayStyle.Flex;

            OnDesignChanged();
            GoToSubsystem(0);
            SetStatus("Step 1: start with the " + template.subsystems[0].name + ". Pick its hardware on the right; it appears in 3D straight away.");
        }

        // ================================================================== design changes

        private void OnDesignChanged()
        {
            SyncView();
            RefreshSubsystemList();
            RefreshTotals();
        }

        /// <summary>Rebuild only the slots whose selection or sliders changed.</summary>
        private void SyncView()
        {
            if (_design == null || view == null)
                return;

            foreach (BuilderSlot slot in _design.AllSlots)
            {
                string key = _design.BuildKey(slot);
                string old;
                if (!_builtKeys.TryGetValue(slot.id, out old))
                    old = string.Empty;
                if (key == old)
                    continue;

                string newVariant = key.Split('|')[0];
                string oldVariant = old.Split('|')[0];
                bool highlight = newVariant.Length > 0 && newVariant != oldVariant;
                view.RebuildSlot(slot.id, _design.BuildParts(slot), highlight);
                _builtKeys[slot.id] = key;
            }
        }

        // ================================================================== screen 2: subsystems

        private void GoToSubsystem(int index)
        {
            if (_design == null)
                return;
            if (index >= _design.Template.subsystems.Count)
            {
                ShowReview();
                return;
            }

            _inReview = false;
            _currentSubsystem = Mathf.Clamp(index, 0, _design.Template.subsystems.Count - 1);
            _previousButton.SetEnabled(_currentSubsystem > 0);
            _nextButton.text = _currentSubsystem == _design.Template.subsystems.Count - 1 ? "Review & launch >" : "Next >";
            RefreshDetail();
            RefreshSubsystemList();
        }

        private void RefreshSubsystemList()
        {
            if (_design == null || _subsystemList == null)
                return;

            _subsystemList.Clear();
            int complete = 0;
            List<BuilderSubsystem> subsystems = _design.Template.subsystems;
            for (int i = 0; i < subsystems.Count; i++)
            {
                int index = i;
                BuilderSubsystem subsystem = subsystems[i];
                int missing = _design.MissingCount(subsystem);
                int added = 0;
                foreach (BuilderSlot slot in subsystem.slots)
                    if (_design.StatusOf(slot) == SlotStatus.Added) added++;
                if (missing == 0) complete++;

                var button = new Button(() => GoToSubsystem(index));
                StyleButton(button, i == _currentSubsystem ? CardSelected : CardBg);
                button.style.flexDirection = FlexDirection.Column;
                button.style.alignItems = Align.FlexStart;
                button.style.paddingTop = button.style.paddingBottom = 6;
                button.text = string.Empty;
                button.Add(MakeLabel((i + 1) + ". " + subsystem.name, 13, true, TextColor));
                string state = missing > 0 ? missing + " required item" + (missing > 1 ? "s" : "") + " missing"
                             : added > 0 ? "Complete · " + added + " hardware" : "Complete";
                button.Add(MakeLabel(state, 11, false, missing > 0 ? Red : Green));
                _subsystemList.Add(button);
            }

            _progressLabel.text = complete + " of " + subsystems.Count + " subsystems complete";
        }

        private void RefreshDetail()
        {
            _detail.Clear();
            BuilderSubsystem subsystem = _design.Template.subsystems[_currentSubsystem];

            _detail.Add(MakeLabel("SUBSYSTEM " + (_currentSubsystem + 1) + " OF " + _design.Template.subsystems.Count, 11, true, Muted));
            Label name = MakeLabel(subsystem.name, 22, true, TextColor);
            name.style.marginTop = 2;
            _detail.Add(name);
            Label description = MakeLabel(subsystem.description, 13, false, Muted);
            description.style.marginBottom = 10;
            _detail.Add(description);

            int missing = _design.MissingCount(subsystem);
            if (missing > 0)
            {
                Button recommended = MakeButton("Fill required slots with recommended hardware", () =>
                {
                    _design.FillRecommended(subsystem);
                    RefreshDetail();
                    SetStatus("Recommended hardware added to " + subsystem.name + ". Try the other variants and sliders to see the differences.");
                }, 0);
                recommended.style.marginBottom = 12;
                _detail.Add(recommended);
            }

            foreach (BuilderSlot slot in subsystem.slots)
                _detail.Add(SlotCard(slot, subsystem));
        }

        private VisualElement SlotCard(BuilderSlot slot, BuilderSubsystem subsystem)
        {
            SlotStatus status = _design.StatusOf(slot);
            SlotRequirement requirement = SatelliteDesign.RequirementOf(slot);
            BuilderVariant selected = _design.GetSelectedVariant(slot.id);

            var card = new VisualElement();
            card.style.backgroundColor = CardBg;
            card.style.paddingLeft = card.style.paddingRight = 12;
            card.style.paddingTop = card.style.paddingBottom = 10;
            card.style.marginBottom = 10;
            card.style.borderLeftWidth = 4;
            card.style.borderLeftColor = StatusColor(status);
            Round(card, 8);

            VisualElement header = Row();
            header.style.alignItems = Align.Center;
            Label title = MakeLabel(slot.name, 15, true, TextColor);
            title.style.flexGrow = 1;
            title.style.flexShrink = 1;
            header.Add(title);
            header.Add(Badge(RequirementText(requirement), RequirementColor(requirement)));
            card.Add(header);

            card.Add(MakeLabel(StatusText(status), 11, true, StatusColor(status)));
            if (!string.IsNullOrEmpty(slot.why))
                card.Add(MakeLabel(slot.why, 12, false, Muted));
            if (!string.IsNullOrEmpty(slot.quantity))
                card.Add(MakeLabel("Typical quantity: " + slot.quantity, 11, false, Muted));

            if (requirement == SlotRequirement.Conditional && !string.IsNullOrEmpty(slot.conditionText))
            {
                bool active = status != SlotStatus.NotNeeded;
                card.Add(MakeLabel((active ? "Required because: " : "Becomes required if: ") + slot.conditionText, 11, true, active ? Amber : Grey));
            }

            if (status == SlotStatus.NotNeeded)
            {
                card.style.opacity = 0.6f;
                return card;
            }

            // Variant choices
            var choices = new VisualElement();
            choices.style.marginTop = 8;
            foreach (BuilderVariant variant in slot.variants)
            {
                BuilderVariant v = variant;
                bool isSelected = selected != null && selected.id == v.id;
                var button = new Button(() => OnVariantClicked(slot, subsystem, v));
                StyleButton(button, isSelected ? CardSelected : Accent);
                button.text = string.Empty;
                button.style.flexDirection = FlexDirection.Column;
                button.style.alignItems = Align.FlexStart;
                button.style.paddingTop = button.style.paddingBottom = 6;
                if (isSelected)
                {
                    button.style.borderLeftWidth = button.style.borderRightWidth = 1;
                    button.style.borderTopWidth = button.style.borderBottomWidth = 1;
                    button.style.borderLeftColor = button.style.borderRightColor = Green;
                    button.style.borderTopColor = button.style.borderBottomColor = Green;
                }
                button.Add(MakeLabel((isSelected ? "✓ " : string.Empty) + v.name, 13, true, TextColor));
                if (!string.IsNullOrEmpty(v.specs))
                    button.Add(MakeLabel(v.specs, 11, false, Muted));
                button.Add(MakeLabel(BudgetText(v), 11, false, Muted));
                choices.Add(button);
            }
            card.Add(choices);

            if (selected != null)
            {
                if (!string.IsNullOrEmpty(selected.description))
                {
                    Label desc = MakeLabel(selected.description, 12, false, TextColor);
                    desc.style.marginTop = 6;
                    card.Add(desc);
                }

                foreach (BuilderParameter parameter in selected.parameters)
                    card.Add(ParameterControl(slot, parameter));

                Button remove = MakeButton("Remove", () =>
                {
                    _design.SelectVariant(slot.id, null);
                    RefreshDetail();
                    SetStatus(slot.name + " removed.");
                }, 100);
                remove.style.alignSelf = Align.FlexEnd;
                remove.style.marginTop = 6;
                card.Add(remove);
            }

            return card;
        }

        private VisualElement ParameterControl(BuilderSlot slot, BuilderParameter parameter)
        {
            var box = new VisualElement();
            box.style.marginTop = 8;
            string label = parameter.label + (string.IsNullOrEmpty(parameter.unit) ? string.Empty : " (" + parameter.unit + ")");
            float current = _design.GetParameter(slot.id, parameter);

            if (parameter.wholeNumbers)
            {
                var slider = new SliderInt(label, Mathf.RoundToInt(parameter.min), Mathf.RoundToInt(parameter.max));
                slider.value = Mathf.RoundToInt(current);
                slider.showInputField = true;
                slider.style.color = TextColor;
                slider.RegisterValueChangedCallback(e => _design.SetParameter(slot.id, parameter, e.newValue));
                box.Add(slider);
            }
            else
            {
                var slider = new Slider(label, parameter.min, parameter.max);
                slider.value = current;
                slider.showInputField = true;
                slider.style.color = TextColor;
                slider.RegisterValueChangedCallback(e => _design.SetParameter(slot.id, parameter, e.newValue));
                box.Add(slider);
            }

            if (!string.IsNullOrEmpty(parameter.help))
                box.Add(MakeLabel(parameter.help, 11, false, Muted));
            return box;
        }

        private void OnVariantClicked(BuilderSlot slot, BuilderSubsystem subsystem, BuilderVariant variant)
        {
            BuilderVariant current = _design.GetSelectedVariant(slot.id);
            if (current != null && current.id == variant.id)
                return;

            _design.SelectVariant(slot.id, variant.id);
            RefreshDetail();

            int missing = _design.MissingCount(subsystem);
            SetStatus("Added " + variant.name + " to " + subsystem.name + ". " +
                      (missing > 0 ? missing + " required item" + (missing > 1 ? "s" : "") + " left in this subsystem."
                                   : subsystem.name + " is complete. Press Next to continue."));
        }

        // ================================================================== screen 3: review & launch

        private void ShowReview()
        {
            if (_design == null)
                return;

            _inReview = true;
            _detail.Clear();
            _previousButton.SetEnabled(true);
            _nextButton.text = "Back to editing";
            RefreshSubsystemList();

            _detail.Add(MakeLabel("REVIEW", 11, true, Muted));
            _detail.Add(MakeLabel("Review & launch", 22, true, TextColor));

            var nameField = new TextField("Design name");
            nameField.value = _design.DesignName;
            nameField.style.marginTop = 8;
            StyleTextField(nameField);
            nameField.textEdition.placeholder = "e.g. AzCube-1";
            nameField.RegisterValueChangedCallback(e => _design.DesignName = e.newValue);
            _detail.Add(nameField);
            _detail.Add(MakeLabel("This is your satellite's name in Mission Operations (OpenC3 target and interface use this name).", 11, false, Muted));

            int missing = _design.MissingCount();
            DesignTotals totals = _design.ComputeTotals();
            bool powerOk = totals.PowerMarginW >= 0f;

            Label completeness = MakeLabel(missing == 0 ? "✓ All required hardware is in place." : missing + " required item" + (missing > 1 ? "s are" : " is") + " still missing:",
                                           14, true, missing == 0 ? Green : Red);
            completeness.style.marginTop = 12;
            _detail.Add(completeness);

            if (missing > 0)
            {
                for (int i = 0; i < _design.Template.subsystems.Count; i++)
                {
                    int index = i;
                    BuilderSubsystem subsystem = _design.Template.subsystems[i];
                    foreach (BuilderSlot slot in subsystem.slots)
                    {
                        if (_design.StatusOf(slot) != SlotStatus.Missing)
                            continue;
                        Button jump = MakeButton(subsystem.name + ": " + slot.name, () => GoToSubsystem(index), 0);
                        jump.style.marginTop = 4;
                        _detail.Add(jump);
                    }
                }
            }

            Label budgetTitle = MakeLabel("Budgets", 15, true, TextColor);
            budgetTitle.style.marginTop = 16;
            _detail.Add(budgetTitle);
            _detail.Add(MakeLabel("Mass: " + FormatMass(totals.massKg) + " (" + totals.partCount + " parts)", 13, false, TextColor));
            _detail.Add(MakeLabel("Average power use: " + totals.powerW.ToString("0.0") + " W", 13, false, TextColor));
            _detail.Add(MakeLabel("Average solar generation: " + totals.powerGenW.ToString("0.0") + " W", 13, false, TextColor));
            _detail.Add(MakeLabel("Power margin: " + (totals.PowerMarginW >= 0 ? "+" : string.Empty) + totals.PowerMarginW.ToString("0.0") + " W",
                                  13, true, powerOk ? Green : Red));
            if (!powerOk)
                _detail.Add(MakeLabel("The satellite uses more power than its solar arrays produce. Choose bigger arrays or lower-power hardware.", 12, false, Red));
            _detail.Add(MakeLabel("Simplified teaching budgets: average values, before margins for eclipse, battery charging and ageing.", 11, false, Muted));

            bool canLaunch = missing == 0 && powerOk;
            _detail.Add(OrbitSection(canLaunch));
            if (canLaunch) LastDesignStore.Remember(_design.ToSave()); // Mission Operations Training and Orbit Simulation fly this one
            Button launch = MakeButton("Launch: watch the deployment", () =>
            {
                if (view != null) view.PlayLaunchSequence();
                _deployed = true;
                _deployButton.text = "Stow for launch";
                SetStatus("Separation! The satellite leaves the launcher stowed, then deploys its solar arrays, antennas and reflectors.");
            }, 0);
            launch.style.marginTop = 16;
            launch.SetEnabled(canLaunch);
            _detail.Add(launch);
            if (!canLaunch)
                _detail.Add(MakeLabel("Launch unlocks when every required item is added and the power margin is positive.", 11, false, Amber));

            Button fly = MakeButton("Fly it: Mission Operations", () =>
            {
                // Same path as the main menu's Mission Operations Training: this design, flown in OpenC3 COSMOS
                // (the start-up screen offers the Unity console when OpenC3 is not available).
                DesignSave save = _design.ToSave();
                LastDesignStore.Remember(save);
                Academy.Operations.OperationsSession.FlyDesign(save);
                Academy.OpsLink.OpsLinkFlow.Begin(_document.panelSettings);
            }, 0);
            fly.style.marginTop = 8;
            fly.SetEnabled(canLaunch);
            fly.tooltip = "Take this satellite into orbit and fly it from OpenC3 COSMOS: ground passes, power, attitude control, failures.";
            _detail.Add(fly);

            Button save = MakeButton("Save design", SaveDesign, 0);
            save.style.marginTop = 8;
            _detail.Add(save);
            if (!string.IsNullOrEmpty(_lastSavedPath))
                _detail.Add(MakeLabel("Saved: " + _lastSavedPath, 11, false, Muted));
        }

        /// <summary>
        /// Orbit choice: altitude and inclination inside the template's orbit class (GEO: inclination and slot longitude).
        /// Used by Mission Operations, OpenC3 and the Orbit Simulation.
        /// </summary>
        private VisualElement OrbitSection(bool canLaunch)
        {
            string cls = _design.Template.orbitClass;
            if (_design.Orbit == null) _design.Orbit = DesignOrbit.DefaultFor(cls);
            DesignOrbit orbit = _design.Orbit;
            bool geo = DesignOrbit.IsGeo(cls);

            var box = new VisualElement();
            box.style.marginTop = 16;
            box.Add(MakeLabel("Orbit (" + cls + ")", 15, true, TextColor));
            box.Add(MakeLabel(geo ? "Geostationary altitude is fixed at 35 786 km. Choose the orbital slot and a small inclination."
                                  : "Choose a circular orbit. The satellite type fixes the orbit class.", 11, false, Muted));

            Label info = MakeLabel(string.Empty, 12, false, TextColor);
            Label sso = MakeLabel(string.Empty, 11, false, Muted);

            System.Action changed = () =>
            {
                info.text = "Period " + orbit.PeriodMinutes.ToString("0.0") + " min  ·  speed " + orbit.SpeedKmS.ToString("0.00") + " km/s" +
                            (geo ? string.Empty : "  ·  " + (orbit.SemiMajorAxisKm).ToString("0") + " km from Earth's centre");
                float ssoInc = orbit.SunSynchronousInclinationDeg;
                sso.text = geo || float.IsNaN(ssoInc) ? string.Empty
                         : "Sun-synchronous at this altitude: " + ssoInc.ToString("0.0") + "°" +
                           (Mathf.Abs(ssoInc - orbit.inclinationDeg) < 0.15f ? "  ✓ (your orbit is Sun-synchronous)" : string.Empty);
                if (canLaunch) LastDesignStore.Remember(_design.ToSave());
            };

            if (!geo)
            {
                Vector2 r = DesignOrbit.AltitudeRange(cls);
                var alt = new SliderInt("Altitude (km)", Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y));
                alt.value = Mathf.RoundToInt(orbit.altitudeKm);
                alt.showInputField = true;
                alt.style.color = TextColor;
                alt.style.marginTop = 6;
                alt.RegisterValueChangedCallback(e => { orbit.altitudeKm = e.newValue; orbit.ClampTo(cls); changed(); });
                box.Add(alt);
            }

            Vector2 ir = DesignOrbit.InclinationRange(cls);
            var inc = new Slider("Inclination (°)", ir.x, ir.y);
            inc.value = orbit.inclinationDeg;
            inc.showInputField = true;
            inc.style.color = TextColor;
            inc.style.marginTop = 6;
            inc.RegisterValueChangedCallback(e => { orbit.inclinationDeg = Mathf.Round(e.newValue * 100f) / 100f; changed(); });
            box.Add(inc);

            if (geo)
            {
                var lon = new Slider("Slot longitude (° E)", -180f, 180f);
                lon.value = orbit.geoLongitudeDeg;
                lon.showInputField = true;
                lon.style.color = TextColor;
                lon.style.marginTop = 6;
                lon.RegisterValueChangedCallback(e => { orbit.geoLongitudeDeg = Mathf.Round(e.newValue * 10f) / 10f; changed(); });
                box.Add(lon);
            }
            else
            {
                Button ssoButton = MakeButton("Make it Sun-synchronous", () =>
                {
                    float i = orbit.SunSynchronousInclinationDeg;
                    if (float.IsNaN(i)) return;
                    inc.value = Mathf.Clamp(Mathf.Round(i * 10f) / 10f, ir.x, ir.y);
                }, 0);
                ssoButton.style.marginTop = 6;
                ssoButton.style.alignSelf = Align.FlexStart;
                box.Add(ssoButton);
            }

            Button reset = MakeButton("Default orbit", () => { _design.Orbit = DesignOrbit.DefaultFor(cls); ShowReview(); }, 0);
            reset.style.marginTop = 6;
            reset.style.alignSelf = Align.FlexStart;
            box.Add(reset);

            info.style.marginTop = 6;
            box.Add(info);
            box.Add(sso);
            changed();
            return box;
        }

        /// <summary>Dark input box with light text, matching the builder cards (the default runtime theme is white).</summary>
        private static void StyleTextField(TextField field)
        {
            field.labelElement.style.color = Muted;
            field.labelElement.style.unityFontStyleAndWeight = FontStyle.Bold;
            VisualElement input = field.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                input.style.backgroundColor = CardBg;
                input.style.color = TextColor;
                input.style.borderTopColor = input.style.borderBottomColor = input.style.borderLeftColor = input.style.borderRightColor = Grey;
                input.style.borderTopWidth = input.style.borderBottomWidth = input.style.borderLeftWidth = input.style.borderRightWidth = 1;
                input.style.borderTopLeftRadius = input.style.borderTopRightRadius = input.style.borderBottomLeftRadius = input.style.borderBottomRightRadius = 4;
                input.style.paddingLeft = 8;
                input.style.minHeight = 28;
                input.style.fontSize = 14;
            }
            VisualElement text = field.Q<TextElement>(className: "unity-text-element");
            if (text != null) text.style.color = TextColor;
            field.textSelection.cursorColor = Amber;
            field.textSelection.selectionColor = new Color(0.949f, 0.710f, 0.267f, 0.35f);
            field.RegisterCallback<FocusInEvent>(_ => { if (input != null) input.style.borderTopColor = input.style.borderBottomColor = input.style.borderLeftColor = input.style.borderRightColor = Amber; });
            field.RegisterCallback<FocusOutEvent>(_ => { if (input != null) input.style.borderTopColor = input.style.borderBottomColor = input.style.borderLeftColor = input.style.borderRightColor = Grey; });
        }

        private void SaveDesign()
        {
            if (_design == null)
                return;
            try
            {
                string folder = Path.Combine(Application.persistentDataPath, "SatelliteDesigns");
                Directory.CreateDirectory(folder);
                string safeName = string.IsNullOrEmpty(_design.DesignName) ? "design" : _design.DesignName;
                foreach (char c in Path.GetInvalidFileNameChars())
                    safeName = safeName.Replace(c, '_');
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string designPath = Path.Combine(folder, safeName + "_" + stamp + ".design.json");
                string assemblyPath = Path.Combine(folder, safeName + "_" + stamp + ".assembly.json");
                File.WriteAllText(designPath, JsonUtility.ToJson(_design.ToSave(), true));
                LastDesignStore.Remember(_design.ToSave());
                File.WriteAllText(assemblyPath, JsonUtility.ToJson(_design.ToAssembly(), true));
                _lastSavedPath = designPath;
                SetStatus("Design saved to " + folder);
                ShowReview();
            }
            catch (Exception e)
            {
                SetStatus("Could not save the design: " + e.Message);
            }
        }

        // ================================================================== bottom bar

        private void RefreshTotals()
        {
            if (_design == null)
                return;
            DesignTotals t = _design.ComputeTotals();
            _massLabel.text = "Mass  " + FormatMass(t.massKg);
            _powerLabel.text = "Power use  " + t.powerW.ToString("0.0") + " W";
            _generationLabel.text = "Solar  " + t.powerGenW.ToString("0.0") + " W";
            bool ok = t.PowerMarginW >= 0f;
            _marginLabel.text = "Margin  " + (ok ? "+" : string.Empty) + t.PowerMarginW.ToString("0.0") + " W";
            _marginLabel.style.color = t.powerGenW <= 0f ? Muted : (ok ? Green : Red);
        }

        private void ToggleDeploy()
        {
            if (view == null)
                return;
            _deployed = !_deployed;
            view.AnimateDeploy(_deployed);
            _deployButton.text = _deployed ? "Stow for launch" : "Deploy";
            SetStatus(_deployed ? "Deploying: wings unfold, antennas extend, reflectors swing out."
                                : "Stowing: everything folds into its launch position, the way it rides inside the rocket.");
        }

        private void GoToMainMenu()
        {
            if (!SatelliteBuilderSceneFactory.OpenMainMenu(mainMenuScene))
                SetStatus("Scene '" + mainMenuScene + "' is not in Build Settings.");
        }

        private void SetStatus(string message)
        {
            if (_statusLine != null)
                _statusLine.text = message;
        }

        // ================================================================== input

        private bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (_root == null || _root.panel == null)
                return false;
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            return _root.panel.Pick(panelPosition) != null;
        }

        // ================================================================== UI helpers

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            return row;
        }

        private static void Stretch(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = element.style.right = element.style.top = element.style.bottom = 0;
        }

        private static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
        }

        private static Label MakeLabel(string text, int size, bool bold, Color color)
        {
            var label = new Label(text ?? string.Empty);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            if (bold)
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private static Label Badge(string text, Color color)
        {
            Label badge = MakeLabel(text, 10, true, new Color(0.03f, 0.05f, 0.09f));
            badge.style.backgroundColor = color;
            badge.style.paddingLeft = badge.style.paddingRight = 7;
            badge.style.paddingTop = badge.style.paddingBottom = 2;
            Round(badge, 4);
            return badge;
        }

        private static Button MakeButton(string text, Action action, float width)
        {
            var button = new Button(action) { text = text };
            StyleButton(button, Accent);
            if (width > 0f)
                button.style.width = width;
            return button;
        }

        private static void StyleButton(Button button, Color background)
        {
            button.style.backgroundColor = background;
            button.style.color = TextColor;
            button.style.minHeight = 34;
            button.style.marginTop = button.style.marginBottom = 3;
            button.style.marginLeft = button.style.marginRight = 3;
            button.style.borderLeftWidth = button.style.borderRightWidth = 0;
            button.style.borderTopWidth = button.style.borderBottomWidth = 0;
            Round(button, 6);
            button.RegisterCallback<MouseEnterEvent>(_ => button.style.backgroundColor = Color.Lerp(background, AccentHover, 0.6f));
            button.RegisterCallback<MouseLeaveEvent>(_ => button.style.backgroundColor = background);
        }

        private static Label StatLabel(VisualElement parent)
        {
            Label label = MakeLabel(string.Empty, 14, true, TextColor);
            label.style.marginRight = 26;
            parent.Add(label);
            return label;
        }

        private static string FormatMass(float kg)
        {
            return kg < 10f ? kg.ToString("0.00") + " kg" : kg.ToString("0") + " kg";
        }

        private static string BudgetText(BuilderVariant v)
        {
            string text = FormatMass(v.massKg) + "  ·  " + v.powerW.ToString("0.#") + " W use";
            if (v.powerGenW > 0f)
                text += "  ·  " + v.powerGenW.ToString("0.#") + " W solar";
            return text;
        }

        private static string RequirementText(SlotRequirement r)
        {
            return r == SlotRequirement.Mandatory ? "MANDATORY" : r == SlotRequirement.Conditional ? "CONDITIONAL" : "OPTIONAL";
        }

        private static Color RequirementColor(SlotRequirement r)
        {
            return r == SlotRequirement.Mandatory ? Red : r == SlotRequirement.Conditional ? Amber : Grey;
        }

        private static string StatusText(SlotStatus s)
        {
            switch (s)
            {
                case SlotStatus.Added: return "✓ Added";
                case SlotStatus.Missing: return "Missing: required";
                case SlotStatus.Empty: return "Not added (optional)";
                default: return "Not needed for this design";
            }
        }

        private static Color StatusColor(SlotStatus s)
        {
            switch (s)
            {
                case SlotStatus.Added: return Green;
                case SlotStatus.Missing: return Red;
                default: return Grey;
            }
        }

        private static Color OrbitColor(string orbit)
        {
            switch ((orbit ?? string.Empty).ToUpperInvariant())
            {
                case "LEO": return new Color(0.45f, 0.85f, 0.95f);
                case "MEO": return new Color(0.75f, 0.65f, 1f);
                case "GEO": return Amber;
                default: return Grey;
            }
        }
    }
}
