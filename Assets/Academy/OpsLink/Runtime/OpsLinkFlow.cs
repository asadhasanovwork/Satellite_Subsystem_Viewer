using System;
using System.Threading;
using System.Threading.Tasks;
using Academy.Builder;
using Academy.Operations;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UI.Button;
using Image = UnityEngine.UI.Image;

namespace Academy.OpsLink
{
    /// <summary>
    /// "Mission Operations Training": flies the satellite built in the Builder inside OpenC3 COSMOS.
    /// Shows a start-up screen while Docker/OpenC3 come up, opens the simulator in the background,
    /// installs the generated plugin and then shows OpenC3 full screen. "← Academy" returns to the menu.
    /// </summary>
    public sealed class OpsLinkFlow : MonoBehaviour
    {
        static OpsLinkFlow _current;

        PanelSettings _panel;
        OpsLinkSettings _settings;
        CancellationTokenSource _cts;
        DesignSave _design;

        // UI
        Text _title, _steps, _detail;
        GameObject _buttons;
        Button _retry, _offline, _builder, _back;
        int _step;
        static readonly string[] StepNames =
        {
            "Satellite from the Builder",
            "Docker engine",
            "OpenC3 COSMOS",
            "Satellite simulator",
            "Satellite installed in OpenC3",
        };

        /// <summary>Entry point for the main menu button.</summary>
        public static void Begin(PanelSettings panelSettings)
        {
            if (_current != null) return;
            var go = new GameObject("OpsLinkFlow");
            DontDestroyOnLoad(go);
            _current = go.AddComponent<OpsLinkFlow>();
            _current._panel = panelSettings;
            _current.BuildUi();
            _current.Run();
        }

        // ------------------------------------------------------------------ flow

        async void Run()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;
            ShowButtons(false, false, false);
            _settings = OpsLinkSettings.Load();

            // 1. design
            SetStep(0, "Looking for the satellite you built");
            _design = OperationsSession.PendingDesign ?? LastDesignStore.Load();
            if (_design == null)
            {
                Error("No satellite yet. Build one in the Satellite Builder: when every required item is added and the power margin is positive, it is ready to fly here.",
                      retry: false, offline: false, builder: true);
                return;
            }

            // 2-3. docker + openc3
            SetStep(1, "Checking Docker");
            var launcher = new OpenC3Launcher(_settings, s => _statusFromThread = s);
            bool ok = await launcher.EnsureReadyAsync(ct);
            if (this == null || ct.IsCancellationRequested) return;
            if (!ok)
            {
                if (launcher.Error == LaunchError.Cancelled) return;
                SetStep(launcher.Error == LaunchError.DockerMissing || launcher.Error == LaunchError.DockerNotStarting ? 1 : 2, null);
                Error(launcher.ErrorText, retry: true, offline: true, builder: false);
                return;
            }
            SetStep(3, "Starting the satellite simulator");

            // 4. simulator (the Operations scene, generated in code, runs behind OpenC3)
            OperationsSession.FlyDesign(_design);
            OperationsSceneFactory.Open(_panel);
            OperationsSimulation ops = null;
            for (int i = 0; i < 300 && (ops == null || ops.Sim == null); i++)
            {
                await Task.Yield();
                await Task.Delay(50);
                if (this == null) return;
                ops = FindFirstObjectByType<OperationsSimulation>();
            }
            if (ops == null || ops.Sim == null)
            {
                Error("The satellite simulator did not start. Check the design in the Builder.", retry: true, offline: false, builder: true);
                return;
            }
            HideOperationsConsole();

            // 5. plugin + link
            SetStep(4, "Preparing the satellite for OpenC3");
            OpsLinkSession.StatusChanged += OnSessionStatus;
            bool linked = await OpsLinkSession.StartAsync(ops, _settings);
            OpsLinkSession.StatusChanged -= OnSessionStatus;
            if (this == null) return;
            if (!linked)
            {
                Error(OpsLinkSession.LastError, retry: true, offline: true, builder: false);
                return;
            }

            // Done: OpenC3 full screen, logged in
            OpsBrowserScreen.AutoLoginPassword = _settings.password;
            OpsBrowserScreen.Open(OpsLinkSession.CommandSenderUrl(_settings), ReturnToMenu);
            SetVisible(false);
        }

        void OnSessionStatus(string s) => _statusFromThread = s;

        void HideOperationsConsole()
        {
            // The Unity console stays out of the way (its hotkeys would also react to typing in OpenC3).
            var ui = GameObject.Find("Operations UI");
            if (ui != null) ui.SetActive(false);
        }

        void ReturnToMenu()
        {
            _cts?.Cancel();
            var ops = FindFirstObjectByType<OperationsSimulation>();
            if (ops != null)
            {
                var link = ops.GetComponent<SpacecraftLink>();
                if (link != null) Destroy(link);
            }
            OperationsSession.Clear();
            SatelliteBuilderSceneFactory.OpenMainMenu(null);
            Destroy(gameObject);
        }

        void OpenOfflineConsole()
        {
            // Fallback without OpenC3: the Unity ground-station console flies the same design.
            _cts?.Cancel();
            OperationsSession.FlyDesign(_design);
            OperationsSceneFactory.Open(_panel);
            Destroy(gameObject);
        }

