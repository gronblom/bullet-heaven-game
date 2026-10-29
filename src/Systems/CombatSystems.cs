using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using Godot;

namespace BulletHeaven.Systems;

/// <summary>
/// Car vs. enemy contact. Hitting an enemy fast enough rams it (damage scales with closing speed);
/// anything slower and the enemy bites the car instead. Bugs only touch the car when it's at
/// their height, so jumping clears a swarm, and landing hard on one squashes everything nearby.
/// </summary>
public sealed class RamSystem(World world, GameState state) : GameSystem(world, state)
{
    private const float RamSpeed = 6f;
    private const float ContactHeight = 0.7f;
    private const float StompImpact = 7f; // ramp jumps and drops, not a plain hop
    private readonly List<int> _hits = new();

    public override void Update(float dt)
    {
        var player = State.Player;
        ref var health = ref World.Get<Health>(player);
        ref var velocity = ref World.Get<Velocity>(player);
        var elevation = World.Get<Elevation>(player);
        var position = World.Get<Position>(player).Value;
        float radius = World.Get<Radius>(player).Value;
        var grid = State.EnemyGrid;
        var terrain = State.Terrain;

        health.Invulnerable = MathF.Max(0f, health.Invulnerable - dt);
        if (elevation.Height < -0.5f)
            return; // falling off the table

        if (elevation.LandingImpact > StompImpact)
            Stomp(position, elevation.LandingImpact);

        _hits.Clear();
        grid.Query(position, radius, _hits);

        foreach (int i in _hits)
        {
            var target = grid.Entities[i];
            ref var enemyHealth = ref World.Get<Health>(target);
            if (enemyHealth.Current <= 0f)
                continue;
            if (MathF.Abs(elevation.Height - terrain.GroundHeight(grid.Positions[i])) > ContactHeight)
                continue; // flying over it, or it's down below a book we're parked on

            ref var enemy = ref World.Get<Enemy>(target);
            var direction = (grid.Positions[i] - position).Normalized();
            float closing = velocity.Value.Dot(direction);

            if (closing > RamSpeed)
            {
                enemyHealth.Current -= closing * State.RamDamagePerSpeed;
                enemy.Knockback = direction * (6f + closing * 0.8f);
                enemy.HitFlash = 0.1f;
                velocity.Value *= 0.97f; // each squash costs a little speed
            }
            else if (health.Invulnerable <= 0f)
            {
                health.Current -= enemy.ContactDamage;
                health.Invulnerable = 0.6f;
                enemy.Knockback = direction * 8f;
            }
        }

        if (health.Current <= 0f && !State.GameOver)
        {
            State.GameOver = true;
            State.ShowMessage("WRECKED!\nPress R / Start to restart", float.MaxValue);
        }
    }

    /// <summary>Ground-pound: damage and fling every bug around the landing spot.</summary>
    private void Stomp(Vector2 position, float impact)
    {
        var grid = State.EnemyGrid;
        float stompRadius = 1.2f + impact * 0.12f;
        _hits.Clear();
        grid.Query(position, stompRadius, _hits);
        foreach (int i in _hits)
        {
            var target = grid.Entities[i];
            var away = grid.Positions[i] - position;
            var direction = away.LengthSquared() > 1e-6f ? away.Normalized() : Vector2.Right;
            World.Get<Health>(target).Current -= impact * 5f;
            ref var enemy = ref World.Get<Enemy>(target);
            enemy.Knockback = direction * (8f + impact);
            enemy.HitFlash = 0.15f;
        }
    }
}

/// <summary>Fires the car's auto-aim gun at the nearest enemy in range.</summary>
public sealed class WeaponSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<PlayerTag, Position, Velocity, Elevation, Weapon>();
    private readonly List<int> _scratch = new();
    private readonly List<(Vector2 Position, Vector2 Velocity, Bullet Bullet)> _shots = new();

    public override void Update(float dt)
    {
        _shots.Clear();
        var grid = State.EnemyGrid;

        World.Query(in Query, (ref Position position, ref Velocity carVelocity, ref Elevation elevation, ref Weapon weapon) =>
        {
            weapon.Timer -= dt;
            if (weapon.Timer > 0f)
                return;

            int target = grid.Nearest(position.Value, weapon.Range, _scratch);
            if (target < 0)
                return; // hold fire until something is in range

            weapon.Timer = weapon.Cooldown;
            float aim = (grid.Positions[target] - position.Value).Angle();
            for (int k = 0; k < weapon.Projectiles; k++)
            {
                float angle = aim + (k - (weapon.Projectiles - 1) * 0.5f) * weapon.Spread;
                var velocity = Vector2.FromAngle(angle) * weapon.BulletSpeed + carVelocity.Value * 0.5f;
                _shots.Add((position.Value, velocity, new Bullet
                {
                    Damage = weapon.Damage,
                    Lifetime = weapon.Lifetime,
                    PierceLeft = weapon.Pierce,
                    LastHit = Entity.Null,
                    Height = elevation.Height + 0.5f,
                }));
            }
        });

        foreach (var (position, velocity, bullet) in _shots)
            World.Create(new Position(position), new Velocity(velocity), new Radius(0.18f), bullet);
    }
}

/// <summary>Moves bullets' lifetimes along and applies hits against the enemy grid.</summary>
public sealed class BulletSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<Bullet, Position, Velocity, Radius>();
    private readonly List<int> _hits = new();

    public override void Update(float dt)
    {
        var grid = State.EnemyGrid;

        World.Query(in Query, (Entity entity, ref Bullet bullet, ref Position position, ref Velocity velocity, ref Radius radius) =>
        {
            bullet.Lifetime -= dt;
            if (bullet.Lifetime <= 0f)
            {
                State.Destroy(entity);
                return;
            }

            _hits.Clear();
            grid.Query(position.Value, radius.Value, _hits);
            foreach (int i in _hits)
            {
                var target = grid.Entities[i];
                if (target == bullet.LastHit)
                    continue;
                ref var health = ref World.Get<Health>(target);
                if (health.Current <= 0f)
                    continue;

                health.Current -= bullet.Damage;
                ref var enemy = ref World.Get<Enemy>(target);
                enemy.HitFlash = 0.08f;
                enemy.Knockback += velocity.Value.Normalized() * 2f;
                bullet.LastHit = target;

                if (--bullet.PierceLeft < 0)
                {
                    State.Destroy(entity);
                    break;
                }
            }
        });
    }
}
