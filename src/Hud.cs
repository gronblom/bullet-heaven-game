using System.Collections.Generic;
using System.Text;
using BulletHeaven.Ecs;
using BulletHeaven.Systems;
using Godot;

namespace BulletHeaven;

/// <summary>Minimal debug-style HUD built in code: health, XP, stats and a centre message.</summary>
public sealed partial class Hud : CanvasLayer
{
    private readonly ProgressBar _health = Bar(new Color(0.85f, 0.2f, 0.2f));
    private readonly ProgressBar _xp = Bar(new Color(0.3f, 0.85f, 0.4f));
    private readonly Label _stats = new();
    private readonly Label _message = new();

    public Hud()
    {
        var panel = new VBoxContainer { Position = new Vector2(16f, 16f), CustomMinimumSize = new Vector2(280f, 0f) };
        panel.AddChild(_health);
        panel.AddChild(_xp);
        panel.AddChild(_stats);
        AddChild(panel);

        _message.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _message.HorizontalAlignment = HorizontalAlignment.Center;
        _message.VerticalAlignment = VerticalAlignment.Center;
        _message.AddThemeFontSizeOverride("font_size", 40);
        _message.AddThemeConstantOverride("outline_size", 8);
        _message.AddThemeColorOverride("font_outline_color", Colors.Black);
        AddChild(_message);

        _stats.AddThemeConstantOverride("outline_size", 6);
        _stats.AddThemeColorOverride("font_outline_color", Colors.Black);
    }

    public void Sync(GameState state, string level, in Health health, int enemies, float speed,
        List<(WeaponKind Kind, int Level, float Spin)> weapons, float dt)
    {
        _health.MaxValue = health.Max;
        _health.Value = health.Current;
        _xp.MaxValue = state.XpToNext;
        _xp.Value = state.Xp;

        int seconds = (int)state.Time;
        _stats.Text = $"{level}  (F1-F4)\nLevel {state.Level}   Kills {state.Kills}\n" +
                      $"Time {seconds / 60}:{seconds % 60:00}   Speed {speed:0}\n" +
                      $"Enemies {enemies}   FPS {Engine.GetFramesPerSecond()}" + WeaponList(weapons);

        state.MessageTimer -= dt;
        _message.Text = state.MessageTimer > 0f ? state.Message : "";
    }

    private static string WeaponList(List<(WeaponKind Kind, int Level, float Spin)> weapons)
    {
        var text = new StringBuilder("\n");
        foreach (var (kind, level, _) in weapons)
            text.Append($"\n{WeaponStats.Name(kind)}  Lv {level}");
        return text.ToString();
    }

    private static ProgressBar Bar(Color color)
    {
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(280f, 16f) };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = color });
        return bar;
    }
}
