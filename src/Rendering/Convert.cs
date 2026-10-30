using Core = BulletHeaven.Numerics;
using Godot;

namespace BulletHeaven.Rendering;

/// <summary>Core (engine-independent) maths types to Godot's, at the edge between gameplay and drawing.</summary>
public static class Convert
{
    public static Vector2 ToGodot(this Core.Vector2 v) => new(v.X, v.Y);
    public static Vector3 ToGodot(this Core.Vector3 v) => new(v.X, v.Y, v.Z);
    public static Color ToGodot(this Core.Color c) => new(c.R, c.G, c.B, c.A);
    public static Rect2 ToGodot(this Core.Rect2 r) => new(r.Position.ToGodot(), r.Size.ToGodot());
    public static Core.Vector2 ToCore(this Vector2 v) => new(v.X, v.Y);
}
