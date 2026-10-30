using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Numerics;

namespace BulletHeaven.Systems;

/// <summary>
/// Fires the four weapons from their arithmetic stats (base + modules, see <see cref="Weapon"/>).
/// Each weapon's geometry decides the volley's shape (<see cref="Patterns.Volley"/>) and how its
/// projectiles fly (<see cref="PatternMotion"/>). The minigun's Laser geometry is a continuous beam.
/// </summary>
public sealed class WeaponSystem(World world, GameState state) : GameSystem(world, state)
{
    public const float MinigunSpeed = 38f;
    public const float OrbSpeed = 4f;
    public const float VoidSpeed = 3.2f;

    private readonly List<int> _scratch = new();
    private readonly List<int> _targets = new();
    private readonly HashSet<Entity> _struck = new();
    private readonly float[] _angles = new float[96];
    private float _laserSparkTimer;

    public override void Update(float dt)
    {
        var player = State.Player;
        var position = World.Get<Position>(player).Value;
        var velocity = World.Get<Velocity>(player).Value;
        float height = World.Get<Elevation>(player).Height;

        foreach (var weapon in State.Weapons)
        {
            weapon.PatternTime += dt;
            weapon.Spin = MathF.Max(0f, weapon.Spin - dt * 2f);
            weapon.Timer -= dt;

            if (weapon.Kind == WeaponKind.Minigun && weapon.Stats.Geometry == Geometry.Laser)
            {
                Laser(weapon, position, height, dt);
                continue;
            }
            if (weapon.Timer > 0f)
                continue;

            bool fired = weapon.Kind switch
            {
                WeaponKind.Minigun => Minigun(weapon, position, velocity, height),
                WeaponKind.ArcaneWand => Wand(weapon, position, height),
                WeaponKind.TdMortar => Mortar(weapon, position, height),
                _ => Void(weapon, position, height),
            };
            if (fired)
            {
                weapon.Timer = State.CooldownScale / MathF.Max(0.05f, weapon.Stats.FireRate);
                State.Emit(GameEventKind.Muzzle, position, height + 0.7f, tag: (int)weapon.Kind);
            }
        }
    }

    /// <summary>Aim at the nearest bug in range on about our level; false if there is none.</summary>
    private bool Aim(Vector2 position, float height, float range, out float aim)
    {
        var grid = State.EnemyGrid;
        int target = grid.Nearest(position, range, _scratch, height, WeaponBase.MaxHeightDifference);
        aim = target >= 0 ? (grid.Positions[target] - position).Angle() : 0f;
        return target >= 0;
    }

    private bool AnyNear(Vector2 position, float range)
    {
        _scratch.Clear();
        State.EnemyGrid.Query(position, range, _scratch, 1);
        return _scratch.Count > 0;
    }

    private static Geometry Motion(Geometry geometry) =>
        geometry is Geometry.Sine or Geometry.Spiral ? geometry : Geometry.Linear;

    private bool Minigun(Weapon weapon, Vector2 position, Vector2 velocity, float height)
    {
        var s = weapon.Stats;
        bool patterned = s.Geometry is Geometry.Spiral or Geometry.Radial;
        if (!Aim(position, height, s.Range, out float aim) && !(patterned && AnyNear(position, s.Range * 1.5f)))
            return false;

        weapon.Spin = 1f;
        int particles = s.Particles;
        int n = Patterns.Volley(s.Geometry, particles, aim, weapon.PatternTime, s.Frequency, _angles);
        float speed = s.Geometry == Geometry.Spiral ? MinigunSpeed * 0.5f : MinigunSpeed;
        float damage = s.Damage * State.DamageScale;
        for (int k = 0; k < n; k++)
        {
            var dir = Vector2.FromAngle(_angles[k] + ((float)State.Rng.NextDouble() - 0.5f) * 0.04f);
            var motion = new PatternMotion
            {
                Mode = Motion(s.Geometry),
                Direction = dir,
                Speed = speed,
                Frequency = s.Frequency,
                Amplitude = s.Amplitude,
                Phase = k * Mathf.Tau / Math.Max(1, particles), // parallel sine streams braid around each other
            };
            var bullet = new Bullet
            {
                Damage = damage,
                Lifetime = s.Range / speed * (s.Geometry == Geometry.Spiral ? 1.6f : 1f),
                PierceLeft = (int)s.Pierce,
                LastHit = Entity.Null,
                Height = height + 0.6f,
            };
            World.Create(new Position(position + dir * 0.6f), new Velocity(Patterns.Velocity(motion) + velocity * 0.3f),
                new Radius(0.15f * s.Area), bullet, motion);
        }
        return true;
    }

