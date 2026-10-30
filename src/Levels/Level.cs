using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Rendering;
using BulletHeaven.Systems;
using Godot;

namespace BulletHeaven.Levels;

/// <summary>
/// A level: its playable area, where the player starts, the geometry (collision as ECS
/// Obstacle/Ramp entities plus meshes) and its atmosphere (sky, light, fog).
///
/// Builders use the helpers below, which create collision first and then, only when there is a
/// scene to build into, the visuals: generated models from assets/models/ when present, primitive
/// fallbacks otherwise. Built without a scene (parent null) a level yields its collision geometry
/// only, which is what the headless tests use.
/// Positions are on the ground plane (x, y) => world (x, height, y).
/// </summary>
public abstract class Level
{
    /// <summary>Every level, in level-select order (F1, F2, ...).</summary>
    public static readonly Level[] All = { new Kitchen(), new Study(), new Windowsill() };

    public abstract string Name { get; }
    public abstract Rect2 Bounds { get; }
    public abstract Vector2 PlayerStart { get; }

    /// <summary>A secret exit: standing in this area at or above <see cref="ExitHeight"/> unlocks <see cref="ExitTo"/>.</summary>
    public virtual Rect2? Exit => null;
    public virtual float ExitHeight => 0f;
    /// <summary>Index into <see cref="All"/> of the level the exit leads to.</summary>
    public virtual int ExitTo => -1;
    /// <summary>Boss levels: where the Queen Ant appears, or null for none.</summary>
    public virtual Vector2? BossSpawn => null;

    protected Node3D Parent { get; private set; }
    protected World World { get; private set; }
    protected OcclusionFader Fader { get; private set; }

    /// <summary>False when building collision only (headless tests): skip everything visual.</summary>
    protected bool Visual => Parent != null;

    public void Build(Node3D parent, World world, OcclusionFader fader)
    {
        Parent = parent;
        World = world;
        Fader = fader;
        BuildLevel();
    }

    protected abstract void BuildLevel();

    /// <summary>Sets the mood: sky, ambient light, sun, fog, extra lights. Called after Build.</summary>
    public virtual void Atmosphere(Godot.Environment environment, DirectionalLight3D sun, Node3D root) { }

    // --- collision ---------------------------------------------------------------------------

    protected void BoxObstacle(Vector2 center, Vector2 footprint, float top) =>
        World.Create(new Position(center), new Obstacle { Shape = ObstacleShape.Box, HalfExtents = footprint / 2f, Height = top });

    protected void CircleObstacle(Vector2 center, float radius, float top) =>
        World.Create(new Position(center), new Obstacle { Shape = ObstacleShape.Circle, Radius = radius, Height = top });

    // --- solid props -------------------------------------------------------------------------

    /// <summary>Box-shaped prop standing on <paramref name="baseHeight"/>, stretched to the footprint if a
    /// <paramref name="model"/> exists, else a coloured box. Tall ones fade when they hide the player.</summary>
    protected BaseMaterial3D[] Block(Vector2 center, Vector2 footprint, float baseHeight, float height, Color color,
        string model = null, Color? tint = null)
    {
        BoxObstacle(center, footprint, baseHeight + height);
        if (!Visual)
            return null;

        var size = new Vector3(footprint.X, height, footprint.Y);
        var materials = model == null ? null : Model(model, new Vector3(center.X, baseHeight, center.Y), size, tint);
        if (materials == null)
        {
            var mat = Materials.Plastic(color, 0.6f);
            Add(new BoxMesh { Size = size }, new Vector3(center.X, baseHeight + height / 2f, center.Y), mat);
            materials = new BaseMaterial3D[] { mat };
        }
        if (height > 1f)
            Fader?.Register(new Rect2(center - footprint / 2f, footprint), baseHeight + height, materials);
        return materials;
    }

