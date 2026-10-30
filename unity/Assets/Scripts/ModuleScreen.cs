using BulletHeaven.Ecs;
using BulletHeaven.Systems;
using UnityEngine;
using static BulletHeaven.Unity.UiKit;
using CoreModules = BulletHeaven.Systems.Modules;

namespace BulletHeaven.Unity
{
    /// <summary>
    /// The module screen (Tab): the four weapons with their stats, the equation each runs and four
    /// module slots, plus the inventory bar. Drag a module (or click it, then click where it goes) into
    /// a slot to slot it: the weapon's numbers update at once. Right-click a slot to take a module out,
    /// drop one on the bin to throw it away, Auto-fit puts everything that fits into free slots.
    /// </summary>
    public sealed class ModuleScreen
    {
        private const float Cell = 66f;

        private enum Place { None, Inventory, Slot, Bin }

        private struct Spot
        {
            public Place Place;
            public int Index;  // inventory index, or slot index
            public int Weapon; // weapon index (slots)
        }

        public bool IsOpen { get; private set; }
        private int _highlight = -1; // a freshly taken module, pulsing in the inventory
        private Spot _held;          // picked up by click or drag
        private Spot _pressed;
        private bool _dragging;
        private bool _justPicked;
        private Vector2 _pressAt;
        private string _note;
        private float _noteTime;

        public void Open(int highlight = -1)
        {
            IsOpen = true;
            _highlight = highlight;
            _held = default;
            _pressed = default;
            _dragging = false;
        }

        public void Close()
        {
            IsOpen = false;
            _held = default;
            _dragging = false;
        }

        public void Draw(GameState state)
        {
            float width = Mathf.Min(1180f, Screen.width - 40f);
            float x0 = (Screen.width - width) / 2f;
            float panelW = (width - 20f) / 2f, panelH = 214f;
            float top = Mathf.Max(70f, (Screen.height - (panelH * 2f + 20f + 150f)) / 2f);

            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.015f, 0.01f, 0.72f));
            Shadowed(new Rect(0, top - 62f, Screen.width, 44f), "MODULES", Title);
            Shadowed(new Rect(0, top - 24f, Screen.width, 20f),
                "Drag modules into weapon slots (or click, then click a slot). Right-click a slot to take it out. +ADD applies before ×MULTIPLY.", Centered);

            // Weapon panels, 2 x 2.
            for (int w = 0; w < state.Weapons.Count; w++)
            {
                var rect = new Rect(x0 + (w % 2) * (panelW + 20f), top + (w / 2) * (panelH + 20f), panelW, panelH);
                WeaponPanel(state, state.Weapons[w], w, rect);
            }

            // Inventory bar, bin and buttons.
            float invY = top + panelH * 2f + 40f;
            Shadowed(new Rect(x0, invY - 22f, 300f, 20f), $"INVENTORY  {state.Inventory.Count}/{GameState.InventorySize}", Category);
            for (int i = 0; i < GameState.InventorySize; i++)
            {
                var cell = InventoryCell(x0, invY, i);
                var module = i < state.Inventory.Count ? state.Inventory[i] : null;
                bool pulse = i == _highlight && module != null;
                Chip(cell, module, IsHeld(Place.Inventory, i, 0), pulse);
                Hit(cell, new Spot { Place = Place.Inventory, Index = i }, module, state);
            }
            var bin = InventoryCell(x0, invY, GameState.InventorySize);
            bin.x += 18f;
            Rounded(bin, new Color(0.25f, 0.06f, 0.05f, 0.85f));
            Outline(bin, new Color(1f, 0.35f, 0.3f, 0.5f));
            Text(bin, "BIN", Badge, new Color(1f, 0.6f, 0.55f));
            Hit(bin, new Spot { Place = Place.Bin }, null, state);

            var auto = new Rect(x0 + width - 260f, invY, 120f, Cell);
            var done = new Rect(x0 + width - 130f, invY, 130f, Cell);
            if (Button(auto, "AUTO-FIT", new Color(0.45f, 0.9f, 0.45f)))
            {
                CoreModules.AutoFitInventory(state);
                Note(state.Inventory.Count == 0 ? "Everything slotted." : "Slotted what fits; no free slot for the rest.");
            }
            if (Button(done, "DONE  (Tab)", new Color(1f, 0.8f, 0.35f)))
                Close();

            // Tooltip for whatever is under the mouse, and notes.
            var hovered = ModuleAt(state, Event.current.mousePosition, x0, top, panelW, panelH, invY);
            float tipY = invY + Cell + 16f;
            if (hovered != null)
            {
                var accent = Cards.Accent(hovered.Op).ToUnity();
                Shadowed(new Rect(x0, tipY, width, 22f), $"{hovered.Name}   ·   {Cards.Category(hovered)}", LabelBold);
                Text(new Rect(x0, tipY + 22f, width, 22f), hovered.Text, Label, accent);
            }
            if (_noteTime > Time.unscaledTime && _note != null)
                Shadowed(new Rect(0, tipY + 50f, Screen.width, 22f), _note, Centered);

