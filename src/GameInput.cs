using Godot;

namespace BulletHeaven;

/// <summary>Registers input actions in code so the project needs no hand-edited input map.
/// They can be rebound later in Project Settings > Input Map.</summary>
public static class GameInput
{
    public const string Accelerate = "accelerate";
    public const string Brake = "brake";
    public const string SteerLeft = "steer_left";
    public const string SteerRight = "steer_right";
    public const string MoveUp = "move_up";
    public const string MoveDown = "move_down";
    public const string Handbrake = "handbrake";
    public const string Jump = "jump";
    public const string RunnerJump = "runner_jump"; // A is free when not driving
    public const string Restart = "restart";
    public const string Pause = "pause";
    /// <summary>1-3: pick a card in a menu.</summary>
    public static readonly string[] Choices = { "choice_1", "choice_2", "choice_3" };
    /// <summary>Shift + 1-3: add the minigun, wand or mortar (development).</summary>
    public static readonly string[] DebugWeapons = { "debug_weapon_1", "debug_weapon_2", "debug_weapon_3" };
    /// <summary>F1-F3: jump straight to a level (development).</summary>
    public static readonly string[] Levels = { "level_1", "level_2", "level_3" };

    public static void Register()
    {
        Add(Accelerate, Key(Godot.Key.W), Key(Godot.Key.Up), Button(JoyButton.A), Axis(JoyAxis.TriggerRight, 1f));
        Add(Brake, Key(Godot.Key.S), Key(Godot.Key.Down), Button(JoyButton.X), Axis(JoyAxis.TriggerLeft, 1f));
        Add(SteerLeft, Key(Godot.Key.A), Key(Godot.Key.Left), Axis(JoyAxis.LeftX, -1f));
        Add(SteerRight, Key(Godot.Key.D), Key(Godot.Key.Right), Axis(JoyAxis.LeftX, 1f));
        Add(MoveUp, Key(Godot.Key.W), Key(Godot.Key.Up), Axis(JoyAxis.LeftY, -1f));
        Add(MoveDown, Key(Godot.Key.S), Key(Godot.Key.Down), Axis(JoyAxis.LeftY, 1f));
        Add(Handbrake, Key(Godot.Key.Shift), Key(Godot.Key.Ctrl), Button(JoyButton.B), Button(JoyButton.RightShoulder));
        Add(Jump, Key(Godot.Key.Space), Button(JoyButton.Y));
        Add(RunnerJump, Key(Godot.Key.Space), Button(JoyButton.A), Button(JoyButton.Y));
        Add(Restart, Key(Godot.Key.R));
        Add(Pause, Key(Godot.Key.Escape), Key(Godot.Key.P), Button(JoyButton.Start));
        for (int i = 0; i < Choices.Length; i++)
            Add(Choices[i], Key(Godot.Key.Key1 + i), Key(Godot.Key.Kp1 + i));
        for (int i = 0; i < DebugWeapons.Length; i++)
            Add(DebugWeapons[i], new InputEventKey { PhysicalKeycode = Godot.Key.Key1 + i, ShiftPressed = true });
        for (int i = 0; i < Levels.Length; i++)
            Add(Levels[i], Key(Godot.Key.F1 + i));
    }

    private static void Add(string action, params InputEvent[] events)
    {
        if (InputMap.HasAction(action))
            return;

        InputMap.AddAction(action, 0.2f);
        foreach (var e in events)
            InputMap.ActionAddEvent(action, e);
    }

    private static InputEventKey Key(Key key) => new() { PhysicalKeycode = key };
    private static InputEventJoypadButton Button(JoyButton button) => new() { ButtonIndex = button };
    private static InputEventJoypadMotion Axis(JoyAxis axis, float value) => new() { Axis = axis, AxisValue = value };
}
