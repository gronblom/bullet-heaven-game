using System;
using System.Collections.Generic;
using Arch.Core;
using Godot;

namespace BulletHeaven.Ecs;

/// <summary>
/// Uniform grid over the play area, rebuilt from scratch every frame (counting sort, no allocations
/// after warm-up). Used for enemy separation, bullet hits, ramming and auto-aim.
/// </summary>
public sealed class SpatialGrid
{
    private readonly float _invCell;
    private readonly Vector2 _min;
    private readonly int _cols;
    private readonly int _rows;
    private readonly int[] _cellStart;

    private int[] _itemCell = new int[256];
    private int[] _sorted = new int[256];

    public Entity[] Entities = new Entity[256];
    public Vector2[] Positions = new Vector2[256];
    public float[] Radii = new float[256];
    public float[] Heights = new float[256];
    public int Count { get; private set; }

    /// <summary>Largest item radius added since the last Clear; widens queries so big items are never missed.</summary>
    private float _maxRadius;

    public SpatialGrid(Rect2 area, float cellSize)
    {
        _invCell = 1f / cellSize;
        _min = area.Position;
        _cols = Math.Max(1, (int)MathF.Ceiling(area.Size.X * _invCell));
        _rows = Math.Max(1, (int)MathF.Ceiling(area.Size.Y * _invCell));
        _cellStart = new int[_cols * _rows + 1];
    }

    public void Clear()
    {
        Count = 0;
        _maxRadius = 0f;
    }

    public void Add(Entity entity, Vector2 position, float radius, float height)
    {
        if (Count == Entities.Length)
        {
            int size = Count * 2;
            Array.Resize(ref Entities, size);
            Array.Resize(ref Positions, size);
            Array.Resize(ref Radii, size);
            Array.Resize(ref Heights, size);
            Array.Resize(ref _itemCell, size);
            Array.Resize(ref _sorted, size);
        }

        Entities[Count] = entity;
        Positions[Count] = position;
        Radii[Count] = radius;
        Heights[Count] = height;
        _maxRadius = MathF.Max(_maxRadius, radius);
        Count++;
    }

    public void Build()
    {
        Array.Clear(_cellStart);
        for (int i = 0; i < Count; i++)
        {
            int cell = CellIndex(CellX(Positions[i].X), CellY(Positions[i].Y));
            _itemCell[i] = cell;
            _cellStart[cell + 1]++;
        }

        for (int c = 0; c < _cols * _rows; c++)
            _cellStart[c + 1] += _cellStart[c];

        // Fill using a running cursor per cell; _cellStart is restored afterwards.
        for (int i = 0; i < Count; i++)
            _sorted[_cellStart[_itemCell[i]]++] = i;

        for (int c = _cols * _rows; c > 0; c--)
            _cellStart[c] = _cellStart[c - 1];
        _cellStart[0] = 0;
    }

    /// <summary>Adds the index of every item whose circle overlaps the query circle,
    /// stopping early once <paramref name="maxResults"/> are found.</summary>
    public void Query(Vector2 center, float radius, List<int> results, int maxResults = int.MaxValue)
    {
        float reach = radius + _maxRadius;
        int x0 = CellX(center.X - reach), x1 = CellX(center.X + reach);
        int y0 = CellY(center.Y - reach), y1 = CellY(center.Y + reach);

        for (int cy = y0; cy <= y1; cy++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                int cell = CellIndex(cx, cy);
                for (int k = _cellStart[cell]; k < _cellStart[cell + 1]; k++)
                {
                    int i = _sorted[k];
                    float r = radius + Radii[i];
                    if ((Positions[i] - center).LengthSquared() <= r * r)
                    {
                        results.Add(i);
                        if (results.Count >= maxResults)
                            return;
                    }
                }
            }
        }
    }

    /// <summary>Index of the nearest item within range and within <paramref name="maxHeightDiff"/> of
    /// <paramref name="height"/>, or -1.</summary>
    public int Nearest(Vector2 center, float range, List<int> scratch, float height, float maxHeightDiff)
    {
        scratch.Clear();
        Query(center, range, scratch);
        int best = -1;
        float bestDist = float.MaxValue;
        foreach (int i in scratch)
        {
            if (MathF.Abs(Heights[i] - height) > maxHeightDiff)
                continue;
            float d = (Positions[i] - center).LengthSquared();
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    private int CellX(float x) => Math.Clamp((int)((x - _min.X) * _invCell), 0, _cols - 1);
    private int CellY(float y) => Math.Clamp((int)((y - _min.Y) * _invCell), 0, _rows - 1);
    private int CellIndex(int cx, int cy) => cy * _cols + cx;
}
