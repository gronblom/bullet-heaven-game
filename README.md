# Bullet Heaven (working title)

A 3D bullet heaven where you drive a tiny toy car around a giant kitchen table, Micro Machines
style, while endless swarms of bugs pour in. Ram them, drift through them, and let the auto-gun
mop up the rest. Built with **Godot 4.7 (.NET / C#)** and the **[Arch](https://github.com/genaray/Arch) ECS**.

## Getting started

1. Install the **.NET SDK 8** or newer: https://dotnet.microsoft.com/download
2. Download **Godot 4.7 – .NET** (the C# edition, *not* the standard one): https://godotengine.org/download
   - To check whether you already have it: open Godot; the project manager title shows the version,
     and the .NET edition says ".NET" / "mono" in its name.
3. In Godot's project manager: **Import** → pick this folder's `project.godot` → open it.
4. Press **F5** (or the ▶ button). The first run builds the C# project and restores NuGet packages.

You can also compile without the editor: `dotnet build`.

## Controls

| Action | Keyboard | Gamepad |
|---|---|---|
| Accelerate | W / ↑ | RT or A |
| Brake / reverse | S / ↓ | LT or X |
| Steer | A D / ← → | Left stick |
| Handbrake (drift) | Space / Shift | B |
| Restart after wrecking | R / Enter | Start |

## Gameplay so far

- **Micro Machines driving**: arcade grip model with drifting, a fixed overhead camera with
  look-ahead, and you can **drive off the table edge** (fall, respawn, lose some health).
- **Ramming**: hit bugs faster than a threshold to squash them; slower contact and they bite you.
- **Auto-aim gun** targeting the nearest bug.
- **Enemies**: ants, plus tanky beetles after 45 s; spawn rate ramps up and a swarm arrives every 30 s.
- **XP gems** with magnet pickup; each level-up applies a random upgrade (fire rate, damage,
  extra barrel, piercing, turbo, spiked bumper, magnet, repair).
- **Miniature look**: narrow FOV + near/far depth of field (tilt-shift), soft shadows, SSAO,
  oversized props (mugs, books, cereal box, pencils, apple, coins, crumbs).

All art is placeholder primitives, ready to swap for real models.

## Architecture

```
src/
  Main.cs              Entry point: creates the ECS world, runs systems, syncs visuals
  GameInput.cs         Input actions registered in code
  Hud.cs               HUD
  Ecs/
    Components.cs      Plain data structs (Position, Velocity, Car, Enemy, Weapon, ...)
    GameState.cs       Run-wide state + deferred entity destruction
    SpatialGrid.cs     Per-frame uniform grid for neighbour / hit queries
  Systems/             One class per behaviour, run in a fixed order from Main
  Rendering/           MultiMesh batch renderer, car model, materials
  Levels/              Level builders (KitchenTable)
```

- **Gameplay is pure ECS.** Godot nodes are only used for presentation. Everything runs on the
  ground plane as `Vector2 (x, y)` → world `(x, 0, y)`.
- **Rendering of crowds uses `MultiMesh`.** Enemies, bullets and gems are uploaded as one float
  buffer per frame, so thousands of entities cost a few draw calls.
- **Structural changes** (create/destroy) never happen inside a query: destroys go through
  `GameState.Destroy`, spawns are collected and created after the query.
- Measured headless: 1,500 enemies piled onto the car cost about 2.5 ms per frame for all
  gameplay systems.

## Ideas / next steps

- Pick-one-of-three upgrade screen instead of random upgrades
- Pikmin-style follower swarm (followers as weapons)
- Skid marks, dust particles, screen shake, sound
- Ramps (pencils, rulers) and jumps; more levels (garden, sandbox, bathtub)
- Real car and bug models
