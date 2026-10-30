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

## Unity version

There is also a Unity 6 (URP) version of the same game in `unity/`; see `unity/README.md`. Both
front-ends share the gameplay in `core/` (engine-independent C#: the Arch ECS components and systems,
the levels, small maths types) and the models in `assets/`.

Gameplay checks run without any engine: `dotnet run --project tests` builds the real levels headless and
plays them with bots (climbing routes, the parachute glide, puddles, weapons, modifiers, the boss, a
1,500-bug stress test).

## Controls

| Action | Keyboard | Gamepad |
|---|---|---|
| Run | WASD / arrows | Left stick |
| Jump (hold for higher) | Space | A or Y |
| Glide (with the parachute): hold jump while falling | Space | A or Y |
| Pause | Esc / P | Start |
| Pick a card (weapon, modifier, pause menu) | 1 / 2 / 3, or arrows + Enter, or click | D-pad + A |
| Add a weapon (debug) | Shift + 1 / 2 / 3 | |
| Switch level (debug) | F1 / F2 / F3 | |
| Restart after getting squashed | R | Start |

**Playing as the car instead:** select the `Main` node in `scenes/main.tscn` and tick **Drive Car**
in the Inspector. Car controls: W/S or RT/LT to drive and brake, A/D or the stick to steer,
Shift/Ctrl or B to drift, Space or Y to jump.

## Levels

Switch any time with **F1-F3** (or set `Start Level` on the `Main` node in the Inspector).

| Key | Level | Mood | The way up |
|---|---|---|---|
| F1 | **Kitchen** | daylight, oak floor, butcher-block counter | stools + cutting board, or the toy-block staircase, to the counter (10) |
| F2 | **The Study** | Indiana Jones: wood panelling, Persian rug, Chesterfield armchair, golden idol, globe, chest, candle and lamp light in dusty air | books -> footstool -> seat -> armrest -> chair back -> book towers -> desk (13) |
| F3 | **The Windowsill** (secret, boss) | outdoors on the painted sill: flower pots, a leaky watering can, the garden far below | seed trays (3.2); mostly an open arena for the **Queen Ant** |

**The secret way to the Windowsill:** on the kitchen counter, climb the ruler ramps onto the books,
hop onto the **toaster** and grab the **parachute** (MDK style). Then jump off the toaster's back
edge towards the window and **hold jump** to glide over to the back cabinets, which you can't reach
any other way, and into the open window. That unlocks the Windowsill (remembered in
`user://progress.cfg`) and takes you there with your weapons. F3 jumps straight there while developing.

**The Queen Ant** appears 12 s in: a huge ant (2,500 HP, boss bar at the top) that calls in a ring
of ants every 7 s and every 10 s rears up for a moment and then charges in a straight line.

**Spilled liquids** slow down everyone wading through them, player and bugs alike: water (70 %
speed), coffee (60 %), juice (50 %), jam (35 %), honey (30 %). The kitchen has coffee and honey on
the counter and water and juice on the floor; the study has tea, jam and honey; the sill has water
from the watering can. They're drawn with `shaders/puddle.gdshader` (a wobbly blob outline, ripples
on thin liquids and a surface-tension bulge on thick ones, glossy so the room reflects in them).

Every climbing route, the parachute glide (and that it falls short without one), the puddle slow,
the modifiers and the boss fight are checked headless against the real level collision.
Each level is one class in `src/Levels/` built from the helpers in `Level.cs`.

## Gameplay so far

- **The kitchen** (level 1): the floor is the main arena. The counter in the middle (10 up) is high ground
  with mugs, books, a toaster, a cereal box, an apple and a pencil on it.
  - **Ways up**: a cutting board leaning on a stool (then jump to the counter), or the toy-block
    staircase (one jump per block). Ramps on the counter launch you, one of them right off the edge.
  - **Ways down**: anywhere. A long fall ends in a **shockwave** that flattens bugs around you.
  - Tall things in front of the camera turn **see-through** so you never lose the player.
- **Runner**: push a direction and it runs that way. Jumps have a short input buffer, coyote time
  and variable height. **Land on a bug to stomp it** and bounce off: chain stomps across a swarm.
- **Bugs climb**: they run straight at you and scale any wall in the way, so camping on the
  counter just means watching ants pour over the edge. They drop off edges to follow you down.
- **Weapons, chosen not grinded**: at the start of a run you pick **two of the three** (all fire
  automatically):
  - **Minigun**: a hose of tracer rounds at the nearest bug on your level; spinning barrels.
  - **Ball-lightning wand**: slow glowing orbs that drift into the swarm and chain-zap several
    bugs at once, lighting up everything around them.
  - **Seed mortar**: lobs seeds at random bugs in range, even onto the counter from the floor,
    that burst on landing.
- **Modifiers**: every level-up pauses the game and offers **three modifiers to pick one from**, for
  the weapons you carry (Hollow points, Extra barrel, Tungsten rounds, Twin orbs, Forked
  lightning, Static field, Cluster seeds, Big bang, Sticky jam that leaves slowing jam where seeds
  burst, ...) or yourself (Thick skin, Running shoes, Gem magnet, Heavy boots). Each has a pick
  limit; the HUD lists what each weapon has. The catalogue is `Modifiers.All` in
  `src/Systems/ProgressionSystems.cs`, the numbers are in `WeaponStats`.
- **Bugs walk**: legs step in a tripod gait and antennae feel around (animated in a shader, so
  a thousand of them stay cheap); they turn nose-up to climb walls and tip over as they fall.
