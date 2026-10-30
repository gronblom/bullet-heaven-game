using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Levels;
using BulletHeaven.Numerics;
using BulletHeaven.Systems;

// Builds the real levels without any engine and plays them with bots: climbing routes, the
// parachute glide, puddles, weapons, modifiers and the boss. Prints PASS/FAIL per check.
int failures = 0;
void Check(bool ok, string name, string detail = "")
{
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-46} {detail}");
}

Sim.Jumps(Check);
Sim.Routes(Check);
Sim.Glide(Check);
Sim.Puddles(Check);
Sim.Weapons(Check);
Sim.Modifiers(Check);
Sim.Boss(Check);
Sim.Stress(Check);

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) failed.");
return failures == 0 ? 0 : 1;

delegate void CheckFn(bool ok, string name, string detail = "");

/// <summary>A level built headless plus the gameplay systems in Main's order.</summary>
sealed class Sim : IDisposable
{
    public const float Dt = 1f / 60f;
    public readonly World World = World.Create();
    public readonly GameState State;
    public readonly Level Level;
    private readonly GameSystem[] _systems;
    private readonly EnemySpawnSystem _spawner;

    public Sim(int levelIndex, Vector2 start, bool spawn = false)
    {
        Level = Level.All[levelIndex];
        State = new GameState(Level.Bounds);
        Level.Build(World);
        State.Terrain = Terrain.FromWorld(World, Level.Bounds);
        State.ExitArea = Level.Exit;
        State.ExitHeight = Level.ExitHeight;
        if (Level.BossSpawn is { } boss)
        {
            State.BossLevel = true;
            State.BossSpawn = boss;
            State.SpawnRateScale = 0.6f;
        }
        State.Player = World.Create(new PlayerTag(), new Position(start), new Velocity(), new Health(100f),
            new Elevation { Height = State.Terrain.GroundHeight(start), Grounded = true }, new Radius(0.35f),
            new Runner(), RunnerStats.Default);
        _spawner = spawn ? new EnemySpawnSystem(World, State) : null;
        _systems = new GameSystem[]
        {
            new PlayerInputSystem(World, State), new RunnerPhysicsSystem(World, State), new CarPhysicsSystem(World, State),
            new EnemySteeringSystem(World, State), new BossSystem(World, State), new LiquidSystem(World, State),
            new MovementSystem(World, State), new CollisionSystem(World, State), new VerticalPhysicsSystem(World, State),
            new ItemSystem(World, State), new EnemyGridSystem(World, State), new PlayerContactSystem(World, State),
            new WeaponSystem(World, State), new BulletSystem(World, State), new LightningOrbSystem(World, State),
            new ShellSystem(World, State), new EnemyDeathSystem(World, State), new PickupSystem(World, State),
        };
    }

    public Vector2 Pos => World.Get<Position>(State.Player).Value;
    public Elevation Elevation => World.Get<Elevation>(State.Player);
    public float Height => Elevation.Height;

    public void Step(Vector2 move, bool jumpPressed = false, bool jumpHeld = false, bool immortal = false)
    {
        State.Input = new PlayerInput { Move = move, JumpPressed = jumpPressed, JumpHeld = jumpHeld };
        State.Time += Dt;
        foreach (var system in _systems)
            system.Update(Dt);
        _spawner?.Update(Dt);
        State.FlushDestroyed(World);
        if (immortal)
        {
            World.Get<Health>(State.Player).Current = 100f;
            State.GameOver = false;
        }
    }

    /// <summary>Picks a random modifier for every pending level-up.</summary>
    public int AutoLevelUp(Random rng)
    {
        int picked = 0;
        while (State.PendingLevelUps > 0)
        {
            State.PendingLevelUps--;
            var offers = BulletHeaven.Systems.Modifiers.Roll(World, State, 3);
            if (offers.Count == 0)
                continue;
            BulletHeaven.Systems.Modifiers.Pick(World, State, offers[rng.Next(offers.Count)]);
            picked++;
        }
        return picked;
    }

