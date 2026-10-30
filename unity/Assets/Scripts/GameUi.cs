using System;
using System.Collections.Generic;
using BulletHeaven.Ecs;
using BulletHeaven.Systems;
using UnityEngine;
using static BulletHeaven.Unity.UiKit;

namespace BulletHeaven.Unity
{
    /// <summary>
    /// HUD (health, armour, XP, the four weapons with their modules and equations, boss bar, centre
    /// message), the card menus (level-up modules, pause) and the module screen (<see cref="ModuleScreen"/>),
    /// all IMGUI. Cards come from the shared core (<see cref="MenuCard"/>). Click, 1-4, arrows / stick
    /// to move, Enter / A to pick.
    /// </summary>
    public sealed class GameUi : MonoBehaviour
    {
        private const float CardWidth = 250f;
        private const float CardHeight = 350f;
        private const float CardGap = 26f;

        public GameRunner Runner;
        public int EnemyCount;
        public Health Health;
        public Armor Armor;
        public readonly ModuleScreen Modules = new ModuleScreen();

        private string _title;
        private string _footer;
        private List<MenuCard> _cards;
        private Action<int> _onPick;
        private int _selected;
        private readonly float[] _lift = new float[8];
        private float _stickCooldown;
        private float _openedAt;
        private Vector2 _lastMouse;

        public bool CardsOpen => _cards != null;
        public bool IsOpen => _cards != null || Modules.IsOpen;

        public void ShowCards(string title, string footer, List<MenuCard> cards, Action<int> onPick)
        {
            Modules.Close();
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
            Modules.Close();
        }

        private void Pick(int index)
        {
            var onPick = _onPick;
            _cards = null;
            _onPick = null;
            onPick?.Invoke(index); // may open the next menu straight away
        }

        private void Update()
        {
            if (Runner != null && Runner.State != null)
                Runner.State.MessageTimer -= Time.unscaledDeltaTime;
            if (!CardsOpen)
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
            if (Modules.IsOpen)
                Modules.Draw(Runner.State);
            else if (CardsOpen)
                CardMenu();
        }

        private void Hud(GameState state)
        {
            // Health (red), armour (blue) and XP (amber, like the crystals), top left.
            Rounded(new Rect(10, 10, 300, 70), new Color(0f, 0f, 0f, 0.35f));
            Bar(new Rect(20, 18, 280, 14), Health.Max > 0f ? Health.Current / Health.Max : 0f, new Color(0.9f, 0.25f, 0.2f));
            Bar(new Rect(20, 37, 280, 10), Armor.Max > 0f ? Armor.Current / Armor.Max : 0f, new Color(0.35f, 0.65f, 1f));
            Bar(new Rect(20, 53, 280, 14), state.XpToNext > 0 ? state.Xp / (float)state.XpToNext : 0f, new Color(1f, 0.65f, 0.2f));

            int seconds = (int)state.Time;
            string stats = $"{Runner.Level.Name}   LV {state.Level}   {seconds / 60}:{seconds % 60:00}\n" +
                           $"Kills {state.Kills}   Bugs {EnemyCount}   FPS {1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):0}\n" +
                           $"Modules {state.Inventory.Count}/{GameState.InventorySize}   (Tab: fit modules)";
            if (state.HasParachute)
                stats += "\nParachute: hold jump while falling";
            Shadowed(new Rect(16, 86, 440, 80), stats, Label);

            // The four weapons: icon, name, slotted modules as chips, and the equation they run.
            float y = state.HasParachute ? 172f : 154f;
            foreach (var weapon in state.Weapons)
            {
                var accent = Cards.Accent(weapon.Kind).ToUnity();
                Rounded(new Rect(12, y, 40, 40), new Color(accent.r, accent.g, accent.b, 0.3f));
                var icon = Icon(Cards.Icon(weapon.Kind));
                if (icon != null)
                    GUI.DrawTexture(new Rect(14, y + 2, 36, 36), icon, ScaleMode.ScaleToFit);
                Shadowed(new Rect(60, y, 200, 20), WeaponBase.Name(weapon.Kind), LabelBold);
                float x = 190f;
                foreach (var module in weapon.Slots)
                {
                    if (module == null)
                        continue;
                    var c = Cards.Accent(module.Op).ToUnity();
                    var chip = new Rect(x, y + 1, 42, 18);
                    Rounded(chip, new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 0.9f));
                    Text(chip, Cards.Glyph(module), Badge, c);
                    x += 46f;
                }
                Shadowed(new Rect(60, y + 20, 400, 20), WeaponBase.Formula(weapon.Stats), Small);
                y += 46f;
            }

            // Boss bar, top centre.
            if (state.BossSpawned && !state.BossDefeated)
            {
                float width = 520f, bx = (Screen.width - width) / 2f;
                Shadowed(new Rect(bx, 8, width, 22), "THE QUEEN ANT", Category);
                Bar(new Rect(bx, 32, width, 14), state.BossMaxHealth > 0f ? state.BossHealth / state.BossMaxHealth : 0f, new Color(0.75f, 0.1f, 0.35f));
            }

            if (state.MessageTimer > 0f && !IsOpen)
                Shadowed(new Rect(0, 0, Screen.width, Screen.height), state.Message, Title);
        }

