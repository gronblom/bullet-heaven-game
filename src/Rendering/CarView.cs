using System;
using BulletHeaven.Ecs;
using BulletHeaven.Systems;
using Godot;

namespace BulletHeaven.Rendering;

/// <summary>
/// Placeholder toy car built from primitives, modelled facing +X. Leans into drifts, pitches when
/// accelerating, steers its front wheels and tumbles when it falls off the table.
/// Replace the primitives with an imported model when one exists.
/// </summary>
public sealed partial class CarView : Node3D
{
    private readonly Node3D _body = new();
    private readonly Node3D[] _frontWheels = new Node3D[2];
    private float _previousSpeed;

    public CarView() : this(new Color(0.85f, 0.1f, 0.1f)) { }

    public CarView(Color paint)
    {
        AddChild(_body);
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

    public void Sync(in Position position, in Car car, in Health health, float dt)
    {
        float fallTime = car.FallTimer > 0f ? CollisionSystem.FallDuration - car.FallTimer : 0f;
        float drop = 0.5f * 30f * fallTime * fallTime;
        Position = new Vector3(position.Value.X, -drop, position.Value.Y);
        Rotation = new Vector3(fallTime * 4f, -car.Heading, fallTime * 2.5f);

        float accel = dt > 0f ? (car.ForwardSpeed - _previousSpeed) / dt : 0f;
        _previousSpeed = car.ForwardSpeed;
        float roll = Math.Clamp(-car.Slip * 0.035f, -0.25f, 0.25f);
        float pitch = Math.Clamp(accel * 0.004f, -0.12f, 0.12f);
        _body.Rotation = _body.Rotation.Lerp(new Vector3(roll, 0f, pitch), Math.Min(1f, dt * 12f));

        foreach (var wheel in _frontWheels)
            wheel.Rotation = new Vector3(0f, -car.Steer * 0.45f, 0f);

        // Blink while invulnerable.
        Visible = health.Invulnerable <= 0f || (int)(health.Invulnerable * 20f) % 2 == 0;
    }

    private static MeshInstance3D Box(Vector3 size, Vector3 position, Material material) => new()
    {
        Mesh = new BoxMesh { Size = size },
        MaterialOverride = material,
        Position = position,
    };
}
