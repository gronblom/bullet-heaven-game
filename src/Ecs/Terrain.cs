using System;
using System.Collections.Generic;
using Arch.Core;
using Godot;

namespace BulletHeaven.Ecs;

/// <summary>
/// Static level geometry gathered from Obstacle and Ramp entities once after the level is built.
/// Answers "how high is the ground here?" and pushes movers out of anything too tall to step onto.
/// </summary>
public sealed class Terrain
{
    /// <summary>Ground height beyond the table edge: effectively a bottomless drop.</summary>
    public const float OffTable = -1000f;

    private readonly Rect2 _bounds;
    private readonly (Vector2 Position, Obstacle Obstacle)[] _obstacles;
    private readonly (Vector2 Position, Ramp Ramp)[] _ramps;

    private Terrain(Rect2 bounds, (Vector2, Obstacle)[] obstacles, (Vector2, Ramp)[] ramps)
    {
        _bounds = bounds;
        _obstacles = obstacles;
        _ramps = ramps;
    }

    public static Terrain FromWorld(World world, Rect2 bounds)
    {
        var obstacles = new List<(Vector2, Obstacle)>();
        var ramps = new List<(Vector2, Ramp)>();
        world.Query(new QueryDescription().WithAll<Position, Obstacle>(),
            (ref Position position, ref Obstacle obstacle) => obstacles.Add((position.Value, obstacle)));
        world.Query(new QueryDescription().WithAll<Position, Ramp>(),
            (ref Position position, ref Ramp ramp) => ramps.Add((position.Value, ramp)));
        return new Terrain(bounds, obstacles.ToArray(), ramps.ToArray());
    }

    public bool OnTable(Vector2 point) => _bounds.HasPoint(point);

    /// <summary>Highest surface under a point: table (0), prop tops and ramps.</summary>
    public float GroundHeight(Vector2 point)
    {
        if (!_bounds.HasPoint(point))
            return OffTable;

        float height = 0f;
        foreach (var (center, obstacle) in _obstacles)
        {
            if (obstacle.Height > height && Contains(point, center, obstacle))
                height = obstacle.Height;
        }
        foreach (var (center, ramp) in _ramps)
        {
            var local = point - center;
            if (MathF.Abs(local.X) <= ramp.HalfExtents.X && MathF.Abs(local.Y) <= ramp.HalfExtents.Y)
                height = MathF.Max(height, ramp.HeightAt(local));
        }
        return height;
    }

    /// <summary>
    /// Pushes a circle at <paramref name="height"/> out of every solid whose surface is more than
    /// <paramref name="stepUp"/> above it. Returns the summed push-out normals (zero if nothing was hit).
    /// </summary>
    public Vector2 ResolveWalls(ref Vector2 point, float radius, float height, float stepUp)
    {
        var normals = Vector2.Zero;
        float limit = height + stepUp;

        foreach (var (center, obstacle) in _obstacles)
        {
            if (obstacle.Height > limit && Resolve(ref point, radius, center, obstacle.Shape, obstacle.Radius, obstacle.HalfExtents, out var n))
                normals += n;
        }

        foreach (var (center, ramp) in _ramps)
        {
            // A ramp is a wall where its nearest edge is too high (its back and upper sides).
            var nearest = (point - center).Clamp(-ramp.HalfExtents, ramp.HalfExtents);
            if (ramp.HeightAt(nearest) > limit && Resolve(ref point, radius, center, ObstacleShape.Box, 0f, ramp.HalfExtents, out var n))
                normals += n;
        }

        return normals;
    }

    private static bool Contains(Vector2 point, Vector2 center, in Obstacle obstacle)
    {
        var local = point - center;
        return obstacle.Shape == ObstacleShape.Circle
            ? local.LengthSquared() <= obstacle.Radius * obstacle.Radius
            : MathF.Abs(local.X) <= obstacle.HalfExtents.X && MathF.Abs(local.Y) <= obstacle.HalfExtents.Y;
    }

    /// <summary>Moves a circle out of a circle or box. Returns true with the push-out normal if they overlapped.</summary>
    private static bool Resolve(ref Vector2 point, float radius, Vector2 center, ObstacleShape shape, float shapeRadius,
        Vector2 halfExtents, out Vector2 normal)
    {
        normal = Vector2.Zero;
        if (shape == ObstacleShape.Circle)
        {
            var diff = point - center;
            float minDist = radius + shapeRadius;
            float distSq = diff.LengthSquared();
            if (distSq >= minDist * minDist)
                return false;

            float dist = MathF.Sqrt(distSq);
            normal = dist > 1e-4f ? diff / dist : Vector2.Right;
            point = center + normal * minDist;
            return true;
        }

        var min = center - halfExtents;
        var max = center + halfExtents;
        var closest = point.Clamp(min, max);
        var delta = point - closest;
        float dSq = delta.LengthSquared();

        if (dSq > 1e-8f)
        {
            if (dSq >= radius * radius)
                return false;
            float d = MathF.Sqrt(dSq);
            normal = delta / d;
            point = closest + normal * radius;
            return true;
        }

        // Centre is inside the box: push out along the shallowest axis.
        float left = point.X - min.X, right = max.X - point.X;
        float top = point.Y - min.Y, bottom = max.Y - point.Y;
        float smallest = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
        if (smallest == left) { normal = Vector2.Left; point.X = min.X - radius; }
        else if (smallest == right) { normal = Vector2.Right; point.X = max.X + radius; }
        else if (smallest == top) { normal = Vector2.Up; point.Y = min.Y - radius; }
        else { normal = Vector2.Down; point.Y = max.Y + radius; }
        return true;
    }
}