    /// <summary>Laser geometry: a continuous beam at the nearest bug that burns everything along it.</summary>
    private void Laser(Weapon weapon, Vector2 position, float height, float dt)
    {
        var s = weapon.Stats;
        if (!Aim(position, height, s.Range * 1.2f, out float aim))
            return;
        weapon.Spin = 1f;
        var grid = State.EnemyGrid;
        var dir = Vector2.FromAngle(aim);
        float length = s.Range * 1.2f;
        float width = (0.35f + 0.18f * (s.Particles - 1)) * s.Area;
        float dps = s.Damage * s.FireRate * 0.8f * State.DamageScale;

        var mid = position + dir * (length / 2f);
        _scratch.Clear();
        grid.Query(mid, length / 2f + width, _scratch);
        _laserSparkTimer -= dt;
        bool sparks = _laserSparkTimer <= 0f;
        if (sparks)
            _laserSparkTimer = 0.05f;
        foreach (int i in _scratch)
        {
            var offset = grid.Positions[i] - position;
            float along = offset.Dot(dir);
            if (along < 0f || along > length || MathF.Abs(grid.Heights[i] - height) > WeaponBase.MaxHeightDifference)
                continue;
            float across = MathF.Abs(offset.X * dir.Y - offset.Y * dir.X);
            if (across > width + grid.Radii[i])
                continue;
            var bug = grid.Entities[i];
            World.Get<Health>(bug).Current -= dps * dt;
            ref var enemy = ref World.Get<Enemy>(bug);
            enemy.HitFlash = MathF.Max(enemy.HitFlash, 0.05f);
            enemy.Knockback += dir * 6f * dt;
            if (sparks && State.Rng.NextDouble() < 0.3)
                State.Emit(GameEventKind.Hit, grid.Positions[i], grid.Heights[i] + 0.5f, dir, (int)WeaponKind.Minigun);
        }
        var from = new Vector3(position.X + dir.X * 0.6f, height + 0.7f, position.Y + dir.Y * 0.6f);
        var end = position + dir * length;
        State.Lasers.Add(new LaserBeam(from, new Vector3(end.X, height + 0.7f, end.Y), width));
    }

    private bool Wand(Weapon weapon, Vector2 position, float height)
    {
        var s = weapon.Stats;
        var grid = State.EnemyGrid;
        if (s.Geometry != Geometry.Linear)
        {
            // Geometry modules turn the wand into orbs flying the pattern, zapping as they go.
            bool radial = s.Geometry is Geometry.Spiral or Geometry.Radial;
            if (!Aim(position, height, s.Range, out float aim) && !(radial && AnyNear(position, s.Range)))
                return false;
            int n = Patterns.Volley(s.Geometry, s.Particles, aim, weapon.PatternTime, s.Frequency, _angles);
            for (int k = 0; k < n; k++)
            {
                var motion = new PatternMotion
                {
                    Mode = Motion(s.Geometry),
                    Direction = Vector2.FromAngle(_angles[k]),
                    Speed = OrbSpeed,
                    Frequency = s.Frequency,
                    Amplitude = s.Amplitude,
                    Phase = k * Mathf.Tau / Math.Max(1, n),
                };
                var orb = new LightningOrb
                {
                    Damage = s.Damage * 0.5f * State.DamageScale,
                    ZapRadius = 2.6f * s.Area,
                    MaxTargets = 1 + (int)s.Chain,
                    ZapTimer = 0.2f,
                    Lifetime = 3.5f,
                    Height = height + 1f,
                };
                World.Create(new Position(position), new Velocity(Patterns.Velocity(motion)), new Radius(0.3f), orb, motion);
            }
            return true;
        }

        // Linear: hitscan chain lightning. Each arc strikes a bug and jumps on to the nearest unhit
        // bug within reach, Chain times, losing a little power with each jump.
        _targets.Clear();
        grid.Query(position, s.Range, _targets);
        _targets.RemoveAll(i => MathF.Abs(grid.Heights[i] - height) > WeaponBase.MaxHeightDifference + 1f);
        if (_targets.Count == 0)
            return false;
        _targets.Sort((a, b) => (grid.Positions[a] - position).LengthSquared().CompareTo((grid.Positions[b] - position).LengthSquared()));
        _struck.Clear();
        float reach = 4f * s.Area;
        var hand = new Vector3(position.X, height + 0.9f, position.Y);
        for (int arc = 0, next = 0; arc < s.Particles && next < _targets.Count; arc++)
        {
            int current = _targets[next++];
            while (_struck.Contains(grid.Entities[current]) && next < _targets.Count)
                current = _targets[next++];
            var from = hand;
            float damage = s.Damage * State.DamageScale;
            for (int jump = 0; jump <= (int)s.Chain && current >= 0; jump++)
            {
                var bug = grid.Entities[current];
                _struck.Add(bug);
                World.Get<Health>(bug).Current -= damage;
                World.Get<Enemy>(bug).HitFlash = 0.12f;
                var to = new Vector3(grid.Positions[current].X, grid.Heights[current] + grid.Radii[current], grid.Positions[current].Y);
                State.Arcs.Add(new LightningArc(from, to, State.Rng.Next()));
                State.Emit(GameEventKind.Hit, grid.Positions[current], to.Y, default, (int)WeaponKind.ArcaneWand);
                from = to;
                damage *= 0.93f;
                current = NearestUnstruck(grid.Positions[current], reach);
            }
        }
        return true;
    }

