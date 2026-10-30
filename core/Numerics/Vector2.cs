using System;

namespace BulletHeaven.Numerics;

/// <summary>
/// 2D vector on the ground plane. Same names and behaviour as Godot's Vector2 (the game was written
/// against it), so gameplay code reads the same in every engine. Front-ends convert at the edges.
/// </summary>
public struct Vector2 : IEquatable<Vector2>
{
    public float X;
    public float Y;

    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static readonly Vector2 Zero = new(0f, 0f);
    public static readonly Vector2 One = new(1f, 1f);
    public static readonly Vector2 Right = new(1f, 0f);
    public static readonly Vector2 Left = new(-1f, 0f);
    public static readonly Vector2 Up = new(0f, -1f);   // Godot convention: up the screen is -Y
    public static readonly Vector2 Down = new(0f, 1f);

    public static Vector2 FromAngle(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));

    public readonly float Length() => MathF.Sqrt(X * X + Y * Y);
    public readonly float LengthSquared() => X * X + Y * Y;
    public readonly float Angle() => MathF.Atan2(Y, X);
    public readonly float Dot(Vector2 other) => X * other.X + Y * other.Y;
    public readonly float DistanceTo(Vector2 other) => (other - this).Length();
    public readonly float DistanceSquaredTo(Vector2 other) => (other - this).LengthSquared();
    public readonly Vector2 Orthogonal() => new(Y, -X);
    public readonly Vector2 Abs() => new(MathF.Abs(X), MathF.Abs(Y));

    public readonly Vector2 Normalized()
    {
        float length = Length();
        return length == 0f ? Zero : new Vector2(X / length, Y / length);
    }

    public readonly Vector2 LimitLength(float max = 1f)
    {
        float length = Length();
        return length > 0f && length > max ? this / length * max : this;
    }

    public readonly Vector2 MoveToward(Vector2 to, float delta)
    {
        var diff = to - this;
        float length = diff.Length();
        return length <= delta || length < 1e-5f ? to : this + diff / length * delta;
    }

    public readonly Vector2 Lerp(Vector2 to, float weight) => new(X + (to.X - X) * weight, Y + (to.Y - Y) * weight);

    public readonly Vector2 Clamp(Vector2 min, Vector2 max) =>
        new(Math.Clamp(X, min.X, max.X), Math.Clamp(Y, min.Y, max.Y));

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator -(Vector2 a) => new(-a.X, -a.Y);
    public static Vector2 operator *(Vector2 a, float s) => new(a.X * s, a.Y * s);
    public static Vector2 operator *(float s, Vector2 a) => new(a.X * s, a.Y * s);
    public static Vector2 operator *(Vector2 a, Vector2 b) => new(a.X * b.X, a.Y * b.Y);
    public static Vector2 operator /(Vector2 a, float s) => new(a.X / s, a.Y / s);
    public static Vector2 operator /(Vector2 a, Vector2 b) => new(a.X / b.X, a.Y / b.Y);
    public static bool operator ==(Vector2 a, Vector2 b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);

    public readonly bool Equals(Vector2 other) => this == other;
    public override readonly bool Equals(object obj) => obj is Vector2 other && this == other;
    public override readonly int GetHashCode() => HashCode.Combine(X, Y);
    public override readonly string ToString() => $"({X:0.###}, {Y:0.###})";
}
