using System;
using BulletHeaven.Ecs;
using Godot;

namespace BulletHeaven.Rendering;

/// <summary>
/// The player's car, facing +X. Uses assets/models/car.glb if present, otherwise a toy car built
/// from primitives. Leans into drifts, pitches with acceleration and in the air, squashes on
/// landing.
/// </summary>
public sealed partial class CarView : Node3D
{
    private readonly Node3D _body = new();
    private readonly Node3D[] _frontWheels = new Node3D[2];
    private float _previousSpeed;
    private float _squash;

    public CarView() : this(new Color(0.85f, 0.1f, 0.1f)) { }

    public CarView(Color paint)
    {
        AddChild(_body);
        if (ModelLibrary.LoadScene(ModelLibrary.Car) is { } model)
            _body.AddChild(model);
        else
            BuildPlaceholder(paint);
    }

    public void Sync(in Position position, in Car car, in Elevation elevation, in Health health, float dt)
    {
        Position = new Vector3(position.Value.X, elevation.Height, position.Value.Y);
        Rotation = new Vector3(0f, -car.Heading, 0f);

        float accel = dt > 0f ? (car.ForwardSpeed - _previousSpeed) / dt : 0f;
        _previousSpeed = car.ForwardSpeed;
        float roll = Math.Clamp(-car.Slip * 0.035f, -0.25f, 0.25f);
        float pitch = elevation.Grounded
            ? Math.Clamp(accel * 0.004f, -0.12f, 0.12f)
            : Math.Clamp(elevation.VerticalSpeed * 0.04f, -0.4f, 0.4f); // nose follows the arc
        _body.Rotation = _body.Rotation.Lerp(new Vector3(roll, 0f, pitch), Math.Min(1f, dt * 12f));

        if (elevation.LandingImpact > 2f)
            _squash = Math.Min(0.35f, elevation.LandingImpact * 0.03f);
        _squash = Math.Max(0f, _squash - dt * 1.5f);
        _body.Scale = new Vector3(1f + _squash * 0.5f, 1f - _squash, 1f + _squash * 0.5f);

        foreach (var wheel in _frontWheels)
        {
            if (wheel != null)
                wheel.Rotation = new Vector3(0f, -car.Steer * 0.45f, 0f);
        }

        // Blink while invulnerable.
        Visible = health.Invulnerable <= 0f || (int)(health.Invulnerable * 20f) % 2 == 0;
    }

    private void BuildPlaceholder(Color paint)
    {
        var paintMat = Materials.Plastic(paint);
        var glassMat = Materials.Plastic(new Color(0.15f, 0.2f, 0.3f), 0.1f);
        var tyreMat = Materials.Plastic(new Color(0.08f, 0.08f, 0.08f), 0.9f);

        _body.AddChild(Box(new Vector3(1.3f, 0.32f, 0.7f), new Vector3(0f, 0.3f, 0f), paintMat));
        _body.AddChild(Box(new Vector3(0.62f, 0.26f, 0.6f), new Vector3(-0.1f, 0.58f, 0f), glassMat));
        _body.AddChild(Box(new Vector3(0.12f, 0.1f, 0.72f), new Vector3(-0.66f, 0.55f, 0f), paintMat)); // spoiler

        var wheelMesh = new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 0.14f, RadialSegments = 12 };
        float[] xs = { 0.42f, -0.42f };
        float[] zs = { 0.38f, -0.38f };
        for (int xi = 0; xi < 2; xi++)
        {
            for (int zi = 0; zi < 2; zi++)
            {
                var pivot = new Node3D { Position = new Vector3(xs[xi], 0.17f, zs[zi]) };
                pivot.AddChild(new MeshInstance3D
                {
                    Mesh = wheelMesh,
                    MaterialOverride = tyreMat,
                    RotationDegrees = new Vector3(90f, 0f, 0f),
                });
                AddChild(pivot);
                if (xi == 0)
                    _frontWheels[zi] = pivot;
            }
        }
    }

    private static MeshInstance3D Box(Vector3 size, Vector3 position, Material material) => new()
    {
        Mesh = new BoxMesh { Size = size },
        MaterialOverride = material,
        Position = position,
    };
}
