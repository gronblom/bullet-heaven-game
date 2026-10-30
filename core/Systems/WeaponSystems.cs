using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Numerics;

namespace BulletHeaven.Systems;

/// <summary>Weapon numbers from the base stats plus picked modifiers (see <see cref="Modifiers"/>). Tune here.</summary>
public static class WeaponStats
{
    public const float MaxHeightDifference = 2f; // guns only hit bugs on roughly your level

    public static string Name(WeaponKind kind) => kind switch
    {
        WeaponKind.Minigun => "Minigun",
        WeaponKind.LightningWand => "Ball-lightning wand",
        WeaponKind.SeedMortar => "Seed mortar",
        _ => "Bug spray",
    };

    public static string Blurb(WeaponKind kind) => kind switch
    {
        WeaponKind.Minigun => "A hose of tracer rounds at the nearest bug.",
        WeaponKind.LightningWand => "Glowing orbs drift into the swarm and chain-zap it.",
        WeaponKind.SeedMortar => "Lobs seeds anywhere in range, even up onto the counter. Boom.",
        _ => "A cone of insecticide mist up close: poisons and slows every bug in it.",
    };

    private static float DamageScale(in WeaponSlot w) => 1f + 0.4f * w.Damage;
    private static float RateScale(in WeaponSlot w) => MathF.Pow(0.8f, w.Rate);

    // Minigun: a hose of small bullets at the nearest bug.
    public static float MinigunCooldown(in WeaponSlot w) => 0.08f * RateScale(w);
    public static float MinigunDamage(in WeaponSlot w) => 6f * DamageScale(w);
    public static int MinigunStreams(in WeaponSlot w) => 1 + w.Count;
    public static int MinigunPierce(in WeaponSlot w) => 1 + w.Pierce;
    public static float MinigunRange(in WeaponSlot w) => 13f + 3f * w.Area;

    // Wand: slow orbs that drift into the swarm and hang around in it, zapping up to N bugs nearby
    // several times a second with chain lightning.
    public static float WandCooldown(in WeaponSlot w) => 2.2f * RateScale(w);
    public static int WandOrbs(in WeaponSlot w) => 1 + w.Count;
    public static float WandDamage(in WeaponSlot w) => 9f * DamageScale(w);
    public static float WandZapRadius(in WeaponSlot w) => 3.2f + 0.9f * w.Area;
    public static int WandTargets(in WeaponSlot w) => 3 + 2 * w.Pierce;
    public static float WandOrbLifetime(in WeaponSlot w) => 4f + 2f * w.Special;
    public const float WandRange = 12f;
    public const float OrbSpeed = 3.5f;
    public const float ZapInterval = 0.25f;

    // Mortar: lobs seeds at random bugs anywhere in range, including other levels, that burst on landing.
    public static float MortarCooldown(in WeaponSlot w) => 2.4f * RateScale(w);
    public static int MortarShells(in WeaponSlot w) => 1 + w.Count;
    public static float MortarDamage(in WeaponSlot w) => 24f * DamageScale(w);
    public static float MortarRadius(in WeaponSlot w) => 2.5f + 0.6f * w.Area;
    public static bool MortarSticky(in WeaponSlot w) => w.Special > 0;
    public const float MortarRange = 16f;

    // Bug spray: a short cone of mist towards the nearest bug. Every puff poisons (damage per second)
    // and slows the bugs inside it as it drifts out and swells.
    public static float SprayCooldown(in WeaponSlot w) => 0.09f * RateScale(w);
    public static int SprayPuffs(in WeaponSlot w) => 1 + w.Count;
    public static float SpraySpread(in WeaponSlot w) => 0.3f + 0.12f * w.Count; // half-angle of the cone, radians
    public static float SprayDps(in WeaponSlot w) => 30f * DamageScale(w);
    public static float SprayRange(in WeaponSlot w) => 5f + 1.5f * w.Area;
    public static float SprayPuffSize(in WeaponSlot w) => 1.1f + 0.3f * w.Area;
    public static float SprayPush(in WeaponSlot w) => 3f * w.Pierce;
    public static float SprayLinger(in WeaponSlot w) => w.Special > 0 ? 1.8f : 1f;
    public const float SpraySpeed = 9f;
    public const float SpraySlow = 0.55f;
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

