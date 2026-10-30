using BulletHeaven.Ecs;
using BulletHeaven.Rendering;
using Godot;

namespace BulletHeaven.Levels;

/// <summary>
/// Level 2. An adventurer's study: dark wood panelling, a walnut floor with a Persian rug, a leather
/// Chesterfield armchair, towers of old books and a big partners' desk with a golden idol, a
/// candle and a spilt inkwell on it. Warm lamp and candle light in dusty air.
///
/// The way up is a staircase of furniture, each step about one jump (1.6) high:
/// book pile 1.6 -> footstool 3.2 -> seat 4 -> armrest 5.6 -> chair back 7.2 -> book towers
/// 8.8 / 10.4 / 12 -> desk 13.
/// </summary>
public sealed class Study : Level
{
    private const float DeskTop = 13f;
    private static readonly Vector2 Chair = new(-15f, -28f);
    private static readonly Vector2 Desk = new(20f, -32f);

    public override string Name => "The Study";
    public override Rect2 Bounds => new(-70f, -50f, 140f, 100f);
    public override Vector2 PlayerStart => new(0f, 30f);

    protected override void BuildLevel()
    {
        Room();
        Armchair();
        DeskAndArtifacts();
        FloorArtifacts();
    }

    private void Room()
    {
        Floor("floor_walnut", new Color(0.18f, 0.08f, 0.04f), toneVariation: 0.2f);
        Decor("rug", new Vector3(0f, 0f, 8f));
        if (!Visual)
            return;

        // Wood panelling all round (fallback: plain dark walls).
        if (ModelLibrary.LoadProp("panel_wall") == null)
        {
            RoomWalls(Materials.Plastic(new Color(0.2f, 0.08f, 0.04f), 0.5f));
            return;
        }
        var b = Bounds;
        for (float x = b.Position.X + 6f; x < b.End.X; x += 16f)
            Decor("panel_wall", new Vector3(x, 0f, b.Position.Y - 1f));
        for (float y = b.Position.Y + 8f; y < b.End.Y; y += 16f)
        {
            Decor("panel_wall", new Vector3(b.Position.X - 1f, 0f, y), yawDegrees: 90f);
            Decor("panel_wall", new Vector3(b.End.X + 1f, 0f, y), yawDegrees: -90f);
        }
        Add(new BoxMesh { Size = new Vector3(b.Size.X + 4f, 1.2f, 1f) }, new Vector3(b.GetCenter().X, 0.6f, b.End.Y + 0.5f),
            Materials.Plastic(new Color(0.2f, 0.08f, 0.04f), 0.5f));
    }

    private void Armchair()
    {
        // Collision follows the chair's shape: seat 4, armrests 5.6, back 7.2 (see build_armchair).
        BoxObstacle(Chair, new Vector2(12f, 11f), 4f);
        BoxObstacle(Chair + new Vector2(5.05f, 0.5f), new Vector2(2.2f, 9f), 5.6f);
        BoxObstacle(Chair + new Vector2(-5.05f, 0.5f), new Vector2(2.2f, 9f), 5.6f);
        BoxObstacle(Chair + new Vector2(0f, -4.5f), new Vector2(12f, 2f), 7.2f);
        if (Visual)
        {
            var materials = Model("armchair", new Vector3(Chair.X, 0f, Chair.Y));
            if (materials == null)
            {
                var leather = Materials.Plastic(new Color(0.3f, 0.06f, 0.04f), 0.4f);
                Add(new BoxMesh { Size = new Vector3(12f, 4f, 11f) }, new Vector3(Chair.X, 2f, Chair.Y), leather);
                Add(new BoxMesh { Size = new Vector3(12f, 7.2f, 2f) }, new Vector3(Chair.X, 3.6f, Chair.Y - 4.5f), leather);
                materials = new BaseMaterial3D[] { leather };
            }
            Fader?.Register(new Rect2(Chair - new Vector2(6f, 5.5f), new Vector2(12f, 11f)), 7.2f, materials);
        }

        // In front: a leather footstool, and a pile of books to get onto it.
        Round("footstool", Chair + new Vector2(0f, 8.5f), 0f, 2.8f, 3.2f, new Color(0.3f, 0.06f, 0.04f), sphere: false);
        var bookTints = new[] { Colors.White, new Color(0.85f, 0.8f, 0.75f), new Color(0.75f, 0.85f, 0.75f) };
        Stack(Chair + new Vector2(0f, 13.5f), new Vector2(6f, 4f), 1.6f, "book_red", bookTints, 0);

        // Behind the chair, towers of old books climb to the desk.
        Stack(new Vector2(-6.85f, -32f), new Vector2(4f, 5.5f), 8.8f, "book_green", bookTints, 1);
        Stack(new Vector2(-2.85f, -32f), new Vector2(4f, 5.5f), 10.4f, "book_red", bookTints, 2);
        Stack(new Vector2(1.15f, -32f), new Vector2(4f, 5.5f), 12f, "book_blue", bookTints, 0);
    }

