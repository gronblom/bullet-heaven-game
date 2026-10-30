using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Numerics;

namespace BulletHeaven.Ecs;

/// <summary>Run-wide data shared by systems that doesn't belong to a single entity.</summary>
public sealed class GameState
{
    public Entity Player;
    /// <summary>This frame's controls, written by the engine front-end before the systems run.</summary>
    public PlayerInput Input;
    public readonly Rect2 Bounds;
    public readonly SpatialGrid EnemyGrid;
    /// <summary>Static level geometry; set by Main once the level has been built.</summary>
    public Terrain Terrain;
    public readonly Random Rng = new();

    public float Time;
    public int Kills;
    public int Level = 1;
    public int Xp;
    public int XpToNext = 5;
    public bool GameOver;
    /// <summary>Level-ups whose modifier choice hasn't been made yet; Main pauses and asks.</summary>
    public int PendingLevelUps;

    // Player-wide modifiers changed by upgrades.
    public float MagnetRadius = 4f;
    public float RamDamagePerSpeed = 2.5f;
    public float StompDamage = 30f;
    public float DamageScale = 1f;   // all weapons
    public float CooldownScale = 1f; // all weapons, lower is faster

    /// <summary>The four weapons, each with its module slots (see <see cref="Weapon"/>).</summary>
    public readonly List<Weapon> Weapons = new()
    {
        new Weapon(WeaponKind.ArcaneWand), new Weapon(WeaponKind.Minigun), new Weapon(WeaponKind.TdMortar), new Weapon(WeaponKind.VoidPulser),
    };
    /// <summary>Modules picked up or chosen but not slotted yet.</summary>
    public readonly List<Module> Inventory = new();
    public const int InventorySize = 8;
    public int NextRingId = 1;

    /// <summary>What happened this frame, for effects (sparks, flashes); cleared by <see cref="BeginFrame"/>.</summary>
    public readonly List<GameEvent> Events = new();
    /// <summary>Laser beams firing this frame (visual; damage is already dealt).</summary>
    public readonly List<LaserBeam> Lasers = new();

    /// <summary>Speed multiplier from liquids the player stands in (1 = none); set by LiquidSystem.</summary>
    public float PlayerSlow = 1f;
    public bool HasParachute;

    /// <summary>Secret exit: reaching this area at or above <see cref="ExitHeight"/> sets <see cref="ReachedExit"/>.</summary>
    public Rect2? ExitArea;
    public float ExitHeight;
    public bool ReachedExit;

    // Boss fight (set up by the level, run by BossSystem).
    public bool BossLevel;
    public Vector2 BossSpawn;
    public Entity Boss = Entity.Null;
    public bool BossSpawned;
    public bool BossDefeated;
    public float BossHealth;
    public float BossMaxHealth;
    public float SpawnRateScale = 1f;

    /// <summary>Lightning bolts to draw (visual only; damage is already dealt). Aged out by the renderer.</summary>
    public readonly List<LightningArc> Arcs = new();

    public string Message = "";
    public float MessageTimer;

    private readonly HashSet<Entity> _toDestroy = new();

    public Weapon Weapon(WeaponKind kind) => Weapons.Find(w => w.Kind == kind);

    /// <summary>Call before running the systems each frame.</summary>
    public void BeginFrame()
    {
        Events.Clear();
        Lasers.Clear();
    }

    public void Emit(GameEventKind kind, Vector2 at, float height, Vector2 direction = default, int tag = 0) =>
        Events.Add(new GameEvent(kind, at, height, direction, tag));

    public GameState(Rect2 bounds)
    {
        Bounds = bounds;
        EnemyGrid = new SpatialGrid(bounds.Grow(2f), 1f);
    }

    /// <summary>Queue an entity for destruction at the end of the frame (safe to call inside queries).</summary>
    public void Destroy(Entity entity) => _toDestroy.Add(entity);

    public void FlushDestroyed(World world)
    {
        foreach (var entity in _toDestroy)
        {
            if (world.IsAlive(entity))
                world.Destroy(entity);
        }
        _toDestroy.Clear();
    }

    public void ShowMessage(string text, float seconds = 2f)
    {
        Message = text;
        MessageTimer = seconds;
    }
}

/// <summary>Engine-independent controls. Move is on the ground plane (screen up = -Y), length &lt;= 1.</summary>
public struct PlayerInput
{
    public Vector2 Move;
    public bool JumpPressed; // this frame
    public bool JumpHeld;
    // Car mode
    public float Throttle;   // -1 brake/reverse .. 1
    public float Steer;      // -1 left .. 1 right
    public bool Handbrake;
    public bool CarJump;     // this frame
}

public enum GameEventKind : byte
{
    Hit,      // a projectile hit a bug (Tag = WeaponKind)
    Kill,     // a bug died (Tag = EnemyKind)
    Burst,    // an explosion / implosion (Tag = WeaponKind)
    Muzzle,   // a weapon fired (Tag = WeaponKind)
    Pickup,   // the player picked something up
    Pulse,    // a pulse ring was emitted
}

/// <summary>Something that happened this frame, for the front-end's particles and flashes.</summary>
public readonly record struct GameEvent(GameEventKind Kind, Vector2 Position, float Height, Vector2 Direction, int Tag);

/// <summary>A laser beam firing this frame, from the gun to where it ends.</summary>
public readonly record struct LaserBeam(Vector3 From, Vector3 To, float Width);

/// <summary>One lightning bolt between two points, flickering for a moment.</summary>
public sealed class LightningArc(Vector3 from, Vector3 to, int seed)
{
    public readonly Vector3 From = from;
    public readonly Vector3 To = to;
    public readonly int Seed = seed;
    public float Age;
}
