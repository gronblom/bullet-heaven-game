using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Rendering;
using Godot;

namespace BulletHeaven.Levels;

/// <summary>
/// A kitchen seen from a bug's-eye scale. The floor is the main arena; the kitchen counter in the
/// middle is high ground (10 up) with the old tabletop props on it. Two ways up:
///   - west: a cutting board leaning on a stool, then a jump across to the counter;
///   - east: a staircase of toy blocks, one jump per block.
/// Anywhere is a way down. Every solid prop is a mesh plus an ECS Obstacle/Ramp entity, and
/// anything tall registers with the occlusion fader so it turns see-through over the player.
/// Props use the generated models in assets/models/ when present (see tools/blender), and fall
/// back to primitives otherwise.
/// Positions are on the ground plane (x, y) => world (x, height, y).
/// </summary>
public static class Kitchen
{
    public static readonly Rect2 Bounds = new(-80f, -55f, 160f, 110f);
    public static readonly Vector2 PlayerStart = new(0f, 30f);

    private const float CounterTop = 10f;
    private static readonly Rect2 Counter = new(-26f, -18f, 52f, 26f);

    public static void Build(Node3D parent, World world, OcclusionFader fader)
    {
        var builder = new Builder(parent, world, fader);
        builder.Room();
        builder.KitchenCounter();
        builder.CounterProps();
        builder.WaysUp();
        builder.FloorProps();
    }

    private sealed class Builder(Node3D parent, World world, OcclusionFader fader)
    {
        private readonly RandomNumberGenerator _rng = new() { Seed = 7 };

        public void Room()
        {
            // Tiles: baked textures cover 2 x 2 tiles (8 units); fall back to a plain checker.
            var tileRepeat = new Vector3(Bounds.Size.X / 8f, Bounds.Size.Y / 8f, 1f);
            StandardMaterial3D floor;
            if (ModelLibrary.LoadTexture("floor_albedo.png") is { } albedo)
            {
                floor = new StandardMaterial3D
                {
                    AlbedoTexture = albedo,
                    NormalEnabled = true,
                    NormalTexture = ModelLibrary.LoadTexture("floor_normal.png"),
                    RoughnessTexture = ModelLibrary.LoadTexture("floor_roughness.png"),
                    Roughness = 1f,
                    Uv1Scale = tileRepeat,
                };
            }
            else
            {
                var tiles = Image.CreateEmpty(2, 2, false, Image.Format.Rgb8);
                tiles.SetPixel(0, 0, new Color(0.88f, 0.86f, 0.8f));
                tiles.SetPixel(1, 1, new Color(0.88f, 0.86f, 0.8f));
                tiles.SetPixel(1, 0, new Color(0.55f, 0.6f, 0.62f));
                tiles.SetPixel(0, 1, new Color(0.55f, 0.6f, 0.62f));
                floor = new StandardMaterial3D
                {
                    AlbedoTexture = ImageTexture.CreateFromImage(tiles),
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                    Uv1Scale = tileRepeat,
                    Roughness = 0.35f,
                };
            }
            Add(new PlaneMesh { Size = Bounds.Size }, new Vector3(Bounds.GetCenter().X, 0f, Bounds.GetCenter().Y), floor);

            // Walls behind and to the sides; only a skirting board on the camera side.
            var wall = Materials.Plastic(new Color(0.93f, 0.9f, 0.82f), 0.9f);
            float w = Bounds.Size.X + 4f, d = Bounds.Size.Y + 4f;
            Add(new BoxMesh { Size = new Vector3(w, 50f, 2f) }, new Vector3(0f, 25f, Bounds.Position.Y - 1f), wall);
            Add(new BoxMesh { Size = new Vector3(2f, 50f, d) }, new Vector3(Bounds.Position.X - 1f, 25f, 0f), wall);
            Add(new BoxMesh { Size = new Vector3(2f, 50f, d) }, new Vector3(Bounds.End.X + 1f, 25f, 0f), wall);
            Add(new BoxMesh { Size = new Vector3(w, 1.2f, 1f) }, new Vector3(0f, 0.6f, Bounds.End.Y + 0.5f), wall);
            Add(new BoxMesh { Size = new Vector3(30f, 16f, 0.3f) }, new Vector3(10f, 26f, Bounds.Position.Y + 0.2f),
                Materials.Glow(new Color(0.85f, 0.93f, 1f), 1.2f)); // window

            // Fridge and a row of cabinets along the back wall.
            var fridge = Materials.Plastic(new Color(0.92f, 0.93f, 0.95f), 0.2f);
            Solid(new Vector2(-66f, -45f), new Vector2(24f, 20f), 45f, fridge);
            var cabinets = Materials.Plastic(new Color(0.42f, 0.55f, 0.5f), 0.6f);
            Solid(new Vector2(10f, -50f), new Vector2(120f, 10f), CounterTop, cabinets);
        }

