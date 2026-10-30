# Bullet Heaven: Unity version

The game, on Unity 6 + URP. The gameplay (Arch ECS components and systems, levels) lives in
`../core` as engine-independent C# (also run headless by `../tests`); the models in `../assets`.
This project draws and controls it.

## First time

1. Install the **.NET 8 SDK** if it isn't already (`winget install Microsoft.DotNet.SDK.8`); Unity calls
   `dotnet` to build `../core`.
2. Unity Hub: **Add > Add project from disk** and pick this `unity` folder. Open it with Unity 6 (LTS).
   Unity creates the rest of the project files and downloads the packages (URP, glTFast).
3. On first open, the editor script `Assets/Editor/Setup.cs` runs by itself:
   - copies `../assets/models/*.glb` and `../assets/textures/*.jpg` into `Assets/Resources`,
   - creates a URP asset if the project has no render pipeline yet,
   - switches Active Input Handling to *Both* if only the new Input System was on (then restart Unity).
   The shared core is already there as DLLs in `Assets/Plugins/Core` (committed, built from `../core`).
   If Unity opened in Safe Mode earlier, exit Safe Mode.
4. Press **Play** in any scene. The game creates its own camera, light and post-processing.

After changing anything in `../core` or regenerating models, use **Bullet Heaven > Sync core + models**
(Unity warns in the console when `../core` is newer than the DLLs), and commit the updated DLLs.

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

See the main README. Tab opens the module screen; Shift + 1-4 gives you a random module (debug).

## Scripts

| File | What |
|---|---|
| `GameRunner.cs` | creates itself on Play: world, systems, camera, post-processing, drawing |
| `Effects.cs` | arcade layer: bullet streaks, laser beams, ring fronts, void halos, sparks (from `GameState.Events`) |
| `GameUi.cs`, `ModuleScreen.cs`, `UiKit.cs` | HUD, level-up / pause cards, module screen (IMGUI) |
| `LevelView.cs` | builds a level's `LevelLayout`, occlusion fading |
| `ModelLibrary.cs`, `Looks.cs` | glTF models via glTFast, materials |
| `RunnerView.cs`, `LightningView.cs`, `InstancedBatch.cs` | the hero, lightning arcs, instanced drawing |
| `Resources/Shaders/` | `Lit` (glTF textures, matte, dithered fade, bug walk cycle), `Glow` (effects), `Puddle`, `Additive` |

Coordinates: the core's ground plane (x, y) with height h is Unity (x, h, -y), so the world isn't
mirrored (Unity is left-handed). glTFast imports models mirrored on X, which `ModelLibrary` undoes with a
half turn. If a model ever shows up facing backwards, that's the place to look.

Not ported yet: the car mode, the floor shader's macro variation, SSAO, baked lighting.
