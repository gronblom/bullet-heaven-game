using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Numerics;

namespace BulletHeaven.Systems;

/// <summary>XP gems fly to the player once they get close; level-ups queue a module choice (shown by the front-end).</summary>
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

/// <summary>
/// The module catalogue and the inventory rules. Modules come from level-ups (pick one of three) and
/// from drops; weapon modules go into the inventory and from there into a weapon's slots, where they
/// change its numbers (+ADD, xMULTIPLY) or its geometry. Player modules apply at once.
/// </summary>
public static class Modules
{
    private static Module Add(string name, Stat stat, float value, string text, WeaponKind? only = null) =>
        new(name, $"+{value:0.#} {Abbrev(stat)}", ModuleOp.Add, stat, value, Geometry.Linear, only, text);

    private static Module Mul(string name, Stat stat, float value, string text, WeaponKind? only = null) =>
        new(name, $"x{value:0.#} {Abbrev(stat)}", ModuleOp.Multiply, stat, value, Geometry.Linear, only, text);

    private static Module Shape(string name, Geometry geometry, string text, WeaponKind? only = null) =>
        new(name, geometry.ToString().ToUpperInvariant(), ModuleOp.Geometry, Stat.Count, 0f, geometry, only, text);

    private static Module You(string name, string shortName, string text, Action<World, GameState> apply) =>
        new(name, shortName, ModuleOp.Player, Stat.Count, 0f, Geometry.Linear, null, text, apply);

    public static string Abbrev(Stat stat) => stat switch
    {
        Stat.Damage => "DMG",
        Stat.FireRate => "RATE",
        Stat.Count => "PARTICLES",
        Stat.Pierce => "PIERCE",
        Stat.Chain => "CHAIN",
        Stat.Area => "AREA",
        Stat.Frequency => "FREQ",
        Stat.Amplitude => "AMP",
        _ => "RANGE",
    };

    public static readonly Module[] All =
    {
        // + ADD: flat additions.
        Add("+4 Damage", Stat.Damage, 4f, "Flat damage on every hit."),
        Add("+1 Particle", Stat.Count, 1f, "One more stream, arc, ring or orb per shot."),
        Add("+2 Pierce", Stat.Pierce, 2f, "Rounds pass through two more bugs.", WeaponKind.Minigun),
        Add("+1 Chain target", Stat.Chain, 1f, "Every arc jumps to one more bug.", WeaponKind.ArcaneWand),
        Add("+0.6 Amplitude", Stat.Amplitude, 0.6f, "Wider waves (a in sin(θ + t·f)·a)."),
        Add("+3 Range", Stat.Range, 3f, "Reaches further."),
        // x MULTIPLY: scaling.
        Mul("x1.5 Fire rate", Stat.FireRate, 1.5f, "Fires half again as often."),
        Mul("x2 Particle count", Stat.Count, 2f, "Doubles the particles of every volley."),
        Mul("x1.5 Damage", Stat.Damage, 1.5f, "Every hit lands harder."),
        Mul("x1.5 Frequency", Stat.Frequency, 1.5f, "Faster waves and spirals (f)."),
        Mul("x1.5 Amplitude", Stat.Amplitude, 1.5f, "Bigger waves (a)."),
        Mul("x1.4 Area", Stat.Area, 1.4f, "Bigger rings, orbs, arcs and beams."),
        // Geometry overrides: swap the pattern function.
        Shape("Geometry: Sine", Geometry.Sine, "Projectiles weave: offset = sin(θ + t·f)·a."),
        Shape("Geometry: Spiral", Geometry.Spiral, "Rotating arms wind outwards, all around you."),
        Shape("Geometry: Pentagon", Geometry.Pentagon, "Five-way star volleys; pentagonal fire rings."),
        Shape("Geometry: Radial", Geometry.Radial, "A burst in every direction."),
        Shape("Geometry: Laser", Geometry.Laser, "A continuous beam that pierces everything.", WeaponKind.Minigun),
        // Player modules: apply at once.
        You("Armor plating", "+30 ARMOR", "Thirty more armour, fully plated.", (world, state) =>
        {
            ref var armor = ref world.Get<Armor>(state.Player);
            armor.Max += 30f;
            armor.Current = armor.Max;
        }),
        You("Running shoes", "+SPEED", "Run faster.", (world, state) =>
        {
            if (world.Has<RunnerStats>(state.Player))
                world.Get<RunnerStats>(state.Player).MaxSpeed += 0.8f;
            else
                world.Get<CarStats>(state.Player).MaxSpeed += 1.5f;
        }),
        You("Gem magnet", "+MAGNET", "Pick up crystals from further away.", (_, state) => state.MagnetRadius += 2f),
        You("Field medic", "+HEAL", "+20 max health, healed in full.", (world, state) =>
        {
            ref var health = ref world.Get<Health>(state.Player);
            health.Max += 20f;
            health.Current = health.Max;
        }),
    };

