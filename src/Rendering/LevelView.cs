using System;
using System.Collections.Generic;
using BulletHeaven.Levels;
using Godot;
using Core = BulletHeaven.Numerics;

namespace BulletHeaven.Rendering;

/// <summary>
/// Builds a level's <see cref="LevelLayout"/> (plain data from the shared core) into Godot nodes:
/// generated models where they exist, primitive fallbacks otherwise, plus floor, walls, lights and
/// the level's atmosphere. Tall props are registered with the <see cref="OcclusionFader"/>.
/// </summary>
public static class LevelView
{
    public static void Build(Node3D parent, Level level, LevelLayout layout, OcclusionFader fader)
    {
        var bounds = level.Bounds.ToGodot();
        if (layout.Floor != null)
            Floor(parent, bounds, layout.Floor);
        if (layout.Walls != null)
            Walls(parent, bounds, layout.Walls);
        foreach (var prop in layout.Props)
            Prop(parent, prop, fader);
        foreach (var plank in layout.Planks)
            Plank(parent, plank);
    }

    /// <summary>Applies the level's mood on top of Main's default daylight, and adds its lights.</summary>
    public static void Atmosphere(Godot.Environment environment, DirectionalLight3D sun, Node3D root, Atmosphere mood, LevelLayout layout)
    {
        foreach (var light in layout.Lights)
        {
            OmniLight3D node = light.Flicker ? new FlickerLight() : new OmniLight3D();
            node.Position = light.Position.ToGodot();
            node.LightColor = light.Color.ToGodot();
            node.LightEnergy = light.Energy;
            node.OmniRange = light.Range;
            node.ShadowEnabled = light.Shadows;
            root.AddChild(node);
        }
        if (mood == null)
            return;

        if (environment.Sky.SkyMaterial is ProceduralSkyMaterial sky)
        {
            if (mood.SkyTop is { } top) sky.SkyTopColor = top.ToGodot();
            if (mood.SkyHorizon is { } horizon) sky.SkyHorizonColor = horizon.ToGodot();
            if (mood.GroundHorizon is { } groundHorizon) sky.GroundHorizonColor = groundHorizon.ToGodot();
            if (mood.GroundBottom is { } groundBottom) sky.GroundBottomColor = groundBottom.ToGodot();
        }
        if (mood.AmbientEnergy is { } ambient)
            environment.AmbientLightEnergy = ambient;
        if (mood.VolumetricFogDensity is { } volumetric)
        {
            environment.VolumetricFogEnabled = true;
            environment.VolumetricFogDensity = volumetric;
            environment.VolumetricFogAlbedo = mood.VolumetricFogAlbedo.ToGodot();
        }
        if (mood.FogDensity is { } fog)
        {
            environment.FogEnabled = true;
            environment.FogDensity = fog;
            environment.FogLightColor = mood.FogColor.ToGodot();
        }
        if (mood.SunEnergy is { } energy) sun.LightEnergy = energy;
        if (mood.SunColor is { } color) sun.LightColor = color.ToGodot();
        if (mood.SunRotationDegrees is { } rotation) sun.RotationDegrees = rotation.ToGodot();
    }

    private static void Prop(Node3D parent, PropVisual prop, OcclusionFader fader)
    {
        var at = prop.Position.ToGodot();
        var holder = new Node3D { Position = at, RotationDegrees = new Vector3(0f, prop.YawDegrees, 0f) };
        parent.AddChild(holder);

        BaseMaterial3D[] materials = null;
        if (prop.Model != null && ModelLibrary.LoadProp(prop.Model, prop.Size?.ToGodot(), prop.Tint?.ToGodot()) is { } model)
        {
            holder.AddChild(model.Node);
            materials = model.Materials;
        }
        else if (prop.Fallback != Shape.None)
        {
            var material = Material(prop.FallbackLook);
            var size = prop.FallbackSize.ToGodot();
            var mesh = new MeshInstance3D
            {
                Mesh = Primitive(prop.Fallback, size, prop.TopScale),
                MaterialOverride = material,
                Position = new Vector3(0f, prop.Fallback == Shape.Plane ? 0f : size.Y / 2f, 0f),
                CastShadow = prop.CastShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            };
            holder.AddChild(mesh);
            materials = new[] { material };
        }

        if (materials != null && prop.Fade is { } fade)
            fader?.Register(fade.ToGodot(), prop.FadeTop, materials);
    }

