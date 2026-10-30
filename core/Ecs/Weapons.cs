using System;
using BulletHeaven.Numerics;

namespace BulletHeaven.Ecs;

/// <summary>The four fixed weapons every run starts with.</summary>
public enum WeaponKind : byte
{
    ArcaneWand, // chain lightning
    Minigun,    // kinetic rapid fire
    TdMortar,   // pulsating fire rings
    VoidPulser, // slow, heavy gravitational plasma
}

/// <summary>How a weapon's volley is shaped and how its projectiles fly (geometry override).</summary>
public enum Geometry : byte
{
    Linear,   // straight at the nearest bug
    Sine,     // weaving: offset = sin(theta + t*f) * a
    Spiral,   // rotating arms winding outwards, regardless of targets
    Pentagon, // five-way star / pentagonal front
    Radial,   // a burst in every direction
    Laser,    // minigun only: a continuous piercing beam
}

/// <summary>The numbers modules work on.</summary>
public enum Stat : byte
{
    Damage,
    FireRate,  // shots, arcs or pulses per second
    Count,     // particles: streams, arcs, rings, orbs per shot
    Pierce,    // minigun: bugs a bullet passes through
    Chain,     // wand: jumps per arc
    Area,      // radius scale
    Frequency, // f in the pattern equations
    Amplitude, // a in the pattern equations
    Range,
}

/// <summary>Add: flat (+). Multiply: scaling (x). Geometry: replaces the pattern. Player: applies to you at once.</summary>
public enum ModuleOp : byte
{
    Add,
    Multiply,
    Geometry,
    Player,
}

/// <summary>
/// An arithmetic modifier module. Slotted into a weapon it changes one stat (Add, then Multiply, in
/// that order) or overrides the weapon's geometry. <see cref="Only"/> limits it to one weapon.
/// </summary>
public sealed record Module(string Name, string Short, ModuleOp Op, Stat Stat, float Value, Geometry Geometry, WeaponKind? Only,
    string Text, Action<Arch.Core.World, GameState> ApplyToPlayer = null)
{
    public bool Fits(WeaponKind kind) => Op != ModuleOp.Player && (Only == null || Only == kind);
}

/// <summary>A weapon's effective numbers after its modules: base, + every Add, x every Multiply, last Geometry.</summary>
public struct WeaponStats
{
    public float Damage, FireRate, Count, Pierce, Chain, Area, Frequency, Amplitude, Range;
    public Geometry Geometry;

    public float this[Stat stat]
    {
        readonly get => stat switch
        {
            Stat.Damage => Damage,
            Stat.FireRate => FireRate,
            Stat.Count => Count,
            Stat.Pierce => Pierce,
            Stat.Chain => Chain,
            Stat.Area => Area,
            Stat.Frequency => Frequency,
            Stat.Amplitude => Amplitude,
            _ => Range,
        };
        set
        {
            switch (stat)
            {
                case Stat.Damage: Damage = value; break;
                case Stat.FireRate: FireRate = value; break;
                case Stat.Count: Count = value; break;
                case Stat.Pierce: Pierce = value; break;
                case Stat.Chain: Chain = value; break;
                case Stat.Area: Area = value; break;
                case Stat.Frequency: Frequency = value; break;
                case Stat.Amplitude: Amplitude = value; break;
                default: Range = value; break;
            }
        }
    }

    /// <summary>Whole particles per shot (at least one).</summary>
    public readonly int Particles => Math.Max(1, (int)MathF.Round(Count));
}

/// <summary>One of the player's weapons: its module slots and the stats they add up to.</summary>
public sealed class Weapon
{
    public const int SlotCount = 4;

    public readonly WeaponKind Kind;
    public readonly Module[] Slots = new Module[SlotCount];
    public WeaponStats Stats;
    public float Timer;
    public float Spin;        // minigun barrel spin 0..1 (visual)
    public float PatternTime; // t for patterns that run continuously (spirals)

    public Weapon(WeaponKind kind)
    {
        Kind = kind;
        Recompute();
    }

    /// <summary>Re-runs the arithmetic chain: base stats, then all Add modules, then all Multiply, then geometry.</summary>
    public void Recompute()
    {
        var stats = WeaponBase.Stats(Kind);
        foreach (var module in Slots)
        {
            if (module is { Op: ModuleOp.Add })
                stats[module.Stat] += module.Value;
        }
        foreach (var module in Slots)
        {
            if (module is { Op: ModuleOp.Multiply })
                stats[module.Stat] *= module.Value;
        }
        foreach (var module in Slots)
        {
            if (module is { Op: ModuleOp.Geometry })
                stats.Geometry = module.Geometry;
        }
        Stats = stats;
    }
}

/// <summary>Names and base numbers of the four weapons. Tune here.</summary>
public static class WeaponBase
{
    public const float MaxHeightDifference = 2f; // guns only hit bugs on roughly your level