    private void DeskAndArtifacts()
    {
        Block(Desk, new Vector2(30f, 14f), 0f, DeskTop, new Color(0.25f, 0.1f, 0.05f), "desk");
        const float b = DeskTop;
        Round("idol", new Vector2(27f, -33f), b, 0.9f, 3.4f, new Color(0.9f, 0.7f, 0.25f));
        Round("candle", new Vector2(11f, -36f), b, 1f, 4.5f, new Color(0.9f, 0.85f, 0.7f), sphere: false);
        Spill(new Vector2(31f, -28f), b, 2.2f, LiquidKind.Coffee); // cold tea gone over the papers
        Block(new Vector2(16f, -28f), new Vector2(6f, 4f), b, 1.2f, new Color(0.35f, 0.06f, 0.04f), "book_red");
    }

    private void FloorArtifacts()
    {
        Round("globe", new Vector2(-52f, -32f), 0f, 4.4f, 14f, new Color(0.6f, 0.5f, 0.3f));
        Block(new Vector2(48f, 18f), new Vector2(8f, 5f), 0f, 5f, new Color(0.3f, 0.18f, 0.08f), "chest");
        Round("urn", new Vector2(-55f, 30f), 0f, 2f, 7f, new Color(0.6f, 0.27f, 0.12f));
        Round("urn", new Vector2(58f, -12f), 0f, 2f, 7f, new Color(0.6f, 0.27f, 0.12f));
        Spill(new Vector2(52f, -6f), 0f, 3.5f, LiquidKind.Jam);   // something sticky leaked out of the urn
        Spill(new Vector2(-40f, 20f), 0f, 4f, LiquidKind.Honey);

        // Brass reading lamp by the armchair.
        CircleObstacle(new Vector2(-26f, -38f), 1.2f, 16f);
        if (Visual)
        {
            var brass = Materials.Plastic(new Color(0.8f, 0.6f, 0.3f), 0.25f);
            brass.Metallic = 1f;
            Add(new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 15f }, new Vector3(-26f, 7.5f, -38f), brass);
            Add(new CylinderMesh { TopRadius = 1.6f, BottomRadius = 1.6f, Height = 0.4f }, new Vector3(-26f, 0.2f, -38f), brass);
            Add(new CylinderMesh { TopRadius = 1.4f, BottomRadius = 3f, Height = 3f, CapTop = false, CapBottom = false },
                new Vector3(-26f, 15.5f, -38f), Materials.Glow(new Color(1f, 0.8f, 0.5f), 0.8f));
        }
    }

    public override void Atmosphere(Godot.Environment environment, DirectionalLight3D sun, Node3D root)
    {
        // Evening: little daylight, warm lamps, dust hanging in the air.
        if (environment.Sky.SkyMaterial is ProceduralSkyMaterial sky)
        {
            sky.SkyTopColor = new Color(0.1f, 0.07f, 0.05f);
            sky.SkyHorizonColor = new Color(0.25f, 0.15f, 0.08f);
            sky.GroundHorizonColor = new Color(0.25f, 0.15f, 0.08f);
            sky.GroundBottomColor = new Color(0.05f, 0.03f, 0.02f);
        }
        environment.AmbientLightEnergy = 0.35f;
        environment.VolumetricFogEnabled = true;
        environment.VolumetricFogDensity = 0.008f;
        environment.VolumetricFogAlbedo = new Color(0.95f, 0.8f, 0.6f);
        sun.LightEnergy = 0.45f;
        sun.LightColor = new Color(1f, 0.75f, 0.5f);
        sun.RotationDegrees = new Vector3(-35f, 60f, 0f);

        Light(root, new Vector3(-26f, 14f, -38f), new Color(1f, 0.78f, 0.5f), 6f, 40f, shadows: true);      // reading lamp
        Light(root, new Vector3(11f, DeskTop + 4.6f, -36f), new Color(1f, 0.6f, 0.3f), 3f, 25f, shadows: true, flicker: true); // candle
        Light(root, new Vector3(0f, 38f, 0f), new Color(1f, 0.85f, 0.65f), 3f, 90f);                        // chandelier
    }
}