        void OpenBuilder()
        {
            _cts?.Cancel();
            OperationsSession.Clear();
            SatelliteBuilderSceneFactory.Open(_panel);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_current == this) _current = null;
            OpsLinkSession.StatusChanged -= OnSessionStatus;
            _cts?.Cancel();
        }

        // ------------------------------------------------------------------ status

        volatile string _statusFromThread;

        void Update()
        {
            string s = _statusFromThread;
            if (s != null) { _statusFromThread = null; _detail.text = s; }
        }

        void SetStep(int step, string detail)
        {
            _step = step;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < StepNames.Length; i++)
            {
                string mark = i < step ? "<color=#3FC1A5>✓</color>" : i == step ? "<color=#F2C75C>●</color>" : "<color=#5A6478>○</color>";
                string name = i == step ? "<b>" + StepNames[i] + "</b>" : StepNames[i];
                sb.Append(mark).Append("   ").Append(name).Append('\n');
            }
            _steps.text = sb.ToString();
            if (detail != null) _detail.text = detail;
            _detail.color = new Color(0.75f, 0.8f, 0.88f);
        }

        void Error(string message, bool retry, bool offline, bool builder)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < StepNames.Length; i++)
            {
                string mark = i < _step ? "<color=#3FC1A5>✓</color>" : i == _step ? "<color=#FF6B5E>✕</color>" : "<color=#5A6478>○</color>";
                sb.Append(mark).Append("   ").Append(StepNames[i]).Append('\n');
            }
            _steps.text = sb.ToString();
            _detail.text = message;
            _detail.color = new Color(1f, 0.55f, 0.5f);
            ShowButtons(retry, offline, builder);
        }

        // ------------------------------------------------------------------ UI (uGUI, built in code)

        GameObject _canvasGo;

        void SetVisible(bool v) { if (_canvasGo != null) _canvasGo.SetActive(v); }

        void ShowButtons(bool retry, bool offline, bool builder)
        {
            _retry.gameObject.SetActive(retry);
            _offline.gameObject.SetActive(offline);
            _builder.gameObject.SetActive(builder);
            _buttons.SetActive(true);
        }

        void BuildUi()
        {
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem (OpsLink)", typeof(EventSystem), typeof(InputSystemUIInputModule));
                DontDestroyOnLoad(es);
            }
            _canvasGo = new GameObject("OpsLinkFlowCanvas");
            _canvasGo.transform.SetParent(transform, false);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            var bg = Panel(_canvasGo.transform, "Background", new Color(0.035f, 0.05f, 0.09f, 1f));
            Stretch(bg.rectTransform);

            var card = Panel(bg.transform, "Card", new Color(0.07f, 0.10f, 0.16f, 1f));
            var crt = card.rectTransform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(760, 520);

            _title = Label(card.transform, "Mission Operations Training", 34, FontStyle.Bold, new Color(0.95f, 0.78f, 0.36f), TextAnchor.UpperLeft);
            Place(_title.rectTransform, 40, 36, 680, 50);
            var sub = Label(card.transform, "Your satellite, flown from a real ground-control system (OpenC3 COSMOS).", 18, FontStyle.Normal, new Color(0.7f, 0.76f, 0.86f), TextAnchor.UpperLeft);
            Place(sub.rectTransform, 40, 88, 680, 30);

            _steps = Label(card.transform, "", 22, FontStyle.Normal, new Color(0.9f, 0.93f, 0.97f), TextAnchor.UpperLeft);
            _steps.supportRichText = true;
            _steps.lineSpacing = 1.35f;
            Place(_steps.rectTransform, 40, 140, 680, 210);

            _detail = Label(card.transform, "", 17, FontStyle.Normal, new Color(0.75f, 0.8f, 0.88f), TextAnchor.UpperLeft);
            Place(_detail.rectTransform, 40, 350, 680, 90);

            _buttons = new GameObject("Buttons", typeof(RectTransform));
            _buttons.transform.SetParent(card.transform, false);
            Place(_buttons.GetComponent<RectTransform>(), 40, 452, 680, 44);
            var h = _buttons.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12; h.childForceExpandWidth = false; h.childControlWidth = false; h.childControlHeight = true;

            _retry = MakeButton(_buttons.transform, "Try again", () => Run());
            _offline = MakeButton(_buttons.transform, "Use the Unity console", OpenOfflineConsole);
            _builder = MakeButton(_buttons.transform, "Open the Builder", OpenBuilder);
            _back = MakeButton(_buttons.transform, "← Academy", ReturnToMenu);
            _buttons.SetActive(true);
            ShowButtons(false, false, false);
        }

        static Font UiFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static Image Panel(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            return img;
        }

        static Text Label(Transform parent, string text, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = UiFont; t.text = text; t.fontSize = size; t.fontStyle = style; t.color = color; t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static Button MakeButton(Transform parent, string text, Action onClick)
        {
            var img = Panel(parent, "Button " + text, new Color(0.13f, 0.19f, 0.29f, 1f));
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = Mathf.Max(170, text.Length * 12 + 48);
            var b = img.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => onClick());
            var t = Label(img.transform, text, 17, FontStyle.Bold, new Color(0.95f, 0.78f, 0.36f), TextAnchor.MiddleCenter);
            Stretch(t.rectTransform);
            return b;
        }

        static void Place(RectTransform rt, float x, float y, float w, float hgt)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, hgt);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