            var w = slot;
            switch (slot.Kind)
            {
                case WeaponKind.Minigun:
                {
                    int target = grid.Nearest(position, WeaponStats.MinigunRange(w), _scratch, height, WeaponStats.MaxHeightDifference);
                    if (target < 0)
                        return;
                    slot.Timer = WeaponStats.MinigunCooldown(w) * State.CooldownScale;
                    slot.Spin = 1f;
                    float aim = (grid.Positions[target] - position).Angle();
                    int streams = WeaponStats.MinigunStreams(w);
                    float damage = WeaponStats.MinigunDamage(w) * State.DamageScale;
                    for (int k = 0; k < streams; k++)
                    {
                        float angle = aim + (k - (streams - 1) * 0.5f) * 0.12f + ((float)State.Rng.NextDouble() - 0.5f) * 0.1f;
                        var v = Vector2.FromAngle(angle) * 40f + velocity * 0.5f;
                        var bullet = new Bullet { Damage = damage, Lifetime = 0.4f + 0.08f * w.Area, PierceLeft = WeaponStats.MinigunPierce(w),
                            LastHit = Entity.Null, Height = height + 0.6f };
                        _spawns.Add(() => World.Create(new Position(position), new Velocity(v), new Radius(0.15f), bullet));
                    }
                    break;
                }
                case WeaponKind.LightningWand:
                {
                    int target = grid.Nearest(position, WeaponStats.WandRange, _scratch, height, 3f);
                    if (target < 0)
                        return;
                    slot.Timer = WeaponStats.WandCooldown(w) * State.CooldownScale;
                    float aim = (grid.Positions[target] - position).Angle();
                    int orbs = WeaponStats.WandOrbs(w);
                    for (int k = 0; k < orbs; k++)
                    {
                        float angle = aim + (k - (orbs - 1) * 0.5f) * 0.5f;
                        var orb = new LightningOrb
                        {
                            Damage = WeaponStats.WandDamage(w) * State.DamageScale,
                            ZapRadius = WeaponStats.WandZapRadius(w),
                            MaxTargets = WeaponStats.WandTargets(w),
                            ZapTimer = 0.15f,
                            Lifetime = WeaponStats.WandOrbLifetime(w),
                            Height = height + 1f,
                        };
                        var start = position + Vector2.FromAngle(angle) * 0.6f;
                        var v = Vector2.FromAngle(angle) * WeaponStats.OrbSpeed;
                        _spawns.Add(() => World.Create(new Position(start), new Velocity(v), new Radius(0.3f), orb));
                    }
                    break;
                }
                case WeaponKind.BugSpray:
                {
                    float range = WeaponStats.SprayRange(w);
                    int target = grid.Nearest(position, range, _scratch, height, WeaponStats.MaxHeightDifference);
                    if (target < 0)
                        return;
                    slot.Timer = WeaponStats.SprayCooldown(w) * State.CooldownScale;
                    float aim = (grid.Positions[target] - position).Angle();
                    int puffs = WeaponStats.SprayPuffs(w);
                    float spread = WeaponStats.SpraySpread(w);
                    for (int k = 0; k < puffs; k++)
                    {
                        float angle = aim + ((float)State.Rng.NextDouble() * 2f - 1f) * spread;
                        float speed = WeaponStats.SpraySpeed * (0.8f + 0.4f * (float)State.Rng.NextDouble());
                        var puff = new SprayPuff
                        {
                            Dps = WeaponStats.SprayDps(w) * State.DamageScale,
                            Lifetime = range / WeaponStats.SpraySpeed * WeaponStats.SprayLinger(w),
                            StartRadius = 0.35f,
                            EndRadius = WeaponStats.SprayPuffSize(w),
                            Height = height + 0.6f,
                            Push = WeaponStats.SprayPush(w),
                        };
                        var start = position + Vector2.FromAngle(angle) * 0.5f;
                        var v = Vector2.FromAngle(angle) * speed + velocity * 0.5f;
                        _spawns.Add(() => World.Create(new Position(start), new Velocity(v), puff));
                    }
                    break;
                }
                case WeaponKind.SeedMortar:
                {
                    _scratch.Clear();
                    grid.Query(position, WeaponStats.MortarRange, _scratch);
                    if (_scratch.Count == 0)
                        return;
                    slot.Timer = WeaponStats.MortarCooldown(w) * State.CooldownScale;
                    int shells = WeaponStats.MortarShells(w);
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
                            Damage = WeaponStats.MortarDamage(w) * State.DamageScale,
                            BlastRadius = WeaponStats.MortarRadius(w),
                            Sticky = WeaponStats.MortarSticky(w),
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

/// <summary>Bug spray mist: slows down as it drifts, swells, and poisons and slows the bugs inside each puff.</summary>
public sealed class SpraySystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<SprayPuff, Position, Velocity>();
    private readonly List<int> _inside = new();

    public override void Update(float dt)
    {
        var grid = State.EnemyGrid;
        float drag = MathF.Exp(-2.5f * dt);
        World.Query(in Query, (Entity entity, ref SprayPuff puff, ref Position position, ref Velocity velocity) =>
        {
            puff.Age += dt;
            if (puff.Age >= puff.Lifetime)
            {
                State.Destroy(entity);
                return;
            }
            velocity.Value *= drag;
            float radius = puff.Radius;
            // Fades out over the last third: less poison as it thins.
            float strength = MathF.Min(1f, 3f * (1f - puff.Age / puff.Lifetime));

            _inside.Clear();
            grid.Query(position.Value, radius, _inside);
            foreach (int i in _inside)
            {
                if (MathF.Abs(grid.Heights[i] + 0.5f - puff.Height) > 1.5f)
                    continue;
                var bug = grid.Entities[i];
                if (!World.IsAlive(bug))
                    continue;
                World.Get<Health>(bug).Current -= puff.Dps * strength * dt;
                World.Get<Velocity>(bug).Value *= WeaponStats.SpraySlow;
                ref var enemy = ref World.Get<Enemy>(bug);
                if (puff.Push > 0f && velocity.Value.LengthSquared() > 0.01f)
                    enemy.Knockback += velocity.Value.Normalized() * puff.Push * dt * 6f;
                enemy.HitFlash = MathF.Max(enemy.HitFlash, 0.03f);
            }
        });
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
    private readonly List<(Vector2 At, float Radius, float Height)> _jams = new();

    public override void Update(float dt)
    {
        var grid = State.EnemyGrid;
        _blasts.Clear();
        _jams.Clear();

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
            if (shell.Sticky)
                _jams.Add((shell.To, shell.BlastRadius * 0.8f, shell.EndHeight));
        });

        World.Query(in BlastQuery, (Entity entity, ref Blast blast) =>
        {
            blast.Age += dt;
            if (blast.Age > BlastDuration)
                State.Destroy(entity);
        });

        foreach (var (at, blast) in _blasts)
            World.Create(new Position(at), blast);
        foreach (var (at, radius, height) in _jams)
            Liquids.Spill(World, at, height, radius, LiquidKind.Jam, lifetime: 6f, seed: (float)State.Rng.NextDouble());
    }
}

/// <summary>The weapons the player carries.</summary>
public static class Arsenal
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<WeaponSlot>();

    public static bool Has(World world, WeaponKind kind)
    {
        bool found = false;
        world.Query(in Query, (ref WeaponSlot slot) => found |= slot.Kind == kind);
        return found;
    }

    /// <summary>Adds the weapon if it isn't carried yet. Returns a message for the HUD.</summary>
    public static string Grant(World world, WeaponKind kind)
    {
        if (Has(world, kind))
            return $"{WeaponStats.Name(kind)} already carried";
        world.Create(new WeaponSlot { Kind = kind });
        return $"New weapon: {WeaponStats.Name(kind)}!";
    }

    public delegate void SlotChange(ref WeaponSlot slot);

    /// <summary>Runs <paramref name="change"/> on the carried weapon of that kind, if any.</summary>
    public static void Modify(World world, WeaponKind kind, SlotChange change)
    {
        world.Query(in Query, (ref WeaponSlot slot) =>
        {
            if (slot.Kind == kind)
                change(ref slot);
        });
    }

    public static List<WeaponSlot> Carried(World world)
    {
        var list = new List<WeaponSlot>();
        world.Query(in Query, (ref WeaponSlot slot) => list.Add(slot));
        list.Sort((a, b) => a.Kind.CompareTo(b.Kind));
        return list;
    }
}
