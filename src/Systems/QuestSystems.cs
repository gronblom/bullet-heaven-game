using System;
using Arch.Core;
using BulletHeaven.Ecs;
using Godot;

namespace BulletHeaven.Systems;

/// <summary>Picks up items (the parachute) the player walks into, and spots the player reaching the secret exit.</summary>
public sealed class ItemSystem(World world, GameState state) : GameSystem(world, state)
{
    private const float Reach = 1.3f;
    private static readonly QueryDescription Query = new QueryDescription().WithAll<Pickup, Position>();

    public override void Update(float dt)
    {
        var playerPos = World.Get<Position>(State.Player).Value;
        float playerHeight = World.Get<Elevation>(State.Player).Height;

        World.Query(in Query, (Entity entity, ref Pickup pickup, ref Position position) =>
        {
            if (playerPos.DistanceTo(position.Value) > Reach || MathF.Abs(playerHeight - pickup.Height) > 1.5f)
                return;
            State.Destroy(entity);
            if (pickup.Kind == PickupKind.Parachute)
            {
                State.HasParachute = true;
                State.ShowMessage("PARACHUTE!\nHold jump while falling to glide", 4f);
            }
        });

        if (State.ExitArea is { } exit && !State.ReachedExit && exit.HasPoint(playerPos) && playerHeight >= State.ExitHeight)
            State.ReachedExit = true;
    }
}

/// <summary>
/// The Queen Ant. Appears a little into the boss level, calls in a ring of ants every few seconds
/// and now and then rears up (telegraph) and charges in a straight line. Runs after steering so it
/// can override her velocity.
/// </summary>
public sealed class BossSystem(World world, GameState state) : GameSystem(world, state)
{
    public const float AppearTime = 12f;
    private const float SummonInterval = 7f;
    private const int SummonCount = 10;
    private const float ChargeInterval = 10f;
    private const float TelegraphTime = 0.9f;
    private const float ChargeTime = 0.8f;
    private const float ChargeSpeed = 20f;

    public override void Update(float dt)
    {
        if (!State.BossLevel || State.BossDefeated)
            return;

        if (!State.BossSpawned)
        {
            if (State.Time < AppearTime)
                return;
            State.BossSpawned = true;
            State.Boss = Bugs.Spawn(World, State, State.BossSpawn, EnemyKind.Queen);
            World.Add(State.Boss, new Boss { SummonTimer = 3f, ChargeTimer = ChargeInterval * 0.6f });
            State.BossMaxHealth = Bugs.QueenHealth;
            State.ShowMessage("THE QUEEN ANT", 3f);
        }

        if (!World.IsAlive(State.Boss) || World.Get<Health>(State.Boss).Current <= 0f)
        {
            State.BossDefeated = true;
            State.BossHealth = 0f;
            State.SpawnRateScale = 0.3f;
            State.ShowMessage("QUEEN DEFEATED!", 5f);
            return;
        }

        State.BossHealth = World.Get<Health>(State.Boss).Current;
        ref var boss = ref World.Get<Boss>(State.Boss);
        ref var velocity = ref World.Get<Velocity>(State.Boss);
        var position = World.Get<Position>(State.Boss).Value;
        var playerPos = World.Get<Position>(State.Player).Value;

        boss.SummonTimer -= dt;
        if (boss.SummonTimer <= 0f)
        {
            boss.SummonTimer = SummonInterval;
            var inner = State.Bounds.Grow(-1f);
            for (int i = 0; i < SummonCount; i++)
            {
                var at = (position + Vector2.FromAngle(i * Mathf.Tau / SummonCount) * 3.5f).Clamp(inner.Position, inner.End);
                Bugs.Spawn(World, State, at, EnemyKind.Ant);
            }
        }

        if (boss.Charging > 0f)
        {
            boss.Charging -= dt;
            velocity.Value = boss.ChargeDirection * ChargeSpeed;
            return;
        }
        if (boss.Telegraph > 0f)
        {
            boss.Telegraph -= dt;
            velocity.Value = Vector2.Zero;
            if (boss.Telegraph <= 0f)
                boss.Charging = ChargeTime;
            return;
        }

        boss.ChargeTimer -= dt;
        if (boss.ChargeTimer <= 0f && position.DistanceTo(playerPos) < 30f)
        {
            boss.ChargeTimer = ChargeInterval;
            boss.Telegraph = TelegraphTime;
            var toward = playerPos - position;
            boss.ChargeDirection = toward.LengthSquared() > 1e-4f ? toward.Normalized() : Vector2.Right;
        }
    }
}