        public void KitchenCounter()
        {
            var center = Counter.GetCenter();
            AddObstacle(center, new Obstacle { Shape = ObstacleShape.Box, HalfExtents = Counter.Size / 2f, Height = CounterTop });
            if (Model("counter", new Vector3(center.X, 0f, center.Y), new Vector3(Counter.Size.X + 1f, CounterTop, Counter.Size.Y + 1f)) is { } fitted)
            {
                fader.Register(Counter.Grow(0.5f), CounterTop, fitted);
                return;
            }

            var cabinet = Materials.Plastic(new Color(0.42f, 0.55f, 0.5f), 0.6f);
            var top = Materials.Plastic(new Color(0.2f, 0.2f, 0.22f), 0.25f);
            var doors = Materials.Plastic(new Color(0.5f, 0.64f, 0.58f), 0.55f);
            var handles = Materials.Plastic(new Color(0.8f, 0.8f, 0.82f), 0.2f);
            handles.Metallic = 0.9f;

            Add(new BoxMesh { Size = new Vector3(Counter.Size.X, CounterTop - 0.6f, Counter.Size.Y) },
                new Vector3(center.X, (CounterTop - 0.6f) / 2f, center.Y), cabinet);
            Add(new BoxMesh { Size = new Vector3(Counter.Size.X + 1f, 0.6f, Counter.Size.Y + 1f) },
                new Vector3(center.X, CounterTop - 0.3f, center.Y), top);

            // Cabinet doors on the side facing the camera, for scale.
            for (int i = 0; i < 6; i++)
            {
                float x = Counter.Position.X + 4.33f + i * 8.67f;
                Add(new BoxMesh { Size = new Vector3(7.6f, 7.8f, 0.2f) }, new Vector3(x, 4.6f, Counter.End.Y + 0.1f), doors);
                Add(new BoxMesh { Size = new Vector3(0.4f, 1.6f, 0.4f) }, new Vector3(x + 2.8f, 6.5f, Counter.End.Y + 0.35f), handles);
            }

            fader.Register(Counter.Grow(0.5f), CounterTop, cabinet, top, doors, handles);
        }

        public void CounterProps()
        {
            const float b = CounterTop;
            var rulerWood = new Color(0.85f, 0.7f, 0.45f);

            Mug(new Vector2(-18f, -12f), b, "mug_white", new Color(0.92f, 0.92f, 0.9f));
            Mug(new Vector2(19f, 3f), b, "mug_red", new Color(0.8f, 0.2f, 0.2f));

            // Two-level book stack with ruler ramps; fly off the far side.
            Block(new Vector2(6f, -10f), new Vector2(9f, 6.5f), b, 1.6f, new Color(0.2f, 0.35f, 0.7f), "book_blue");
            Block(new Vector2(8f, -10f), new Vector2(5f, 4f), b + 1.6f, 1.4f, new Color(0.75f, 0.6f, 0.2f), "book_gold");
            Ramp(new Vector2(-1.5f, -10f), Vector2.Right, 6f, 3f, b, 1.6f, rulerWood);
            Ramp(new Vector2(3.5f, -10f), Vector2.Right, 4f, 3f, b + 1.6f, 1.4f, rulerWood);

            Block(new Vector2(22f, -13f), new Vector2(4f, 7f), b, 14f, new Color(0.95f, 0.75f, 0.15f), "cereal_box");

            // Apple.
            var appleMaterials = Model("apple", new Vector3(-8f, b, 2f));
            if (appleMaterials == null)
            {
                var apple = Materials.Plastic(new Color(0.75f, 0.1f, 0.08f), 0.3f);
                Add(new SphereMesh { Radius = 2.4f, Height = 4.6f }, new Vector3(-8f, b + 2.2f, 2f), apple);
                appleMaterials = new BaseMaterial3D[] { apple };
            }
            AddObstacle(new Vector2(-8f, 2f), new Obstacle { Shape = ObstacleShape.Circle, Radius = 2.3f, Height = b + 4.4f });
            fader.Register(Square(new Vector2(-8f, 2f), 2.4f), b + 4.4f, appleMaterials);

            // Pencil with an eraser kicker in front, and a launch ramp that throws you off the counter.
            Block(new Vector2(-14f, -3f), new Vector2(12f, 0.5f), b, 0.5f, new Color(0.95f, 0.8f, 0.1f), "pencil");
            Ramp(new Vector2(-14f, 1f), Vector2.Up, 4f, 3f, b, 1f, new Color(0.95f, 0.55f, 0.6f));
            Ramp(new Vector2(19.5f, -3f), Vector2.Right, 5f, 3f, b, 1.4f, new Color(0.3f, 0.3f, 0.35f));

            // Coins and crumbs (decoration only).
            var coin = Materials.Plastic(new Color(0.85f, 0.65f, 0.25f), 0.25f);
            coin.Metallic = 0.9f;
            var crumb = Materials.Plastic(new Color(0.85f, 0.7f, 0.45f), 0.9f);
            for (int i = 0; i < 8; i++)
                Add(new CylinderMesh { TopRadius = 1.1f, BottomRadius = 1.1f, Height = 0.1f }, OnCounter(b + 0.05f), coin);
            for (int i = 0; i < 40; i++)
            {
                float s = _rng.RandfRange(0.1f, 0.3f);
                Add(new BoxMesh { Size = new Vector3(s, s * 0.7f, s * 1.3f) }, OnCounter(b + 0.08f), crumb);
            }
        }

