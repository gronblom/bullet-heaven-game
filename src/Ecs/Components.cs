using Arch.Core;
using Godot;

namespace BulletHeaven.Ecs;

// All gameplay runs on the XZ ground plane, stored as Vector2 (x, y) => world (x, 0, y).
// Components are plain data structs; behaviour lives in systems.

public struct Position
{
    public Vector2 Value;
    public Position(Vector2 value) => Value = value;
}

public struct Velocity
{
    public Vector2 Value;
    public Velocity(Vector2 value) => Value = value;
}

/// <summary>Collision radius on the ground plane.</summary>
public struct Radius
{
    public float Value;
    public Radius(float value) => Value = value;
}

public struct Health
{
    public float Current;
    public float Max;
    /// <summary>Seconds of remaining invulnerability after taking a hit.</summary>
    public float Invulnerable;

    public Health(float max)
    {
        Current = max;
        Max = max;
        Invulnerable = 0f;
    }
}

public struct PlayerTag;

/// <summary>Live driving state of a car. Heading is the angle of the car's nose on the ground plane.</summary>
public struct Car
{
    public float Heading;
    public float Throttle; // -1..1, negative brakes then reverses
    public float Steer;    // -1..1, positive turns right on screen
    public bool Handbrake;

    // Written by CarPhysicsSystem, read by visuals.
    public float ForwardSpeed;
    public float Slip; // lateral speed, i.e. how hard the car is drifting

    // Falling off the table (Micro Machines style).
    public float FallTimer;  // > 0 while falling
    public Vector2 LastSafePosition;
    public float LastSafeHeading;
}

/// <summary>Tunable handling. Upgrades modify these.</summary>
public struct CarStats
{
    public float MaxSpeed;
    public float Acceleration;
    public float BrakeForce;
    public float MaxReverse;
    public float TurnRate;      // rad/s at full steering lock
    public float Grip;          // how fast sideways velocity is killed
    public float HandbrakeGrip; // lower = longer drifts
    public float RollingDrag;

    public static CarStats Default => new()
    {
        MaxSpeed = 16f,
        Acceleration = 22f,
        BrakeForce = 40f,
        MaxReverse = 6f,
        TurnRate = 3.6f,
        Grip = 9f,
        HandbrakeGrip = 1.2f,
        RollingDrag = 0.8f,
    };
}

public enum EnemyKind : byte
{
    Ant,
    Beetle,
}

public struct Enemy
{
    public EnemyKind Kind;
    public float Speed;
    public float ContactDamage;
    public int XpValue;
    public Vector2 Knockback;
    public float HitFlash;
}

public struct Bullet
{
    public float Damage;
    public float Lifetime;
    public int PierceLeft;
    public Entity LastHit;
}

public struct XpGem
{
    public int Value;
    public bool Magnetized;
}

/// <summary>Auto-aiming gun mounted on the car.</summary>
public struct Weapon
{
    public float Cooldown;
    public float Timer;
    public float Range;
    public float BulletSpeed;
    public float Damage;
    public float Lifetime;
    public float Spread; // radians between projectiles
    public int Projectiles;
    public int Pierce;

    public static Weapon Default => new()
    {
        Cooldown = 0.45f,
        Range = 14f,
        BulletSpeed = 32f,
        Damage = 6f,
        Lifetime = 0.8f,
        Spread = 0.12f,
        Projectiles = 1,
        Pierce = 0,
    };
}

public enum ObstacleShape : byte
{
    Circle,
    Box,
}

/// <summary>Static prop the car and enemies collide with (mugs, books, ...).</summary>
public struct Obstacle
{
    public ObstacleShape Shape;
    public float Radius;        // Circle
    public Vector2 HalfExtents; // Box, axis aligned
}
