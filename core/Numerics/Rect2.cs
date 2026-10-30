using System;

namespace BulletHeaven.Numerics;

/// <summary>Axis-aligned rectangle on the ground plane (Godot's Rect2: position = min corner).</summary>
public struct Rect2 : IEquatable<Rect2>
{
    public Vector2 Position;
    public Vector2 Size;

    public Rect2(Vector2 position, Vector2 size)
    {
        Position = position;
        Size = size;
    }

    public Rect2(float x, float y, float width, float height) : this(new Vector2(x, y), new Vector2(width, height)) { }

    public readonly Vector2 End => Position + Size;
    public readonly Vector2 GetCenter() => Position + Size / 2f;

    public readonly bool HasPoint(Vector2 point) =>
        point.X >= Position.X && point.Y >= Position.Y && point.X < Position.X + Size.X && point.Y < Position.Y + Size.Y;

    public readonly Rect2 Grow(float by) => new(Position - new Vector2(by, by), Size + new Vector2(by, by) * 2f);

    public static bool operator ==(Rect2 a, Rect2 b) => a.Position == b.Position && a.Size == b.Size;
    public static bool operator !=(Rect2 a, Rect2 b) => !(a == b);
    public readonly bool Equals(Rect2 other) => this == other;
    public override readonly bool Equals(object obj) => obj is Rect2 other && this == other;
    public override readonly int GetHashCode() => HashCode.Combine(Position, Size);
    public override readonly string ToString() => $"[{Position} {Size}]";
}
