using UnityEngine;
using Core = BulletHeaven.Numerics;

namespace BulletHeaven.Unity
{
    /// <summary>
    /// From the shared core's coordinates to Unity's. The core works like Godot: ground plane (x, y)
    /// with screen-up = -y, height separate, right-handed. Unity is left-handed, so the ground plane
    /// maps to (x, height, -y): east stays right, "up the screen" is +z, away from the camera.
    /// Rotations about Y flip sign; a model that faces +X turned to heading h gets yaw h (degrees).
    /// </summary>
    public static class Conv
    {
        public static Vector3 World(Core.Vector2 ground, float height) => new Vector3(ground.X, height, -ground.Y);
        public static Vector3 ToUnity(this Core.Vector3 v) => new Vector3(v.X, v.Y, -v.Z);
        /// <summary>A size (width, height, depth): no flip.</summary>
        public static Vector3 Size(this Core.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Color ToUnity(this Core.Color c) => new Color(c.R, c.G, c.B, c.A);
        public static Core.Vector3 ToCore(this Vector3 v) => new Core.Vector3(v.x, v.y, -v.z);

        /// <summary>A yaw given Godot-style (degrees, the layout's convention) as a Unity rotation.</summary>
        public static Quaternion Yaw(float godotDegrees) => Quaternion.Euler(0f, -godotDegrees, 0f);

        /// <summary>Unity yaw (degrees) that turns a +X-facing model towards a ground-plane direction.</summary>
        public static float Heading(Core.Vector2 direction) => Mathf.Atan2(direction.Y, direction.X) * Mathf.Rad2Deg;
        public static float Heading(float coreAngleRadians) => coreAngleRadians * Mathf.Rad2Deg;
    }
}