            // The module being dragged follows the mouse.
            if (_dragging && Get(state, _held) is Module dragged)
                Chip(new Rect(Event.current.mousePosition.x - Cell / 2f, Event.current.mousePosition.y - Cell / 2f, Cell, Cell), dragged, true, false);

            if (Event.current.type == EventType.MouseUp && Event.current.button == 0 && _dragging)
            {
                _dragging = false;
                _held = default; // dropped outside any cell: cancel
            }
        }

        private void WeaponPanel(GameState state, Weapon weapon, int index, Rect rect)
        {
            var accent = Cards.Accent(weapon.Kind).ToUnity();
            Rounded(rect, new Color(0.09f, 0.08f, 0.075f, 0.95f));
            Rounded(new Rect(rect.x, rect.y, rect.width, 56f), new Color(accent.r * 0.3f, accent.g * 0.3f, accent.b * 0.3f, 0.8f));
            Outline(rect, new Color(accent.r, accent.g, accent.b, 0.35f));

            var icon = Icon(Cards.Icon(weapon.Kind));
            Glow(new Rect(rect.x - 6f, rect.y - 8f, 72f, 72f), new Color(accent.r, accent.g, accent.b, 0.4f));
            if (icon != null)
                GUI.DrawTexture(new Rect(rect.x + 8f, rect.y + 4f, 48f, 48f), icon, ScaleMode.ScaleToFit);
            Text(new Rect(rect.x + 64f, rect.y + 6f, rect.width - 70f, 24f), WeaponBase.Name(weapon.Kind).ToUpperInvariant(), LabelBold, accent);
            Text(new Rect(rect.x + 64f, rect.y + 28f, rect.width - 70f, 22f), WeaponBase.Blurb(weapon.Kind), Small, new Color(0.85f, 0.82f, 0.78f));

            var s = weapon.Stats;
            Text(new Rect(rect.x + 14f, rect.y + 64f, rect.width - 28f, 20f), StatsLine(weapon.Kind, s), Label, Color.white);
            Text(new Rect(rect.x + 14f, rect.y + 86f, rect.width - 28f, 20f), $"{s.Geometry.ToString().ToUpperInvariant()}:  {WeaponBase.Formula(s)}", Label,
                Cards.Accent(ModuleOp.Geometry).ToUnity());

            for (int k = 0; k < Weapon.SlotCount; k++)
            {
                var cell = new Rect(rect.x + 14f + k * (Cell + 10f), rect.yMax - Cell - 14f, Cell, Cell);
                Chip(cell, weapon.Slots[k], IsHeld(Place.Slot, k, index), false);
                Hit(cell, new Spot { Place = Place.Slot, Index = k, Weapon = index }, weapon.Slots[k], state);
            }
        }

        private static string StatsLine(WeaponKind kind, in WeaponStats s) => kind switch
        {
            WeaponKind.ArcaneWand => $"DMG {s.Damage:0.#}   RATE {s.FireRate:0.##}/s   ARCS {s.Particles}   CHAIN {s.Chain:0}   REACH x{s.Area:0.##}   RANGE {s.Range:0.#}",
            WeaponKind.Minigun => $"DMG {s.Damage:0.#}   RATE {s.FireRate:0.#}/s   STREAMS {s.Particles}   PIERCE {s.Pierce:0}   RANGE {s.Range:0.#}",
            WeaponKind.TdMortar => $"DMG {s.Damage:0.#}   RATE {s.FireRate:0.##}/s   RINGS {s.Particles}   RADIUS {s.Range * s.Area:0.#}",
            _ => $"DMG {s.Damage:0.#}/s   RATE {s.FireRate:0.##}/s   ORBS {s.Particles}   AREA x{s.Area:0.##}   RANGE {s.Range:0.#}",
        };

        /// <summary>A module chip: dark tile in its kind's colour with the arithmetic glyph, or an empty socket.</summary>
        private static void Chip(Rect cell, Module module, bool held, bool pulse)
        {
            if (module == null)
            {
                Rounded(cell, new Color(1f, 1f, 1f, 0.04f));
                Outline(cell, new Color(1f, 1f, 1f, 0.16f));
                return;
            }
            var accent = Cards.Accent(module.Op).ToUnity();
            if (pulse)
                Glow(new Rect(cell.x - 18f, cell.y - 18f, cell.width + 36f, cell.height + 36f),
                    new Color(accent.r, accent.g, accent.b, 0.4f + 0.3f * Mathf.Sin(Time.unscaledTime * 6f)));
            Rounded(cell, new Color(accent.r * 0.22f, accent.g * 0.22f, accent.b * 0.22f, held ? 0.5f : 0.95f));
            Outline(cell, new Color(accent.r, accent.g, accent.b, held ? 0.4f : 0.9f));
            Text(new Rect(cell.x, cell.y + 4f, cell.width, cell.height * 0.6f), Cards.Glyph(module), GlyphSmall, accent);
            string label = module.Op == ModuleOp.Geometry ? module.Short : Modules.Abbrev(module.Stat);
            Text(new Rect(cell.x, cell.yMax - 24f, cell.width, 20f), label, Badge, new Color(0.9f, 0.88f, 0.84f));
            if (module.Only is WeaponKind only && Icon(Cards.Icon(only)) is Texture2D icon)
                GUI.DrawTexture(new Rect(cell.xMax - 20f, cell.y + 2f, 18f, 18f), icon, ScaleMode.ScaleToFit);
        }

        /// <summary>Mouse handling for one cell: press, drag, drop, click-to-place, right-click to unslot.</summary>
        private void Hit(Rect cell, Spot spot, Module module, GameState state)
        {
            var e = Event.current;
            if (!cell.Contains(e.mousePosition))
                return;

            if (e.type == EventType.MouseDown && e.button == 1 && spot.Place == Place.Slot && module != null)
            {
                if (!CoreModules.Unslot(state, state.Weapons[spot.Weapon].Kind, spot.Index))
                    Note("The inventory is full.");
                e.Use();
                return;
            }
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _pressed = spot;
                _pressAt = e.mousePosition;
                if (_held.Place == Place.None && module != null)
                {
                    _held = spot;
                    _justPicked = true;
                }
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _held.Place != Place.None && !_dragging &&
                     (e.mousePosition - _pressAt).sqrMagnitude > 36f)
            {
                _dragging = true;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                if (_held.Place != Place.None && !Same(spot, _held))
                {
                    Drop(state, _held, spot);
                    _held = default;
                }
                else if (Same(spot, _held) && (_dragging || !_justPicked))
                {
                    _held = default; // dropped back on itself, or clicked again: put it down
                }
                // A plain click on a module leaves it held: the next click places it.
                _justPicked = false;
                _dragging = false;
                _pressed = default;
                e.Use();
            }
        }

        private void Drop(GameState state, Spot from, Spot to)
        {
            var module = Get(state, from);
            if (module == null)
                return;
            _highlight = -1;
            switch (to.Place)
            {
                case Place.Slot:
                    var kind = state.Weapons[to.Weapon].Kind;
                    bool ok = from.Place == Place.Inventory
                        ? CoreModules.Slot(state, from.Index, kind, to.Index)
                        : CoreModules.Move(state, state.Weapons[from.Weapon].Kind, from.Index, kind, to.Index);
                    if (!ok)
                        Note($"{module.Name} only fits the {WeaponBase.Name(module.Only ?? kind)}.");
                    break;
                case Place.Inventory when from.Place == Place.Slot:
                    if (!CoreModules.Unslot(state, state.Weapons[from.Weapon].Kind, from.Index))
                        Note("The inventory is full.");
                    break;
                case Place.Bin:
                    if (from.Place == Place.Inventory)
                    {
                        state.Inventory.RemoveAt(from.Index);
                    }
                    else
                    {
                        var weapon = state.Weapons[from.Weapon];
                        weapon.Slots[from.Index] = null;
                        weapon.Recompute();
                    }
                    Note($"Threw away {module.Name}.");
                    break;
            }
        }

        private static Module Get(GameState state, Spot spot) => spot.Place switch
        {
            Place.Inventory => spot.Index < state.Inventory.Count ? state.Inventory[spot.Index] : null,
            Place.Slot => state.Weapons[spot.Weapon].Slots[spot.Index],
            _ => null,
        };

        private bool IsHeld(Place place, int index, int weapon) =>
            _held.Place == place && _held.Index == index && (place != Place.Slot || _held.Weapon == weapon);

        private static bool Same(Spot a, Spot b) =>
            a.Place == b.Place && a.Index == b.Index && (a.Place != Place.Slot || a.Weapon == b.Weapon);

        private static Rect InventoryCell(float x0, float y, int i) => new Rect(x0 + i * (Cell + 10f), y, Cell, Cell);

        private static Module ModuleAt(GameState state, Vector2 mouse, float x0, float top, float panelW, float panelH, float invY)
        {
            for (int i = 0; i < state.Inventory.Count; i++)
            {
                if (InventoryCell(x0, invY, i).Contains(mouse))
                    return state.Inventory[i];
            }
            for (int w = 0; w < state.Weapons.Count; w++)
            {
                var rect = new Rect(x0 + (w % 2) * (panelW + 20f), top + (w / 2) * (panelH + 20f), panelW, panelH);
                for (int k = 0; k < Weapon.SlotCount; k++)
                {
                    var cell = new Rect(rect.x + 14f + k * (Cell + 10f), rect.yMax - Cell - 14f, Cell, Cell);
                    if (cell.Contains(mouse))
                        return state.Weapons[w].Slots[k];
                }
            }
            return null;
        }

        private static bool Button(Rect rect, string text, Color accent)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            Rounded(rect, new Color(accent.r * 0.25f, accent.g * 0.25f, accent.b * 0.25f, hover ? 1f : 0.85f));
            Outline(rect, new Color(accent.r, accent.g, accent.b, hover ? 1f : 0.6f));
            Text(rect, text, Badge, accent);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        private void Note(string text)
        {
            _note = text;
            _noteTime = Time.unscaledTime + 2.5f;
        }
    }
}