    public static Module Get(int index) => All[index];
    public static int IndexOf(Module module) => Array.IndexOf(All, module);

    private static int Weight(Module m) => m.Op switch
    {
        ModuleOp.Add => 10,
        ModuleOp.Multiply => 6,
        ModuleOp.Geometry => 3,
        _ => 4,
    };

    /// <summary>A random module index, rarer kinds less often (geometry overrides are the rarest).</summary>
    public static int RandomIndex(Random rng, bool weaponOnly = false)
    {
        int total = 0;
        foreach (var m in All)
            total += weaponOnly && m.Op == ModuleOp.Player ? 0 : Weight(m);
        int roll = rng.Next(total);
        for (int i = 0; i < All.Length; i++)
        {
            int w = weaponOnly && All[i].Op == ModuleOp.Player ? 0 : Weight(All[i]);
            if (roll < w)
                return i;
            roll -= w;
        }
        return 0;
    }

    /// <summary>Up to <paramref name="count"/> different modules for a level-up.</summary>
    public static List<Module> Roll(GameState state, int count)
    {
        var result = new List<Module>();
        for (int attempt = 0; result.Count < count && attempt < 50; attempt++)
        {
            var m = All[RandomIndex(state.Rng)];
            if (!result.Contains(m))
                result.Add(m);
        }
        return result;
    }

    /// <summary>
    /// Takes a module: player modules apply now; weapon modules go into the inventory (or straight
    /// into a free slot with <paramref name="autoFit"/>). False if the inventory is full.
    /// </summary>
    public static bool Take(World world, GameState state, Module module, bool autoFit)
    {
        if (module.Op == ModuleOp.Player)
        {
            module.ApplyToPlayer?.Invoke(world, state);
            return true;
        }
        if (autoFit && AutoFit(state, module))
            return true;
        if (state.Inventory.Count >= GameState.InventorySize)
            return false;
        state.Inventory.Add(module);
        return true;
    }

    /// <summary>Puts an inventory module into a weapon slot; what was there goes back to the inventory.</summary>
    public static bool Slot(GameState state, int inventoryIndex, WeaponKind kind, int slot)
    {
        if (inventoryIndex < 0 || inventoryIndex >= state.Inventory.Count || slot < 0 || slot >= Weapon.SlotCount)
            return false;
        var module = state.Inventory[inventoryIndex];
        var weapon = state.Weapon(kind);
        if (!module.Fits(kind))
            return false;
        state.Inventory.RemoveAt(inventoryIndex);
        if (weapon.Slots[slot] is { } previous)
            state.Inventory.Insert(Math.Min(inventoryIndex, state.Inventory.Count), previous);
        weapon.Slots[slot] = module;
        weapon.Recompute();
        return true;
    }

    /// <summary>Moves a slotted module back to the inventory (if there is room).</summary>
    public static bool Unslot(GameState state, WeaponKind kind, int slot)
    {
        var weapon = state.Weapon(kind);
        if (weapon.Slots[slot] is not { } module || state.Inventory.Count >= GameState.InventorySize)
            return false;
        weapon.Slots[slot] = null;
        state.Inventory.Add(module);
        weapon.Recompute();
        return true;
    }

    /// <summary>Moves a module between two slots (same or different weapons), swapping if needed.</summary>
    public static bool Move(GameState state, WeaponKind fromKind, int fromSlot, WeaponKind toKind, int toSlot)
    {
        var from = state.Weapon(fromKind);
        var to = state.Weapon(toKind);
        var module = from.Slots[fromSlot];
        var other = to.Slots[toSlot];
        if (module == null || !module.Fits(toKind) || (other != null && !other.Fits(fromKind)))
            return false;
        to.Slots[toSlot] = module;
        from.Slots[fromSlot] = other;
        from.Recompute();
        to.Recompute();
        return true;
    }

    /// <summary>Slots a module into the first free fitting slot, preferring its own weapon and emptier weapons.</summary>
    public static bool AutoFit(GameState state, Module module)
    {
        Weapon best = null;
        int bestFree = 0;
        foreach (var weapon in state.Weapons)
        {
            if (!module.Fits(weapon.Kind))
                continue;
            int free = Array.FindAll(weapon.Slots, m => m == null).Length;
            if (free > bestFree)
            {
                best = weapon;
                bestFree = free;
            }
        }
        if (best == null)
            return false;
        best.Slots[Array.IndexOf(best.Slots, null)] = module;
        best.Recompute();
        return true;
    }

    /// <summary>Slots everything in the inventory that fits somewhere.</summary>
    public static void AutoFitInventory(GameState state)
    {
        for (int i = state.Inventory.Count - 1; i >= 0; i--)
        {
            if (AutoFit(state, state.Inventory[i]))
                state.Inventory.RemoveAt(i);
        }
    }
}
