using Arch.Core;
using BulletHeaven.Numerics;

namespace BulletHeaven.Ecs;

// Horizontal movement runs on the XZ ground plane, stored as Vector2 (x, y) => world (x, 0, y).
// Height is a separate concern (see Elevation and Terrain). Components are plain data structs;
// behaviour lives in systems.

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

/// <summary>Live driving state of a car (optional player vehicle). Heading is the angle of the car's nose on the ground plane.</summary>
public struct Car
{
    public float Heading;
    public float Throttle; // -1..1, negative brakes then reverses
    public float Steer;    // -1..1, positive turns right on screen
    public bool Handbrake;
    public bool Jump;

    // Written by CarPhysicsSystem, read by visuals.
    public float ForwardSpeed;
    public float Slip; // lateral speed, i.e. how hard the car is drifting
    public float JumpCooldown;
}

/// <summary>Direct-control character: push a direction and it runs that way.</summary>
public struct Runner
{
    public Vector2 Move;   // input, length <= 1, in ground-plane axes
    public bool JumpPressed;
    public bool JumpHeld;
    public float Facing;   // angle the character looks towards (visual + nothing else)

    public float JumpBuffer; // remembers a jump press for a moment before landing
    public bool Jumped;      // since last grounded; blocks coyote-time double jumps
    public bool Gliding;     // parachute open
}

public struct RunnerStats
{
    public float MaxSpeed;
    public float Acceleration;
    public float AirAcceleration;
    public float Friction;
    public float JumpSpeed;
    public float StompBounce;

    public static RunnerStats Default => new()
    {
        MaxSpeed = 7.5f,
        Acceleration = 70f,
        AirAcceleration = 30f,
        Friction = 60f,
        JumpSpeed = 9.5f, // ~2 units high: enough to hop up one toy block or book
        StompBounce = 7f,
    };
}

/// <summary>Height above the table top and vertical motion. Entities without it stay on the ground.</summary>
public struct Elevation
{
    public float Height;
    public float VerticalSpeed;
    public bool Grounded;
    public bool Climbing; // scaling a wall (bugs); gravity is suspended
    public float MaxFallSpeed; // > 0 caps falling speed this frame (parachute)
    public float AirTime;
    /// <summary>Downward speed at the moment of landing; non-zero only on the landing frame.</summary>
    public float LandingImpact;
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
    public float JumpSpeed;

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
        JumpSpeed = 7f,
    };
}

public enum EnemyKind : byte
{
    Ant,
    Beetle,
    Queen,
}

/// <summary>Boss behaviour state (the Queen Ant): summons, telegraphed charges.</summary>
public struct Boss
{
    public float SummonTimer;
    public float ChargeTimer;
    public float Telegraph; // > 0 while winding up a charge
    public float Charging;  // > 0 while charging
    public Vector2 ChargeDirection;
}

public struct Enemy
{
    public EnemyKind Kind;
    public float Speed;
    public float ClimbSpeed;
    public float ContactDamage;
    public int XpValue;
    public Vector2 Knockback;
    public float HitFlash;
    public float WalkPhase; // animation only: advances with distance walked
}

public struct Bullet
{
    public float Damage;
    public float Lifetime;
    public int PierceLeft;
    public Entity LastHit;
    public float Height; // visual only; bullets hit anything they pass over
}

public struct XpGem
{
    public int Value;
    public bool Magnetized;
}

public enum WeaponKind : byte
{
    Minigun,
    LightningWand,
    SeedMortar,
}

/// <summary>A weapon the player carries. Each is its own entity, so the player can carry several.
/// Its stats come from <c>WeaponStats</c> by kind plus the modifiers picked for it (stack counts).</summary>
public struct WeaponSlot
{
    public WeaponKind Kind;
    public byte Damage;  // +40% damage each
    public byte Rate;    // +25% fire rate each
    public byte Count;   // +1 stream / orb / seed each
    public byte Pierce;  // minigun: +1 pierce; wand: +2 chain targets
    public byte Area;    // minigun: range; wand: zap radius; mortar: blast radius
    public byte Special; // wand: +2 s orb life each; mortar: seeds leave sticky jam
    public float Timer;
    public float Spin; // minigun barrel spin, 0..1 (visual)
}

public enum LiquidKind : byte
{
    Water,
    Coffee,
    Juice,
    Honey,
    Jam,
}

/// <summary>A spilled liquid. Anything walking through it at its height is slowed.</summary>
public struct Puddle
{
    public LiquidKind Kind;
    public float Radius;
    public float Height;
    public float Slow;     // speed multiplier inside (0.3 = very sticky)
    public float Lifetime; // < 0: permanent
    public float Age;
    public float Seed;     // shape variation (visual)
}

public enum PickupKind : byte
{
    Parachute,
}

/// <summary>An item lying in the level, collected by touching it.</summary>
public struct Pickup
{
    public PickupKind Kind;
    public float Height;
}

/// <summary>Slow floating ball of lightning that zaps nearby bugs with chain lightning.</summary>
public struct LightningOrb
{
    public float Damage;
    public float ZapRadius;
    public int MaxTargets;
    public float ZapTimer;
    public float Lifetime;
    public float Height;
}

/// <summary>Seed lobbed in an arc that explodes where it lands.</summary>
public struct Shell
{
    public Vector2 From;
    public Vector2 To;
    public float StartHeight;
    public float EndHeight;
    public float Apex;
    public float Flight;
    public float Duration;
    public float Damage;
    public float BlastRadius;
    public bool Sticky;  // leaves a jam puddle where it lands
    public float Height; // current, for drawing
}

/// <summary>Explosion flash (visual only; damage is dealt when it spawns).</summary>
public struct Blast
{
    public float Age;
    public float Radius;
    public float Height;
}

public enum ObstacleShape : byte
{
    Circle,
    Box,
}

/// <summary>
/// Static solid prop (mugs, books, ...). Its top is a surface you can land on; its sides are walls
/// for anything below the top that can't step up onto it.
/// </summary>
public struct Obstacle
{
    public ObstacleShape Shape;
    public float Radius;        // Circle
    public Vector2 HalfExtents; // Box, axis aligned
    public float Height;        // top surface above the table
}

/// <summary>Axis-aligned wedge that rises along <see cref="Direction"/>. Drive up the low end;
/// its high sides act as walls. Leaving the high end at speed launches you.</summary>
public struct Ramp
{
    public Vector2 HalfExtents;
    public Vector2 Direction; // unit vector along +X, -X, +Y or -Y
    public float BaseHeight;
    public float Rise;

    /// <summary>Surface height at an offset from the ramp centre (assumed inside the footprint).</summary>
    public readonly float HeightAt(Vector2 local)
    {
        float halfLength = Mathf.Abs(Direction.X) > 0.5f ? HalfExtents.X : HalfExtents.Y;
        float t = (local.Dot(Direction) + halfLength) / (2f * halfLength);
        return BaseHeight + Rise * Mathf.Clamp(t, 0f, 1f);
    }
}
