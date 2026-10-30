using System.Collections.Generic;
using BulletHeaven.Rendering;
using Godot;

namespace BulletHeaven.Levels;

/// <summary>
/// Level 3. The Backrooms: an empty maze of mono-yellow wallpaper and damp carpet under humming,
/// flickering fluorescent light, thick with haze. Walls are far too tall to climb (the bugs climb
/// them anyway). The only way up is a staircase that leads to nowhere.
/// </summary>
public sealed class Backrooms : Level
{
    private const float WallHeight = 25f;
    private const float WallThickness = 1.5f;

    public override string Name => "The Backrooms";
    public override Rect2 Bounds => new(-75f, -55f, 150f, 110f);
    public override Vector2 PlayerStart => new(0f, 0f);

    private Material _wallpaper;

    protected override void BuildLevel()
    {
        Floor("carpet", new Color(0.45f, 0.38f, 0.18f), toneVariation: 0.3f, sheenVariation: 0.05f);
        if (Visual)
        {
            _wallpaper = WallMaterial("wallpaper", new Color(0.62f, 0.55f, 0.25f));
            RoomWalls(_wallpaper, 40f);
        }

        // Walls of the maze, with doorway gaps: horizontal runs (y, x from..to) and vertical (x, y from..to).
        float[][] horizontal =
        {
            new[] { -20f, -75f, -45f }, new[] { -20f, -35f, -5f }, new[] { -20f, 5f, 35f }, new[] { -20f, 45f, 75f },
            new[] { 22f, -75f, -55f }, new[] { 22f, -45f, -15f }, new[] { 22f, -5f, 25f }, new[] { 22f, 35f, 75f },
        };
        float[][] vertical =
        {
            new[] { -40f, -55f, -35f }, new[] { -40f, -25f, 10f }, new[] { -40f, 30f, 55f },
            new[] { 40f, -55f, -40f }, new[] { 40f, -30f, 5f }, new[] { 40f, 15f, 55f },
            new[] { 0f, -55f, -32f }, new[] { 0f, 34f, 55f },
        };
        foreach (var h in horizontal)
            Wall(new Vector2((h[1] + h[2]) / 2f, h[0]), new Vector2(h[2] - h[1], WallThickness));
        foreach (var v in vertical)
            Wall(new Vector2(v[0], (v[1] + v[2]) / 2f), new Vector2(WallThickness, v[2] - v[1]));
        foreach (var pillar in new[] { new Vector2(-20f, 0f), new Vector2(20f, 0f), new Vector2(-60f, 0f), new Vector2(60f, 0f),
                                       new Vector2(-20f, 40f), new Vector2(20f, -40f), new Vector2(-58f, -40f), new Vector2(58f, 40f) })
            Wall(pillar, new Vector2(3f, 3f));

        // The staircase to nowhere: eight steps, then a landing in mid-air.
        var concrete = new Color(0.55f, 0.52f, 0.45f);
        for (int i = 0; i < 8; i++)
            Block(new Vector2(9.5f + i * 2.5f, 11f), new Vector2(2.5f, 6f), 0f, 1.6f * (i + 1), concrete);
        Block(new Vector2(31.5f, 11f), new Vector2(6f, 6f), 0f, 12.8f, concrete);

        // Low office partitions, just high enough to hop onto.
        var partition = new Color(0.4f, 0.4f, 0.42f);
        Block(new Vector2(-57f, 38f), new Vector2(10f, 1.5f), 0f, 1.6f, partition);
        Block(new Vector2(-22f, -38f), new Vector2(1.5f, 10f), 0f, 1.6f, partition);
        Block(new Vector2(58f, -32f), new Vector2(8f, 1.5f), 0f, 1.6f, partition);
    }

    private void Wall(Vector2 center, Vector2 footprint)
    {
        BoxObstacle(center, footprint, WallHeight);
        if (!Visual)
            return;
        // Walls hide the player often in a maze, so each gets its own copy of the material to fade.
        var own = (BaseMaterial3D)_wallpaper.Duplicate();
        Add(new BoxMesh { Size = new Vector3(footprint.X, WallHeight, footprint.Y) }, new Vector3(center.X, WallHeight / 2f, center.Y), own);
        Fader?.Register(new Rect2(center - footprint / 2f, footprint), WallHeight, own);
    }

    public override void Atmosphere(Godot.Environment environment, DirectionalLight3D sun, Node3D root)
    {
        // No daylight. Only buzzing tubes in a yellow haze.
        sun.Visible = false;
        environment.BackgroundMode = Godot.Environment.BGMode.Color;
        environment.BackgroundColor = new Color(0.05f, 0.045f, 0.02f);
        environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        environment.AmbientLightColor = new Color(0.55f, 0.5f, 0.3f);
        environment.AmbientLightEnergy = 0.35f;
        environment.SdfgiEnabled = false; // its light comes from the tubes; skip the cost
        environment.VolumetricFogEnabled = true;
        environment.VolumetricFogDensity = 0.022f;
        environment.VolumetricFogAlbedo = new Color(0.9f, 0.85f, 0.6f);
        environment.AdjustmentSaturation = 0.85f;
        environment.AdjustmentContrast = 1.1f;

        var tube = new Color(1f, 0.95f, 0.78f);
        var lights = new List<Vector2>();
        for (float x = -60f; x <= 60f; x += 30f)
            for (float y = -40f; y <= 45f; y += 28f)
                lights.Add(new Vector2(x, y));
        for (int i = 0; i < lights.Count; i++)
        {
            if (i == 7)
                continue; // one tube has died
            Light(root, new Vector3(lights[i].X, 22f, lights[i].Y), tube, 2.6f, 30f, shadows: i % 4 == 0, flicker: i % 3 == 1);
        }
    }
}