    public Entity Ant(Vector2 at, float health, float speed = 4.5f) => World.Create(new Position(at), new Velocity(), new Radius(0.35f),
        new Elevation { Height = State.Terrain.GroundHeight(at), Grounded = true },
        new Enemy { Kind = EnemyKind.Ant, Speed = speed, ClimbSpeed = 3f, ContactDamage = 6f, XpValue = 1 }, new Health(health));

    public void Dispose() => World.Destroy(World);

    // --- checks ------------------------------------------------------------------------------

    public static void Jumps(CheckFn check)
    {
        using var full = new Sim(0, new Vector2(0f, 40f));
        float top = 0f;
        for (int f = 0; f < 70; f++) { full.Step(Vector2.Zero, f == 1, true); top = MathF.Max(top, full.Height); }
        check(MathF.Abs(top - 1.97f) < 0.1f, "Full jump height ~1.97", $"{top:0.00}");

        using var hop = new Sim(0, new Vector2(0f, 40f));
        top = 0f;
        for (int f = 0; f < 70; f++) { hop.Step(Vector2.Zero, f == 1, f < 4); top = MathF.Max(top, hop.Height); }
        check(top < 1f, "Tap = short hop", $"{top:0.00}");
    }

    public static void Routes(CheckFn check)
    {
        Route(check, 0, "Kitchen: board -> stool -> counter", 10f, (-14, 40), (-14, 13), (-14, 4));
        Route(check, 0, "Kitchen: toy blocks -> counter", 10f, (36, 20), (36, 14), (36, 8), (36, 2), (36, -4), (36, -10), (29.5f, -10), (24, -6));
        var toaster = Route(check, 0, "Kitchen: rulers -> books -> toaster", 14.6f, (-8, -10), (-1, -10), (4, -10), (8, -10), (8, -15));
        check(toaster, "Kitchen: parachute picked up on the toaster");
        Route(check, 1, "Study: books/stool/chair/towers -> desk", 13f, (-15, -8), (-15, -14.5f), (-15, -19.5f), (-15, -25.5f), (-10, -27), (-10, -32.5f),
            (-6.85f, -32), (-2.85f, -32), (1.15f, -32), (9, -32));
        Route(check, 2, "Windowsill: seed trays", 3.2f, (40, 20), (40, 14), (48, 14));
    }

    /// <summary>Walks a bot through the waypoints, jumping at walls and across gaps. Returns whether it got the parachute.</summary>
    private static bool Route(CheckFn check, int level, string name, float wantHeight, params (float X, float Y)[] points)
    {
        using var sim = new Sim(level, new Vector2(points[0].X, points[0].Y));
        int reached = 1;
        for (int f = 0; f < 60 * 30 && reached < points.Length; f++)
        {
            var target = new Vector2(points[reached].X, points[reached].Y);
            var to = target - sim.Pos;
            var e = sim.Elevation;
            if (to.Length() < 0.6f && e.Grounded) { reached++; continue; }
            var dir = to.Normalized();
            float ahead = sim.State.Terrain.GroundHeight(sim.Pos + dir * 1.2f);
            float goal = sim.State.Terrain.GroundHeight(target);
            bool jump = e.Grounded && (ahead > e.Height + 0.3f || (ahead < e.Height - 0.5f && goal > e.Height - 0.5f));
            sim.Step(to.Length() < 1.5f ? dir * 0.5f : dir, jump, true, immortal: true);
        }
        bool ok = reached == points.Length && MathF.Abs(sim.Height - wantHeight) < 0.3f;
        check(ok, name, $"waypoints {reached}/{points.Length}, height {sim.Height:0.0} (want {wantHeight})");
        return sim.State.HasParachute;
    }

