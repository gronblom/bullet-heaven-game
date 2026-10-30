using System;

namespace BulletHeaven.Numerics;

/// <summary>Linear-ish RGBA colour as used by level layouts and liquids (same meaning as Godot's Color).</summary>
public struct Color : IEquatable<Color>
{
    public float R;
    public float G;
    public float B;
    public float A;

    public Color(float r, float g, float b, float a = 1f)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static readonly Color White = new(1f, 1f, 1f);
    public static readonly Color Black = new(0f, 0f, 0f);

    /// <summary>Blends towards white by <paramref name="amount"/>.</summary>
    public readonly Color Lightened(float amount) =>
        new(R + (1f - R) * amount, G + (1f - G) * amount, B + (1f - B) * amount, A);

    public static bool operator ==(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
    public static bool operator !=(Color a, Color b) => !(a == b);
    public readonly bool Equals(Color other) => this == other;
    public override readonly bool Equals(object obj) => obj is Color other && this == other;
    public override readonly int GetHashCode() => HashCode.Combine(R, G, B, A);
}
