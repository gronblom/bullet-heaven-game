# Bullet Heaven (working title)

A 3D bullet heaven at bug's-eye scale: a tiny guy runs around a giant kitchen while endless swarms
of bugs pour in. Climb stools and toy blocks onto the counter, jump off it onto the swarm, stomp
bugs Mario-style and let the auto-gun do the rest. Built with **Godot 4.7 (.NET / C#)** and the
**[Arch](https://github.com/genaray/Arch) ECS**. A Micro Machines-style toy car is still in as an
alternative player.

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
| Run | WASD / arrows | Left stick |
| Jump (hold for higher) | Space | A or Y |
| Restart after getting squashed | R / Enter | Start |

**Playing as the car instead:** select the `Main` node in `scenes/main.tscn` and tick **Drive Car**
in the Inspector. Car controls: W/S or RT/LT to drive and brake, A/D or the stick to steer,
Shift/Ctrl or B to drift, Space or Y to jump.

## Gameplay so far

- **The kitchen**: the floor is the main arena. The counter in the middle (10 up) is high ground
  with mugs, books, a cereal box, an apple and pencils on it.
  - **Ways up**: a cutting board leaning on a stool (then jump to the counter), or the toy-block
    staircase (one jump per block). Ramps on the counter launch you, one of them right off the edge.
  - **Ways down**: anywhere. A long fall ends in a **shockwave** that flattens bugs around you.
  - Tall things in front of the camera turn **see-through** so you never lose the player.
- **Runner**: push a direction and it runs that way. Jumps have a short input buffer, coyote time
  and variable height. **Land on a bug to stomp it** and bounce off: chain stomps across a swarm.
- **Bugs climb**: they run straight at you and scale any wall in the way, so camping on the
  counter just means watching ants pour over the edge. They drop off edges to follow you down.
- **Auto-aim gun** targeting the nearest bug on your level.
- **Enemies**: ants, plus tanky, slow-climbing beetles after 45 s; spawn rate ramps up and a swarm
  arrives every 30 s.
- **XP gems** with magnet pickup; each level-up applies a random upgrade (fire rate, damage,
  extra barrel, piercing, running shoes, heavy boots, magnet, repair).
- **Miniature look**: narrow FOV + near/far depth of field (tilt-shift), soft shadows, SSAO.

## Using your own 3D models

Placeholder shapes are used until you add models. Export as **.glb** and drop them into
`assets/models/` with these names:

| File | Replaces |
|---|---|
| `runner.glb` | the running guy (fitted by height) |
| `car.glb` | the player's car (car mode) |
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
  Rendering/           MultiMesh batches, runner/car views, model loading, occlusion fading
  Levels/              Level builders (Kitchen)
  Ecs/Terrain.cs       Ground height + walls from Obstacle/Ramp entities
```

- **Gameplay is pure ECS.** Godot nodes are only used for presentation. Horizontal movement runs
  on the ground plane as `Vector2 (x, y)` → world `(x, 0, y)`; height is a separate `Elevation`
  component driven by gravity and `Terrain.GroundHeight`. Props are walls only for things below
  their top that can't step up onto them; bugs climb walls instead. The level is a height map
  (no overhangs), so stools and the counter are solid blocks.
- **Rendering of crowds uses `MultiMesh`.** Enemies, bullets and gems are uploaded as one float
  buffer per frame, so thousands of entities cost a few draw calls.
- **Structural changes** (create/destroy) never happen inside a query: destroys go through
  `GameState.Destroy`, spawns are collected and created after the query.
- Measured headless: 1,500 bugs crowding the player cost about 2.3 ms per frame for all
  gameplay systems.

## Ideas / next steps

- Pick-one-of-three upgrade screen instead of random upgrades
- Pikmin-style follower swarm (followers as weapons)
- More ways around the kitchen: climbable fridge handles, a window sill, the back counter
- Skid marks, dust particles, screen shake, sound
- More levels (garden, sandbox, bathtub), loops and gaps to jump
- Real car and bug models
