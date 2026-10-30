using System;
using System.Collections.Generic;
using Godot;

namespace BulletHeaven;

/// <summary>
/// Full-screen menus over the game, built in code: a row of cards to pick one from (weapon
/// loadout, level-up modifiers) and the pause menu. While one is open Main stops the simulation.
/// Cards are buttons, so mouse, keyboard (arrows + Enter, or 1-3) and gamepad (d-pad + A) all work.
/// </summary>
public sealed partial class Overlay : CanvasLayer
{
    private readonly ColorRect _dim = new() { Color = new Color(0f, 0f, 0f, 0.55f) };
    private readonly Label _title = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Label _footer = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly HBoxContainer _cards = new() { Alignment = BoxContainer.AlignmentMode.Center };
    private readonly List<Button> _buttons = new();
    private Action<int> _onPick;

    public bool IsOpen => _dim.Visible;

    public Overlay()
    {
        Layer = 10;
        _dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_dim);

        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        column.AddThemeConstantOverride("separation", 28);
        _title.AddThemeFontSizeOverride("font_size", 40);
        _footer.AddThemeFontSizeOverride("font_size", 18);
        _footer.Modulate = new Color(1f, 1f, 1f, 0.7f);
        _cards.AddThemeConstantOverride("separation", 24);
        column.AddChild(_title);
        column.AddChild(_cards);
        column.AddChild(_footer);
        _dim.AddChild(column);
        _dim.Visible = false;
    }

    /// <summary>Shows cards (title + description each); calls <paramref name="onPick"/> with the index picked.</summary>
    public void ShowCards(string title, string footer, IReadOnlyList<(string Title, string Text)> cards, Action<int> onPick)
    {
        Clear();
        _title.Text = title;
        _footer.Text = footer;
        _onPick = onPick;
        for (int i = 0; i < cards.Count; i++)
        {
            int index = i;
            var button = new Button
            {
                Text = $"{i + 1}.  {cards[i].Title}\n\n{cards[i].Text}",
                CustomMinimumSize = new Vector2(260f, 150f),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            button.AddThemeFontSizeOverride("font_size", 20);
            button.Pressed += () => Pick(index);
            _cards.AddChild(button);
            _buttons.Add(button);
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
        if (!IsOpen || _onPick == null)
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
}