    private int NearestUnstruck(Vector2 from, float reach)
    {
        var grid = State.EnemyGrid;
        _scratch.Clear();
        grid.Query(from, reach, _scratch);
        int best = -1;
        float bestDistance = float.MaxValue;
        foreach (int i in _scratch)
        {
            if (_struck.Contains(grid.Entities[i]))
                continue;
            float d = (grid.Positions[i] - from).LengthSquared();
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        return best;
    }

    private bool Mortar(Weapon weapon, Vector2 position, float height)
    {
        var s = weapon.Stats;
        if (!AnyNear(position, s.Range * s.Area * 1.1f))
            return false;
        Pulses.Emit(World, State, position, height, s, weapon.PatternTime);
        return true;
    }

    private bool Void(Weapon weapon, Vector2 position, float height)
    {
        var s = weapon.Stats;
        bool radial = s.Geometry is Geometry.Spiral or Geometry.Radial;
        if (!Aim(position, height, s.Range, out float aim) && !(radial && AnyNear(position, s.Range)))
            return false;
        int n = Patterns.Volley(s.Geometry, s.Particles, aim, weapon.PatternTime, s.Frequency, _angles);
        for (int k = 0; k < n; k++)
        {
            var motion = new PatternMotion
            {
                Mode = Motion(s.Geometry),
                Direction = Vector2.FromAngle(_angles[k]),
                Speed = VoidSpeed,
                Frequency = s.Frequency,
                Amplitude = s.Amplitude,
                Phase = k * Mathf.Tau / Math.Max(1, n),
            };
            var orb = new VoidOrb
            {
                Dps = s.Damage * State.DamageScale,
                CoreRadius = 1.3f * s.Area,
                PullRadius = 4f * s.Area,
                Pull = 7f,
                BurstDamage = s.Damage * 3f * State.DamageScale,
                BurstRadius = 2.8f * s.Area,
                Lifetime = MathF.Min(4f, s.Range / VoidSpeed),
                Height = height + 0.8f,
            };
            World.Create(new Position(position + motion.Direction * 0.8f), new Velocity(Patterns.Velocity(motion)), new Radius(0.6f * s.Area), orb, motion);
        }
        return true;
    }
}

/// <summary>TD Mortar pulse rings: emitting them (player or turret) from the mortar's current stats.</summary>
public static class Pulses
{
    public const float Speed = 9f;

