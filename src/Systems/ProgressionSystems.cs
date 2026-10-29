using System;
using Arch.Core;
using BulletHeaven.Ecs;
using Godot;

namespace BulletHeaven.Systems;

/// <summary>XP gems fly to the car once it gets close, and level-ups grant a random upgrade.</summary>
public sealed class PickupSystem(World world, GameState state) : GameSystem(world, state)
{
    private const float PickupRadius = 0.9f;
    private static readonly QueryDescription Query = new QueryDescription().WithAll<XpGem, Position, Velocity>();

    public override void Update(float dt)
    {
        var playerPos = World.Get<Position>(State.Player).Value;
        float playerHeight = World.Get<Elevation>(State.Player).Height;
        var terrain = State.Terrain;
        float magnet = State.MagnetRadius;
        int collected = 0;

        World.Query(in Query, (Entity entity, ref XpGem gem, ref Position position, ref Velocity velocity) =>
        {
            var toPlayer = playerPos - position.Value;
            float distance = toPlayer.Length();
            if (!gem.Magnetized && MathF.Abs(terrain.GroundHeight(position.Value) - playerHeight) > 2.5f)
                return; // on another level: come and get it
            if (distance < PickupRadius)
            {
                collected += gem.Value;
                State.Destroy(entity);
                return;
            }

            if (gem.Magnetized || distance < magnet)
            {
                gem.Magnetized = true;
                float speed = MathF.Max(velocity.Value.Length() + 45f * dt, 8f);
                velocity.Value = toPlayer / distance * speed;
            }
        });

        if (collected > 0)
            AddXp(collected);
    }

    private void AddXp(int amount)
    {
        State.Xp += amount;
        while (State.Xp >= State.XpToNext)
        {
            State.Xp -= State.XpToNext;
            State.Level++;
            State.XpToNext = 5 + (int)(State.Level * State.Level * 1.5f);
            State.ShowMessage($"LEVEL {State.Level}: {Upgrades.ApplyRandom(World, State)}");
        }
    }
}

/// <summary>Placeholder upgrade pool, applied at random. Swap for a pick-one-of-three screen later.</summary>
public static class Upgrades
{
    public static string ApplyRandom(World world, GameState state)
    {
        var player = state.Player;
        ref var weapon = ref world.Get<Weapon>(player);
        ref var health = ref world.Get<Health>(player);
        bool isCar = world.Has<CarStats>(player);

        switch (state.Rng.Next(8))
        {
            case 0:
                weapon.Cooldown *= 0.85f;
                return "Faster firing";
            case 1:
                weapon.Damage *= 1.25f;
                return "Bigger bullets";
            case 2:
                weapon.Projectiles++;
                return "Extra barrel";
            case 3:
                weapon.Pierce++;
                return "Piercing rounds";
            case 4 when isCar:
                ref var car = ref world.Get<CarStats>(player);
                car.MaxSpeed += 1.5f;
                car.Acceleration += 2f;
                return "Turbo";
            case 4:
                world.Get<RunnerStats>(player).MaxSpeed += 0.8f;
                return "Running shoes";
            case 5 when isCar:
                state.RamDamagePerSpeed *= 1.3f;
                return "Spiked bumper";
            case 5:
                state.StompDamage *= 1.4f;
                world.Get<RunnerStats>(player).StompBounce += 1f;
                return "Heavy boots";
            case 6:
                state.MagnetRadius += 1.5f;
                return "Gem magnet";
            default:
                health.Max += 15f;
                health.Current = Math.Min(health.Max, health.Current + 40f);
                return "Repair kit";
        }
    }
}
