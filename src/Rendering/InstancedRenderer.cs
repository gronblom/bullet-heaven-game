using System;
using Godot;

namespace BulletHeaven.Rendering;

/// <summary>
/// Draws any number of copies of one mesh in a single draw call. Each frame: Begin, Add per
/// instance, End. Transforms are written into one float buffer and uploaded in a single call,
/// so thousands of enemies cost almost nothing on the C# side.
/// </summary>
public sealed class InstancedRenderer
{
    private const int Stride = 16; // 12 floats transform (3x4, row-major) + 4 floats colour

    private readonly MultiMesh _multiMesh;
    private float[] _buffer = Array.Empty<float>();
    private int _capacity;
    private int _count;

    public InstancedRenderer(Node parent, PrimitiveMesh mesh, Material material, bool castShadows = true)
    {
        mesh.Material = material;
        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            // Instances move every frame; a fixed generous bound avoids per-frame AABB work and culling surprises.
            CustomAabb = new Aabb(new Vector3(-200f, -50f, -200f), new Vector3(400f, 100f, 400f)),
        };

        parent.AddChild(new MultiMeshInstance3D
        {
            Multimesh = _multiMesh,
            CastShadow = castShadows
                ? GeometryInstance3D.ShadowCastingSetting.On
                : GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public void Begin() => _count = 0;

    /// <summary>Adds one instance rotated around Y by <paramref name="yaw"/> with per-axis scale.</summary>
    public void Add(Vector3 position, float yaw, Vector3 scale, Color color)
    {
        if (_count == _capacity)
            Grow();

        float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
        int o = _count * Stride;
        // Rows of (RotationY * Scale), then origin per row.
        _buffer[o + 0] = c * scale.X; _buffer[o + 1] = 0f;      _buffer[o + 2] = s * scale.Z;  _buffer[o + 3] = position.X;
        _buffer[o + 4] = 0f;          _buffer[o + 5] = scale.Y; _buffer[o + 6] = 0f;           _buffer[o + 7] = position.Y;
        _buffer[o + 8] = -s * scale.X; _buffer[o + 9] = 0f;     _buffer[o + 10] = c * scale.Z; _buffer[o + 11] = position.Z;
        _buffer[o + 12] = color.R; _buffer[o + 13] = color.G; _buffer[o + 14] = color.B; _buffer[o + 15] = color.A;
        _count++;
    }

    public void End()
    {
        if (_capacity > 0)
            RenderingServer.MultimeshSetBuffer(_multiMesh.GetRid(), _buffer);
        _multiMesh.VisibleInstanceCount = _count;
    }

    private void Grow()
    {
        int newCapacity = Math.Max(256, _capacity * 2);
        var newBuffer = new float[newCapacity * Stride];
        Array.Copy(_buffer, newBuffer, _count * Stride);
        _buffer = newBuffer;
        _capacity = newCapacity;
        _multiMesh.InstanceCount = newCapacity; // reallocates; we upload the whole buffer in End anyway
    }
}