    public static void Emit(World world, GameState state, Vector2 center, float height, in WeaponStats s, float time)
    {
        int rings = s.Particles;
        for (int k = 0; k < rings; k++)
        {
            var ring = new PulseRing
            {
                Id = state.NextRingId++,
                Radius = 0.5f,
                MaxRadius = s.Range * s.Area * (1f - 0.12f * k),
                Speed = Speed * (1f - 0.15f * k),
                Width = 1.1f * s.Area,
                Damage = s.Damage * state.DamageScale,
                Height = height,
                Wobble = s.Geometry == Geometry.Sine ? MathF.Max(0.6f, s.Amplitude) : s.Amplitude,
                WobbleFrequency = s.Frequency,
                Lobes = 6,
                Sides = s.Geometry switch { Geometry.Pentagon => 5, Geometry.Spiral => 3, Geometry.Radial => 8, _ => 0 },
                Spin = time * s.Frequency + k * 0.6f,
            };
            world.Create(new Position(center), ring);
        }
        state.Emit(GameEventKind.Pulse, center, height);
    }
}

/// <summary>Turns each patterned projectile's equation into this frame's velocity (movement integrates it).</summary>
public sealed class PatternMotionSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<PatternMotion, Velocity>();

    public override void Update(float dt)
    {
        World.Query(in Query, (ref PatternMotion motion, ref Velocity velocity) =>
        {
            motion.Age += dt;
            if (motion.Mode != Geometry.Linear)
                velocity.Value = Patterns.Velocity(motion);
        });
    }
}

/// <summary>Arcane wand orbs: chain-zap the nearest bugs around them a few times a second.</summary>
public sealed class LightningOrbSystem(World world, GameState state) : GameSystem(world, state)
{
    public const float ZapInterval = 0.25f;
    private static readonly QueryDescription Query = new QueryDescription().WithAll<LightningOrb, Position>();
    private readonly List<int> _near = new();

    public override void Update(float dt)
    {
        var grid = State.EnemyGrid;
        World.Query(in Query, (Entity entity, ref LightningOrb orb, ref Position position) =>
        {
            orb.Lifetime -= dt;
            if (orb.Lifetime <= 0f)
            {
                State.Destroy(entity);
                return;
            }
            orb.ZapTimer -= dt;
            if (orb.ZapTimer > 0f)
                return;
            orb.ZapTimer = ZapInterval;

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
                World.Get<Enemy>(target).HitFlash = 0.12f;
                var to = new Vector3(grid.Positions[i].X, grid.Heights[i] + grid.Radii[i], grid.Positions[i].Y);
                State.Arcs.Add(new LightningArc(from, to, State.Rng.Next()));
                State.Emit(GameEventKind.Hit, grid.Positions[i], to.Y, default, (int)WeaponKind.ArcaneWand);
                from = to; // chain: each bolt jumps on from the last bug it hit
            }
        });
    }
}

/// <summary>Expands TD Mortar rings; each burns every bug its front crosses, once.</summary>
public sealed class PulseRingSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<PulseRing, Position>();
    private readonly List<int> _near = new();

    public override void Update(float dt)
    {
        var grid = State.EnemyGrid;
        World.Query(in Query, (Entity entity, ref PulseRing ring, ref Position position) =>
        {
            ring.Age += dt;
            ring.Radius += ring.Speed * dt;
            if (ring.Sides == 3)
                ring.Spin += dt * 2.5f; // Spiral geometry: a turning triangle front
            if (ring.Radius > ring.MaxRadius)
            {
                State.Destroy(entity);
                return;
            }

            float reach = ring.Radius * (ring.Sides >= 3 ? 1.3f : 1f) + ring.Wobble + ring.Width;
            _near.Clear();
            grid.Query(position.Value, reach, _near);
            foreach (int i in _near)
            {
                if (MathF.Abs(grid.Heights[i] - ring.Height) > 1.5f)
                    continue;
                var offset = grid.Positions[i] - position.Value;
                float distance = offset.Length();
                float front = ring.FrontAt(offset.Angle());
                if (MathF.Abs(distance - front) > ring.Width / 2f + grid.Radii[i])
                    continue;
                var bug = grid.Entities[i];
                ref var enemy = ref World.Get<Enemy>(bug);
                if (enemy.LastRing == ring.Id)
                    continue;
                enemy.LastRing = ring.Id;
                World.Get<Health>(bug).Current -= ring.Damage;
                enemy.HitFlash = 0.12f;
                var outward = distance > 1e-3f ? offset / distance : Vector2.Right;
                enemy.Knockback += outward * 5f;
                State.Emit(GameEventKind.Hit, grid.Positions[i], grid.Heights[i] + 0.3f, outward, (int)WeaponKind.TdMortar);
            }
        });
    }
}

