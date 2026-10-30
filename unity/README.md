# Bullet Heaven: Unity version

The same game as the Godot project in the repository root, on Unity 6 + URP. Both share all of the
gameplay: the Arch ECS components and systems, the levels and the tests live in `../core`
(engine-independent C#), and the models in `../assets`. This project only draws and controls it.

## First time

1. Install the **.NET 8 SDK** if it isn't already (`winget install Microsoft.DotNet.SDK.8`); Unity calls
   `dotnet` to build `../core`.
2. Unity Hub: **Add > Add project from disk** and pick this `unity` folder. Open it with Unity 6 (LTS).
   Unity creates the rest of the project files and downloads the packages (URP, glTFast).
3. On first open, the editor script `Assets/Editor/Setup.cs` runs by itself:
   - builds `../core` and copies the DLLs into `Assets/Plugins/Core` (Arch and its helpers included),
   - copies `../assets/models/*.glb` and `../assets/textures/*.jpg` into `Assets/Resources`,
   - creates a URP asset if the project has no render pipeline yet,
   - switches Active Input Handling to *Both* if only the new Input System was on (then restart Unity).
   Before that first sync the console shows errors about missing `BulletHeaven` types: those are
   expected, and go once the DLLs are in.
4. Press **Play** in any scene. The game creates its own camera, light and post-processing.

After changing anything in `../core` or regenerating models, use **Bullet Heaven > Sync core + models**
(the core is also rebuilt automatically when Unity reloads and the sources are newer than the DLL).

## Command line

From the repository root (adjust the editor path/version):

```
"C:\Program Files\Unity\Hub\Editor\6000.0.XXf1\Editor\Unity.exe" -batchmode -quit -projectPath unity ^
  -executeMethod BulletHeaven.Unity.Editor.Setup.Run -logFile unity-setup.log
```

That syncs everything and compiles the scripts; `unity-setup.log` then lists any compile or shader
errors (search for `error`). Run it twice the very first time: the first run fetches packages and builds
the core, the second compiles against it.

## Controls

Same as the Godot version: WASD / arrows / left stick to run, Space / A to jump (hold while falling to
glide with the parachute), Esc / P / Start to pause, 1-3 or arrows + Enter (stick + A) in menus,
Shift + 1-3 to add a weapon, F1-F3 to switch level, R to restart.

## How it maps

| Godot | Unity |
|---|---|
| `src/Main.cs` | `Scripts/GameRunner.cs` (creates itself on Play) |
| `src/Rendering/LevelView.cs` | `Scripts/LevelView.cs` (+ the occlusion fader) |
| `src/Rendering/RunnerView.cs`, `LightningRenderer.cs` | `Scripts/RunnerView.cs`, `Scripts/LightningView.cs` |
| `InstancedRenderer` (MultiMesh) | `Scripts/InstancedBatch.cs` (`Graphics.RenderMeshInstanced`) |
| `shaders/bug_walk.gdshader` | `Resources/Shaders/Lit.shader` with the `_BUGWALK` keyword |
| `shaders/puddle.gdshader` | `Resources/Shaders/Puddle.shader` |
| `Hud.cs`, `Overlay.cs` | `Scripts/GameUi.cs` (IMGUI) |

Coordinates: the core's ground plane (x, y) with height h is Unity (x, h, -y), so the world isn't
mirrored (Unity is left-handed). glTFast imports models mirrored on X, which `ModelLibrary` undoes with a
half turn. If a model ever shows up facing backwards, that's the place to look.

Not ported yet: the car mode, the floor shader's macro variation, SSAO, baked lighting.
