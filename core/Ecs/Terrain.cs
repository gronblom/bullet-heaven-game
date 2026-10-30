using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Numerics;

namespace BulletHeaven.Ecs;

/// <summary>
/// Static level geometry gathered from Obstacle and Ramp entities once after the level is built.
/// Answers "how high is the ground here?" and pushes movers out of anything too tall to step onto.
/// Props are bucketed into a coarse grid so each query only looks at the few props nearby.
/// </summary>
public sealed class Terrain
{
    /// <summary>Ground height outside the level bounds: effectively a bottomless drop.</summary>
    public const float OffTable = -1000f;
    private const float CellSize = 8f;

    private struct Solid
    {
        public Vector2 Center;
        public bool IsRamp;
        public Obstacle Obstacle;
        public Ramp Ramp;
    }

    private readonly Rect2 _bounds;
    private readonly Solid[] _solids;
    private readonly int[][] _cells;
    private readonly int _cols, _rows;
    private readonly int[] _visited; // per-solid stamp so a query handles each solid once
    private int _stamp;

    private Terrain(Rect2 bounds, Solid[] solids)
    {
        _bounds = bounds;
        _solids = solids;
        _visited = new int[solids.Length];
        _cols = Math.Max(1, (int)MathF.Ceiling(bounds.Size.X / CellSize));
        _rows = Math.Max(1, (int)MathF.Ceiling(bounds.Size.Y / CellSize));

        var lists = new List<int>[_cols * _rows];
        for (int c = 0; c < lists.Length; c++)
            lists[c] = new List<int>();
        for (int i = 0; i < solids.Length; i++)
        {
            var half = solids[i].IsRamp ? solids[i].Ramp.HalfExtents
                : solids[i].Obstacle.Shape == ObstacleShape.Circle ? new Vector2(solids[i].Obstacle.Radius, solids[i].Obstacle.Radius)
                : solids[i].Obstacle.HalfExtents;
            ForCells(solids[i].Center - half, solids[i].Center + half, c => lists[c].Add(i));
        }
        _cells = Array.ConvertAll(lists, l => l.ToArray());
    }

    public static Terrain FromWorld(World world, Rect2 bounds)
    {
        var solids = new List<Solid>();
        world.Query(new QueryDescription().WithAll<Position, Obstacle>(),
            (ref Position position, ref Obstacle obstacle) => solids.Add(new Solid { Center = position.Value, Obstacle = obstacle }));
        world.Query(new QueryDescription().WithAll<Position, Ramp>(),
            (ref Position position, ref Ramp ramp) => solids.Add(new Solid { Center = position.Value, IsRamp = true, Ramp = ramp }));
        return new Terrain(bounds, solids.ToArray());
    }

    public bool InBounds(Vector2 point) => _bounds.HasPoint(point);

    /// <summary>Highest surface under a point: the floor (0), prop tops and ramps.</summary>
    public float GroundHeight(Vector2 point)
    {
        if (!_bounds.HasPoint(point))
            return OffTable;

        float height = 0f;
        foreach (int i in _cells[CellIndex(point)])
        {
            ref var solid = ref _solids[i];
            var local = point - solid.Center;
            if (solid.IsRamp)
            {
                if (MathF.Abs(local.X) <= solid.Ramp.HalfExtents.X && MathF.Abs(local.Y) <= solid.Ramp.HalfExtents.Y)
                    height = MathF.Max(height, solid.Ramp.HeightAt(local));
            }
            else if (solid.Obstacle.Height > height && Contains(point, solid.Center, solid.Obstacle))
            {
                height = solid.Obstacle.Height;
            }
        }
        return height;
    }

    public Vector2 ResolveWalls(ref Vector2 point, float radius, float height, float stepUp) =>
        ResolveWalls(ref point, radius, height, stepUp, out _);

    /// <summary>
    /// Pushes a circle at <paramref name="height"/> out of every solid whose surface is more than
    /// <paramref name="stepUp"/> above it. Returns the summed push-out normals (zero if nothing was
    /// hit) and the top of the highest wall that was hit.
    /// </summary>
    public Vector2 ResolveWalls(ref Vector2 point, float radius, float height, float stepUp, out float wallTop)
    {
        var normals = Vector2.Zero;
        float limit = height + stepUp;
        wallTop = 0f;
        _stamp++;

        var reach = new Vector2(radius, radius);
        int x0 = CellX(point.X - reach.X), x1 = CellX(point.X + reach.X);
        int y0 = CellY(point.Y - reach.Y), y1 = CellY(point.Y + reach.Y);
        for (int cy = y0; cy <= y1; cy++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                foreach (int i in _cells[cy * _cols + cx])
                {
                    if (_visited[i] == _stamp)
                        continue;
                    _visited[i] = _stamp;
                    ref var solid = ref _solids[i];

                    float top;
                    bool hit;
                    Vector2 n = default;
                    if (solid.IsRamp)
                    {
                        // A ramp is a wall where its nearest edge is too high (its back and upper sides).
                        var nearest = (point - solid.Center).Clamp(-solid.Ramp.HalfExtents, solid.Ramp.HalfExtents);
                        top = solid.Ramp.HeightAt(nearest);
                        hit = top > limit && Resolve(ref point, radius, solid.Center, ObstacleShape.Box, 0f, solid.Ramp.HalfExtents, out n);
                    }
                    else
                    {
                        top = solid.Obstacle.Height;
                        hit = top > limit && Resolve(ref point, radius, solid.Center, solid.Obstacle.Shape, solid.Obstacle.Radius,
                            solid.Obstacle.HalfExtents, out n);
                    }

                    if (hit)
                    {
                        normals += n;
                        wallTop = MathF.Max(wallTop, top);
                    }
                }
            }
        }

        return normals;
    }

    private void ForCells(Vector2 min, Vector2 max, Action<int> action)
    {
        for (int cy = CellY(min.Y); cy <= CellY(max.Y); cy++)
            for (int cx = CellX(min.X); cx <= CellX(max.X); cx++)
                action(cy * _cols + cx);
    }

    private int CellIndex(Vector2 p) => CellY(p.Y) * _cols + CellX(p.X);
    private int CellX(float x) => Math.Clamp((int)((x - _bounds.Position.X) / CellSize), 0, _cols - 1);
    private int CellY(float y) => Math.Clamp((int)((y - _bounds.Position.Y) / CellSize), 0, _rows - 1);

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
