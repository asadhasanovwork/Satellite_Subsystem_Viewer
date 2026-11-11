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
        volatile bool _loginPending;

        /// <summary>When set, the OpenC3 login form is filled in automatically after every page load.</summary>
        public static string AutoLoginPassword;

        public static bool IsOpen => _current != null;

        /// <summary>Last page titles (diagnostics: pages can report through document.title).</summary>
        public static readonly System.Collections.Concurrent.ConcurrentQueue<string> Titles = new System.Collections.Concurrent.ConcurrentQueue<string>();

        /// <summary>Runs JavaScript in the open OpenC3 page (diagnostics and automation).</summary>
        public static bool RunJs(string js)
        {
            if (_current == null || _current._browser == null) return false;
            try { _current._browser.browserClient.ExecuteJs(js); return true; }
            catch (Exception e) { Debug.LogWarning("[OpsBrowser] JS failed: " + e.Message); return false; }
        }
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
            client.OnLoadFinish += _ => _loginPending = true;   // raised off the main thread
            client.OnTitleChange += t => { Titles.Enqueue(t ?? ""); while (Titles.Count > 50) Titles.TryDequeue(out _); };

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
            bg.color = new Color(0.263f, 0.627f, 0.278f, 0.92f);   // light green, same as the Academy buttons
            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
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
            text.color = Color.white;
            text.fontStyle = FontStyle.Bold;
        }

        void Update()
        {
            if (!_loginPending || string.IsNullOrEmpty(AutoLoginPassword) || _browser == null) return;
            var client = _browser.browserClient;
            if (client == null || !client.IsConnected || !client.ReadySignalReceived) return;
            _loginPending = false;
            try { client.ExecuteJs(LoginScript(AutoLoginPassword)); }
            catch (Exception e) { Debug.LogWarning("[OpsBrowser] Auto-login script failed: " + e.Message); }
        }

        /// <summary>Polls for OpenC3's login form for a few seconds; fills the password and presses Login.</summary>
        static string LoginScript(string password)
        {
            string pw = password.Replace("\\", "\\\\").Replace("'", "\\'");
            return "(function(){var n=0;var t=setInterval(function(){n++;" +
                   "var p=document.querySelector('input[type=password]');" +
                   "var b=Array.from(document.querySelectorAll('button')).find(function(x){return x.textContent.trim().toLowerCase()==='login';});" +
                   "if(p&&b&&!document.querySelectorAll('input[type=password]')[1]){" +
                   "var set=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;set.call(p,'" + pw + "');" +
                   "p.dispatchEvent(new Event('input',{bubbles:true}));setTimeout(function(){b.click();},150);clearInterval(t);}" +
                   "if(n>40)clearInterval(t);},250);})();";
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
