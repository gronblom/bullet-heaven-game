using System;
using System.Collections.Generic;
using BulletHeaven.Ecs;
using BulletHeaven.Systems;
using UnityEngine;

namespace BulletHeaven.Unity
{
    /// <summary>
    /// HUD (health, XP, stats, weapons with their modifiers, boss bar, centre message) and the card
    /// menus (loadout, level-up modifiers, pause), drawn with IMGUI from a few generated textures, so
    /// the project needs no UI assets. Cards come from the shared core (<see cref="MenuCard"/>): icon,
    /// accent colour, category and pick pips. Click, 1-4, arrows / stick to move, Enter / A to pick.
    /// </summary>
    public sealed class GameUi : MonoBehaviour
    {
        private const float CardWidth = 250f;
        private const float CardHeight = 350f;
        private const float CardGap = 26f;

        public GameRunner Runner;
        public int EnemyCount;
        public float Speed;
        public Health Health;
        public List<WeaponSlot> Weapons = new List<WeaponSlot>();

        private string _title;
        private string _footer;
        private List<MenuCard> _cards;
        private Action<int> _onPick;
        private int _selected;
        private readonly float[] _lift = new float[8];
        private float _stickCooldown;
        private float _openedAt;
        private Vector2 _lastMouse;

        private Texture2D _rounded, _outline, _glow, _white;
        private GUIStyle _roundedStyle, _outlineStyle;
        private GUIStyle _title1, _cardTitle, _cardText, _category, _small, _label, _labelBold, _badge;
        private readonly Dictionary<string, Texture2D> _icons = new Dictionary<string, Texture2D>();

        public bool IsOpen => _cards != null;

        public void ShowCards(string title, string footer, List<MenuCard> cards, Action<int> onPick)
        {
            _title = title;
            _footer = footer;
            _cards = cards;
            _onPick = onPick;
            _selected = 0;
            _openedAt = Time.unscaledTime;
            Array.Clear(_lift, 0, _lift.Length);
        }

        public void Close()
        {
            _cards = null;
            _onPick = null;
        }

        private void Pick(int index)
        {
            var onPick = _onPick;
            Close();
            onPick?.Invoke(index); // may open the next menu straight away
        }