    public static void Glide(CheckFn check)
    {
        foreach (bool parachute in new[] { true, false })
        {
            using var sim = new Sim(0, new Vector2(8f, -17.5f)); // on the toaster, at its back edge
            sim.State.HasParachute = parachute;
            for (int f = 0; f < 60 * 12; f++)
            {
                sim.Step(sim.State.ReachedExit ? Vector2.Zero : new Vector2(0f, -1f), f == 0, true, immortal: true);
                if (f > 10 && sim.Elevation.Grounded)
                    break;
            }
            if (parachute)
                check(sim.State.ReachedExit && MathF.Abs(sim.Height - 10f) < 0.3f, "Glide toaster -> back cabinets -> exit", $"end {sim.Pos} h {sim.Height:0.0}");
            else
                check(!sim.State.ReachedExit && sim.Height < 0.5f, "Without parachute: falls short", $"end {sim.Pos} h {sim.Height:0.0}");
        }
    }

    public static void Puddles(CheckFn check)
    {
        using var sim = new Sim(0, new Vector2(-48f, 36f)); // west of the water spilled by the dog bowl
        float inside = 0f, outside = 0f;
        for (int f = 0; f < 60 * 3; f++)
        {
            sim.Step(Vector2.Right);
            float speed = sim.World.Get<Velocity>(sim.State.Player).Value.Length();
            if (sim.Pos.DistanceTo(new Vector2(-38f, 36f)) < 3f) inside = MathF.Max(inside, speed);
            else if (sim.Pos.X < -44f) outside = MathF.Max(outside, speed);
        }
        check(inside < outside * 0.8f, "Water slows the player", $"{outside:0.0} -> {inside:0.0}");

        sim.World.Get<Position>(sim.State.Player).Value = new Vector2(-20f, 36f);
        var ant = sim.Ant(new Vector2(-40f, 36f), 1000f, speed: 4.5f);
        for (int f = 0; f < 20; f++)
            sim.Step(Vector2.Zero, immortal: true);
        float antSpeed = sim.World.Get<Velocity>(ant).Value.Length();
        check(antSpeed < 4.5f * 0.8f, "Water slows bugs", $"4.5 -> {antSpeed:0.0}");
    }

    public static void Weapons(CheckFn check)
    {
        foreach (var kind in new[] { WeaponKind.Minigun, WeaponKind.LightningWand, WeaponKind.SeedMortar })
        {
            using var sim = new Sim(0, new Vector2(0f, 30f));
            Arsenal.Grant(sim.World, kind);
            var rng = new Random(5);
            for (int i = 0; i < 60; i++)
                sim.Ant(new Vector2(0f, 30f) + Vector2.FromAngle((float)rng.NextDouble() * Mathf.Tau) * (4f + (float)rng.NextDouble() * 8f), 12f);
            for (int f = 0; f < 600; f++)
                sim.Step(Vector2.Zero, immortal: true);
            check(sim.State.Kills >= 55, $"{WeaponStats.Name(kind)} clears a crowd", $"{sim.State.Kills}/60 in 10 s");
        }

        using var mortar = new Sim(0, new Vector2(0f, 12f));
        Arsenal.Grant(mortar.World, WeaponKind.SeedMortar);
        for (int i = 0; i < 10; i++)
            mortar.World.Get<Elevation>(mortar.Ant(new Vector2(-5 + i, 0f), 25f, speed: 0f)).Height = 10f;
        for (int f = 0; f < 600; f++)
            mortar.Step(Vector2.Zero, immortal: true);
        check(mortar.State.Kills >= 5, "Mortar hits bugs up on the counter", $"{mortar.State.Kills}/10");
    }

