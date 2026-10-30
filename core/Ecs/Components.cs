using System;
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
    public int LastRing;    // id of the last pulse ring that hit it (each ring hits once)
}

public struct Bullet
{
    public float Damage;
    public float Lifetime;
    public int PierceLeft;
    public Entity LastHit;
    public float Height; // visual only; bullets hit anything they pass over
}

/// <summary>
/// Equation-driven flight (see <see cref="Patterns"/>): the entity's velocity is recomputed every
/// frame from its pattern, so bullets and orbs can weave (sine) or wind outwards (spiral).
/// </summary>
public struct PatternMotion
{
    public Geometry Mode;    // Linear, Sine or Spiral motion (the other geometries only shape the volley)
    public Vector2 Direction;
    public float Speed;
    public float Frequency;  // f
    public float Amplitude;  // a
    public float Phase;      // theta
    public float Age;        // t
}

public struct XpGem
{
    public int Value;
    public bool Magnetized;
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
    ArmorPlate, // +25 armour
    TurretKit,  // deploys a turret running the player's TD Mortar equations
}

/// <summary>An item lying in the level, collected by touching it.</summary>
public struct Pickup
{
    public PickupKind Kind;
    public float Height;
}

/// <summary>An arithmetic module dropped by a bug; walk over it to put it in the inventory.</summary>
public struct ModuleDrop
{
    public int Module; // index into Modules.All
    public float Height;
    public float Age;
}

/// <summary>Armour plating: absorbs contact damage before health.</summary>
public struct Armor
{
    public float Current;
    public float Max;
}

/// <summary>A deployed turret: runs the player's current TD Mortar pulse equations from its spot.</summary>
public struct Turret
{
    public float Lifetime;
    public float Timer;
    public float Height;
}

/// <summary>
/// An expanding pulse wave (TD Mortar): its front sweeps outwards from the centre and burns each bug
/// it crosses once. The front can wobble (sine) or be a polygon (pentagon).
/// </summary>
public struct PulseRing
{
    public int Id;
    public float Radius;
    public float MaxRadius;
    public float Speed;
    public float Width;
    public float Damage;
    public float Height;
    public int Sides;        // 0 = circle, n = regular polygon
    public float Wobble;     // amplitude of the sine wobble along the front
    public float WobbleFrequency;
    public int Lobes;        // wobbles around the ring
    public float Spin;       // polygon / wobble rotation, radians
    public float Age;

    /// <summary>Radius of the front in a direction (angle in radians).</summary>
    public readonly float FrontAt(float angle)
    {
        float r = Radius;
        if (Sides >= 3)
        {
            float sector = Mathf.Tau / Sides;
            float local = ((angle - Spin) % sector + sector) % sector - sector / 2f;
            r *= MathF.Cos(Mathf.Pi / Sides) / MathF.Cos(local);
        }
        if (Wobble > 0f)
            r += MathF.Sin(Lobes * angle + Spin + Age * WobbleFrequency) * Wobble;
        return r;
    }
}

/// <summary>A heavy, slow plasma orb (Void Pulser): pulls bugs in, burns those at its core and
/// implodes at the end of its life.</summary>
public struct VoidOrb
{
    public float Dps;
    public float CoreRadius;
    public float PullRadius;
    public float Pull;
    public float BurstDamage;
    public float BurstRadius;
    public float Lifetime;
    public float Age;
    public float Height;
}

/// <summary>Arcane wand orb (wand with a geometry module): flies its pattern and chain-zaps bugs nearby.</summary>
public struct LightningOrb
{
    public float Damage;
    public float ZapRadius;
    public int MaxTargets;
    public float ZapTimer;
    public float Lifetime;
    public float Height;
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