    public static string Name(WeaponKind kind) => kind switch
    {
        WeaponKind.ArcaneWand => "Arcane Wand",
        WeaponKind.Minigun => "Minigun",
        WeaponKind.TdMortar => "TD Mortar",
        _ => "Void Pulser",
    };

    public static string Blurb(WeaponKind kind) => kind switch
    {
        WeaponKind.ArcaneWand => "Lightning arcs that jump from bug to bug.",
        WeaponKind.Minigun => "A dense stream of fast rounds.",
        WeaponKind.TdMortar => "Rhythmic rings of fire pulsing out around you.",
        _ => "Slow plasma orbs that drag bugs in and implode.",
    };

    public static WeaponStats Stats(WeaponKind kind) => kind switch
    {
        WeaponKind.ArcaneWand => new WeaponStats
        {
            Damage = 16f, FireRate = 1.4f, Count = 1f, Chain = 4f, Area = 1f, Range = 11f, Frequency = 2.5f, Amplitude = 2f,
        },
        WeaponKind.Minigun => new WeaponStats
        {
            Damage = 6f, FireRate = 12f, Count = 1f, Pierce = 1f, Area = 1f, Range = 13f, Frequency = 9f, Amplitude = 0.7f,
        },
        WeaponKind.TdMortar => new WeaponStats
        {
            Damage = 14f, FireRate = 0.55f, Count = 1f, Area = 1f, Range = 7f, Frequency = 3f, Amplitude = 0f,
        },
        _ => new WeaponStats
        {
            Damage = 10f, FireRate = 0.3f, Count = 1f, Area = 1f, Range = 12f, Frequency = 1.5f, Amplitude = 1.5f,
        },
    };

    /// <summary>The equation each weapon's current geometry runs, for the module screen.</summary>
    public static string Formula(in WeaponStats s) => s.Geometry switch
    {
        Geometry.Sine => $"offset = sin(θ + t·{s.Frequency:0.#})·{s.Amplitude:0.#}",
        Geometry.Spiral => $"angle = θ + t·{s.Frequency:0.#},  r = v·t   ×{s.Particles} arms",
        Geometry.Pentagon => $"5 × {s.Particles} directions, 72° apart",
        Geometry.Radial => $"{8 * s.Particles} directions, all around",
        Geometry.Laser => "continuous beam, pierces everything",
        _ => s.Particles > 1 ? $"{s.Particles} parallel streams" : "straight line",
    };
}

/// <summary>
/// The arithmetic pattern engine: where each particle of a volley goes, and how a patterned
/// projectile moves (its velocity at age t, so ordinary movement integrates the curve).
/// </summary>
public static class Patterns
{
    /// <summary>
    /// Launch angles for one volley: <paramref name="count"/> particles shaped by the geometry around
    /// <paramref name="aim"/> (the target's direction). Returns how many angles were written.
    /// </summary>
    public static int Volley(Geometry geometry, int count, float aim, float time, float frequency, Span<float> angles)
    {
        int n = 0;
        switch (geometry)
        {
            case Geometry.Spiral:
                // Arms evenly spaced, the whole spiral turning with time.
                for (int i = 0; i < count && n < angles.Length; i++)
                    angles[n++] = time * frequency + i * Mathf.Tau / count;
                break;
            case Geometry.Pentagon:
                for (int i = 0; i < 5 * count && n < angles.Length; i++)
                    angles[n++] = aim + (i % 5) * Mathf.Tau / 5f + (i / 5) * 0.12f;
                break;
            case Geometry.Radial:
                for (int i = 0; i < 8 * count && n < angles.Length; i++)
                    angles[n++] = aim + i * Mathf.Tau / (8 * count);
                break;
            default:
                // Linear and Sine: parallel streams, slightly fanned.
                for (int i = 0; i < count && n < angles.Length; i++)
                    angles[n++] = aim + (i - (count - 1) * 0.5f) * 0.1f;
                break;
        }
        return n;
    }

    /// <summary>Velocity of a patterned projectile at its current age (derivative of its path).</summary>
    public static Vector2 Velocity(in PatternMotion m)
    {
        var dir = m.Direction;
        switch (m.Mode)
        {
            case Geometry.Sine:
            {
                // path = dir * v * t + perp * sin(theta + t f) * a  =>  d/dt = dir * v + perp * cos(theta + t f) * a * f
                var perp = new Vector2(-dir.Y, dir.X);
                return dir * m.Speed + perp * (MathF.Cos(m.Phase + m.Age * m.Frequency) * m.Amplitude * m.Frequency);
            }
            case Geometry.Spiral:
            {
                // path = r(t) * (cos A, sin A) with r = v t, A = angle0 + w t  =>  d/dt = v (cos, sin) + r w (-sin, cos)
                float w = m.Frequency * 0.35f;
                float angle = dir.Angle() + w * m.Age;
                var radial = Vector2.FromAngle(angle);
                return radial * m.Speed + new Vector2(-radial.Y, radial.X) * (m.Speed * m.Age * w);
            }
            default:
                return dir * m.Speed;
        }
    }
}
