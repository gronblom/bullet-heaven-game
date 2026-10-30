using BulletHeaven.Ecs;
using BulletHeaven.Numerics;

namespace BulletHeaven.Levels;

/// <summary>
/// Level 3, the secret one: the painted windowsill outside the kitchen window, reached by gliding
/// there with the parachute. A long, narrow arena between the window and a drop to the garden, with
/// flower pots and a leaky watering can. Home of the Queen Ant.
/// </summary>
public sealed class Windowsill : Level
{
    public override string Name => "The Windowsill";
    public override Rect2 Bounds => new(-60f, -22f, 120f, 44f);
    public override Vector2 PlayerStart => new(-40f, 10f);
    public override Vector2? BossSpawn => new Vector2(35f, -8f);

    protected override void BuildLevel()
    {
        Floor("floor_sill", new Color(0.78f, 0.76f, 0.7f), toneVariation: 0.15f, sheenVariation: 0.1f);
        Window();

        // Pots to dodge round (too tall to climb), and a watering can that has leaked over the sill.
        Round("pot_cactus", new Vector2(-18f, -12f), 0f, 4.1f, 13f, new Color(0.6f, 0.27f, 0.12f), sphere: false);
        Round("pot_basil", new Vector2(22f, 12f), 0f, 4.1f, 12f, new Color(0.6f, 0.27f, 0.12f), sphere: false);
        Round("watering_can", new Vector2(-2f, 13f), 0f, 3f, 9.5f, new Color(0.6f, 0.62f, 0.62f), sphere: false);
        Spill(new Vector2(5f, 10f), 0f, 4.5f, LiquidKind.Water);
        Spill(new Vector2(12f, 3f), 0f, 3f, LiquidKind.Water);
        Spill(new Vector2(-35f, -10f), 0f, 3f, LiquidKind.Honey); // bees were here

        // A seed-tray step and a flowerpot saucer to get the high ground for a while.
        var trayTints = new[] { new Color(0.25f, 0.25f, 0.27f) };
        Stack(new Vector2(40f, 14f), new Vector2(8f, 5f), 1.6f, "toy_block", trayTints, 0);
        Stack(new Vector2(48f, 14f), new Vector2(6f, 5f), 3.2f, "toy_block", trayTints, 0);
    }

    private void Window()
    {
        var b = Bounds;
        var paint = new Look(new Color(0.82f, 0.8f, 0.75f), 0.45f);

        // The window behind (north): the frame, glass you can see the kitchen glow through.
        Model("window_frame", new Vector3(0f, 0f, b.Position.Y - 1f), null, Shape.Box, new Vector3(120f, 60f, 2f), paint);
        Primitive(Shape.Box, new Vector3(0f, 1f, b.Position.Y - 1.5f), new Vector3(118f, 58f, 0.2f),
            new Look(new Color(1f, 0.85f, 0.6f), Glow: 0.35f));

        // The drop to the garden (south): a lip, then green far below.
        Primitive(Shape.Box, new Vector3(0f, 0f, b.End.Y + 0.75f), new Vector3(b.Size.X + 4f, 1f, 1.5f), paint);
        Primitive(Shape.Plane, new Vector3(0f, -60f, b.End.Y + 180f), new Vector3(600f, 0f, 400f), new Look(new Color(0.18f, 0.35f, 0.1f), 0.95f));

        // Brick wall either side.
        var brick = new Look(new Color(0.45f, 0.2f, 0.12f), 0.9f);
        Primitive(Shape.Box, new Vector3(b.Position.X - 2f, -10f, 0f), new Vector3(4f, 70f, b.Size.Y + 6f), brick);
        Primitive(Shape.Box, new Vector3(b.End.X + 2f, -10f, 0f), new Vector3(4f, 70f, b.Size.Y + 6f), brick);
    }

    // Outdoors in the late afternoon: bright sky, warm low sun, a little haze.
    public override Atmosphere Atmosphere => new()
    {
        SkyTop = new Color(0.35f, 0.55f, 0.85f),
        SkyHorizon = new Color(0.85f, 0.75f, 0.6f),
        GroundHorizon = new Color(0.5f, 0.55f, 0.35f),
        GroundBottom = new Color(0.15f, 0.25f, 0.1f),
        AmbientEnergy = 0.6f,
        FogDensity = 0.002f,
        FogColor = new Color(0.8f, 0.8f, 0.85f),
        SunEnergy = 1.8f,
        SunColor = new Color(1f, 0.85f, 0.65f),
        SunRotationDegrees = new Vector3(-40f, -30f, 0f),
    };
}
