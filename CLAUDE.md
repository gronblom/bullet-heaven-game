# Notes for Claude

- Unity 6 (URP) game in `unity/`; all gameplay in `core/` (netstandard2.1 C#, Arch ECS, no engine types;
  `BulletHeaven.Numerics` has Godot-style maths names). There is no Godot version any more.
- Checks: `dotnet build core` and `dotnet run --project tests` (headless gameplay checks; exit code 1 on failure).
- Unity can't run in cloud sessions. Check Unity scripts by parsing them as C# 9 and compiling them against
  the core DLL (missing-UnityEngine errors filtered out). Unity scripts are C# 9: no file-scoped namespaces,
  no primary constructors. The user sends `Editor.log` for real errors.
- After changing `core/`, rebuild the committed Unity copies: `dotnet publish core -c Release -o <tmp>` and copy
  the DLLs (except System.Runtime.CompilerServices.Unsafe.dll) into `unity/Assets/Plugins/Core`. They must be
  committed: Unity only runs the editor setup script once everything compiles.
- Gameplay state lives only in ECS components (`core/Ecs/Components.cs`) and `GameState` (weapons, inventory,
  events); systems in `core/Systems/` run in the order listed in `GameRunner.Begin` (keep `tests/Program.cs` in sync).
- Never create/destroy entities inside `World.Query` lambdas: use `GameState.Destroy` or collect and create afterwards.
  Grid entries (`State.EnemyGrid`) can be stale before the grid rebuild: check `World.IsAlive`.
- Ground plane: core `Vector2 (x, y)` with screen-up = -y; Unity world is `(x, height, -y)` (see `Conv`).
  Models face +X; a Unity yaw of `Conv.Heading(dir)` faces a direction. glTFast imports mirrored on X;
  `ModelLibrary` undoes it with a half turn.
- Height: `Elevation` component + `Terrain` (height map from Obstacle/Ramp entities, no overhangs). Bugs climb walls.
- Weapons (`core/Ecs/Weapons.cs`): four fixed weapons in `GameState.Weapons`, each with 4 module slots.
  Stats = base, + every ADD module, x every MULTIPLY module, last GEOMETRY override (`Weapon.Recompute`).
  `Patterns` is the arithmetic pattern engine (volley angles, sine/spiral velocities via `PatternMotion`).
  Module catalogue and inventory rules in `Modules` (ProgressionSystems.cs). Level-ups roll 3 module cards;
  bugs drop modules / armour plates / turret kits; turrets pulse the player's current TD Mortar stats.
- Effects are driven by `GameState.Events` (hit, kill, burst, muzzle, pulse, pickup) and `GameState.Lasers`,
  cleared by `GameState.BeginFrame()` before the systems run; `unity/Assets/Scripts/Effects.cs` draws them.
- Levels derive from `core/Levels/Level.cs` and only use its helpers (collision + matching visual record in
  `LevelLayout`). Check new climbing routes with a bot run in the tests; steps <= ~1.8 high.
- Models in `assets/models/*.glb` come from `tools/blender/generate_models.py` (bakes procedural materials to
  textures; `--icons` renders menu icons into `assets/icons`); always keep a primitive fallback. In cloud
  sessions use the official `pip install bpy` build (Python 3.13); Ubuntu's blender bakes blank textures.
- Core C# style: file-scoped namespaces, primary-constructor systems, XML summary on each type.
