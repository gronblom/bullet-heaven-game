using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using Godot;

namespace BulletHeaven.Systems;

/// <summary>
/// Player vs. bug contact. Bugs only touch the player at their own height.
/// Runner: coming down onto a bug stomps it and bounces you up (chain them!); touching one on the
/// ground gets you bitten. Car: hitting a bug fast enough rams it, slower and it bites.
/// Either way a long fall ends in a shockwave that flattens everything around the landing spot.
/// </summary>
public sealed class PlayerContactSystem(World world, GameState state) : GameSystem(world, state)
{
    private const float RamSpeed = 6f;
    private const float ContactHeight = 0.5f;
    private const float StompReach = 0.7f; // how far above a bug's base counts as landing on it
    private const float ShockwaveImpact = 11f; // roughly a drop from 3 units up

    private readonly List<int> _hits = new();

    public override void Update(float dt)
    {
        var player = State.Player;
        ref var health = ref World.Get<Health>(player);
        ref var velocity = ref World.Get<Velocity>(player);
        ref var elevation = ref World.Get<Elevation>(player);
        var position = World.Get<Position>(player).Value;
        float radius = World.Get<Radius>(player).Value;
        bool isCar = World.Has<Car>(player);
        var grid = State.EnemyGrid;

        health.Invulnerable = MathF.Max(0f, health.Invulnerable - dt);

        if (elevation.LandingImpact > ShockwaveImpact)
            Shockwave(position, elevation.Height, elevation.LandingImpact);

        _hits.Clear();
        grid.Query(position, radius, _hits);
        bool bounce = false;

        foreach (int i in _hits)
        {
            var target = grid.Entities[i];
            ref var enemyHealth = ref World.Get<Health>(target);
            if (enemyHealth.Current <= 0f)
                continue;

            ref var enemy = ref World.Get<Enemy>(target);
            var away = grid.Positions[i] - position;
            var direction = away.LengthSquared() > 1e-6f ? away.Normalized() : Vector2.Right;
            float above = elevation.Height - grid.Heights[i];

            bool descending = (!elevation.Grounded && elevation.VerticalSpeed <= 0f) || elevation.LandingImpact > 0f;
            if (!isCar && descending && above >= -0.1f && above <= StompReach)
            {
                enemyHealth.Current -= State.StompDamage;
                enemy.HitFlash = 0.15f;
                enemy.Knockback = direction * 6f;
                bounce = true;
                continue;
            }

            if (MathF.Abs(above) > ContactHeight)
                continue; // on another level, or passing overhead

            float closing = velocity.Value.Dot(direction);
            if (isCar && closing > RamSpeed)
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

        if (bounce)
        {
            float bounceSpeed = World.Has<RunnerStats>(player) ? World.Get<RunnerStats>(player).StompBounce : 7f;
            elevation.VerticalSpeed = bounceSpeed;
            elevation.Grounded = false;
        }

        if (health.Current <= 0f && !State.GameOver)
        {
            State.GameOver = true;
            State.ShowMessage("SQUASHED!\nPress R / Start to restart", float.MaxValue);
        }
    }

    /// <summary>Hard landing: damage and fling every bug around the landing spot on the same level.</summary>
    private void Shockwave(Vector2 position, float height, float impact)
    {
        var grid = State.EnemyGrid;
        float reach = 1.2f + impact * 0.15f;
        _hits.Clear();
        grid.Query(position, reach, _hits);
        foreach (int i in _hits)
        {
            if (MathF.Abs(grid.Heights[i] - height) > 1f)
                continue;
            var target = grid.Entities[i];
            var away = grid.Positions[i] - position;
            var direction = away.LengthSquared() > 1e-6f ? away.Normalized() : Vector2.Right;
            World.Get<Health>(target).Current -= impact * 3f;
            ref var enemy = ref World.Get<Enemy>(target);
            enemy.Knockback = direction * (8f + impact);
            enemy.HitFlash = 0.15f;
        }
        State.ShowMessage("STOMP!", 0.6f);
    }
}

/// <summary>Ages bullets and applies hits against bugs at about the bullet's height.</summary>
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
                if (target == bullet.LastHit || MathF.Abs(grid.Heights[i] + 0.5f - bullet.Height) > WeaponStats.MaxHeightDifference)
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