        public void WaysUp()
        {
            // West: stools by the counter, and a cutting board leaning on one of them.
            Stool(new Vector2(-14f, 13.5f));
            Stool(new Vector2(14f, 13.5f));
            // Its top end meets the seat's edge, so the last step onto the seat is flush.
            Ramp(new Vector2(-14f, 27.5f), Vector2.Up, 22f, 4f, 0f, 8.5f, new Color(0.7f, 0.5f, 0.3f), plank: true);

            // East: toy block staircase, about one jump per block.
            var colors = new[]
            {
                new Color(0.9f, 0.2f, 0.2f), new Color(0.2f, 0.5f, 0.9f), new Color(0.95f, 0.8f, 0.15f),
                new Color(0.2f, 0.7f, 0.3f), new Color(0.6f, 0.3f, 0.8f), new Color(0.95f, 0.5f, 0.1f),
            };
            (Vector2 At, float Size, float Top)[] steps =
            {
                (new Vector2(36f, 14f), 5f, 1.6f),
                (new Vector2(36f, 8f), 5f, 3.2f),
                (new Vector2(36f, 2f), 5f, 4.8f),
                (new Vector2(36f, -4f), 5f, 6.4f),
                (new Vector2(36f, -10f), 5f, 8f),
                (new Vector2(30f, -10f), 4f, 9f),
            };
            for (int i = 0; i < steps.Length; i++)
                ToyBlockTower(steps[i].At, steps[i].Size, steps[i].Top, colors, i);
        }

        public void FloorProps()
        {
            // Dog bowl with kibble around it.
            if (Model("dog_bowl", new Vector3(-45f, 0f, 30f)) == null)
            {
                var steel = Materials.Plastic(new Color(0.75f, 0.77f, 0.8f), 0.2f);
                steel.Metallic = 0.8f;
                Add(new CylinderMesh { TopRadius = 4f, BottomRadius = 3.4f, Height = 1.5f }, new Vector3(-45f, 0.75f, 30f), steel);
            }
            AddObstacle(new Vector2(-45f, 30f), new Obstacle { Shape = ObstacleShape.Circle, Radius = 4f, Height = 1.5f });
            var kibble = Materials.Plastic(new Color(0.5f, 0.3f, 0.15f), 0.8f);
            for (int i = 0; i < 25; i++)
            {
                var at = new Vector2(-45f, 30f) + Vector2.FromAngle(_rng.Randf() * Mathf.Tau) * _rng.RandfRange(4.5f, 9f);
                Add(new SphereMesh { Radius = 0.35f, Height = 0.5f }, new Vector3(at.X, 0.2f, at.Y), kibble);
            }

            // A big rubber ball you can climb onto.
            var ballMaterials = Model("ball", new Vector3(55f, 0f, 32f));
            if (ballMaterials == null)
            {
                var ball = Materials.Plastic(new Color(0.2f, 0.6f, 0.95f), 0.4f);
                Add(new SphereMesh { Radius = 4f, Height = 8f }, new Vector3(55f, 4f, 32f), ball);
                ballMaterials = new BaseMaterial3D[] { ball };
            }
            AddObstacle(new Vector2(55f, 32f), new Obstacle { Shape = ObstacleShape.Circle, Radius = 3.6f, Height = 8f });
            fader.Register(Square(new Vector2(55f, 32f), 4f), 8f, ballMaterials);

            // A sneaker.
            Block(new Vector2(-58f, 0f), new Vector2(5f, 12f), 0f, 3.5f, new Color(0.9f, 0.9f, 0.92f));
            Add(new BoxMesh { Size = new Vector3(5.4f, 0.8f, 12.4f) }, new Vector3(-58f, 0.4f, 0f),
                Materials.Plastic(new Color(0.85f, 0.2f, 0.2f), 0.7f));
        }