    /// <summary>Round prop (fruit, mug, urn...): a model at its natural size if generated, else a sphere
    /// or cylinder. Solid; climbable when low enough.</summary>
    protected BaseMaterial3D[] Round(string model, Vector2 at, float baseHeight, float radius, float height, Color fallback,
        bool sphere = true, float fadeRadius = 0f)
    {
        CircleObstacle(at, radius, baseHeight + height);
        if (!Visual)
            return null;

        var materials = Model(model, new Vector3(at.X, baseHeight, at.Y));
        if (materials == null)
        {
            var mat = Materials.Plastic(fallback, 0.3f);
            Mesh mesh = sphere
                ? new SphereMesh { Radius = radius, Height = height }
                : new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height };
            Add(mesh, new Vector3(at.X, baseHeight + height / 2f, at.Y), mat);
            materials = new BaseMaterial3D[] { mat };
        }
        if (height > 2f)
            Fader?.Register(Square(at, fadeRadius > 0f ? fadeRadius : radius), baseHeight + height, materials);
        return materials;
    }

    /// <summary>A stack of tinted generated blocks or books forming one climbable step.</summary>
    protected void Stack(Vector2 at, Vector2 footprint, float top, string model, Color[] palette, int offset, float layer = 1.6f)
    {
        BoxObstacle(at, footprint, top);
        if (!Visual)
            return;

        int count = Math.Max(1, (int)MathF.Round(top / layer));
        float h = top / count;
        var materials = new List<BaseMaterial3D>();
        for (int i = 0; i < count; i++)
        {
            var paint = palette[(offset + i) % palette.Length];
            var size = new Vector3(footprint.X, h * 0.98f, footprint.Y);
            if (Model(model, new Vector3(at.X, h * i, at.Y), size, paint) is { } fitted)
            {
                materials.AddRange(fitted);
                continue;
            }
            var mat = Materials.Plastic(paint, 0.5f);
            Add(new BoxMesh { Size = size }, new Vector3(at.X, h * (i + 0.5f), at.Y), mat);
            materials.Add(mat);
        }
        Fader?.Register(new Rect2(at - footprint / 2f, footprint), top, materials.ToArray());
    }

    /// <summary>Slope rising along an axis-aligned <paramref name="direction"/>: a wedge (the eraser, say), or a
    /// leaning plank (a ruler or the cutting board). <paramref name="model"/> is fitted when generated.</summary>
    protected void Ramp(Vector2 center, Vector2 direction, float length, float width, float baseHeight, float rise,
        Color color, bool plank = false, string model = null)
    {
        bool alongX = MathF.Abs(direction.X) > 0.5f;
        World.Create(new Position(center), new Ecs.Ramp
        {
            HalfExtents = alongX ? new Vector2(length, width) / 2f : new Vector2(width, length) / 2f,
            Direction = direction,
            BaseHeight = baseHeight,
            Rise = rise,
        });
        if (!Visual)
            return;

        var mat = Materials.Plastic(color, 0.5f);
        var yaw = new Vector3(0f, Mathf.RadToDeg(-direction.Angle()), 0f);
        var at = new Vector3(center.X, baseHeight + rise / 2f, center.Y);
        if (plank)
        {
            // Tilt a long board up towards +X, then turn it to face the direction.
            var pivot = new Node3D { Position = at, RotationDegrees = yaw };
            var tilt = new Node3D { Rotation = new Vector3(0f, 0f, MathF.Atan2(rise, length)) };
            pivot.AddChild(tilt);
            Parent.AddChild(pivot);
            var boardSize = new Vector3(MathF.Sqrt(length * length + rise * rise), 0.4f, width);
            if (Model(model ?? "cutting_board", new Vector3(0f, -0.4f, 0f), boardSize, into: tilt) == null)
                tilt.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = boardSize }, MaterialOverride = mat, Position = new Vector3(0f, -0.2f, 0f) });
        }
        else
        {
            // Wedge models rise towards +X like PrismMesh with LeftToRight = 1.
            var pivot = new Node3D { Position = new Vector3(center.X, baseHeight, center.Y), RotationDegrees = yaw };
            Parent.AddChild(pivot);
            if (model == null || Model(model, Vector3.Zero, new Vector3(length, rise, width), into: pivot) == null)
                pivot.AddChild(new MeshInstance3D
                {
                    Mesh = new PrismMesh { Size = new Vector3(length, rise, width), LeftToRight = 1f },
                    MaterialOverride = mat,
                    Position = new Vector3(0f, rise / 2f, 0f),
                });
        }
    }

    // --- gameplay objects ----------------------------------------------------------------------

    /// <summary>A permanent spill (see <see cref="Liquids"/>); drawn by Main with the puddle shader.</summary>
    protected void Spill(Vector2 at, float height, float radius, LiquidKind kind) =>
        Liquids.Spill(World, at, height, radius, kind, seed: at.X * 0.37f + at.Y * 0.11f);

    /// <summary>An item lying on a surface, picked up by walking into it.</summary>
    protected void Item(PickupKind kind, Vector2 at, float height) =>
        World.Create(new Position(at), new Pickup { Kind = kind, Height = height });

    // --- decoration, floors, walls, lights ---------------------------------------------------

    /// <summary>A model with no collision (rugs, wall panels, antennae...).</summary>
    protected void Decor(string model, Vector3 bottomCenter, Vector3? exactSize = null, float yawDegrees = 0f, Color? tint = null)
    {
        if (!Visual || ModelLibrary.LoadProp(model, exactSize, tint) is not { } prop)
            return;
        prop.Node.Position = bottomCenter;
        prop.Node.RotationDegrees = new Vector3(0f, yawDegrees, 0f);
        Parent.AddChild(prop.Node);
    }

    /// <summary>The floor: a baked texture set (textures/{set}_*.jpg) through the floor shader, which hides
    /// the repeat, or a flat colour.</summary>
    protected void Floor(string textureSet, Color fallback, float toneVariation = 0.25f, float sheenVariation = 0.18f)
    {
        if (!Visual)
            return;

        Material material;
        if (ModelLibrary.LoadTexture($"{textureSet}_albedo.jpg") is { } albedo)
        {
            var shader = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/floor.gdshader") };
            shader.SetShaderParameter("albedo_texture", albedo);
            shader.SetShaderParameter("roughness_texture", ModelLibrary.LoadTexture($"{textureSet}_roughness.jpg"));
            shader.SetShaderParameter("normal_texture", ModelLibrary.LoadTexture($"{textureSet}_normal.jpg"));
            shader.SetShaderParameter("repeats", Bounds.Size / 16f);
            shader.SetShaderParameter("tone_variation", toneVariation);
            shader.SetShaderParameter("sheen_variation", sheenVariation);
            shader.SetShaderParameter("variation_texture", new NoiseTexture2D
            {
                Width = 256,
                Height = 256,
                Seamless = true,
                Noise = new FastNoiseLite { Frequency = 0.012f, FractalOctaves = 3 },
            });
            material = shader;
        }
        else
        {
            material = Materials.Plastic(fallback, 0.6f);
        }
        Add(new PlaneMesh { Size = Bounds.Size }, new Vector3(Bounds.GetCenter().X, 0f, Bounds.GetCenter().Y), material);
    }

    /// <summary>Material for walls: a baked texture set mapped in world space (triplanar), or a colour.</summary>
    protected Material WallMaterial(string textureSet, Color fallback)
    {
        if (ModelLibrary.LoadTexture($"{textureSet}_albedo.jpg") is not { } albedo)
            return Materials.Plastic(fallback, 0.85f);
        return new StandardMaterial3D
        {
            AlbedoTexture = albedo,
            NormalEnabled = true,
            NormalTexture = ModelLibrary.LoadTexture($"{textureSet}_normal.jpg"),
            RoughnessTexture = ModelLibrary.LoadTexture($"{textureSet}_roughness.jpg"),
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = Vector3.One / 16f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
    }

    /// <summary>Back and side walls just outside the play area, plus a low skirting on the camera side.</summary>
    protected void RoomWalls(Material material, float height = 50f)
    {
        if (!Visual)
            return;
        var b = Bounds;
        float w = b.Size.X + 4f, d = b.Size.Y + 4f;
        var center = b.GetCenter();
        Add(new BoxMesh { Size = new Vector3(w, height, 2f) }, new Vector3(center.X, height / 2f, b.Position.Y - 1f), material);
        Add(new BoxMesh { Size = new Vector3(2f, height, d) }, new Vector3(b.Position.X - 1f, height / 2f, center.Y), material);
        Add(new BoxMesh { Size = new Vector3(2f, height, d) }, new Vector3(b.End.X + 1f, height / 2f, center.Y), material);
        Add(new BoxMesh { Size = new Vector3(w, 1.2f, 1f) }, new Vector3(center.X, 0.6f, b.End.Y + 0.5f), material);
    }

    protected OmniLight3D Light(Node3D root, Vector3 at, Color color, float energy, float range, bool shadows = false, bool flicker = false)
    {
        OmniLight3D light = flicker ? new FlickerLight() : new OmniLight3D();
        light.Position = at;
        light.LightColor = color;
        light.LightEnergy = energy;
        light.OmniRange = range;
        light.ShadowEnabled = shadows;
        root.AddChild(light);
        return light;
    }

    /// <summary>Glowing tube (neon, fluorescent) with no collision.</summary>
    protected void Tube(Vector3 from, Vector3 to, float thickness, Color color, float energy = 4f)
    {
        if (!Visual)
            return;
        var along = to - from;
        var x = along.Normalized();
        var up = MathF.Abs(x.Y) > 0.99f ? Vector3.Back : Vector3.Up;
        var z = x.Cross(up).Normalized();
        Parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(along.Length(), thickness, thickness) }, // long along local X
            MaterialOverride = Materials.Glow(color, energy),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Transform = new Transform3D(new Basis(x, z.Cross(x), z), (from + to) / 2f),
        });
    }

    // --- plumbing ----------------------------------------------------------------------------

    /// <summary>Places a generated model if assets/models/{name}.glb exists and returns its materials
    /// (for fading); returns null so the caller can build its primitive fallback.</summary>
    protected BaseMaterial3D[] Model(string name, Vector3 bottomCenter, Vector3? exactSize = null, Color? tint = null, Node3D into = null)
    {
        if (!Visual || ModelLibrary.LoadProp(name, exactSize, tint) is not { } prop)
            return null;
        prop.Node.Position = bottomCenter;
        (into ?? Parent).AddChild(prop.Node);
        return prop.Materials;
    }

    protected void Add(Mesh mesh, Vector3 position, Material material, Vector3 rotationDegrees = default) =>
        Parent.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            Position = position,
            RotationDegrees = rotationDegrees,
        });

    protected static Rect2 Square(Vector2 center, float halfSize) =>
        new(center - new Vector2(halfSize, halfSize), new Vector2(halfSize, halfSize) * 2f);
}

/// <summary>A light that buzzes: now and then it dips or cuts out for a moment, like a failing tube.</summary>
public sealed partial class FlickerLight : OmniLight3D
{
    private readonly Random _random = new();
    private float _base = -1f;
    private float _timer;

    public override void _Process(double delta)
    {
        if (_base < 0f)
            _base = LightEnergy;
        _timer -= (float)delta;
        if (_timer > 0f)
            return;

        if (_random.NextDouble() < 0.08)
        {
            LightEnergy = _base * (float)_random.NextDouble() * 0.3f; // stutter
            _timer = 0.03f + (float)_random.NextDouble() * 0.12f;
        }
        else
        {
            LightEnergy = _base * (0.92f + (float)_random.NextDouble() * 0.08f); // hum
            _timer = 0.05f + (float)_random.NextDouble() * 0.6f;
        }
    }
}
