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
| Handbrake (drift) | Shift / Ctrl | B or RB |
| Jump | Space | Y |
| Restart after wrecking | R / Enter | Start |

## Gameplay so far

- **Micro Machines driving**: arcade grip model with drifting, a fixed overhead camera with
  look-ahead, and you can **drive off the table edge** (fall, respawn, lose some health).
- **Height and jumping**: ruler ramps up onto books (including a two-level stack), kickers over
  pencils, a free-standing launch ramp, and a hop button. Driving off a ramp or a book edge at
  speed launches you; no steering in the air.
- **Ramming**: hit bugs faster than a threshold to squash them; slower contact and they bite you.
  Bugs only reach you at their height, so jumping clears a swarm, and a hard landing
  **ground-pounds** everything around you. Bugs climb books, but not mugs or the cereal box.
- **Auto-aim gun** targeting the nearest bug.
- **Enemies**: ants, plus tanky beetles after 45 s; spawn rate ramps up and a swarm arrives every 30 s.
- **XP gems** with magnet pickup; each level-up applies a random upgrade (fire rate, damage,
  extra barrel, piercing, turbo, spiked bumper, magnet, repair).
- **Miniature look**: narrow FOV + near/far depth of field (tilt-shift), soft shadows, SSAO,
  oversized props (mugs, books, cereal box, pencils, apple, coins, crumbs).

## Using your own 3D models

Placeholder shapes are used until you add models. Export as **.glb** and drop them into
`assets/models/` with these names:

| File | Replaces |
|---|---|
| `car.glb` | the player's car |
| `ant.glb` | ants |
| `beetle.glb` | beetles |
| `gem.glb` | XP gems |

They are fitted automatically: scaled to the right size, centred, and stood on the ground. Models
keep their own materials and textures (the red hit flash then becomes a little size "pop").
The game expects glTF's usual facing (front along +Z). If a model drives sideways or backwards,
change its `YawDegrees` in `src/Rendering/ModelLibrary.cs` (try 0, 180 or -90).
Tips for AI-generated models: ask for a single object, low-poly / game-ready, and keep it under
roughly 10k triangles for the bugs, since there can be over a thousand of them on screen.

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
  Ecs/Terrain.cs       Ground height + walls from Obstacle/Ramp entities
```

- **Gameplay is pure ECS.** Godot nodes are only used for presentation. Horizontal movement runs
  on the ground plane as `Vector2 (x, y)` → world `(x, 0, y)`; height is a separate `Elevation`
  component driven by gravity and `Terrain.GroundHeight`. Props are walls only for things below
  their top that can't step up onto them.
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
- More levels (garden, sandbox, bathtub), loops and gaps to jump
- Real car and bug models
