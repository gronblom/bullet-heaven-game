using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using Godot;

namespace BulletHeaven.Systems;

/// <summary>Numbers for every weapon by level (1..MaxLevel). Tune here.</summary>
public static class WeaponStats
{
    public const int MaxLevel = 8;
    public const float MaxHeightDifference = 2f; // guns only hit bugs on roughly your level

    public static string Name(WeaponKind kind) => kind switch
    {
        WeaponKind.Minigun => "Minigun",
        WeaponKind.LightningWand => "Ball-lightning wand",
        _ => "Seed mortar",
    };

    // Minigun: a hose of small bullets at the nearest bug.
    public static float MinigunCooldown(int level) => MathF.Max(0.04f, 0.1f * MathF.Pow(0.88f, level - 1));
    public static float MinigunDamage(int level) => 4f + level;
    public static int MinigunStreams(int level) => 1 + (level >= 4 ? 1 : 0) + (level >= 7 ? 1 : 0);
    public const float MinigunRange = 13f;

    // Wand: slow orbs that drift into the swarm and hang around in it, zapping up to N bugs nearby
    // several times a second with chain lightning.
    public static float WandCooldown(int level) => MathF.Max(0.9f, 2.2f - 0.2f * level);
    public static int WandOrbs(int level) => 1 + (level >= 4 ? 1 : 0) + (level >= 7 ? 1 : 0);
    public static float WandDamage(int level) => 6f + 3f * level;
    public static float WandZapRadius(int level) => 3f + 0.3f * level;
    public static int WandTargets(int level) => 3 + level / 2;
    public static float WandOrbLifetime(int level) => 4f + 0.3f * level;
    public const float WandRange = 12f;
    public const float OrbSpeed = 3.5f;
    public const float ZapInterval = 0.25f;

    // Mortar: lobs seeds at random bugs anywhere in range, including other levels, that burst on landing.
    public static float MortarCooldown(int level) => MathF.Max(0.9f, 2.6f - 0.2f * level);
    public static int MortarShells(int level) => 1 + level / 3;
    public static float MortarDamage(int level) => 18f + 6f * level;
    public static float MortarRadius(int level) => 2.2f + 0.25f * level;
    public const float MortarRange = 16f;
}

