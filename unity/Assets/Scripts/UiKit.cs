using System.Collections.Generic;
using UnityEngine;

namespace BulletHeaven.Unity
{
    /// <summary>
    /// IMGUI drawing helpers shared by the HUD, the card menus and the module screen: rounded panels and
    /// outlines (generated 9-slice textures), soft glows, text styles and icons from Resources/Icons.
    /// </summary>
    public static class UiKit
    {
        private static Texture2D _rounded, _outline;
        private static GUIStyle _roundedStyle, _outlineStyle;
        private static readonly Dictionary<string, Texture2D> Icons = new Dictionary<string, Texture2D>();

        public static Texture2D White => Texture2D.whiteTexture;
        public static Texture2D GlowTexture { get; private set; }
        public static GUIStyle Title, CardTitle, CardText, Category, Small, Label, LabelBold, Badge, Glyph, GlyphSmall, Centered;

        /// <summary>Call at the start of OnGUI (GUI.skin is only available there).</summary>
        public static void Setup()
        {
            if (_rounded != null)
                return;
            _rounded = RoundedTexture(64, 14f, false);
            _outline = RoundedTexture(64, 14f, true);
            GlowTexture = MakeGlow(64);
            _roundedStyle = new GUIStyle { border = new RectOffset(16, 16, 16, 16) };
            _roundedStyle.normal.background = _rounded;
            _outlineStyle = new GUIStyle { border = new RectOffset(16, 16, 16, 16) };
            _outlineStyle.normal.background = _outline;

            GUIStyle Make(int size, FontStyle style, TextAnchor anchor, bool wrap = false)
            {
                var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, alignment = anchor, wordWrap = wrap, richText = false };
                s.normal.textColor = Color.white;
                return s;
            }
            Title = Make(40, FontStyle.Bold, TextAnchor.MiddleCenter, true);
            CardTitle = Make(22, FontStyle.Bold, TextAnchor.MiddleCenter, true);
            CardText = Make(15, FontStyle.Normal, TextAnchor.UpperCenter, true);
            Category = Make(13, FontStyle.Bold, TextAnchor.MiddleLeft);
            Small = Make(14, FontStyle.Normal, TextAnchor.UpperLeft, true);
            Label = Make(15, FontStyle.Normal, TextAnchor.UpperLeft);
            LabelBold = Make(16, FontStyle.Bold, TextAnchor.UpperLeft);
            Badge = Make(16, FontStyle.Bold, TextAnchor.MiddleCenter);
            Glyph = Make(64, FontStyle.Bold, TextAnchor.MiddleCenter);
            GlyphSmall = Make(20, FontStyle.Bold, TextAnchor.MiddleCenter);
            Centered = Make(14, FontStyle.Normal, TextAnchor.MiddleCenter, true);
        }

        public static Texture2D Icon(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (!Icons.TryGetValue(name, out var icon))
                Icons[name] = icon = Resources.Load<Texture2D>("Icons/" + name);
            return icon;
        }

        public static bool Repaint => Event.current.type == EventType.Repaint;

        public static void Rounded(Rect rect, Color color)
        {
            if (!Repaint)
                return;
            GUI.color = color;
            _roundedStyle.Draw(rect, false, false, false, false);
            GUI.color = Color.white;
        }

        public static void Outline(Rect rect, Color color)
        {
            if (!Repaint)
                return;
            GUI.color = color;
            _outlineStyle.Draw(rect, false, false, false, false);
            GUI.color = Color.white;
        }

        public static void Glow(Rect rect, Color color)
        {
            if (!Repaint)
                return;
            GUI.color = color;
            GUI.DrawTexture(rect, GlowTexture);
            GUI.color = Color.white;
        }

        public static void Fill(Rect rect, Color color)
        {
            if (!Repaint)
                return;
            GUI.color = color;
            GUI.DrawTexture(rect, White);
            GUI.color = Color.white;
        }

        public static void Bar(Rect rect, float fill, Color color)
        {
            Rounded(rect, new Color(0f, 0f, 0f, 0.6f));
            if (fill > 0.01f)
                Rounded(new Rect(rect.x, rect.y, Mathf.Max(rect.height, rect.width * Mathf.Clamp01(fill)), rect.height), color);
        }

        public static void Text(Rect rect, string text, GUIStyle style, Color color)
        {
            var old = style.normal.textColor;
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
            style.normal.textColor = old;
        }

        public static void Shadowed(Rect rect, string text, GUIStyle style)
        {
            Text(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), text, style, new Color(0f, 0f, 0f, 0.8f));
            Text(rect, text, style, Color.white);
        }

        /// <summary>White rounded rectangle (or just its 3 px outline), anti-aliased, for 9-slice drawing.</summary>
        private static Texture2D RoundedTexture(int size, float radius, bool outline)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Signed distance to a rounded box.
                    float qx = Mathf.Abs(x + 0.5f - size / 2f) - (size / 2f - radius);
                    float qy = Mathf.Abs(y + 0.5f - size / 2f) - (size / 2f - radius);
                    float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    if (outline)
                        a *= Mathf.Clamp01(d + 3.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Soft round glow, white in the middle fading to nothing at the edge.</summary>
        private static Texture2D MakeGlow(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude / (size / 2f);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