        // --- building blocks -------------------------------------------------------------

        private void Mug(Vector2 at, float baseHeight, string model, Color color)
        {
            const float radius = 3f, height = 6f;
            AddObstacle(at, new Obstacle { Shape = ObstacleShape.Circle, Radius = radius, Height = baseHeight + height });
            if (Model(model, new Vector3(at.X, baseHeight, at.Y)) is { } fitted)
            {
                fader.Register(Square(at, radius + 1.6f), baseHeight + height, fitted);
                return;
            }

            var mat = Materials.Plastic(color, 0.2f);
            Add(new CylinderMesh { TopRadius = radius, BottomRadius = radius * 0.92f, Height = height },
                new Vector3(at.X, baseHeight + height / 2f, at.Y), mat);
            Add(new TorusMesh { InnerRadius = 1.1f, OuterRadius = 1.6f },
                new Vector3(at.X + radius, baseHeight + height * 0.55f, at.Y), mat, new Vector3(90f, 0f, 0f));
            fader.Register(Square(at, radius + 1.6f), baseHeight + height, mat);
        }

        private void Stool(Vector2 at)
        {
            const float seatTop = 8.5f, radius = 3f;
            // Treated as solid so nothing walks between the legs (the height map has no overhangs).
            AddObstacle(at, new Obstacle { Shape = ObstacleShape.Circle, Radius = radius, Height = seatTop });
            if (Model("stool", new Vector3(at.X, 0f, at.Y)) is { } fitted)
            {
                fader.Register(Square(at, radius), seatTop, fitted);
                return;
            }

            var wood = Materials.Plastic(new Color(0.6f, 0.4f, 0.22f), 0.6f);
            Add(new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.8f }, new Vector3(at.X, seatTop - 0.4f, at.Y), wood);
            foreach (var corner in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            {
                var leg = at + corner * 1.7f;
                Add(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.35f, Height = seatTop - 0.8f },
                    new Vector3(leg.X, (seatTop - 0.8f) / 2f, leg.Y), wood);
            }
            Add(new TorusMesh { InnerRadius = 2.1f, OuterRadius = 2.4f }, new Vector3(at.X, 3f, at.Y), wood);
            fader.Register(Square(at, radius), seatTop, wood);
        }

        private void ToyBlockTower(Vector2 at, float size, float top, Color[] palette, int index)
        {
            int count = Math.Max(1, (int)MathF.Round(top / 1.6f));
            float h = top / count;
            var materials = new List<BaseMaterial3D>();
            for (int i = 0; i < count; i++)
            {
                var paint = palette[(index + i) % palette.Length];
                // Generated block: bare wood tinted with the paint colour.
                if (Model("toy_block", new Vector3(at.X, h * i, at.Y), new Vector3(size, h * 0.98f, size), paint.Lightened(0.25f)) is { } fitted)
                {
                    materials.AddRange(fitted);
                    continue;
                }
                var mat = Materials.Plastic(paint, 0.5f);
                Add(new BoxMesh { Size = new Vector3(size, h * 0.98f, size) }, new Vector3(at.X, h * (i + 0.5f), at.Y), mat);
                materials.Add(mat);
            }
            AddObstacle(at, new Obstacle { Shape = ObstacleShape.Box, HalfExtents = new Vector2(size, size) / 2f, Height = top });
            fader.Register(Square(at, size / 2f), top, materials.ToArray());
        }

