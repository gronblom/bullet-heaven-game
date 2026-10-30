using System;
using System.Collections.Generic;
using System.Text;
using BulletHeaven.Ecs;
using BulletHeaven.Systems;
using UnityEngine;

namespace BulletHeaven.Unity
{
    /// <summary>
    /// HUD (health, XP, stats, weapons with their modifiers, boss bar, centre message) and the card
    /// menus (loadout, level-up modifiers, pause), drawn with IMGUI so the project needs no UI assets.
    /// Cards: click, 1-3, arrows / stick to move and Enter / Space / A to pick.
    /// </summary>
    public sealed class GameUi : MonoBehaviour
    {
        public GameRunner Runner;
        public int EnemyCount;
        public float Speed;
        public Health Health;
        public List<WeaponSlot> Weapons = new List<WeaponSlot>();

        private string _title;
        private string _footer;
        private IReadOnlyList<(string Title, string Text)> _cards;
        private Action<int> _onPick;
        private int _selected;
        private float _stickCooldown;
        private float _openedAt;
        private GUIStyle _big, _card, _cardSelected, _label, _small;
        private Texture2D _white;

        public bool IsOpen => _cards != null;

        public void ShowCards(string title, string footer, IReadOnlyList<(string Title, string Text)> cards, Action<int> onPick)
        {
            _title = title;
            _footer = footer;
            _cards = cards;
            _onPick = onPick;
            _selected = 0;
            _openedAt = Time.unscaledTime;
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
            // A and Space also jump: ignore keys for a moment so a jump doesn't pick a card.
            if (!IsOpen || _onPick == null || Time.unscaledTime - _openedAt < 0.3f)
                return;
            for (int i = 0; i < 3 && i < _cards.Count; i++)
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

        private void Styles()
        {
            if (_big != null)
                return;
            _white = Texture2D.whiteTexture;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 36, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            _card = new GUIStyle(GUI.skin.button) { fontSize = 18, wordWrap = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(14, 14, 14, 14) };
            _cardSelected = new GUIStyle(_card) { fontStyle = FontStyle.Bold };
            _cardSelected.normal = _card.hover;
        }

        private void OnGUI()
        {
            if (Runner == null || Runner.State == null)
                return;
            Styles();
            var state = Runner.State;

            // Health and XP bars, stats and weapons, top left.
            Bar(new Rect(16, 16, 280, 16), Health.Max > 0f ? Health.Current / Health.Max : 0f, new Color(0.85f, 0.2f, 0.2f));
            Bar(new Rect(16, 36, 280, 16), state.XpToNext > 0 ? state.Xp / (float)state.XpToNext : 0f, new Color(0.3f, 0.85f, 0.4f));
            int seconds = (int)state.Time;
            var text = new StringBuilder();
            text.Append($"{Runner.Level.Name}  (F1-F3, Esc pause)\nLevel {state.Level}   Kills {state.Kills}\n");
            text.Append($"Time {seconds / 60}:{seconds % 60:00}   Speed {Speed:0}\nEnemies {EnemyCount}   FPS {1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):0}");
            if (state.HasParachute)
                text.Append("\nParachute: hold jump while falling");
            text.Append('\n');
            foreach (var weapon in Weapons)
            {
                text.Append($"\n{WeaponStats.Name(weapon.Kind)}");
                foreach (var modifier in Modifiers.All)
                {
                    int picks = Modifiers.Picks(state, modifier);
                    if (picks > 0 && modifier.Weapon == weapon.Kind)
                        text.Append(picks > 1 ? $"\n   {modifier.Name} x{picks}" : $"\n   {modifier.Name}");
                }
            }
            Shadowed(new Rect(16, 58, 420, 400), text.ToString(), _label);

            // Boss bar, top centre.
            if (state.BossSpawned && !state.BossDefeated)
            {
                float width = 520f, x = (Screen.width - width) / 2f;
                Shadowed(new Rect(x, 8, width, 22), "THE QUEEN ANT", _small);
                Bar(new Rect(x, 32, width, 16), state.BossMaxHealth > 0f ? state.BossHealth / state.BossMaxHealth : 0f, new Color(0.75f, 0.1f, 0.35f));
            }

            // Centre message.
            if (state.MessageTimer > 0f && !IsOpen)
                Shadowed(new Rect(0, 0, Screen.width, Screen.height), state.Message, _big);

            if (IsOpen)
                Cards();
        }

        private void Cards()
        {
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
            GUI.color = Color.white;

            float cardWidth = 260f, cardHeight = 160f, gap = 24f;
            float total = _cards.Count * cardWidth + (_cards.Count - 1) * gap;
            float x = (Screen.width - total) / 2f, y = Screen.height / 2f - cardHeight / 2f;
            Shadowed(new Rect(0, y - 130, Screen.width, 110), _title, _big);
            for (int i = 0; i < _cards.Count; i++)
            {
                var rect = new Rect(x + i * (cardWidth + gap), y, cardWidth, cardHeight);
                if (GUI.Button(rect, $"{i + 1}.  {_cards[i].Title}\n\n{_cards[i].Text}", i == _selected ? _cardSelected : _card))
                {
                    Pick(i);
                    return;
                }
                if (i == _selected)
                {
                    GUI.color = new Color(1f, 0.8f, 0.3f);
                    GUI.DrawTexture(new Rect(rect.x, rect.yMax + 4, rect.width, 4), _white);
                    GUI.color = Color.white;
                }
            }
            Shadowed(new Rect(0, y + cardHeight + 20, Screen.width, 30), _footer, _small);
        }

        private void Bar(Rect rect, float fill, Color color)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(rect, _white);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height), _white);
            GUI.color = Color.white;
        }

        private static void Shadowed(Rect rect, string text, GUIStyle style)
        {
            var color = style.normal.textColor;
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), text, style);
            style.normal.textColor = Color.white;
            GUI.Label(rect, text, style);
            style.normal.textColor = color;
        }
    }
}
