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
    public const string Handbrake = "handbrake";
    public const string Jump = "jump";
    public const string Restart = "restart";

    public static void Register()
    {
        Add(Accelerate, Key(Godot.Key.W), Key(Godot.Key.Up), Button(JoyButton.A), Axis(JoyAxis.TriggerRight, 1f));
        Add(Brake, Key(Godot.Key.S), Key(Godot.Key.Down), Button(JoyButton.X), Axis(JoyAxis.TriggerLeft, 1f));
        Add(SteerLeft, Key(Godot.Key.A), Key(Godot.Key.Left), Axis(JoyAxis.LeftX, -1f));
        Add(SteerRight, Key(Godot.Key.D), Key(Godot.Key.Right), Axis(JoyAxis.LeftX, 1f));
        Add(Handbrake, Key(Godot.Key.Shift), Key(Godot.Key.Ctrl), Button(JoyButton.B), Button(JoyButton.RightShoulder));
        Add(Jump, Key(Godot.Key.Space), Button(JoyButton.Y));
        Add(Restart, Key(Godot.Key.R), Key(Godot.Key.Enter), Button(JoyButton.Start));
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
