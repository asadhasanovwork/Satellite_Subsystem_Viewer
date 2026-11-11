using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Academy.Operations
{
    /// <summary>
    /// Visual language of the operations console: "night shift in the flight dynamics room".
    /// Ink-blue panels; colour always carries meaning:
    ///   sun-amber = power / sunlight, ground-teal = contact / nominal, night-violet = eclipse,
    ///   orange = caution, red = alarm. Live numbers use a monospace face so digits don't jitter.
    /// </summary>
    public static class OpsStyle
    {
        // Surfaces
        public static readonly Color Ink = Hex(0x0E1A2B);
        public static readonly Color Panel = Hex(0x132236, 0.97f);
        public static readonly Color Raised = Hex(0x1A2D46);
        public static readonly Color RaisedHover = Hex(0x23395A);
        public static readonly Color Line = Hex(0x2A4262);
        // Text
        public static readonly Color Text = Hex(0xE6EEF7);
        public static readonly Color Muted = Hex(0x8EA3BC);
        public static readonly Color Faint = Hex(0x5D7491);
        // Meaning
        public static readonly Color Sun = Hex(0xF2B544);
        public static readonly Color Ground = Hex(0x3CC8B4);
        public static readonly Color Night = Hex(0x6B5BA6);
        public static readonly Color Caution = Hex(0xF08A3C);
        public static readonly Color Alarm = Hex(0xE5484D);

        // Compatibility names used across the operations scripts
        public static readonly Color Bg = Ink;
        public static readonly Color PanelBg = Panel;
        public static readonly Color CardBg = Raised;
        public static readonly Color CardHighlight = Hex(0x1F3B52);
        public static readonly Color Accent = Raised;
        public static readonly Color AccentHover = RaisedHover;
        public static readonly Color Green = Ground;
        public static readonly Color Red = Alarm;
        public static readonly Color Amber = Sun;
        public static readonly Color Grey = Faint;

        public static Color Hex(int rgb, float a = 1f)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);
        }

        public static Color Dim(Color c, float k) { return new Color(c.r * k, c.g * k, c.b * k, c.a); }
        public static Color WithAlpha(Color c, float a) { return new Color(c.r, c.g, c.b, a); }

        // ------------------------------------------------------------------ type

        private static Font _mono;
        private static bool _monoTried;

        /// <summary>A monospace OS font for live numbers (falls back to the default font if none is found).</summary>
        public static void Mono(VisualElement e)
        {
            if (!_monoTried)
            {
                _monoTried = true;
                try
                {
                    _mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Cascadia Mono", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Courier New" }, 14);
                }
                catch (Exception)
                {
                    _mono = null;
                }
            }
            if (_mono != null)
                e.style.unityFontDefinition = FontDefinition.FromFont(_mono);
        }

        // ------------------------------------------------------------------ layout helpers

        public static VisualElement Row()
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            return e;
        }

        public static void Stretch(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.right = 0; e.style.top = 0; e.style.bottom = 0;
        }

        public static void Round(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
        }

        public static void Pad(VisualElement e, float h, float v)
        {
            e.style.paddingLeft = h; e.style.paddingRight = h;
            e.style.paddingTop = v; e.style.paddingBottom = v;
        }

        public static void Border(VisualElement e, float width, Color c)
        {
            e.style.borderLeftWidth = width; e.style.borderRightWidth = width;
            e.style.borderTopWidth = width; e.style.borderBottomWidth = width;
            e.style.borderLeftColor = c; e.style.borderRightColor = c;
            e.style.borderTopColor = c; e.style.borderBottomColor = c;
        }

        public static VisualElement Spacer()
        {
            var s = new VisualElement();
            s.style.flexGrow = 1;
            s.pickingMode = PickingMode.Ignore;
            return s;
        }

        // ------------------------------------------------------------------ elements

        public static Label Label(string text, int size, bool bold, Color color)
        {
            var l = new Label(text);
            l.style.fontSize = size;
            l.style.color = color;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginLeft = 0; l.style.marginRight = 0; l.style.marginTop = 0; l.style.marginBottom = 0;
            l.style.paddingLeft = 0; l.style.paddingRight = 0;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        /// <summary>A label for a live number: monospace, no wrapping.</summary>
        public static Label Number(string text, int size, Color color)
        {
            Label l = Label(text, size, false, color);
            l.style.whiteSpace = WhiteSpace.NoWrap;
            Mono(l);
            return l;
        }

        /// <summary>Small heading inside a panel (sentence case).</summary>
        public static Label Heading(string text)
        {
            Label l = Label(text, 12, true, Muted);
            l.style.marginTop = 10;
            l.style.marginBottom = 4;
            return l;
        }

        public static Label Badge(string text, Color color)
        {
            var l = Label(text, 10, true, color);
            l.style.backgroundColor = WithAlpha(color, 0.16f);
            Border(l, 1, WithAlpha(color, 0.55f));
            Pad(l, 6, 1);
            Round(l, 3);
            l.style.alignSelf = Align.FlexStart;
            return l;
        }

        public static Button Button(string text, Action action, float width = 0, int fontSize = 12)
        {
            var b = new Button(action) { text = text };
            b.style.fontSize = fontSize;
            b.style.color = Text;
            b.style.backgroundColor = Raised;
            b.style.minHeight = 26;
            b.style.marginLeft = 2; b.style.marginRight = 2; b.style.marginTop = 2; b.style.marginBottom = 2;
            Pad(b, 8, 2);
            b.style.whiteSpace = WhiteSpace.Normal;
            Border(b, 1, Line);
            Round(b, 4);
            if (width > 0) b.style.width = width;
            b.RegisterCallback<MouseEnterEvent>(_ => b.style.backgroundColor = RaisedHover);
            b.RegisterCallback<MouseLeaveEvent>(_ => b.style.backgroundColor = Raised);
            return b;
        }

        /// <summary>A button that stands out for the one main action on a screen.</summary>
        public static Button Primary(string text, Action action, float width = 0)
        {
            Button b = Button(text, action, width, 13);
            b.style.backgroundColor = Hex(0x1E5E5A);
            Border(b, 1, Ground);
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.minHeight = 32;
            b.RegisterCallback<MouseEnterEvent>(_ => b.style.backgroundColor = Hex(0x26766F));
            b.RegisterCallback<MouseLeaveEvent>(_ => b.style.backgroundColor = Hex(0x1E5E5A));
            return b;
        }

        public static VisualElement Card()
        {
            var c = new VisualElement();
            c.style.backgroundColor = Raised;
            Pad(c, 10, 8);
            c.style.marginBottom = 6;
            Round(c, 4);
            return c;
        }

        public static TextField Field(string label, string value, float width = 0)
        {
            var f = new TextField(label) { value = value };
            f.style.color = Text;
            f.style.marginLeft = 2; f.style.marginRight = 2;
            Mono(f);
            if (width > 0) f.style.width = width;
            return f;
        }

        /// <summary>Round status lamp.</summary>
        public static VisualElement Lamp(float size, Color color)
        {
            var l = new VisualElement();
            l.style.width = size;
            l.style.height = size;
            l.style.minWidth = size;
            Round(l, size * 0.5f);
            l.style.backgroundColor = color;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        public static Color SeverityColor(EventSeverity s)
        {
            return s == EventSeverity.Alarm ? Alarm : (s == EventSeverity.Warning ? Caution : Text);
        }

        public static Color OrbitColor(string orbitClass)
        {
            switch (orbitClass)
            {
                case "LEO": return Ground;
                case "MEO": return Hex(0x8C7BE0);
                case "GEO": return Sun;
                default: return Faint;
            }
        }
    }
}
