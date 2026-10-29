using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Rendering;
using Godot;

namespace BulletHeaven.Levels;

/// <summary>
/// The first level: a giant kitchen table with household props. Every solid prop is both a mesh
/// and an ECS Obstacle (or Ramp) entity. Positions are on the ground plane (x, y) => world (x, 0, y).
/// Props up to about 3 high can be driven onto via ramps or jumps; taller ones are walls.
/// </summary>
public static class KitchenTable
{
    public static readonly Rect2 Bounds = new(-60f, -40f, 120f, 80f);
    private const float LegHeight = 40f;

    public static void Build(Node3D parent, World world)
    {
        var wood = Materials.Plastic(new Color(0.55f, 0.36f, 0.2f), 0.7f);

        // Table top and legs, with the kitchen floor far below so the edge reads as a real drop.
        var size = Bounds.Size;
        parent.AddChild(Mesh(new BoxMesh { Size = new Vector3(size.X, 1.5f, size.Y) }, new Vector3(0f, -0.75f, 0f), wood));
        foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
        {
            var legPos = corner * (size / 2f - new Vector2(4f, 4f));
            parent.AddChild(Mesh(new CylinderMesh { TopRadius = 2f, BottomRadius = 1.6f, Height = LegHeight },
                new Vector3(legPos.X, -1.5f - LegHeight / 2f, legPos.Y), wood));
        }
        parent.AddChild(Mesh(new PlaneMesh { Size = new Vector2(600f, 600f) }, new Vector3(0f, -1.5f - LegHeight, 0f),
            Materials.Plastic(new Color(0.75f, 0.73f, 0.68f), 0.9f)));

        // Coffee mugs.
        Mug(parent, world, new Vector2(-22f, -14f), new Color(0.92f, 0.92f, 0.9f));
        Mug(parent, world, new Vector2(30f, 18f), new Color(0.8f, 0.2f, 0.2f));

        // Two-level book stack with ruler ramps: up onto the big book, up again onto the small one,
        // then fly off its far edge.
        var rulerWood = new Color(0.85f, 0.7f, 0.45f);
        Block(parent, world, new Vector2(18f, -20f), new Vector2(9f, 6.5f), 1.6f, new Color(0.2f, 0.35f, 0.7f));
        Block(parent, world, new Vector2(20f, -20f), new Vector2(5f, 4f), 1.4f, new Color(0.75f, 0.6f, 0.2f), baseHeight: 1.6f);
        Ramp(parent, world, new Vector2(10.5f, -20f), Vector2.Right, 6f, 3f, 0f, 1.6f, rulerWood);
        Ramp(parent, world, new Vector2(15.5f, -20f), Vector2.Right, 4f, 3f, 1.6f, 1.4f, rulerWood);

        // Green book with a ramp from the right.
        Block(parent, world, new Vector2(-35f, 20f), new Vector2(6f, 9f), 1.8f, new Color(0.3f, 0.6f, 0.35f));
        Ramp(parent, world, new Vector2(-29f, 20f), Vector2.Left, 6f, 3f, 0f, 1.8f, rulerWood);

        // Cereal box: far too tall, always a wall.
        Block(parent, world, new Vector2(42f, -10f), new Vector2(4f, 10f), 14f, new Color(0.95f, 0.75f, 0.15f));

        // Pencils: low, long blocks that funnel the swarms. Hop over them, or use the kickers.
        Block(parent, world, new Vector2(-5f, 22f), new Vector2(16f, 0.5f), 0.5f, new Color(0.95f, 0.8f, 0.1f));
        Block(parent, world, new Vector2(-45f, -22f), new Vector2(0.5f, 14f), 0.5f, new Color(0.2f, 0.5f, 0.9f));
        var eraserPink = new Color(0.95f, 0.55f, 0.6f);
        Ramp(parent, world, new Vector2(-5f, 26f), Vector2.Up, 4f, 3f, 0f, 1f, eraserPink);
        Ramp(parent, world, new Vector2(-38f, -24f), Vector2.Left, 4f, 3f, 0f, 1.2f, eraserPink);

        // A free-standing launch ramp in the open.
        Ramp(parent, world, new Vector2(-20f, 6f), Vector2.Right, 5f, 3f, 0f, 1.4f, new Color(0.3f, 0.3f, 0.35f));

        // An apple.
        var apple = Mesh(new SphereMesh { Radius = 2.4f, Height = 4.6f }, new Vector3(-10f, 2.2f, -26f),
            Materials.Plastic(new Color(0.75f, 0.1f, 0.08f), 0.3f));
        parent.AddChild(apple);
        AddObstacle(world, new Vector2(-10f, -26f), new Obstacle { Shape = ObstacleShape.Circle, Radius = 2.3f, Height = 4.4f });

        // Scattered coins and crumbs as decoration (no collision).
        var rng = new RandomNumberGenerator { Seed = 7 };
        var coin = Materials.Plastic(new Color(0.85f, 0.65f, 0.25f), 0.25f);
        coin.Metallic = 0.9f;
        var crumb = Materials.Plastic(new Color(0.85f, 0.7f, 0.45f), 0.9f);
        for (int i = 0; i < 14; i++)
        {
            var p = new Vector3(rng.RandfRange(-55f, 55f), 0.05f, rng.RandfRange(-35f, 35f));
            parent.AddChild(Mesh(new CylinderMesh { TopRadius = 1.1f, BottomRadius = 1.1f, Height = 0.1f }, p, coin));
        }
        for (int i = 0; i < 60; i++)
        {
            var p = new Vector3(rng.RandfRange(-58f, 58f), 0.08f, rng.RandfRange(-38f, 38f));
            float s = rng.RandfRange(0.1f, 0.3f);
            parent.AddChild(Mesh(new BoxMesh { Size = new Vector3(s, s * 0.7f, s * 1.3f) }, p, crumb));
        }
    }