    private static void Plank(Node3D parent, PlankVisual plank)
    {
        // Tilt a long board up towards +X, then turn it to face its direction.
        var pivot = new Node3D { Position = plank.Center.ToGodot(), RotationDegrees = new Vector3(0f, plank.YawDegrees, 0f) };
        var tilt = new Node3D { Rotation = new Vector3(0f, 0f, plank.TiltRadians) };
        pivot.AddChild(tilt);
        parent.AddChild(pivot);
        var size = plank.BoardSize.ToGodot();
        if (ModelLibrary.LoadProp(plank.Model, size) is { } model)
        {
            model.Node.Position = new Vector3(0f, -0.4f, 0f);
            tilt.AddChild(model.Node);
        }
        else
        {
            tilt.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Material(plank.FallbackLook), Position = new Vector3(0f, -0.2f, 0f) });
        }
    }

    private static Mesh Primitive(Shape shape, Vector3 size, float topScale) => shape switch
    {
        Shape.Sphere => new SphereMesh { Radius = size.X / 2f, Height = size.Y },
        Shape.Cylinder => new CylinderMesh
        {
            BottomRadius = size.X / 2f,
            TopRadius = size.X / 2f * topScale,
            Height = size.Y,
            CapTop = topScale == 1f, // shades are open
            CapBottom = topScale == 1f,
        },
        // PrismMesh with LeftToRight = 1 is a right-angle wedge rising towards +X.
        Shape.Wedge => new PrismMesh { Size = size, LeftToRight = 1f },
        Shape.Plane => new PlaneMesh { Size = new Vector2(size.X, size.Z) },
        _ => new BoxMesh { Size = size },
    };

    private static BaseMaterial3D Material(Look look)
    {
        if (look.Glow > 0f)
            return Materials.Glow(look.Color.ToGodot(), look.Glow);
        var material = Materials.Plastic(look.Color.ToGodot(), look.Roughness);
        material.Metallic = look.Metallic;
        return material;
    }

    /// <summary>The floor: a baked texture set through the floor shader (which hides the repeat), or a flat colour.</summary>
    private static void Floor(Node3D parent, Rect2 bounds, FloorVisual floor)
    {
        Material material;
        if (ModelLibrary.LoadTexture($"{floor.TextureSet}_albedo.jpg") is { } albedo)
        {
            var shader = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/floor.gdshader") };
            shader.SetShaderParameter("albedo_texture", albedo);
            shader.SetShaderParameter("roughness_texture", ModelLibrary.LoadTexture($"{floor.TextureSet}_roughness.jpg"));
            shader.SetShaderParameter("normal_texture", ModelLibrary.LoadTexture($"{floor.TextureSet}_normal.jpg"));
            shader.SetShaderParameter("repeats", bounds.Size / 16f);
            shader.SetShaderParameter("tone_variation", floor.ToneVariation);
            shader.SetShaderParameter("sheen_variation", floor.SheenVariation);
            shader.SetShaderParameter("variation_texture", new NoiseTexture2D
            {
                Width = 256,
                Height = 256,
                Seamless = true,
                Noise = new FastNoiseLite { Frequency = 0.012f, FractalOctaves = 3 },
            });
            material = shader;
        }
        else
        {
            material = Materials.Plastic(floor.Fallback.ToGodot(), 0.6f);
        }
        var center = bounds.GetCenter();
        parent.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = bounds.Size }, MaterialOverride = material, Position = new Vector3(center.X, 0f, center.Y) });
    }

    /// <summary>Back and side walls just outside the play area (or wall panels), plus a low skirting on the camera side.</summary>
    private static void Walls(Node3D parent, Rect2 b, WallsVisual walls)
    {
        var material = Material(walls.Look);
        var center = b.GetCenter();
        void Box(Vector3 size, Vector3 at) => parent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = at });

        if (walls.PanelModel != null && ModelLibrary.LoadProp(walls.PanelModel) != null)
        {
            void Panel(Vector3 at, float yaw)
            {
                var panel = ModelLibrary.LoadProp(walls.PanelModel).Value.Node;
                panel.Position = at;
                panel.RotationDegrees = new Vector3(0f, yaw, 0f);
                parent.AddChild(panel);
            }
            for (float x = b.Position.X + 6f; x < b.End.X; x += walls.PanelSpacing)
                Panel(new Vector3(x, 0f, b.Position.Y - 1f), 0f);
            for (float y = b.Position.Y + 8f; y < b.End.Y; y += walls.PanelSpacing)
            {
                Panel(new Vector3(b.Position.X - 1f, 0f, y), 90f);
                Panel(new Vector3(b.End.X + 1f, 0f, y), -90f);
            }
        }
        else
        {
            float w = b.Size.X + 4f, d = b.Size.Y + 4f, h = walls.Height;
            Box(new Vector3(w, h, 2f), new Vector3(center.X, h / 2f, b.Position.Y - 1f));
            Box(new Vector3(2f, h, d), new Vector3(b.Position.X - 1f, h / 2f, center.Y));
            Box(new Vector3(2f, h, d), new Vector3(b.End.X + 1f, h / 2f, center.Y));
        }
        Box(new Vector3(b.Size.X + 4f, 1.2f, 1f), new Vector3(center.X, 0.6f, b.End.Y + 0.5f));
    }
}

/// <summary>A light that buzzes: now and then it dips or cuts out for a moment, like a failing tube or a candle.</summary>
public sealed partial class FlickerLight : OmniLight3D
{
    private readonly Random _random = new();
    private float _base = -1f;
    private float _timer;

    public override void _Process(double delta)
    {
        if (_base < 0f)
            _base = LightEnergy;
        _timer -= (float)delta;
        if (_timer > 0f)
            return;

        if (_random.NextDouble() < 0.08)
        {
            LightEnergy = _base * (float)_random.NextDouble() * 0.3f; // stutter
            _timer = 0.03f + (float)_random.NextDouble() * 0.12f;
        }
        else
        {
            LightEnergy = _base * (0.92f + (float)_random.NextDouble() * 0.08f); // hum
            _timer = 0.05f + (float)_random.NextDouble() * 0.6f;
        }
    }
}
