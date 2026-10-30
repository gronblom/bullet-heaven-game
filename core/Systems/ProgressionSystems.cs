using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Numerics;

namespace BulletHeaven.Systems;

/// <summary>XP gems fly to the car once it gets close, and level-ups queue a modifier choice (shown by Main).</summary>
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
            State.PendingLevelUps++;
        }
    }
}

/// <summary>One level-up reward: a weapon modifier or a passive, pickable up to <see cref="MaxPicks"/> times.</summary>
public sealed record Modifier(string Name, string Description, WeaponKind? Weapon, int MaxPicks, Action<World, GameState> Apply);

/// <summary>
/// The level-up catalogue. No weapon levels: each run starts with two chosen weapons and every
/// level-up offers three of these to pick from, so a build is a set of choices rather than a grind.
/// </summary>
public static class Modifiers
{
    private static Modifier Weapon(WeaponKind kind, string name, string text, int max, Arsenal.SlotChange change) =>
        new(name, text, kind, max, (world, _) => Arsenal.Modify(world, kind, change));

    public static readonly Modifier[] All =
    {
        Weapon(WeaponKind.Minigun, "Hollow points", "Minigun +40% damage", 3, (ref WeaponSlot w) => w.Damage++),
        Weapon(WeaponKind.Minigun, "Spin-up motor", "Minigun fires 25% faster", 3, (ref WeaponSlot w) => w.Rate++),
        Weapon(WeaponKind.Minigun, "Extra barrel", "One more bullet stream", 2, (ref WeaponSlot w) => w.Count++),
        Weapon(WeaponKind.Minigun, "Tungsten rounds", "Bullets pierce one more bug", 3, (ref WeaponSlot w) => w.Pierce++),
        Weapon(WeaponKind.Minigun, "Long barrel", "Minigun range +3", 2, (ref WeaponSlot w) => w.Area++),

        Weapon(WeaponKind.LightningWand, "Overcharge", "Zaps +40% damage", 3, (ref WeaponSlot w) => w.Damage++),
        Weapon(WeaponKind.LightningWand, "Quick incantation", "Orbs come 25% faster", 3, (ref WeaponSlot w) => w.Rate++),
        Weapon(WeaponKind.LightningWand, "Twin orbs", "One more orb per cast", 2, (ref WeaponSlot w) => w.Count++),
        Weapon(WeaponKind.LightningWand, "Forked lightning", "Each zap chains to 2 more bugs", 3, (ref WeaponSlot w) => w.Pierce++),
        Weapon(WeaponKind.LightningWand, "Static field", "Bigger zap radius", 2, (ref WeaponSlot w) => w.Area++),
        Weapon(WeaponKind.LightningWand, "Long fuse", "Orbs last 2 s longer", 2, (ref WeaponSlot w) => w.Special++),

        Weapon(WeaponKind.SeedMortar, "Heavy seeds", "Mortar +40% damage", 3, (ref WeaponSlot w) => w.Damage++),
        Weapon(WeaponKind.SeedMortar, "Quick reload", "Mortar fires 25% faster", 3, (ref WeaponSlot w) => w.Rate++),
        Weapon(WeaponKind.SeedMortar, "Cluster seeds", "One more shell per volley", 3, (ref WeaponSlot w) => w.Count++),
        Weapon(WeaponKind.SeedMortar, "Big bang", "Bigger blasts", 3, (ref WeaponSlot w) => w.Area++),
        Weapon(WeaponKind.SeedMortar, "Sticky jam", "Blasts leave jam that slows bugs", 1, (ref WeaponSlot w) => w.Special++),

        new("Thick skin", "+25 max health, heal 40", null, 3, (world, state) =>
        {
            ref var health = ref world.Get<Health>(state.Player);
            health.Max += 25f;
            health.Current = Math.Min(health.Max, health.Current + 40f);
        }),
        new("Running shoes", "Run faster", null, 3, (world, state) =>
        {
            if (world.Has<RunnerStats>(state.Player))
                world.Get<RunnerStats>(state.Player).MaxSpeed += 0.8f;
            else
                world.Get<CarStats>(state.Player).MaxSpeed += 1.5f;
        }),
        new("Gem magnet", "Pick up gems from further away", null, 3, (_, state) => state.MagnetRadius += 2f),
        new("Heavy boots", "Stomps hit harder and bounce higher", null, 3, (world, state) =>
        {
            state.StompDamage *= 1.4f;
            state.RamDamagePerSpeed *= 1.3f;
            if (world.Has<RunnerStats>(state.Player))
                world.Get<RunnerStats>(state.Player).StompBounce += 1f;
        }),
    };

    public static int Picks(GameState state, Modifier modifier) => state.Passives.GetValueOrDefault(modifier.Name);

    /// <summary>Up to <paramref name="count"/> different modifiers that can still be picked for the weapons carried.</summary>
    public static List<Modifier> Roll(World world, GameState state, int count)
    {
        var pool = new List<Modifier>();
        foreach (var m in All)
        {
            if (Picks(state, m) < m.MaxPicks && (m.Weapon == null || Arsenal.Has(world, m.Weapon.Value)))
                pool.Add(m);
        }
        var result = new List<Modifier>();
        while (result.Count < count && pool.Count > 0)
        {
            int i = state.Rng.Next(pool.Count);
            result.Add(pool[i]);
            pool.RemoveAt(i);
        }
        return result;
    }

    public static void Pick(World world, GameState state, Modifier modifier)
    {
        modifier.Apply(world, state);
        state.Passives[modifier.Name] = Picks(state, modifier) + 1;
    }
}