    public static void Modifiers(CheckFn check)
    {
        using var sim = new Sim(0, new Vector2(0f, 30f));
        Arsenal.Grant(sim.World, WeaponKind.Minigun);
        Arsenal.Grant(sim.World, WeaponKind.SeedMortar);
        var rng = new Random(1);
        bool wrongWeapon = false, duplicate = false;
        int picks = 0;
        for (int i = 0; i < 100; i++)
        {
            var offers = BulletHeaven.Systems.Modifiers.Roll(sim.World, sim.State, 3);
            if (offers.Count == 0)
                break;
            wrongWeapon |= offers.Any(m => m.Weapon == WeaponKind.LightningWand);
            duplicate |= offers.Select(m => m.Name).Distinct().Count() != offers.Count;
            BulletHeaven.Systems.Modifiers.Pick(sim.World, sim.State, offers[rng.Next(offers.Count)]);
            picks++;
        }
        bool overLimit = BulletHeaven.Systems.Modifiers.All.Any(m => BulletHeaven.Systems.Modifiers.Picks(sim.State, m) > m.MaxPicks);
        check(!wrongWeapon && !duplicate && !overLimit, "Modifiers: carried weapons only, within limits", $"{picks} picks until exhausted");

        // Sticky jam: mortar blasts leave temporary puddles.
        for (int i = 0; i < 20; i++)
            sim.Ant(new Vector2(-5f + i * 0.5f, 22f), 12f);
        var puddles = new QueryDescription().WithAll<Puddle>();
        int jam = 0;
        for (int f = 0; f < 60 * 4; f++)
        {
            sim.Step(Vector2.Zero, immortal: true);
            int n = 0;
            sim.World.Query(in puddles, (ref Puddle p) => { if (p.Lifetime > 0f) n++; });
            jam = Math.Max(jam, n);
        }
        check(jam > 0, "Sticky jam modifier spills jam", $"up to {jam} at once");
    }

    public static void Boss(CheckFn check)
    {
        using var sim = new Sim(2, Level.All[2].PlayerStart, spawn: true);
        Arsenal.Grant(sim.World, WeaponKind.Minigun);
        Arsenal.Grant(sim.World, WeaponKind.SeedMortar);
        var rng = new Random(2);
        var move = Vector2.Zero;
        int charges = 0;
        bool wasCharging = false;
        for (int f = 0; f < 60 * 240 && !sim.State.BossDefeated; f++)
        {
            sim.AutoLevelUp(rng);
            if (f % 30 == 0)
                move = (sim.Pos.Orthogonal().Normalized() - sim.Pos * 0.03f).Normalized(); // kite round the middle
            sim.Step(move, immortal: true);
            if (sim.State.BossSpawned && !sim.State.BossDefeated && sim.World.IsAlive(sim.State.Boss))
            {
                bool charging = sim.World.Get<BulletHeaven.Ecs.Boss>(sim.State.Boss).Charging > 0f;
                if (charging && !wasCharging)
                    charges++;
                wasCharging = charging;
            }
        }
        check(sim.State.BossSpawned && charges > 0, "Queen Ant appears and charges", $"{charges} charges");
        check(sim.State.BossDefeated, "Queen Ant can be beaten within 4 minutes", $"{sim.State.Time:0} s, {sim.State.Kills} kills, level {sim.State.Level}");
    }

    public static void Stress(CheckFn check)
    {
        using var sim = new Sim(0, new Vector2(0f, 12f));
        Arsenal.Grant(sim.World, WeaponKind.Minigun);
        var rng = new Random(1);
        for (int i = 0; i < 1500; i++)
            sim.Ant(new Vector2(rng.Next(-75, 75), rng.Next(-50, 50)), 1e6f);
        for (int f = 0; f < 300; f++)
            sim.Step(Vector2.Zero, immortal: true);
        var watch = Stopwatch.StartNew();
        for (int f = 0; f < 300; f++)
            sim.Step(Vector2.Zero, immortal: true);
        double ms = watch.Elapsed.TotalMilliseconds / 300;
        check(ms < 8, "1,500 bugs: gameplay cost per frame", $"{ms:0.00} ms");
    }
}