- **Fruit** (Pikmin-style, glossy and detailed): apple, strawberry and cherries on the counter,
  an orange and a banana on the floor. All solid and climbable.
- **Enemies**: ants, plus tanky, slow-climbing beetles after 45 s; spawn rate ramps up and a swarm
  arrives every 30 s.
- **XP gems** with magnet pickup.
- **Pause** any time with Esc / Start (resume, restart, back to the kitchen).
- **Look**: baked PBR models and textures, oak floorboards, narrow FOV + near/far depth of field
  (tilt-shift), soft sun shadows, real-time global illumination (SDFGI), SSAO, screen-space
  reflections, AgX tone mapping. If it runs slowly on your GPU, turn off `SdfgiEnabled` in
  `Main.BuildEnvironment` first.

## 3D models

All models are generated by a Blender script and committed in `assets/models/` (plus the floor
textures in `assets/textures/`), so they work straight after `git pull`. Each model gets a
detailed procedural material (wood grain, glazed ceramic, marble, brushed steel, insect chitin,
knitted fabric, ...) that is baked into ordinary textures (colour, roughness, metallic, normal,
emission), because only plain textures survive the export to Godot.

**Regenerate or tweak them** (Blender 4.x or 5.x), from the repository root:

```
blender --background --python tools/blender/generate_models.py
blender --background --python tools/blender/generate_models.py -- --only ant mug_white --preview
```

On Windows use the full path, e.g.
`"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b -P tools\blender\generate_models.py`.
`--preview` also renders PNG previews into `assets/previews/` (not used by the game), `--draft`
uses half-size textures for quick iterations. Each model is one `build_*` function at the bottom
of the script: shapes plus a material recipe.

| File | Used for |
|---|---|
| `runner.glb` | the player, a chibi explorer with a fedora (separate `LegL/LegR/ArmL/ArmR` parts so the run cycle still animates) |
| `ant.glb`, `beetle.glb` | enemies (an `anim` vertex colour tags legs and antennae for the walk shader) |
| `minigun.glb`, `wand.glb` | weapons in the runner's hands (the minigun's `Barrels` part spins) |
| `strawberry.glb`, `orange.glb`, `banana.glb`, `cherries.glb` | fruit |
| `gem.glb` | XP gems |
| `counter.glb`, `stool.glb`, `cutting_board.glb`, `toy_block.glb` | the kitchen counter (butcher-block top, navy cabinets) and the ways up (blocks are tinted in game) |
| `back_counter.glb`, `fridge.glb` | cabinets and fridge along the back wall |
| `mug_white.glb`, `mug_red.glb`, `apple.glb`, `book_blue.glb`, `book_gold.glb`, `cereal_box.glb`, `pencil.glb` | props on the counter |
| `ruler.glb`, `eraser.glb`, `toaster.glb` | ramps and the toaster on the counter |
| `parachute.glb` | the parachute pickup and the canopy shown while gliding |
| `ball.glb`, `dog_bowl.glb` | floor props |
| `textures/floor_*.jpg` | oak floorboards (a seamless 16 x 16 patch; `shaders/floor.gdshader` hides the repeat) |
| `armchair`, `footstool`, `desk`, `idol`, `globe`, `chest`, `urn`, `candle`, `panel_wall`, `rug`, `book_red`, `book_green`, `textures/floor_walnut_*` | The Study |
| `window_frame`, `pot_cactus`, `pot_basil`, `watering_can`, `textures/floor_sill_*` | The Windowsill |

**Using your own models instead** (for example AI-generated ones): export a `.glb` with the same
name and replace the file. Bugs, the runner and gems are auto-fitted (scaled, centred, stood on the
ground). Props are expected at in-game size with the origin at the bottom centre; box-like props
(books, cereal box, pencil, blocks, counter, cutting board) are stretched to their exact size.
Characters should face glTF's usual +Z; if one runs sideways, change its `YawDegrees` in
`src/Rendering/ModelLibrary.cs`. Delete a file and the game falls back to simple shapes.
Keep bugs low-poly (a few thousand triangles): there can be over a thousand on screen.

## Architecture

```
core/                  Shared, engine-independent gameplay (netstandard2.1, used by Godot and Unity)
  Ecs/                 Components, GameState, SpatialGrid, Terrain (ground height + walls)
  Systems/             One class per behaviour, run in a fixed order by the front-end
  Levels/              Level base class + Kitchen, Study, Windowsill; LevelLayout = visuals as data
  Numerics/            Vector2/Vector3/Rect2/Mathf/Color with Godot's names and behaviour
tests/                 Headless gameplay checks: dotnet run --project tests
src/                   Godot front-end
  Main.cs              Creates the ECS world, fills in input, runs systems, syncs visuals
  GameInput.cs         Input actions registered in code
  Hud.cs, Overlay.cs   HUD and card menus
  Rendering/           LevelView (layout -> nodes), MultiMesh batches, runner/car views, models, fading
unity/                 Unity front-end (see unity/README.md)
tools/blender/         generate_models.py: builds and bakes every model
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

- Pikmin-style follower swarm (followers as weapons)
- More ways around the kitchen: climbable fridge handles; more items like the parachute that open up
  hard-to-reach places (a grappling hook, spring shoes)
- Skid marks, dust particles, screen shake, sound
- More levels (garden, sandbox, bathtub), loops and gaps to jump
- More models: fridge, back cabinets, walls, a car model for car mode