/// <summary>Fires every weapon the player carries when its cooldown is up and something is in range.</summary>
public sealed class WeaponSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<WeaponSlot>();
    private readonly List<int> _scratch = new();
    private readonly List<Action> _spawns = new();

    public override void Update(float dt)
    {
        var player = State.Player;
        var position = World.Get<Position>(player).Value;
        var velocity = World.Get<Velocity>(player).Value;
        float height = World.Get<Elevation>(player).Height;
        var grid = State.EnemyGrid;
        _spawns.Clear();

        World.Query(in Query, (ref WeaponSlot slot) =>
        {
            slot.Timer -= dt;
            slot.Spin = MathF.Max(0f, slot.Spin - dt * 2f);
            if (slot.Timer > 0f)
                return;

            int level = slot.Level;
            switch (slot.Kind)
            {
                case WeaponKind.Minigun:
                {
                    int target = grid.Nearest(position, WeaponStats.MinigunRange, _scratch, height, WeaponStats.MaxHeightDifference);
                    if (target < 0)
                        return;
                    slot.Timer = WeaponStats.MinigunCooldown(level) * State.CooldownScale;
                    slot.Spin = 1f;
                    float aim = (grid.Positions[target] - position).Angle();
                    int streams = WeaponStats.MinigunStreams(level);
                    float damage = WeaponStats.MinigunDamage(level) * State.DamageScale;
                    for (int k = 0; k < streams; k++)
                    {
                        float angle = aim + (k - (streams - 1) * 0.5f) * 0.12f + ((float)State.Rng.NextDouble() - 0.5f) * 0.1f;
                        var v = Vector2.FromAngle(angle) * 40f + velocity * 0.5f;
                        var bullet = new Bullet { Damage = damage, Lifetime = 0.4f, LastHit = Entity.Null, Height = height + 0.6f };
                        _spawns.Add(() => World.Create(new Position(position), new Velocity(v), new Radius(0.15f), bullet));
                    }
                    break;
                }
                case WeaponKind.LightningWand:
                {
                    int target = grid.Nearest(position, WeaponStats.WandRange, _scratch, height, 3f);
                    if (target < 0)
                        return;
                    slot.Timer = WeaponStats.WandCooldown(level) * State.CooldownScale;
                    float aim = (grid.Positions[target] - position).Angle();
                    int orbs = WeaponStats.WandOrbs(level);
                    for (int k = 0; k < orbs; k++)
                    {
                        float angle = aim + (k - (orbs - 1) * 0.5f) * 0.5f;
                        var orb = new LightningOrb
                        {
                            Damage = WeaponStats.WandDamage(level) * State.DamageScale,
                            ZapRadius = WeaponStats.WandZapRadius(level),
                            MaxTargets = WeaponStats.WandTargets(level),
                            ZapTimer = 0.15f,
                            Lifetime = WeaponStats.WandOrbLifetime(level),
                            Height = height + 1f,
                        };
                        var start = position + Vector2.FromAngle(angle) * 0.6f;
                        var v = Vector2.FromAngle(angle) * WeaponStats.OrbSpeed;
                        _spawns.Add(() => World.Create(new Position(start), new Velocity(v), new Radius(0.3f), orb));
                    }
                    break;
                }
                case WeaponKind.SeedMortar:
                {
                    _scratch.Clear();
                    grid.Query(position, WeaponStats.MortarRange, _scratch);
                    if (_scratch.Count == 0)
                        return;
                    slot.Timer = WeaponStats.MortarCooldown(level) * State.CooldownScale;
                    int shells = WeaponStats.MortarShells(level);
                    for (int k = 0; k < shells; k++)
                    {
                        int pick = _scratch[State.Rng.Next(_scratch.Count)];
                        var to = grid.Positions[pick];
                        float endHeight = State.Terrain.GroundHeight(to);
                        var shell = new Shell
                        {
                            From = position,
                            To = to,
                            StartHeight = height + 1.2f,
                            EndHeight = endHeight,
                            Apex = 5f + MathF.Max(0f, endHeight - height),
                            Duration = 0.8f + to.DistanceTo(position) * 0.02f + k * 0.08f,
                            Damage = WeaponStats.MortarDamage(level) * State.DamageScale,
                            BlastRadius = WeaponStats.MortarRadius(level),
                            Height = height + 1.2f,
                        };
                        _spawns.Add(() => World.Create(new Position(position), shell));
                    }
                    break;
                }
            }
        });

        foreach (var spawn in _spawns)
            spawn();
    }
}

/// <summary>Moves lightning orbs' lifetimes along and zaps the nearest bugs around each one.</summary>
public sealed class LightningOrbSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<LightningOrb, Position, Velocity>();
    private readonly List<int> _near = new();
    private readonly List<int> _scratch = new();

    public override void Update(float dt)
    {
        var grid = State.EnemyGrid;
        World.Query(in Query, (Entity entity, ref LightningOrb orb, ref Position position, ref Velocity velocity) =>
        {
            orb.Lifetime -= dt;
            if (orb.Lifetime <= 0f)
            {
                State.Destroy(entity);
                return;
            }

            // Drift towards the nearest bug so the orb hangs around inside the swarm.
            int prey = grid.Nearest(position.Value, 6f, _scratch, orb.Height - 1f, 3f);
            if (prey >= 0)
            {
                var toward = grid.Positions[prey] - position.Value;
                var desired = toward.LengthSquared() > 0.25f ? toward.Normalized() * WeaponStats.OrbSpeed : Vector2.Zero;
                velocity.Value = velocity.Value.Lerp(desired, 1f - MathF.Exp(-2f * dt));
            }

            orb.ZapTimer -= dt;
            if (orb.ZapTimer > 0f)
                return;
            orb.ZapTimer = WeaponStats.ZapInterval;

            _near.Clear();
            grid.Query(position.Value, orb.ZapRadius, _near);
            var center = position.Value;
            float orbHeight = orb.Height;
            _near.RemoveAll(i => MathF.Abs(grid.Heights[i] - (orbHeight - 1f)) > 3f);
            _near.Sort((a, b) => (grid.Positions[a] - center).LengthSquared().CompareTo((grid.Positions[b] - center).LengthSquared()));

            var from = new Vector3(center.X, orb.Height, center.Y);
            for (int n = 0; n < _near.Count && n < orb.MaxTargets; n++)
            {
                int i = _near[n];
                var target = grid.Entities[i];
                ref var health = ref World.Get<Health>(target);
                if (health.Current <= 0f)
                    continue;
                health.Current -= orb.Damage;
                ref var enemy = ref World.Get<Enemy>(target);
                enemy.HitFlash = 0.12f;
                var to = new Vector3(grid.Positions[i].X, grid.Heights[i] + grid.Radii[i], grid.Positions[i].Y);
                State.Arcs.Add(new LightningArc(from, to, State.Rng.Next()));
                from = to; // chain: each bolt jumps on from the last bug it hit
            }
        });
    }
}

