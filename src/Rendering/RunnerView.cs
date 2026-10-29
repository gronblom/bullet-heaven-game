using System;
using BulletHeaven.Ecs;
using Godot;

namespace BulletHeaven.Rendering;

/// <summary>
/// The little running guy, facing +X. Uses assets/models/runner.glb if present (bobbing and
/// leaning), otherwise a Pikmin-sized dude built from primitives with a procedural run cycle:
/// swinging legs and arms, a jump pose and a squash on landing.
/// </summary>
public sealed partial class RunnerView : Node3D
{
    private readonly Node3D _body = new();
    private readonly Node3D[] _legs = new Node3D[2];
    private readonly Node3D[] _arms = new Node3D[2];
    private readonly bool _hasModel;
    private float _phase;
    private float _squash;

    public RunnerView() : this(new Color(0.95f, 0.45f, 0.1f)) { }

    public RunnerView(Color suit)
    {
        AddChild(_body);
        if (ModelLibrary.LoadScene(ModelLibrary.Runner) is { } model)
        {
            _body.AddChild(model);
            _hasModel = true;
        }
        else
        {
            BuildPlaceholder(suit);
        }
    }

    public void Sync(in Position position, in Velocity velocity, in Runner runner, in Elevation elevation, in Health health, float dt)
    {
        Position = new Vector3(position.Value.X, elevation.Height, position.Value.Y);
        Rotation = new Vector3(0f, -runner.Facing, 0f);

        float speed = velocity.Value.Length();
        float run = elevation.Grounded ? Math.Clamp(speed / 7.5f, 0f, 1f) : 0f;
        _phase += dt * (6f + speed * 1.6f) * (run > 0.05f ? 1f : 0f);
        float swing = MathF.Sin(_phase) * 0.9f * run;

        if (!_hasModel)
        {
            // Legs and arms swing in opposition; tucked legs and raised arms in the air.
            float airLegs = elevation.Grounded ? 0f : 0.6f;
            float airArms = elevation.Grounded ? 0f : -1.2f;
            _legs[0].Rotation = new Vector3(0f, 0f, swing + airLegs);
            _legs[1].Rotation = new Vector3(0f, 0f, -swing - airLegs * 0.5f);
            _arms[0].Rotation = new Vector3(airArms, 0f, -swing * 0.8f);
            _arms[1].Rotation = new Vector3(-airArms, 0f, swing * 0.8f);
        }

        // Bob while running, lean into speed, squash on landing.
        float bob = MathF.Abs(MathF.Sin(_phase)) * 0.08f * run;
        if (elevation.LandingImpact > 2f)
            _squash = Math.Min(0.4f, elevation.LandingImpact * 0.03f);
        _squash = Math.Max(0f, _squash - dt * 2f);
        _body.Position = new Vector3(0f, bob, 0f);
        _body.Rotation = new Vector3(0f, 0f, -run * 0.2f);
        _body.Scale = new Vector3(1f + _squash * 0.5f, 1f - _squash, 1f + _squash * 0.5f);

        // Blink while invulnerable.
        Visible = health.Invulnerable <= 0f || (int)(health.Invulnerable * 20f) % 2 == 0;
    }

    private void BuildPlaceholder(Color suit)
    {
        var suitMat = Materials.Plastic(suit, 0.6f);
        var skinMat = Materials.Plastic(new Color(1f, 0.85f, 0.7f), 0.5f);
        var darkMat = Materials.Plastic(new Color(0.1f, 0.1f, 0.12f), 0.4f);

        // Proportions: big head, short legs, about 1 unit tall.
        _body.AddChild(Part(new CapsuleMesh { Radius = 0.2f, Height = 0.5f }, new Vector3(0f, 0.5f, 0f), suitMat));
        _body.AddChild(Part(new SphereMesh { Radius = 0.22f, Height = 0.44f }, new Vector3(0f, 0.9f, 0f), skinMat));
        _body.AddChild(Part(new SphereMesh { Radius = 0.04f, Height = 0.08f }, new Vector3(0.19f, 0.93f, 0.08f), darkMat)); // eyes
        _body.AddChild(Part(new SphereMesh { Radius = 0.04f, Height = 0.08f }, new Vector3(0.19f, 0.93f, -0.08f), darkMat));
        _body.AddChild(Part(new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.02f, Height = 0.3f }, new Vector3(0f, 1.25f, 0f), darkMat)); // antenna
        _body.AddChild(Part(new SphereMesh { Radius = 0.06f, Height = 0.12f }, new Vector3(0f, 1.4f, 0f), Materials.Glow(new Color(1f, 0.9f, 0.3f), 1f)));

        for (int side = 0; side < 2; side++)
        {
            float z = side == 0 ? 0.1f : -0.1f;
            _legs[side] = Limb(new Vector3(0f, 0.3f, z), new CapsuleMesh { Radius = 0.07f, Height = 0.32f }, -0.16f, darkMat);
            _arms[side] = Limb(new Vector3(0f, 0.65f, z * 2.3f), new CapsuleMesh { Radius = 0.05f, Height = 0.3f }, -0.15f, suitMat);
        }
    }

    /// <summary>A limb hanging from a pivot (hip or shoulder) so rotating the pivot swings it.</summary>
    private Node3D Limb(Vector3 pivotAt, Mesh mesh, float hang, Material material)
    {
        var pivot = new Node3D { Position = pivotAt };
        pivot.AddChild(Part(mesh, new Vector3(0f, hang, 0f), material));
        _body.AddChild(pivot);
        return pivot;
    }

    private static MeshInstance3D Part(Mesh mesh, Vector3 position, Material material) => new()
    {
        Mesh = mesh,
        MaterialOverride = material,
        Position = position,
    };
}