        private void Update()
        {
            if (Runner != null && Runner.State != null)
                Runner.State.MessageTimer -= Time.unscaledDeltaTime;
            if (!IsOpen)
                return;

            for (int i = 0; i < _cards.Count && i < _lift.Length; i++)
                _lift[i] = Mathf.MoveTowards(_lift[i], i == _selected ? 1f : 0f, Time.unscaledDeltaTime * 6f);

            // A and Space also jump: ignore keys for a moment so a jump doesn't pick a card.
            if (_onPick == null || Time.unscaledTime - _openedAt < 0.3f)
                return;
            for (int i = 0; i < 4 && i < _cards.Count; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    Pick(i);
                    return;
                }
            }
            int step = 0;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) step = 1;
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) step = -1;
            float stick = Input.GetAxisRaw("Horizontal");
            _stickCooldown -= Time.unscaledDeltaTime;
            if (step == 0 && Mathf.Abs(stick) > 0.6f && _stickCooldown <= 0f)
            {
                step = stick > 0f ? 1 : -1;
                _stickCooldown = 0.25f;
            }
            if (Mathf.Abs(stick) < 0.3f)
                _stickCooldown = 0f;
            _selected = (_selected + step + _cards.Count) % _cards.Count;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space) ||
                Input.GetKeyDown(KeyCode.JoystickButton0))
                Pick(_selected);
        }

        // --- drawing ---------------------------------------------------------------------------

        private void OnGUI()
        {
            if (Runner == null || Runner.State == null)
                return;
            Setup();
            Hud(Runner.State);
            if (IsOpen)
                CardMenu();
        }

        private void Hud(GameState state)
        {
            // Health and XP (amber, like the crystals), top left.
            Rounded(new Rect(10, 10, 300, 50), new Color(0f, 0f, 0f, 0.35f));
            Bar(new Rect(20, 18, 280, 14), Health.Max > 0f ? Health.Current / Health.Max : 0f, new Color(0.9f, 0.25f, 0.2f));
            Bar(new Rect(20, 38, 280, 14), state.XpToNext > 0 ? state.Xp / (float)state.XpToNext : 0f, new Color(1f, 0.65f, 0.2f));

            int seconds = (int)state.Time;
            string stats = $"{Runner.Level.Name}   LV {state.Level}   {seconds / 60}:{seconds % 60:00}\n" +
                           $"Kills {state.Kills}   Bugs {EnemyCount}   FPS {1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):0}";
            if (state.HasParachute)
                stats += "\nParachute: hold jump while falling";
            Shadowed(new Rect(16, 66, 420, 60), stats, _label);

            // Weapons with their icons and modifiers.
            float y = state.HasParachute ? 128f : 110f;
            foreach (var weapon in Weapons)
            {
                var accent = Cards.Accent(weapon.Kind).ToUnity();
                Rounded(new Rect(12, y, 40, 40), new Color(accent.r, accent.g, accent.b, 0.3f));
                var icon = Icon(Cards.Icon(weapon.Kind));
                if (icon != null)
                    GUI.DrawTexture(new Rect(14, y + 2, 36, 36), icon, ScaleMode.ScaleToFit);
                Shadowed(new Rect(60, y + 1, 300, 20), WeaponStats.Name(weapon.Kind), _labelBold);
                var mods = new List<string>();
                foreach (var modifier in Modifiers.All)
                {
                    int picks = Modifiers.Picks(state, modifier);
                    if (picks > 0 && modifier.Weapon == weapon.Kind)
                        mods.Add(picks > 1 ? $"{modifier.Name} x{picks}" : modifier.Name);
                }
                Shadowed(new Rect(60, y + 20, 380, 40), mods.Count > 0 ? string.Join("  ·  ", mods) : "no modifiers yet", _small);
                y += 48f;
            }

            // Boss bar, top centre.
            if (state.BossSpawned && !state.BossDefeated)
            {
                float width = 520f, x = (Screen.width - width) / 2f;
                Shadowed(new Rect(x, 8, width, 22), "THE QUEEN ANT", _category);
                Bar(new Rect(x, 32, width, 14), state.BossMaxHealth > 0f ? state.BossHealth / state.BossMaxHealth : 0f, new Color(0.75f, 0.1f, 0.35f));
            }

            if (state.MessageTimer > 0f && !IsOpen)
                Shadowed(new Rect(0, 0, Screen.width, Screen.height), state.Message, _title1);
        }

        private void CardMenu()
        {
            float t = Time.unscaledTime - _openedAt;
            float fadeIn = Mathf.Clamp01(t / 0.25f);

            // Darken the game behind, more towards the edges.
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = new Color(0.02f, 0.015f, 0.01f, 0.55f * fadeIn);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
                GUI.color = Color.white;
            }

            int n = _cards.Count;
            float scale = Mathf.Min(1.2f, Mathf.Min((Screen.width - 60f) / (n * CardWidth + (n - 1) * CardGap), (Screen.height - 220f) / CardHeight));
            float w = CardWidth * scale, h = CardHeight * scale, gap = CardGap * scale;
            float total = n * w + (n - 1) * gap;
            float x0 = (Screen.width - total) / 2f, y0 = (Screen.height - h) / 2f + 20f;

            Shadowed(new Rect(0, y0 - 100f, Screen.width, 60), _title, _title1);
            var footerStyle = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter };
            Shadowed(new Rect(0, y0 + h + 30f, Screen.width, 30), _footer, footerStyle);

            for (int i = 0; i < n; i++)
            {
                float appear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - i * 0.07f) / 0.3f));
                float lift = Mathf.SmoothStep(0f, 1f, _lift[Mathf.Min(i, _lift.Length - 1)]);
                var rect = new Rect(x0 + i * (w + gap), y0 + (1f - appear) * 60f - lift * 14f, w, h);

                // Hovering selects, but only when the mouse actually moved (so keys and the stick still work).
                if (Event.current.type == EventType.Repaint && rect.Contains(Event.current.mousePosition) &&
                    (Event.current.mousePosition - _lastMouse).sqrMagnitude > 1f)
                    _selected = i;
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && t > 0.3f)
                {
                    Pick(i);
                    return;
                }
                if (Event.current.type == EventType.Repaint)
                    Card(rect, _cards[i], i, appear, lift, scale);
            }
            if (Event.current.type == EventType.Repaint)
                _lastMouse = Event.current.mousePosition;
        }

        private void Card(Rect rect, MenuCard card, int index, float alpha, float lift, float scale)
        {
            var accent = card.Accent.ToUnity();
            float pad = 16f * scale;

            // Drop shadow, a glow in the card's colour when selected, the panel, a tinted top half and the border.
            Rounded(new Rect(rect.x + 6, rect.y + 10, rect.width, rect.height), new Color(0f, 0f, 0f, 0.45f * alpha));
            if (lift > 0.01f)
            {
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.45f * lift * alpha);
                GUI.DrawTexture(new Rect(rect.x - 40, rect.y - 40, rect.width + 80, rect.height + 80), _glow);
                GUI.color = Color.white;
            }
            Rounded(rect, new Color(0.09f, 0.08f, 0.075f, 0.96f * alpha));
            Rounded(new Rect(rect.x, rect.y, rect.width, rect.height * 0.52f), new Color(accent.r * 0.35f, accent.g * 0.35f, accent.b * 0.35f, 0.6f * alpha));
            var border = Color.Lerp(new Color(1f, 1f, 1f, 0.12f), accent, lift);
            border.a *= alpha;
            Outline(rect, border);

            // Number badge and category tag.
            var badge = new Rect(rect.x + pad * 0.7f, rect.y + pad * 0.7f, 30f * scale, 30f * scale);
            Rounded(badge, new Color(accent.r, accent.g, accent.b, 0.9f * alpha));
            Text(badge, (index + 1).ToString(), _badge, new Color(0.08f, 0.06f, 0.05f, alpha));
            if (!string.IsNullOrEmpty(card.Category))
                Text(new Rect(badge.xMax + 8f, badge.y, rect.width - badge.width - pad * 2f, badge.height), card.Category, _category,
                    new Color(accent.r, accent.g, accent.b, alpha));

            // Icon on a soft halo.
            float iconSize = 120f * scale;
            var iconRect = new Rect(rect.center.x - iconSize / 2f, rect.y + (54f - lift * 4f) * scale, iconSize, iconSize);
            GUI.color = new Color(accent.r, accent.g, accent.b, (0.35f + 0.25f * lift) * alpha);
            GUI.DrawTexture(new Rect(iconRect.x - 30f, iconRect.y - 30f, iconRect.width + 60f, iconRect.height + 60f), _glow);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            var icon = Icon(card.Icon);
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            GUI.color = Color.white;

            // Title and description.
            float textY = iconRect.yMax + 14f * scale;
            Text(new Rect(rect.x + pad, textY, rect.width - pad * 2f, 30f * scale), card.Title, _cardTitle, new Color(1f, 1f, 1f, alpha));
            Text(new Rect(rect.x + pad, textY + 34f * scale, rect.width - pad * 2f, 80f * scale), card.Text, _cardText,
                new Color(0.85f, 0.82f, 0.78f, alpha));

            // Pips: how many times this has been picked, and the one you'd get now (pulsing).
            if (card.MaxPicks > 0)
            {
                float pip = 12f * scale, space = 6f * scale;
                float width = card.MaxPicks * pip + (card.MaxPicks - 1) * space;
                float px = rect.center.x - width / 2f, py = rect.yMax - pad - pip;
                for (int k = 0; k < card.MaxPicks; k++)
                {
                    var r = new Rect(px + k * (pip + space), py, pip, pip);
                    if (k < card.Picks)
                        Rounded(r, new Color(accent.r, accent.g, accent.b, alpha));
                    else if (k == card.Picks)
                        Rounded(r, new Color(accent.r, accent.g, accent.b, (0.35f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f)) * alpha));
                    else
                        Outline(r, new Color(1f, 1f, 1f, 0.3f * alpha));
                }
                if (card.Picks == 0)
                {
                    var newStyle = new GUIStyle(_category) { alignment = TextAnchor.MiddleCenter };
                    Text(new Rect(rect.x, py - 22f * scale, rect.width, 18f * scale), "NEW", newStyle, new Color(1f, 0.85f, 0.4f, alpha));
                }
            }
        }

        // --- helpers -------------------------------------------------------------------------------

        private void Setup()
        {
            if (_rounded != null)
                return;
            _white = Texture2D.whiteTexture;
            _rounded = RoundedTexture(64, 14f, false);
            _outline = RoundedTexture(64, 14f, true);
            _glow = GlowTexture(64);
            _roundedStyle = new GUIStyle { border = new RectOffset(16, 16, 16, 16) };
            _roundedStyle.normal.background = _rounded;
            _outlineStyle = new GUIStyle { border = new RectOffset(16, 16, 16, 16) };
            _outlineStyle.normal.background = _outline;

            GUIStyle Label(int size, FontStyle style, TextAnchor anchor, bool wrap = false)
            {
                var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, alignment = anchor, wordWrap = wrap, richText = false };
                s.normal.textColor = Color.white;
                return s;
            }
            _title1 = Label(40, FontStyle.Bold, TextAnchor.MiddleCenter, true);
            _cardTitle = Label(22, FontStyle.Bold, TextAnchor.MiddleCenter, true);
            _cardText = Label(15, FontStyle.Normal, TextAnchor.UpperCenter, true);
            _category = Label(13, FontStyle.Bold, TextAnchor.MiddleLeft);
            _small = Label(14, FontStyle.Normal, TextAnchor.UpperLeft, true);
            _label = Label(15, FontStyle.Normal, TextAnchor.UpperLeft);
            _labelBold = Label(16, FontStyle.Bold, TextAnchor.UpperLeft);
            _badge = Label(16, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        private Texture2D Icon(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (!_icons.TryGetValue(name, out var icon))
                _icons[name] = icon = Resources.Load<Texture2D>("Icons/" + name);
            return icon;
        }

        private void Rounded(Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            GUI.color = color;
            _roundedStyle.Draw(rect, false, false, false, false);
            GUI.color = Color.white;
        }

        private void Outline(Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            GUI.color = color;
            _outlineStyle.Draw(rect, false, false, false, false);
            GUI.color = Color.white;
        }

        private void Bar(Rect rect, float fill, Color color)
        {
            Rounded(rect, new Color(0f, 0f, 0f, 0.6f));
            if (fill > 0.01f)
                Rounded(new Rect(rect.x, rect.y, Mathf.Max(rect.height, rect.width * Mathf.Clamp01(fill)), rect.height), color);
        }

        private static void Text(Rect rect, string text, GUIStyle style, Color color)
        {
            var old = style.normal.textColor;
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
            style.normal.textColor = old;
        }

        private static void Shadowed(Rect rect, string text, GUIStyle style)
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
        private static Texture2D GlowTexture(int size)
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
