using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Numerics;

namespace BulletHeaven.Systems;

/// <summary>
/// Picks up items the player walks into (parachute, armour plates, turret kits, dropped modules), and
/// spots the player reaching the secret exit. Turret kits deploy a turret on the spot.
/// </summary>
public sealed class ItemSystem(World world, GameState state) : GameSystem(world, state)
{
    private const float Reach = 1.3f;
    public const float TurretLifetime = 30f;
    public const float DropLifetime = 45f;
    private static readonly QueryDescription Query = new QueryDescription().WithAll<Pickup, Position>();
    private static readonly QueryDescription DropQuery = new QueryDescription().WithAll<ModuleDrop, Position>();
    private readonly List<(Vector2 At, float Height)> _turrets = new();
    private float _fullMessage;

    /// <summary>Godot front-end: slot modules into free slots straight away (it has no module screen).</summary>
    public bool AutoFit;

    public override void Update(float dt)
    {
        var playerPos = World.Get<Position>(State.Player).Value;
        float playerHeight = World.Get<Elevation>(State.Player).Height;

        World.Query(in Query, (Entity entity, ref Pickup pickup, ref Position position) =>
        {
            if (playerPos.DistanceTo(position.Value) > Reach || MathF.Abs(playerHeight - pickup.Height) > 1.5f)
                return;
            State.Destroy(entity);
            State.Emit(GameEventKind.Pickup, position.Value, pickup.Height + 0.5f, default, (int)pickup.Kind);
            switch (pickup.Kind)
            {
                case PickupKind.Parachute:
                    State.HasParachute = true;
                    State.ShowMessage("PARACHUTE!\nHold jump while falling to glide", 4f);
                    break;
                case PickupKind.ArmorPlate when World.Has<Armor>(State.Player):
                    ref var armor = ref World.Get<Armor>(State.Player);
                    armor.Current = MathF.Min(armor.Max, armor.Current + 25f);
                    State.ShowMessage("+25 ARMOR", 1.2f);
                    break;
                case PickupKind.TurretKit:
                    _turrets.Add((position.Value, pickup.Height));
                    State.ShowMessage("TURRET DEPLOYED\nIt pulses your TD Mortar rings", 2.5f);
                    break;
            }
        });
        foreach (var (at, height) in _turrets)
            World.Create(new Position(at), new Turret { Lifetime = TurretLifetime, Height = height });
        _turrets.Clear();

        _fullMessage -= dt;
        World.Query(in DropQuery, (Entity entity, ref ModuleDrop drop, ref Position position) =>
        {
            drop.Age += dt;
            if (drop.Age > DropLifetime)
            {
                State.Destroy(entity);
                return;
            }
            if (playerPos.DistanceTo(position.Value) > Reach || MathF.Abs(playerHeight - drop.Height) > 1.5f)
                return;
            var module = Modules.Get(drop.Module);
            if (!Modules.Take(World, State, module, AutoFit))
            {
                if (_fullMessage <= 0f)
                    State.ShowMessage("INVENTORY FULL\nTab: fit or drop modules", 1.5f);
                _fullMessage = 2f;
                return;
            }
            State.Destroy(entity);
            State.Emit(GameEventKind.Pickup, position.Value, drop.Height + 0.5f, default, -1);
            State.ShowMessage(AutoFit ? $"{module.Name}" : $"{module.Name}\nTab: fit modules", 1.6f);
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
