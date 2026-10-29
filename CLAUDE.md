# Notes for Claude

- Godot 4.7 .NET project, C# (net8.0), gameplay in the Arch ECS (`Arch` NuGet package).
- Build check: `dotnet build` (Godot itself is usually not available in cloud sessions).
- Gameplay state lives only in ECS components (`src/Ecs/Components.cs`); systems in `src/Systems/`
  run in the order listed in `Main._Ready`. Godot nodes are presentation only.
- Never create/destroy entities inside `World.Query` lambdas: use `GameState.Destroy` or collect
  and create afterwards.
- Ground plane: `Vector2 (x, y)` maps to world `(x, 0, y)`; models face +X; a node's Y rotation is `-heading`.
- Height: `Elevation` component + `Terrain` (built once from Obstacle/Ramp entities). Enemies have no
  Elevation; they sit at `Terrain.GroundHeight`.
- Optional models load from `assets/models/*.glb` via `ModelLibrary`; always keep the primitive fallback.
- Keep the C# style: file-scoped namespaces, primary-constructor systems, XML summary on each type.
