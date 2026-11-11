using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Academy.Builder;
using Academy.Operations;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UI.Button;
using Image = UnityEngine.UI.Image;

namespace Academy.OpsLink
{
    /// <summary>
    /// "Mission Operations Training": start-up screen (Docker, OpenC3), "My satellites" list with Fly / Delete,
    /// then the chosen satellite flown in OpenC3 COSMOS full screen. "← Academy" returns to the menu.
    /// From the Builder's "Fly it" the list is skipped and the current design flies straight away.
    /// </summary>
    public sealed class OpsLinkFlow : MonoBehaviour
    {
        static OpsLinkFlow _current;

        PanelSettings _panel;
        OpsLinkSettings _settings;
        CancellationTokenSource _cts;
        DesignSave _direct;      // from the Builder's "Fly it": skip the list
        DesignSave _flying;

        static readonly string[] StepNames =
        {
            "Docker engine",
            "OpenC3 COSMOS",
            "Choose your satellite",
            "Satellite simulator",
            "Satellite in OpenC3",
        };

        public static void Begin(PanelSettings panelSettings)
        {
            if (_current != null) return;
            var go = new GameObject("OpsLinkFlow");
            DontDestroyOnLoad(go);
            _current = go.AddComponent<OpsLinkFlow>();
            _current._panel = panelSettings;
            _current._direct = OperationsSession.PendingDesign;
            _current.BuildUi();
            _current.Run();
        }

        // ------------------------------------------------------------------ flow

        async void Run()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;
            ShowList(false);
            ShowButtons(false, false, false);
            _settings = OpsLinkSettings.Load();

            SetStep(0, "Checking Docker");
            var launcher = new OpenC3Launcher(_settings, s => _statusFromThread = s);
            bool ok = await launcher.EnsureReadyAsync(ct);
            if (this == null || ct.IsCancellationRequested) return;
            if (!ok)
            {
                if (launcher.Error == LaunchError.Cancelled) return;
                SetStep(launcher.Error == LaunchError.DockerMissing || launcher.Error == LaunchError.DockerNotStarting ? 0 : 1, null);
                Error(launcher.ErrorText, retry: true, offline: true, builder: false);
                return;
            }

            if (_direct != null) { Fly(_direct); return; }
            await ShowSatellites();
        }

        async Task ShowSatellites()
        {
            SetStep(2, "Pick the satellite to fly. Delete removes it from OpenC3.");
            List<SatelliteInfo> sats;
            try { sats = await OpsLinkSession.ListAsync(_settings); }
            catch (Exception e) { Error("Could not read the satellites from OpenC3: " + e.Message, true, true, false); return; }
            if (this == null) return;

            // The Builder's latest design goes first (merged with its installed copy, if any)
            DesignSave latest = LastDesignStore.Load();
            if (latest != null)
            {
                string target = OpsLinkSession.TargetNameFor(latest.designName, sats.Select(x => x.Target), _settings);
                var match = sats.FirstOrDefault(x => x.Target == target && !x.Legacy);
                if (match == null)
                {
                    match = new SatelliteInfo { Target = target, DisplayName = latest.designName, TemplateId = latest.templateId };
                    sats.Insert(0, match);
                }
                else { sats.Remove(match); sats.Insert(0, match); }
                match.IsLatest = true;
                match.HasDesign = true;
                match.TemplateId = latest.templateId;
            }
            FillList(sats, latest);
            ShowList(true);
            ShowButtons(false, false, sats.Count == 0);
            if (sats.Count == 0)
                _detail.text = "No satellite yet. Build one in the Satellite Builder: when every required item is added and the power margin is positive, it is ready to fly here.";
        }

        async void Fly(DesignSave design)
        {
            _flying = design;
            ShowList(false);
            ShowButtons(false, false, false);
            SetStep(3, "Starting the satellite simulator");

            OperationsSession.FlyDesign(design);
            OperationsSceneFactory.Open(_panel);
            OperationsSimulation ops = null;
            for (int i = 0; i < 300 && (ops == null || ops.Sim == null); i++)
            {
                await Task.Delay(50);
                if (this == null) return;
                ops = FindFirstObjectByType<OperationsSimulation>();
            }
            if (ops == null || ops.Sim == null)
            {
                Error("The satellite simulator did not start. Check the design in the Builder.", retry: true, offline: false, builder: true);
                return;
            }
            var ui = GameObject.Find("Operations UI");   // its hotkeys would react to typing in OpenC3
            if (ui != null) ui.SetActive(false);

            SetStep(4, "Preparing the satellite for OpenC3");
            OpsLinkSession.StatusChanged += OnSessionStatus;
            bool linked = await OpsLinkSession.StartAsync(ops, _settings, design);
            OpsLinkSession.StatusChanged -= OnSessionStatus;
            if (this == null) return;
            if (!linked) { Error(OpsLinkSession.LastError, retry: true, offline: true, builder: false); return; }

            OpsBrowserScreen.AutoLoginPassword = _settings.password;
            OpsBrowserScreen.AutoTarget = OpsLinkSession.CurrentTarget;
            OpsBrowserScreen.Open(OpsLinkSession.CommandSenderUrl(_settings), ReturnToMenu);
            SetVisible(false);
        }

