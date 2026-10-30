using BulletHeaven.Rendering;
using Godot;

namespace BulletHeaven.Levels;

/// <summary>
/// Level 4. A kid's bedroom in 1986, at night: Memphis-pattern carpet, neon tubes, a lava lamp,
/// a CRT TV showing colour bars, a boombox, cassettes everywhere and a giant Rubik's cube.
///
/// The way up to the bed: cassettes 1.6 -> more cassettes 3.2 -> Rubik's cube 4.8 -> boombox 6.6
/// -> bed 8.2, then across to the nightstand (9.6) with the lava lamp.
/// </summary>
public sealed class Bedroom80s : Level
{
    private const float BedTop = 8.2f;
    private static readonly Vector2 Bed = new(35f, -32f);

    public override string Name => "80s Bedroom";
    public override Rect2 Bounds => new(-70f, -50f, 140f, 100f);
    public override Vector2 PlayerStart => new(0f, 25f);

    protected override void BuildLevel()
    {
        Floor("memphis", new Color(0.08f, 0.07f, 0.12f), toneVariation: 0.15f, sheenVariation: 0.05f);
        if (Visual)
            RoomWalls(Materials.Plastic(new Color(0.32f, 0.24f, 0.45f), 0.8f));
        NeonOnTheWalls();

        // The bed against the back wall: mattress and a tall headboard.
        BoxObstacle(Bed, new Vector2(20f, 36f), BedTop);
        BoxObstacle(Bed + new Vector2(0f, -17.25f), new Vector2(20f, 1.5f), 15.25f);
        if (Visual)
        {
            var materials = Model("bed", new Vector3(Bed.X, 0f, Bed.Y), new Vector3(20f, 15.25f, 36f));
            if (materials == null)
            {
                var duvet = Materials.Plastic(new Color(0.25f, 0.75f, 0.78f), 0.8f);
                Add(new BoxMesh { Size = new Vector3(20f, BedTop, 36f) }, new Vector3(Bed.X, BedTop / 2f, Bed.Y), duvet);
                materials = new BaseMaterial3D[] { duvet };
            }
            Fader?.Register(new Rect2(Bed - new Vector2(10f, 18f), new Vector2(20f, 36f)), BedTop, materials);
        }

        // The staircase of stuff up to it.
        var white = new[] { Colors.White, new Color(0.9f, 0.9f, 1f) };
        Stack(new Vector2(35f, 4.5f), new Vector2(6f, 4f), 1.6f, "cassette", white, 0, layer: 0.8f);
        Stack(new Vector2(35f, -0.5f), new Vector2(6f, 4f), 3.2f, "cassette", white, 1, layer: 0.8f);
        Block(new Vector2(35f, -5.5f), new Vector2(5f, 5f), 0f, 4.8f, new Color(0.9f, 0.2f, 0.2f), "rubiks_cube");
        BoxObstacle(new Vector2(35f, -11f), new Vector2(18f, 5f), 6.6f);
        Decor("boombox", new Vector3(35f, 0f, -11f), new Vector3(18f, 8.45f, 5f));

        // Nightstand with a lava lamp, a short hop from the bed.
        Block(new Vector2(52f, -44f), new Vector2(8f, 8f), 0f, 9.6f, new Color(0.9f, 0.88f, 0.92f));
        Round("lava_lamp", new Vector2(52f, -44f), 9.6f, 1.6f, 10.8f, new Color(1f, 0.4f, 0.6f), sphere: false);

        // The TV on a low stand.
        Block(new Vector2(-30f, -42f), new Vector2(16f, 10f), 0f, 5f, new Color(0.2f, 0.12f, 0.08f));
        Block(new Vector2(-30f, -42f), new Vector2(11f, 9f), 5f, 9f, new Color(0.3f, 0.2f, 0.1f), "crt_tv");

        // Stray cassettes on the floor, and the cube's little brother.
        foreach (var at in new[] { new Vector2(-12f, 18f), new Vector2(-42f, 6f), new Vector2(14f, 32f), new Vector2(-55f, 32f) })
            Block(at, new Vector2(6f, 4f), 0f, 0.8f, new Color(0.1f, 0.1f, 0.12f), "cassette");
        Block(new Vector2(-50f, -12f), new Vector2(5f, 5f), 0f, 4.8f, new Color(0.9f, 0.2f, 0.2f), "rubiks_cube");
    }

    private void NeonOnTheWalls()
    {
        var b = Bounds;
        float back = b.Position.Y - 0.2f;
        var pink = new Color(1f, 0.2f, 0.65f);
        var cyan = new Color(0.2f, 0.9f, 1f);
        // A pink zigzag and a cyan stripe along the back wall, a cyan stripe down the west wall.
        for (int i = 0; i < 8; i++)
        {
            float x0 = -60f + i * 10f, x1 = x0 + 10f;
            Tube(new Vector3(x0, i % 2 == 0 ? 22f : 30f, back), new Vector3(x1, i % 2 == 0 ? 30f : 22f, back), 0.5f, pink, 6f);
        }
        Tube(new Vector3(-65f, 16f, back), new Vector3(15f, 16f, back), 0.4f, cyan, 6f);
        Tube(new Vector3(b.Position.X - 0.2f, 18f, -45f), new Vector3(b.Position.X - 0.2f, 18f, 40f), 0.4f, cyan, 6f);
    }

    public override void Atmosphere(Godot.Environment environment, DirectionalLight3D sun, Node3D root)
    {
        // Night, lit by neon, the lava lamp and the TV.
        if (environment.Sky.SkyMaterial is ProceduralSkyMaterial sky)
        {
            sky.SkyTopColor = new Color(0.08f, 0.04f, 0.15f);
            sky.SkyHorizonColor = new Color(0.3f, 0.1f, 0.35f);
            sky.GroundHorizonColor = new Color(0.3f, 0.1f, 0.35f);
            sky.GroundBottomColor = new Color(0.04f, 0.02f, 0.06f);
        }
        environment.AmbientLightEnergy = 0.5f;
        environment.GlowIntensity = 1.2f;
        environment.GlowBloom = 0.15f;
        environment.AdjustmentSaturation = 1.3f;
        environment.FogEnabled = true;
        environment.FogLightColor = new Color(0.35f, 0.15f, 0.45f);
        environment.FogDensity = 0.004f;
        sun.LightEnergy = 0.35f;
        sun.LightColor = new Color(0.6f, 0.45f, 1f);

        var b = Bounds;
        Light(root, new Vector3(-20f, 26f, b.Position.Y + 3f), new Color(1f, 0.25f, 0.65f), 5f, 50f, shadows: true);  // pink neon
        Light(root, new Vector3(b.Position.X + 3f, 18f, 0f), new Color(0.25f, 0.85f, 1f), 4f, 45f);                   // cyan neon
        Light(root, new Vector3(52f, 16f, -44f), new Color(1f, 0.45f, 0.4f), 4f, 30f, shadows: true);                 // lava lamp
        Light(root, new Vector3(-30f, 9f, -35f), new Color(0.7f, 0.85f, 1f), 3f, 25f, flicker: true);                // TV glow
    }
}
