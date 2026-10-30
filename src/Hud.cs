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
    private readonly VBoxContainer _bossPanel = new();
    private readonly ProgressBar _bossBar = Bar(new Color(0.75f, 0.1f, 0.35f), 520f);

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

        // Boss bar, top centre.
        _bossPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _bossPanel.Position = new Vector2(-260f, 16f);
        var bossName = new Label { Text = "THE QUEEN ANT", HorizontalAlignment = HorizontalAlignment.Center };
        bossName.AddThemeConstantOverride("outline_size", 6);
        bossName.AddThemeColorOverride("font_outline_color", Colors.Black);
        _bossPanel.AddChild(bossName);
        _bossPanel.AddChild(_bossBar);
        _bossPanel.Visible = false;
        AddChild(_bossPanel);

        _stats.AddThemeConstantOverride("outline_size", 6);
        _stats.AddThemeColorOverride("font_outline_color", Colors.Black);
    }

    public void Sync(GameState state, string level, in Health health, int enemies, float speed,
        List<WeaponSlot> weapons, float dt)
    {
        _health.MaxValue = health.Max;
        _health.Value = health.Current;
        _xp.MaxValue = state.XpToNext;
        _xp.Value = state.Xp;

        int seconds = (int)state.Time;
        _stats.Text = $"{level}  (F1-F3, Esc pause)\nLevel {state.Level}   Kills {state.Kills}\n" +
                      $"Time {seconds / 60}:{seconds % 60:00}   Speed {speed:0}\n" +
                      $"Enemies {enemies}   FPS {Engine.GetFramesPerSecond()}" +
                      (state.HasParachute ? "\nParachute: hold jump while falling" : "") + WeaponList(state, weapons);

        _bossPanel.Visible = state.BossSpawned && !state.BossDefeated;
        _bossBar.MaxValue = state.BossMaxHealth;
        _bossBar.Value = state.BossHealth;

        state.MessageTimer -= dt;
        _message.Text = state.MessageTimer > 0f ? state.Message : "";
    }

    /// <summary>Each weapon with the modifiers picked for it.</summary>
    private static string WeaponList(GameState state, List<WeaponSlot> weapons)
    {
        var text = new StringBuilder("\n");
        foreach (var weapon in weapons)
        {
            text.Append($"\n{WeaponStats.Name(weapon.Kind)}");
            foreach (var modifier in Modifiers.All)
            {
                int picks = Modifiers.Picks(state, modifier);
                if (picks > 0 && modifier.Weapon == weapon.Kind)
                    text.Append(picks > 1 ? $"\n   {modifier.Name} x{picks}" : $"\n   {modifier.Name}");
            }
        }
        return text.ToString();
    }

    private static ProgressBar Bar(Color color, float width = 280f)
    {
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(width, 16f) };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = color });
        return bar;
    }
}
