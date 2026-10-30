using System;
using System.Collections.Generic;
using BulletHeaven.Ecs;
using Godot;

namespace BulletHeaven.Rendering;

/// <summary>
/// Draws lightning bolts as jagged, camera-facing ribbons rebuilt every frame (so they flicker),
/// and keeps a few real lights on the lightning orbs so they light up the bugs around them.
/// </summary>
public sealed class LightningRenderer
{
    public const float ArcLifetime = 0.12f;
    private const int Segments = 7;
    private const int MaxLights = 8;

    private readonly ImmediateMesh _mesh = new();
    private readonly OmniLight3D[] _lights = new OmniLight3D[MaxLights];
    private readonly Random _random = new();
    private readonly List<Vector3> _points = new();

    public LightningRenderer(Node parent)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
        };
        parent.AddChild(new MeshInstance3D
        {
            Mesh = _mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // The mesh changes every frame; skip culling.
            CustomAabb = new Aabb(new Vector3(-500f, -100f, -500f), new Vector3(1000f, 200f, 1000f)),
        });

        for (int i = 0; i < MaxLights; i++)
        {
            _lights[i] = new OmniLight3D
            {
                LightColor = new Color(0.55f, 0.8f, 1f),
                LightEnergy = 3f,
                OmniRange = 5f,
                Visible = false,
            };
            parent.AddChild(_lights[i]);
        }
    }

    /// <summary>Ages and draws the arcs; places the orb lights.</summary>
    public void Update(List<LightningArc> arcs, IReadOnlyList<Vector3> orbs, Vector3 camera, float time, float dt)
    {
        _mesh.ClearSurfaces();
        arcs.RemoveAll(a => (a.Age += dt) > ArcLifetime);
        if (arcs.Count > 0)
        {
            _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
            foreach (var arc in arcs)
            {
                float fade = 1f - arc.Age / ArcLifetime;
                Bolt(arc.From.ToGodot(), arc.To.ToGodot(), camera, 0.09f, new Color(0.5f, 0.75f, 1f, fade));     // glow
                Bolt(arc.From.ToGodot(), arc.To.ToGodot(), camera, 0.035f, new Color(0.95f, 0.98f, 1f, fade));   // hot core
            }
            _mesh.SurfaceEnd();
        }

        for (int i = 0; i < MaxLights; i++)
        {
            bool on = i < orbs.Count;
            _lights[i].Visible = on;
            if (on)
            {
                _lights[i].Position = orbs[i];
                _lights[i].LightEnergy = 2.5f + MathF.Sin(time * 37f + i * 1.7f) * 0.8f; // flicker
            }
        }
    }

    private void Bolt(Vector3 from, Vector3 to, Vector3 camera, float width, Color color)
    {
        _points.Clear();
        var along = to - from;
        float length = along.Length();
        var side = along.Cross(Vector3.Up);
        if (side.LengthSquared() < 1e-6f)
            side = Vector3.Right;
        side = side.Normalized();
        var up = side.Cross(along).Normalized();
        for (int i = 0; i <= Segments; i++)
        {
            float t = i / (float)Segments;
            float jitter = i == 0 || i == Segments ? 0f : length * 0.12f;
            var offset = side * ((float)_random.NextDouble() - 0.5f) * 2f * jitter + up * ((float)_random.NextDouble() - 0.5f) * 2f * jitter;
            _points.Add(from + along * t + offset);
        }

        for (int i = 0; i < Segments; i++)
        {
            var a = _points[i];
            var b = _points[i + 1];
            var facing = (b - a).Cross(camera - a);
            if (facing.LengthSquared() < 1e-8f)
                continue;
            var w = facing.Normalized() * width;
            Quad(a - w, a + w, b + w, b - w, color);
        }
    }

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        foreach (var p in new[] { a, b, c, a, c, d })
        {
            _mesh.SurfaceSetColor(color);
            _mesh.SurfaceAddVertex(p);
        }
    }
}
