using System;

namespace BulletHeaven.Numerics;

/// <summary>3D point (Y up), for the few gameplay values that live in 3D (lightning arcs, level visuals).</summary>
public struct Vector3 : IEquatable<Vector3>
{
    public float X;
    public float Y;
    public float Z;

    public Vector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly Vector3 Zero = new(0f, 0f, 0f);
    public static readonly Vector3 One = new(1f, 1f, 1f);
    public static readonly Vector3 Up = new(0f, 1f, 0f);

    public readonly float Length() => MathF.Sqrt(X * X + Y * Y + Z * Z);

    public readonly Vector3 Lerp(Vector3 to, float weight) =>
        new(X + (to.X - X) * weight, Y + (to.Y - Y) * weight, Z + (to.Z - Z) * weight);

    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3 operator *(Vector3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vector3 operator /(Vector3 a, float s) => new(a.X / s, a.Y / s, a.Z / s);
    public static bool operator ==(Vector3 a, Vector3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);

    public readonly bool Equals(Vector3 other) => this == other;
    public override readonly bool Equals(object obj) => obj is Vector3 other && this == other;
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override readonly string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