/// <summary>Flies mortar shells along their arc and blows them up where they land.</summary>
public sealed class ShellSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription ShellQuery = new QueryDescription().WithAll<Shell, Position>();
    private static readonly QueryDescription BlastQuery = new QueryDescription().WithAll<Blast>();
    public const float BlastDuration = 0.45f;
    private readonly List<int> _hits = new();
    private readonly List<(Vector2 At, Blast Blast)> _blasts = new();

    public override void Update(float dt)
    {
        var grid = State.EnemyGrid;
        _blasts.Clear();

        World.Query(in ShellQuery, (Entity entity, ref Shell shell, ref Position position) =>
        {
            shell.Flight += dt;
            float t = MathF.Min(1f, shell.Flight / shell.Duration);
            position.Value = shell.From.Lerp(shell.To, t);
            shell.Height = Mathf.Lerp(shell.StartHeight, shell.EndHeight, t) + 4f * shell.Apex * t * (1f - t);
            if (t < 1f)
                return;

            State.Destroy(entity);
            _hits.Clear();
            grid.Query(shell.To, shell.BlastRadius, _hits);
            foreach (int i in _hits)
            {
                if (MathF.Abs(grid.Heights[i] - shell.EndHeight) > 2.5f)
                    continue;
                var target = grid.Entities[i];
                World.Get<Health>(target).Current -= shell.Damage;
                ref var enemy = ref World.Get<Enemy>(target);
                var away = grid.Positions[i] - shell.To;
                enemy.Knockback = (away.LengthSquared() > 1e-6f ? away.Normalized() : Vector2.Right) * 9f;
                enemy.HitFlash = 0.15f;
            }
            _blasts.Add((shell.To, new Blast { Radius = shell.BlastRadius, Height = shell.EndHeight }));
        });

        World.Query(in BlastQuery, (Entity entity, ref Blast blast) =>
        {
            blast.Age += dt;
            if (blast.Age > BlastDuration)
                State.Destroy(entity);
        });

        foreach (var (at, blast) in _blasts)
            World.Create(new Position(at), blast);
    }
}

/// <summary>Weapon unlocks and upgrades on level-up.</summary>
public static class Arsenal
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<WeaponSlot>();

    /// <summary>Adds the weapon, or levels it up if already carried. Returns a message for the HUD.</summary>
    public static string Grant(World world, WeaponKind kind)
    {
        Entity owned = Entity.Null;
        world.Query(in Query, (Entity entity, ref WeaponSlot slot) =>
        {
            if (slot.Kind == kind)
                owned = entity;
        });

        if (owned == Entity.Null)
        {
            world.Create(new WeaponSlot { Kind = kind, Level = 1 });
            return $"New weapon: {WeaponStats.Name(kind)}!";
        }

        ref var weapon = ref world.Get<WeaponSlot>(owned);
        if (weapon.Level >= WeaponStats.MaxLevel)
            return $"{WeaponStats.Name(kind)} is maxed out";
        weapon.Level++;
        return $"{WeaponStats.Name(kind)} level {weapon.Level}";
    }

    public static List<(WeaponKind Kind, int Level, float Spin)> Carried(World world)
    {
        var list = new List<(WeaponKind, int, float)>();
        world.Query(in Query, (ref WeaponSlot slot) => list.Add((slot.Kind, slot.Level, slot.Spin)));
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return list;
    }
}