        private void CardMenu()
        {
            float t = Time.unscaledTime - _openedAt;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.015f, 0.01f, 0.55f * Mathf.Clamp01(t / 0.25f)));

            int n = _cards.Count;
            float scale = Mathf.Min(1.2f, Mathf.Min((Screen.width - 60f) / (n * CardWidth + (n - 1) * CardGap), (Screen.height - 220f) / CardHeight));
            float w = CardWidth * scale, h = CardHeight * scale, gap = CardGap * scale;
            float total = n * w + (n - 1) * gap;
            float x0 = (Screen.width - total) / 2f, y0 = (Screen.height - h) / 2f + 20f;

            Shadowed(new Rect(0, y0 - 100f, Screen.width, 60), _title, Title);
            Shadowed(new Rect(0, y0 + h + 30f, Screen.width, 30), _footer, Centered);

            for (int i = 0; i < n; i++)
            {
                float appear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - i * 0.07f) / 0.3f));
                float lift = Mathf.SmoothStep(0f, 1f, _lift[Mathf.Min(i, _lift.Length - 1)]);
                var rect = new Rect(x0 + i * (w + gap), y0 + (1f - appear) * 60f - lift * 14f, w, h);

                // Hovering selects, but only when the mouse actually moved (so keys and the stick still work).
                if (Repaint && rect.Contains(Event.current.mousePosition) && (Event.current.mousePosition - _lastMouse).sqrMagnitude > 1f)
                    _selected = i;
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && t > 0.3f)
                {
                    Pick(i);
                    return;
                }
                if (Repaint)
                    Card(rect, _cards[i], i, appear, lift, scale);
            }
            if (Repaint)
                _lastMouse = Event.current.mousePosition;
        }

        private static void Card(Rect rect, MenuCard card, int index, float alpha, float lift, float scale)
        {
            var accent = card.Accent.ToUnity();
            float pad = 16f * scale;

            // Drop shadow, a glow in the card's colour when selected, the panel, a tinted top half and the border.
            Rounded(new Rect(rect.x + 6, rect.y + 10, rect.width, rect.height), new Color(0f, 0f, 0f, 0.45f * alpha));
            if (lift > 0.01f)
                Glow(new Rect(rect.x - 40, rect.y - 40, rect.width + 80, rect.height + 80), new Color(accent.r, accent.g, accent.b, 0.45f * lift * alpha));
            Rounded(rect, new Color(0.09f, 0.08f, 0.075f, 0.96f * alpha));
            Rounded(new Rect(rect.x, rect.y, rect.width, rect.height * 0.52f), new Color(accent.r * 0.35f, accent.g * 0.35f, accent.b * 0.35f, 0.6f * alpha));
            var border = Color.Lerp(new Color(1f, 1f, 1f, 0.12f), accent, lift);
            border.a *= alpha;
            Outline(rect, border);

            // Number badge and category tag.
            var badge = new Rect(rect.x + pad * 0.7f, rect.y + pad * 0.7f, 30f * scale, 30f * scale);
            Rounded(badge, new Color(accent.r, accent.g, accent.b, 0.9f * alpha));
            Text(badge, (index + 1).ToString(), Badge, new Color(0.08f, 0.06f, 0.05f, alpha));
            if (!string.IsNullOrEmpty(card.Category))
                Text(new Rect(badge.xMax + 8f, badge.y, rect.width - badge.width - pad * 2f, badge.height), card.Category, Category,
                    new Color(accent.r, accent.g, accent.b, alpha));

            // Icon (or the module's arithmetic glyph) on a soft halo.
            float iconSize = 120f * scale;
            var iconRect = new Rect(rect.center.x - iconSize / 2f, rect.y + (54f - lift * 4f) * scale, iconSize, iconSize);
            Glow(new Rect(iconRect.x - 30f, iconRect.y - 30f, iconRect.width + 60f, iconRect.height + 60f),
                new Color(accent.r, accent.g, accent.b, (0.35f + 0.25f * lift) * alpha));
            var icon = Icon(card.Icon);
            if (icon != null)
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                if (card.Glyph != null)
                {
                    var tag = new Rect(iconRect.xMax - 40f * scale, iconRect.yMax - 30f * scale, 56f * scale, 30f * scale);
                    Rounded(tag, new Color(0.05f, 0.04f, 0.04f, 0.9f * alpha));
                    Text(tag, card.Glyph, GlyphSmall, new Color(accent.r, accent.g, accent.b, alpha));
                }
            }
            else if (card.Glyph != null)
            {
                Text(iconRect, card.Glyph, Glyph, new Color(accent.r, accent.g, accent.b, alpha));
            }

            // Title and description.
            float textY = iconRect.yMax + 14f * scale;
            Text(new Rect(rect.x + pad, textY, rect.width - pad * 2f, 30f * scale), card.Title, CardTitle, new Color(1f, 1f, 1f, alpha));
            Text(new Rect(rect.x + pad, textY + 34f * scale, rect.width - pad * 2f, 90f * scale), card.Text, CardText,
                new Color(0.85f, 0.82f, 0.78f, alpha));
        }
    }
}