    private static void Mug(Node3D parent, World world, Vector2 at, Color color)
    {
        const float radius = 3f, height = 6f;
        var mat = Materials.Plastic(color, 0.2f);
        parent.AddChild(Mesh(new CylinderMesh { TopRadius = radius, BottomRadius = radius * 0.92f, Height = height },
            new Vector3(at.X, height / 2f, at.Y), mat));
        parent.AddChild(Mesh(new TorusMesh { InnerRadius = 1.1f, OuterRadius = 1.6f },
            new Vector3(at.X + radius, height * 0.55f, at.Y), mat, new Vector3(90f, 0f, 0f)));
        AddObstacle(world, at, new Obstacle { Shape = ObstacleShape.Circle, Radius = radius, Height = height });
    }

    /// <summary>Box prop. <paramref name="baseHeight"/> stacks it on something below.</summary>
    private static void Block(Node3D parent, World world, Vector2 center, Vector2 footprint, float height,
        Color color, float baseHeight = 0f)
    {
        parent.AddChild(Mesh(new BoxMesh { Size = new Vector3(footprint.X, height, footprint.Y) },
            new Vector3(center.X, baseHeight + height / 2f, center.Y), Materials.Plastic(color, 0.6f)));
        AddObstacle(world, center, new Obstacle
        {
            Shape = ObstacleShape.Box,
            HalfExtents = footprint / 2f,
            Height = baseHeight + height,
        });
    }

    /// <summary>Wedge rising along an axis-aligned <paramref name="direction"/>.</summary>
    private static void Ramp(Node3D parent, World world, Vector2 center, Vector2 direction, float length, float width,
        float baseHeight, float rise, Color color)
    {
        // PrismMesh with LeftToRight = 1 is a right-angle wedge rising towards +X; turn it to face the direction.
        var wedge = new PrismMesh { Size = new Vector3(length, rise, width), LeftToRight = 1f };
        parent.AddChild(Mesh(wedge, new Vector3(center.X, baseHeight + rise / 2f, center.Y), Materials.Plastic(color, 0.5f),
            new Vector3(0f, Mathf.RadToDeg(-direction.Angle()), 0f)));

        bool alongX = Mathf.Abs(direction.X) > 0.5f;
        world.Create(new Position(center), new Ecs.Ramp
        {
            HalfExtents = alongX ? new Vector2(length, width) / 2f : new Vector2(width, length) / 2f,
            Direction = direction,
            BaseHeight = baseHeight,
            Rise = rise,
        });
    }

    private static void AddObstacle(World world, Vector2 at, Obstacle obstacle) =>
        world.Create(new Position(at), obstacle);

    private static MeshInstance3D Mesh(Mesh mesh, Vector3 position, Material material, Vector3 rotationDegrees = default) => new()
    {
        Mesh = mesh,
        MaterialOverride = material,
        Position = position,
        RotationDegrees = rotationDegrees,
    };
}
