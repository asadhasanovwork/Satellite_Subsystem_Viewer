using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using VoltstroStudios.UnityWebBrowser;
using VoltstroStudios.UnityWebBrowser.Communication;
using VoltstroStudios.UnityWebBrowser.Core;
using VoltstroStudios.UnityWebBrowser.Core.Engines;
using VoltstroStudios.UnityWebBrowser.Input;

namespace Academy.OpsLink
{
    /// <summary>
    /// Full-screen embedded Chromium (UnityWebBrowser / CEF) that shows the OpenC3 COSMOS
    /// web UI exactly as it is, with a small "← Academy" button in the corner to leave.
    /// Built entirely in code so it needs no scene or prefab.
    /// </summary>
    public sealed class OpsBrowserScreen : MonoBehaviour
    {
        public const string DefaultUrl = "http://localhost:2900";

        static OpsBrowserScreen _current;
        Action _onExit;
        WebBrowserUIFull _browser;

        public static bool IsOpen => _current != null;
        public WebBrowserUIFull Browser => _browser;

        /// <summary>Opens (or re-focuses) the full-screen ops browser.</summary>
        public static OpsBrowserScreen Open(string url = DefaultUrl, Action onExit = null)
        {
            if (_current != null)
            {
                _current._onExit = onExit;
                _current._browser.NavigateUrl(url);
                return _current;
            }

            var root = new GameObject("OpsBrowserScreen");
            DontDestroyOnLoad(root);
            var screen = root.AddComponent<OpsBrowserScreen>();
            screen._onExit = onExit;
            screen.Build(url);
            _current = screen;
            return screen;
        }

        public static void Close()
        {
            if (_current == null) return;
            var exit = _current._onExit;
            Destroy(_current.gameObject);
            _current = null;
            exit?.Invoke();
        }

        void Build(string url)
        {
            EnsureEventSystem();

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000; // above every other UI, including UI Toolkit panels
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            gameObject.AddComponent<GraphicRaycaster>();

            // Browser surface. Created inactive so the client is configured before Start().
            var surface = new GameObject("Browser", typeof(RectTransform));
            surface.SetActive(false);
            surface.transform.SetParent(transform, false);
            Stretch(surface.GetComponent<RectTransform>());
            var img = surface.AddComponent<RawImage>();
            img.color = Color.white;

            _browser = surface.AddComponent<WebBrowserUIFull>();
            var client = _browser.browserClient;
            client.initialUrl = url;
            client.engine = Resources.Load<Engine>("Cef Engine Configuration");
            client.communicationLayer = Resources.Load<CommunicationLayer>("TCP Communication Layer");
            // The public Resolution setter resizes a running engine, so before Start() the
            // initial size must go straight into the serialized field.
            SetInitialResolution(client, (uint)Mathf.Max(640, Screen.width), (uint)Mathf.Max(480, Screen.height));
            client.backgroundColor = new Color32(18, 24, 38, 255);
            client.javascript = true;
            client.localStorage = true;   // OpenC3 keeps its login token in localStorage
            client.cache = true;
            client.windowlessFrameRate = 60;
            _browser.inputHandler = Resources.Load<WebBrowserInputHandler>("Input System Handler");

            if (client.engine == null || client.communicationLayer == null || _browser.inputHandler == null)
                Debug.LogError("[OpsBrowser] UnityWebBrowser resources missing (engine/communication/input).");

            surface.SetActive(true);

            BuildBackButton();
        }

        void BuildBackButton()
        {
            var go = new GameObject("BackToAcademy", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            // Bottom-left, above OpenC3's footer and below the nav drawer's last entry:
            // the only spot that stays empty on every OpenC3 tool page (tool titles and
            // menus live in the top bar).
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(12f, 44f);
            rt.sizeDelta = new Vector2(132f, 30f);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.10f, 0.16f, 0.72f);
            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1.25f);
            btn.colors = colors;
            btn.onClick.AddListener(Close);

            var label = new GameObject("Label", typeof(RectTransform));
            label.transform.SetParent(go.transform, false);
            Stretch(label.GetComponent<RectTransform>());
            var text = label.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = "← Academy";
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.95f, 0.78f, 0.36f); // sun-amber
        }

        static void SetInitialResolution(WebBrowserClient client, uint w, uint h)
        {
            var f = typeof(WebBrowserClient).GetField("resolution",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null) f.SetValue(client, new VoltstroStudios.UnityWebBrowser.Shared.Resolution(w, h));
            else Debug.LogWarning("[OpsBrowser] Could not set initial resolution; using package default.");
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var es = new GameObject("EventSystem (OpsBrowser)", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(es);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        void OnDestroy()
        {
            if (_current == this) _current = null;
        }
    }
}
