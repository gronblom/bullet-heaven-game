using System;
using System.Collections.Generic;
using BulletHeaven.Rendering;
using BulletHeaven.Systems;
using Godot;

namespace BulletHeaven;

/// <summary>
/// Full-screen card menus over the game, built in code: weapon loadout, level-up modifiers and the
/// pause menu. While one is open Main stops the simulation. Cards come from the shared core
/// (<see cref="MenuCard"/>: icon, accent colour, category, pick pips) and are buttons, so mouse,
/// keyboard (arrows + Enter, or 1-4) and gamepad (d-pad + A) all work.
/// </summary>
public sealed partial class Overlay : CanvasLayer
{
    private static readonly Color Panel = new(0.09f, 0.08f, 0.075f, 0.96f);

    private readonly ColorRect _dim = new() { Color = new Color(0.02f, 0.015f, 0.01f, 0.6f) };
    private readonly Label _title = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Label _footer = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly HBoxContainer _cards = new() { Alignment = BoxContainer.AlignmentMode.Center };
    private readonly List<Button> _buttons = new();
    private Action<int> _onPick;
    private double _openedAt;

    public bool IsOpen => _dim.Visible;

    public Overlay()
    {
        Layer = 10;
        _dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_dim);

        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        column.AddThemeConstantOverride("separation", 34);
        _title.AddThemeFontSizeOverride("font_size", 40);
        _title.AddThemeConstantOverride("outline_size", 10);
        _title.AddThemeColorOverride("font_outline_color", Colors.Black);
        _footer.AddThemeFontSizeOverride("font_size", 16);
        _footer.Modulate = new Color(1f, 1f, 1f, 0.7f);
        _cards.AddThemeConstantOverride("separation", 26);
        column.AddChild(_title);
        column.AddChild(_cards);
        column.AddChild(_footer);
        _dim.AddChild(column);
        _dim.Visible = false;
    }

    /// <summary>Shows the cards; calls <paramref name="onPick"/> with the index picked.</summary>
    public void ShowCards(string title, string footer, List<MenuCard> cards, Action<int> onPick)
    {
        Clear();
        _title.Text = title;
        _footer.Text = footer;
        _onPick = onPick;
        _openedAt = Time.GetTicksMsec() / 1000.0;
        for (int i = 0; i < cards.Count; i++)
        {
            int index = i;
            var button = Card(cards[i], i);
            button.Pressed += () => Pick(index);
            _cards.AddChild(button);
            _buttons.Add(button);

            // Slide up and fade in, one after another.
            button.Modulate = new Color(1f, 1f, 1f, 0f);
            var tween = button.CreateTween().SetPauseMode(Tween.TweenPauseMode.Process);
            tween.TweenInterval(i * 0.07);
            tween.TweenProperty(button, "modulate:a", 1f, 0.3).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
        _dim.Visible = true;
        if (_buttons.Count > 0)
            _buttons[0].CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close()
    {
        Clear();
        _dim.Visible = false;
    }

    public override void _Process(double delta)
    {
        // A and Space also jump: ignore keys for a moment so a jump doesn't pick a card.
        if (!IsOpen || _onPick == null || Time.GetTicksMsec() / 1000.0 - _openedAt < 0.3)
            return;
        for (int i = 0; i < GameInput.Choices.Length && i < _buttons.Count; i++)
        {
            if (Input.IsActionJustPressed(GameInput.Choices[i]))
            {
                Pick(i);
                return;
            }
        }
    }

    private void Pick(int index)
    {
        if (Time.GetTicksMsec() / 1000.0 - _openedAt < 0.3)
            return;
        var onPick = _onPick;
        Close();
        onPick?.Invoke(index); // may open the next menu straight away
    }

    private void Clear()
    {
        foreach (var button in _buttons)
            button.QueueFree();
        _buttons.Clear();
        _onPick = null;
    }

    /// <summary>One card: a focusable button holding a number badge, category, icon, title, text and pick pips.</summary>
    private static Button Card(MenuCard card, int index)
    {
        var accent = card.Accent.ToGodot();
        var button = new Button { CustomMinimumSize = new Vector2(250f, 350f), FocusMode = Control.FocusModeEnum.All };
        button.AddThemeStyleboxOverride("normal", Box(Panel, new Color(1f, 1f, 1f, 0.12f), 2, 0f));
        button.AddThemeStyleboxOverride("hover", Box(Panel.Lightened(0.06f), accent * new Color(1f, 1f, 1f, 0.7f), 3, 0.25f, accent));
        button.AddThemeStyleboxOverride("focus", Box(new Color(0, 0, 0, 0), accent, 4, 0.45f, accent));
        button.AddThemeStyleboxOverride("pressed", Box(Panel.Lightened(0.12f), accent, 4, 0.5f, accent));

        var content = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Begin };
        content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        content.OffsetLeft = 14;
        content.OffsetRight = -14;
        content.OffsetTop = 12;
        content.OffsetBottom = -14;
        content.AddThemeConstantOverride("separation", 8);
        button.AddChild(content);

        // Tinted top half behind the icon.
        var tint = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, ShowBehindParent = true };
        tint.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        tint.Size = new Vector2(250f, 182f);
        tint.AddThemeStyleboxOverride("panel", Box(new Color(accent.R * 0.35f, accent.G * 0.35f, accent.B * 0.35f, 0.6f), new Color(0, 0, 0, 0), 0, 0f));
        button.AddChild(tint);
        button.MoveChild(tint, 0);

        var header = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        header.AddThemeConstantOverride("separation", 8);
        var badge = new Label
        {
            Text = (index + 1).ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(30f, 30f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        badge.AddThemeStyleboxOverride("normal", Box(accent, new Color(0, 0, 0, 0), 0, 0f, radius: 8));
        badge.AddThemeColorOverride("font_color", new Color(0.08f, 0.06f, 0.05f));
        badge.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(badge);
        header.AddChild(TextLabel(card.Category, 13, accent, HorizontalAlignment.Left));
        content.AddChild(header);

        var icon = new TextureRect
        {
            Texture = ModelLibrary.LoadIcon(card.Icon),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(0f, 128f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        content.AddChild(icon);
        var title = TextLabel(card.Title, 22, Colors.White, HorizontalAlignment.Center);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(title);
        var text = TextLabel(card.Text, 15, new Color(0.85f, 0.82f, 0.78f), HorizontalAlignment.Center);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        content.AddChild(text);

        if (card.MaxPicks > 0)
        {
            if (card.Picks == 0)
                content.AddChild(TextLabel("NEW", 13, new Color(1f, 0.85f, 0.4f), HorizontalAlignment.Center));
            var pips = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            pips.AddThemeConstantOverride("separation", 6);
            for (int k = 0; k < card.MaxPicks; k++)
            {
                var pip = new Panel { CustomMinimumSize = new Vector2(12f, 12f), MouseFilter = Control.MouseFilterEnum.Ignore };
                var fill = k < card.Picks ? accent : k == card.Picks ? accent * new Color(1f, 1f, 1f, 0.5f) : new Color(0, 0, 0, 0);
                pip.AddThemeStyleboxOverride("panel", Box(fill, new Color(1f, 1f, 1f, 0.35f), k > card.Picks ? 1 : 0, 0f, radius: 6));
                pips.AddChild(pip);
            }
            content.AddChild(pips);
        }
        return button;
    }

    private static Label TextLabel(string text, int size, Color color, HorizontalAlignment alignment)
    {
        var label = new Label { Text = text, HorizontalAlignment = alignment, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static StyleBoxFlat Box(Color fill, Color border, int borderWidth, float glow, Color? glowColor = null, int radius = 14)
    {
        var box = new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = border,
            ShadowColor = glow > 0f && glowColor is { } g ? new Color(g.R, g.G, g.B, glow) : new Color(0f, 0f, 0f, 0.45f),
            ShadowSize = glow > 0f ? 18 : 8,
            ShadowOffset = glow > 0f ? Vector2.Zero : new Vector2(4f, 8f),
            AntiAliasing = true,
        };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(radius);
        if (glow <= 0f && borderWidth == 0 && fill.A < 0.99f)
            box.ShadowSize = 0;
        return box;
    }
}
