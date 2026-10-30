using System;
using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Numerics;
using BulletHeaven.Systems;

namespace BulletHeaven.Levels;

/// <summary>
/// A level: its playable area, where the player starts, its collision (ECS Obstacle/Ramp entities),
/// its gameplay objects (puddles, items, exits, boss) and a <see cref="LevelLayout"/> describing the
/// visuals for the engine front-end to draw.
///
/// Builders use the helpers below, which create the collision and record the matching visual in one
/// go, so the two can't drift apart. Positions are on the ground plane (x, y) => world (x, height, y).
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
    /// <summary>Sky, light and fog; null keeps the default daylight.</summary>
    public virtual Atmosphere Atmosphere => null;

    protected World World { get; private set; }
    protected LevelLayout Layout { get; private set; }

    /// <summary>Creates the level's entities in <paramref name="world"/> and returns what to draw.</summary>
    public LevelLayout Build(World world)
    {
        World = world;
        Layout = new LevelLayout();
        BuildLevel();
        return Layout;
    }

    protected abstract void BuildLevel();

    // --- collision ---------------------------------------------------------------------------

    protected void BoxObstacle(Vector2 center, Vector2 footprint, float top) =>
        World.Create(new Position(center), new Obstacle { Shape = ObstacleShape.Box, HalfExtents = footprint / 2f, Height = top });

    protected void CircleObstacle(Vector2 center, float radius, float top) =>
        World.Create(new Position(center), new Obstacle { Shape = ObstacleShape.Circle, Radius = radius, Height = top });

    // --- solid props -------------------------------------------------------------------------

    /// <summary>Box-shaped prop standing on <paramref name="baseHeight"/>: the model stretched to the footprint,
    /// else a coloured box. Tall ones fade when they hide the player.</summary>
    protected void Block(Vector2 center, Vector2 footprint, float baseHeight, float height, Color color,
        string model = null, Color? tint = null)
    {
        BoxObstacle(center, footprint, baseHeight + height);
        var size = new Vector3(footprint.X, height, footprint.Y);
        Layout.Props.Add(new PropVisual(model, new Vector3(center.X, baseHeight, center.Y), size, Shape.Box, size,
            new Look(color), Tint: tint, Fade: height > 1f ? new Rect2(center - footprint / 2f, footprint) : null,
            FadeTop: baseHeight + height));
    }

    /// <summary>Round prop (fruit, mug, urn...): the model at its natural size, else a sphere or cylinder.
    /// Solid; climbable when low enough.</summary>
    protected void Round(string model, Vector2 at, float baseHeight, float radius, float height, Color fallback,
        bool sphere = true, float fadeRadius = 0f)
    {
        CircleObstacle(at, radius, baseHeight + height);
        Layout.Props.Add(new PropVisual(model, new Vector3(at.X, baseHeight, at.Y), null, sphere ? Shape.Sphere : Shape.Cylinder,
            new Vector3(radius * 2f, height, radius * 2f), new Look(fallback, 0.3f),
            Fade: height > 2f ? Square(at, fadeRadius > 0f ? fadeRadius : radius) : null, FadeTop: baseHeight + height));
    }

    /// <summary>A stack of tinted blocks or books forming one climbable step, fading as one.</summary>
    protected void Stack(Vector2 at, Vector2 footprint, float top, string model, Color[] palette, int offset, float layer = 1.6f)
    {
        BoxObstacle(at, footprint, top);
        int count = Math.Max(1, (int)MathF.Round(top / layer));
        float h = top / count;
        for (int i = 0; i < count; i++)
        {
            var paint = palette[(offset + i) % palette.Length];
            var size = new Vector3(footprint.X, h * 0.98f, footprint.Y);
            Layout.Props.Add(new PropVisual(model, new Vector3(at.X, h * i, at.Y), size, Shape.Box, size, new Look(paint, 0.5f),
                Tint: paint, Fade: new Rect2(at - footprint / 2f, footprint), FadeTop: top));
        }
    }

    /// <summary>Slope rising along an axis-aligned <paramref name="direction"/>: a wedge (the eraser, say), or a
    /// leaning plank (a ruler or the cutting board).</summary>
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

        float yaw = Mathf.RadToDeg(-direction.Angle());
        if (plank)
        {
            var board = new Vector3(MathF.Sqrt(length * length + rise * rise), 0.4f, width);
            Layout.Planks.Add(new PlankVisual(model ?? "cutting_board", new Vector3(center.X, baseHeight + rise / 2f, center.Y), yaw,
                MathF.Atan2(rise, length), board, new Look(color, 0.5f)));
        }
        else
        {
            var size = new Vector3(length, rise, width);
            Layout.Props.Add(new PropVisual(model, new Vector3(center.X, baseHeight, center.Y), size, Shape.Wedge, size,
                new Look(color, 0.5f), YawDegrees: yaw));
        }
    }

    // --- gameplay objects ----------------------------------------------------------------------

    /// <summary>A permanent spill (see <see cref="Liquids"/>); the front-end draws it with the puddle shader.</summary>
    protected void Spill(Vector2 at, float height, float radius, LiquidKind kind) =>
        Liquids.Spill(World, at, height, radius, kind, seed: at.X * 0.37f + at.Y * 0.11f);

    /// <summary>An item lying on a surface, picked up by walking into it.</summary>
    protected void Item(PickupKind kind, Vector2 at, float height) =>
        World.Create(new Position(at), new Pickup { Kind = kind, Height = height });

    // --- visuals only --------------------------------------------------------------------------

    /// <summary>A model with no collision (rugs, wall panels...); nothing if it isn't generated.</summary>
    protected void Decor(string model, Vector3 bottomCenter, Vector3? exactSize = null, float yawDegrees = 0f, Color? tint = null) =>
        Layout.Props.Add(new PropVisual(model, bottomCenter, exactSize, Shape.None, Vector3.Zero, default, yawDegrees, tint));

    /// <summary>A model (no collision) with a primitive fallback; register a fade footprint when it's tall.</summary>
    protected void Model(string model, Vector3 bottomCenter, Vector3? exactSize, Shape fallback, Vector3 fallbackSize, Look look,
        Rect2? fade = null, float fadeTop = 0f) =>
        Layout.Props.Add(new PropVisual(model, bottomCenter, exactSize, fallback, fallbackSize, look, Fade: fade, FadeTop: fadeTop));

    /// <summary>A plain primitive (no collision): windows, lips, lamp stands...</summary>
    protected void Primitive(Shape shape, Vector3 bottomCenter, Vector3 size, Look look, float topScale = 1f, bool castShadows = true) =>
        Layout.Props.Add(new PropVisual(null, bottomCenter, size, shape, size, look, TopScale: topScale, CastShadows: castShadows));

    protected void Floor(string textureSet, Color fallback, float toneVariation = 0.25f, float sheenVariation = 0.18f) =>
        Layout.Floor = new FloorVisual(textureSet, fallback, toneVariation, sheenVariation);

    protected void RoomWalls(Look look, string panelModel = null) => Layout.Walls = new WallsVisual(look, PanelModel: panelModel);

    protected void Light(Vector3 at, Color color, float energy, float range, bool shadows = false, bool flicker = false) =>
        Layout.Lights.Add(new LightVisual(at, color, energy, range, shadows, flicker));

    protected static Rect2 Square(Vector2 center, float halfSize) =>
        new(center - new Vector2(halfSize, halfSize), new Vector2(halfSize, halfSize) * 2f);
}