        async void Delete(SatelliteInfo sat)
        {
            _detail.text = "Deleting " + sat.DisplayName + " from OpenC3…";
            bool ok = await OpsLinkSession.DeleteAsync(_settings, sat);
            if (this == null) return;
            await ShowSatellites();
            _detail.text = ok ? sat.DisplayName + " was deleted." : "Could not delete " + sat.DisplayName + " (see console).";
        }

        void OnSessionStatus(string s) => _statusFromThread = s;

        void Retry()
        {
            if (_flying != null && _step >= 3) { var d = _flying; _flying = null; _direct = d; }
            Run();
        }

        void ReturnToMenu()
        {
            _cts?.Cancel();
            OpsLinkSession.Park(_settings);
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
            _cts?.Cancel();
            OperationsSession.FlyDesign(_flying ?? _direct ?? LastDesignStore.Load());
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
        int _step;

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
                sb.Append(mark).Append("   ").Append(i == step ? "<b>" + StepNames[i] + "</b>" : StepNames[i]).Append('\n');
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
            ShowList(false);
            _detail.text = message;
            _detail.color = new Color(1f, 0.55f, 0.5f);
            ShowButtons(retry, offline, builder);
        }

        // ------------------------------------------------------------------ UI (uGUI, built in code)

        static readonly Color Ink = new Color(0.035f, 0.05f, 0.09f, 1f);
        static readonly Color CardC = new Color(0.07f, 0.10f, 0.16f, 1f);
        static readonly Color RowC = new Color(0.10f, 0.15f, 0.23f, 1f);
        static readonly Color RowLatest = new Color(0.13f, 0.20f, 0.29f, 1f);
        static readonly Color BtnC = new Color(0.263f, 0.627f, 0.278f, 1f);      // light green #43A047
        static readonly Color BtnHover = new Color(0.400f, 0.733f, 0.416f, 1f);  // #66BB6A
        static readonly Color DangerC = new Color(0.827f, 0.184f, 0.184f, 1f);   // confirm-delete only
        static readonly Color BtnText = Color.white;
        static readonly Color AmberC = new Color(0.95f, 0.78f, 0.36f);
        static readonly Color TealC = new Color(0.25f, 0.76f, 0.65f);
        static readonly Color TextC = new Color(0.9f, 0.93f, 0.97f);
        static readonly Color MutedC = new Color(0.6f, 0.66f, 0.76f);

        GameObject _canvasGo, _buttons, _listRoot;
        RectTransform _listContent;
        Text _steps, _detail;
        Button _retry, _offline, _builder;

        void SetVisible(bool v) { if (_canvasGo != null) _canvasGo.SetActive(v); }
        void ShowList(bool v) { if (_listRoot != null) _listRoot.SetActive(v); _steps.gameObject.SetActive(!v); }

        void ShowButtons(bool retry, bool offline, bool builder)
        {
            _retry.gameObject.SetActive(retry);
            _offline.gameObject.SetActive(offline);
            _builder.gameObject.SetActive(builder);
        }

        void BuildUi()
        {
            if (EventSystem.current == null)
                DontDestroyOnLoad(new GameObject("EventSystem (OpsLink)", typeof(EventSystem), typeof(InputSystemUIInputModule)));

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

            var bg = Panel(_canvasGo.transform, "Background", Ink);
            Stretch(bg.rectTransform);
            var card = Panel(bg.transform, "Card", CardC);
            card.rectTransform.anchorMin = card.rectTransform.anchorMax = card.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            card.rectTransform.sizeDelta = new Vector2(920, 660);

            var title = Label(card.transform, "Mission Operations Training", 34, FontStyle.Bold, AmberC, TextAnchor.UpperLeft);
            Place(title.rectTransform, 40, 32, 840, 50);
            var sub = Label(card.transform, "Your satellite, flown from a real ground-control system (OpenC3 COSMOS).", 18, FontStyle.Normal, MutedC, TextAnchor.UpperLeft);
            Place(sub.rectTransform, 40, 84, 840, 30);

            _steps = Label(card.transform, "", 22, FontStyle.Normal, TextC, TextAnchor.UpperLeft);
            _steps.supportRichText = true;
            _steps.lineSpacing = 1.35f;
            Place(_steps.rectTransform, 40, 136, 840, 230);

            BuildList(card.transform);

            _detail = Label(card.transform, "", 17, FontStyle.Normal, MutedC, TextAnchor.UpperLeft);
            Place(_detail.rectTransform, 40, 520, 840, 60);

            _buttons = new GameObject("Buttons", typeof(RectTransform));
            _buttons.transform.SetParent(card.transform, false);
            Place(_buttons.GetComponent<RectTransform>(), 40, 592, 840, 44);
            var h = _buttons.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12; h.childForceExpandWidth = false; h.childControlWidth = true; h.childControlHeight = true;
            _retry = MakeButton(_buttons.transform, "Try again", Retry, BtnC);
            _offline = MakeButton(_buttons.transform, "Use the Unity console", OpenOfflineConsole, BtnC);
            _builder = MakeButton(_buttons.transform, "Open the Builder", OpenBuilder, BtnC);
            MakeButton(_buttons.transform, "← Academy", ReturnToMenu, BtnC);
            ShowButtons(false, false, false);
            ShowList(false);
        }