/// <summary>Void Pulser orbs: pull bugs in, burn the ones at the core, implode at the end.</summary>
public sealed class VoidSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<VoidOrb, Position>();
    private readonly List<int> _near = new();
    private readonly List<(Vector2 At, float Radius, float Height)> _bursts = new();

    public override void Update(float dt)
    {
        var grid = State.EnemyGrid;
        _bursts.Clear();
        World.Query(in Query, (Entity entity, ref VoidOrb orb, ref Position position) =>
        {
            orb.Age += dt;
            bool burst = orb.Age >= orb.Lifetime;
            _near.Clear();
            grid.Query(position.Value, burst ? orb.BurstRadius : orb.PullRadius, _near);
            foreach (int i in _near)
            {
                var bug = grid.Entities[i];
                if (!World.IsAlive(bug) || MathF.Abs(grid.Heights[i] + 0.8f - orb.Height) > 2f)
                    continue;
                var toward = position.Value - grid.Positions[i];
                float distance = toward.Length();
                if (burst)
                {
                    World.Get<Health>(bug).Current -= orb.BurstDamage;
                    ref var hit = ref World.Get<Enemy>(bug);
                    hit.HitFlash = 0.15f;
                    hit.Knockback -= (distance > 1e-3f ? toward / distance : Vector2.Right) * 8f;
                    continue;
                }
                // Gravity: drag bugs towards the orb (runs after steering, before movement).
                if (distance > 0.2f)
                    World.Get<Velocity>(bug).Value += toward / distance * orb.Pull * MathF.Min(1f, orb.PullRadius / distance * 0.5f);
                if (distance < orb.CoreRadius + grid.Radii[i])
                {
                    World.Get<Health>(bug).Current -= orb.Dps * dt;
                    ref var enemy = ref World.Get<Enemy>(bug);
                    enemy.HitFlash = MathF.Max(enemy.HitFlash, 0.04f);
                }
            }
            if (burst)
            {
                State.Destroy(entity);
                _bursts.Add((position.Value, orb.BurstRadius, orb.Height));
            }
        });
        foreach (var (at, radius, height) in _bursts)
        {
            World.Create(new Position(at), new Blast { Radius = radius, Height = height - 0.8f });
            State.Emit(GameEventKind.Burst, at, height, default, (int)WeaponKind.VoidPulser);
        }
    }
}

/// <summary>Deployed turrets: pulse the player's current TD Mortar rings from their spot until they expire.</summary>
public sealed class TurretSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<Turret, Position>();
    private readonly List<(Vector2 At, float Height)> _pulses = new();
    private readonly List<int> _near = new();

    public override void Update(float dt)
    {
        var mortar = State.Weapon(WeaponKind.TdMortar);
        var stats = mortar?.Stats ?? WeaponBase.Stats(WeaponKind.TdMortar);
        float time = mortar?.PatternTime ?? 0f;
        _pulses.Clear();
        World.Query(in Query, (Entity entity, ref Turret turret, ref Position position) =>
        {
            turret.Lifetime -= dt;
            if (turret.Lifetime <= 0f)
            {
                State.Destroy(entity);
                State.Emit(GameEventKind.Burst, position.Value, turret.Height + 0.5f, default, (int)WeaponKind.TdMortar);
                return;
            }
            turret.Timer -= dt;
            if (turret.Timer > 0f)
                return;
            _near.Clear();
            State.EnemyGrid.Query(position.Value, stats.Range * stats.Area * 1.1f, _near, 1);
            if (_near.Count == 0)
                return;
            turret.Timer = 1f / MathF.Max(0.05f, stats.FireRate);
            _pulses.Add((position.Value, turret.Height));
        });
        foreach (var (at, height) in _pulses)
            Pulses.Emit(World, State, at, height, stats, time);
    }
}

/// <summary>Ages explosion flashes.</summary>
public sealed class BlastSystem(World world, GameState state) : GameSystem(world, state)
{
    public const float Duration = 0.45f;
    private static readonly QueryDescription Query = new QueryDescription().WithAll<Blast>();

    public override void Update(float dt)
    {
        World.Query(in Query, (Entity entity, ref Blast blast) =>
        {
            blast.Age += dt;
            if (blast.Age > Duration)
                State.Destroy(entity);
        });
    }
}