        /// <summary>Box prop, stretched to the footprint if a <paramref name="model"/> exists.
        /// <paramref name="baseHeight"/> stands it on something below.</summary>
        private void Block(Vector2 center, Vector2 footprint, float baseHeight, float height, Color color, string model = null)
        {
            AddObstacle(center, new Obstacle { Shape = ObstacleShape.Box, HalfExtents = footprint / 2f, Height = baseHeight + height });
            var size = new Vector3(footprint.X, height, footprint.Y);
            var materials = model == null ? null : Model(model, new Vector3(center.X, baseHeight, center.Y), size);
            if (materials == null)
            {
                var mat = Materials.Plastic(color, 0.6f);
                Add(new BoxMesh { Size = size }, new Vector3(center.X, baseHeight + height / 2f, center.Y), mat);
                materials = new BaseMaterial3D[] { mat };
            }
            if (height > 1f)
                fader.Register(new Rect2(center - footprint / 2f, footprint), baseHeight + height, materials);
        }

        private void Solid(Vector2 center, Vector2 footprint, float height, StandardMaterial3D material)
        {
            Add(new BoxMesh { Size = new Vector3(footprint.X, height, footprint.Y) }, new Vector3(center.X, height / 2f, center.Y), material);
            AddObstacle(center, new Obstacle { Shape = ObstacleShape.Box, HalfExtents = footprint / 2f, Height = height });
        }

        /// <summary>Slope rising along an axis-aligned <paramref name="direction"/>: a solid wedge, or a
        /// thin leaning plank.</summary>
        private void Ramp(Vector2 center, Vector2 direction, float length, float width, float baseHeight, float rise,
            Color color, bool plank = false)
        {
            var mat = Materials.Plastic(color, 0.5f);
            var yaw = new Vector3(0f, Mathf.RadToDeg(-direction.Angle()), 0f);
            var at = new Vector3(center.X, baseHeight + rise / 2f, center.Y);
            if (plank)
            {
                // Tilt a long board up towards +X, then turn it to face the direction.
                var pivot = new Node3D { Position = at, RotationDegrees = yaw };
                var tilt = new Node3D { Rotation = new Vector3(0f, 0f, MathF.Atan2(rise, length)) };
                pivot.AddChild(tilt);
                parent.AddChild(pivot);
                var boardSize = new Vector3(MathF.Sqrt(length * length + rise * rise), 0.4f, width);
                if (Model("cutting_board", new Vector3(0f, -0.4f, 0f), boardSize, into: tilt) == null)
                    tilt.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = boardSize }, MaterialOverride = mat, Position = new Vector3(0f, -0.2f, 0f) });
            }
            else
            {
                // PrismMesh with LeftToRight = 1 is a right-angle wedge rising towards +X.
                Add(new PrismMesh { Size = new Vector3(length, rise, width), LeftToRight = 1f }, at, mat, yaw);
            }

            bool alongX = MathF.Abs(direction.X) > 0.5f;
            world.Create(new Position(center), new Ecs.Ramp
            {
                HalfExtents = alongX ? new Vector2(length, width) / 2f : new Vector2(width, length) / 2f,
                Direction = direction,
                BaseHeight = baseHeight,
                Rise = rise,
            });
        }

        private Vector3 OnCounter(float y) =>
            new(_rng.RandfRange(Counter.Position.X + 2f, Counter.End.X - 2f), y, _rng.RandfRange(Counter.Position.Y + 2f, Counter.End.Y - 2f));

        private static Rect2 Square(Vector2 center, float halfSize) =>
            new(center - new Vector2(halfSize, halfSize), new Vector2(halfSize, halfSize) * 2f);

        private void AddObstacle(Vector2 at, Obstacle obstacle) => world.Create(new Position(at), obstacle);

        /// <summary>Places a generated model if assets/models/{name}.glb exists and returns its materials
        /// (for fading); returns null so the caller can build its primitive fallback.</summary>
        private BaseMaterial3D[] Model(string name, Vector3 bottomCenter, Vector3? exactSize = null, Color? tint = null, Node3D into = null)
        {
            if (ModelLibrary.LoadProp(name, exactSize, tint) is not { } prop)
                return null;
            prop.Node.Position = bottomCenter;
            (into ?? parent).AddChild(prop.Node);
            return prop.Materials;
        }

        private void Add(Mesh mesh, Vector3 position, Material material, Vector3 rotationDegrees = default) =>
            parent.AddChild(new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = material,
                Position = position,
                RotationDegrees = rotationDegrees,
            });
    }
}