        void BuildList(Transform card)
        {
            _listRoot = new GameObject("Satellites", typeof(RectTransform));
            _listRoot.transform.SetParent(card, false);
            var rootRt = _listRoot.GetComponent<RectTransform>();
            Place(rootRt, 40, 130, 840, 380);

            var header = Label(_listRoot.transform, "My satellites", 22, FontStyle.Bold, TextC, TextAnchor.UpperLeft);
            Place(header.rectTransform, 0, 0, 840, 34);

            var viewport = Panel(_listRoot.transform, "Viewport", new Color(0, 0, 0, 0.18f));
            Place(viewport.rectTransform, 0, 40, 840, 340);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _listContent = content.GetComponent<RectTransform>();
            _listContent.anchorMin = new Vector2(0, 1); _listContent.anchorMax = new Vector2(1, 1); _listContent.pivot = new Vector2(0.5f, 1);
            _listContent.offsetMin = _listContent.offsetMax = Vector2.zero;
            var v = content.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6; v.padding = new RectOffset(8, 8, 8, 8);
            v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _listContent; scroll.viewport = viewport.rectTransform;
            scroll.horizontal = false; scroll.scrollSensitivity = 30; scroll.movementType = ScrollRect.MovementType.Clamped;
        }

        void FillList(List<SatelliteInfo> sats, DesignSave latest)
        {
            foreach (Transform c in _listContent) Destroy(c.gameObject);
            foreach (SatelliteInfo sat in sats) AddRow(sat, latest);
        }

        void AddRow(SatelliteInfo sat, DesignSave latest)
        {
            var row = Panel(_listContent, "Row " + sat.Target, sat.IsLatest ? RowLatest : RowC);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(16, 12, 10, 10); h.spacing = 10;
            h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = true;

            string tag = sat.IsLatest ? "  <color=#F2C75C>★ latest from the Builder</color>" : "";
            string status = sat.Legacy ? "<color=#FF9A6B>old format – please delete</color>"
                          : sat.PluginName != null ? "<color=#3FC1A5>in OpenC3</color> · target " + sat.Target
                          : "new · will be installed as " + sat.Target;
            var text = Label(row.transform, "<b>" + sat.DisplayName + "</b>" + tag + "\n<size=14>" + Orbit(sat.TemplateId) + status + "</size>",
                             19, FontStyle.Normal, TextC, TextAnchor.MiddleLeft);
            text.supportRichText = true;
            var le = text.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1; le.minWidth = 300;

            bool canFly = !sat.Legacy && (sat.IsLatest || sat.HasDesign);
            if (canFly)
            {
                DesignSave design = sat.IsLatest ? latest : OpsLinkSession.LoadDesign(sat.Target);
                MakeButton(row.transform, "Fly", () => { if (design != null) Fly(design); }, BtnC, 110);
            }
            if (sat.PluginName != null || (!sat.IsLatest && !sat.Legacy))
            {
                Button del = null;
                bool armed = false;
                del = MakeButton(row.transform, "Delete", () =>
                {
                    if (!armed)
                    {
                        armed = true;
                        del.GetComponentInChildren<Text>().text = "Confirm";
                        del.GetComponent<Image>().color = DangerC;   // red only while asking to confirm
                        var cb = del.colors; cb.highlightedColor = cb.normalColor; del.colors = cb;
                        return;
                    }
                    Delete(sat);
                }, BtnC, 120);
            }
        }

        static string Orbit(string templateId)
        {
            switch (templateId)
            {
                case "leo_cubesat": return "LEO CubeSat · ";
                case "meo_navsat": return "MEO navigation · ";
                case "geo_comsat": return "GEO communications · ";
                default: return "";
            }
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
            t.supportRichText = true;
            return t;
        }

        static Button MakeButton(Transform parent, string text, Action onClick, Color color, float width = 0)
        {
            var img = Panel(parent, "Button " + text, color);
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width > 0 ? width : Mathf.Max(170, text.Length * 12 + 48);
            le.minWidth = le.preferredWidth;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(BtnHover.r / Mathf.Max(0.01f, color.r), BtnHover.g / Mathf.Max(0.01f, color.g), BtnHover.b / Mathf.Max(0.01f, color.b), 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            var t = Label(img.transform, text, 17, FontStyle.Bold, BtnText, TextAnchor.MiddleCenter);
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
