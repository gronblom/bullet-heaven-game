using System;
using Godot;

namespace BulletHeaven.Rendering;

/// <summary>
/// Draws any number of copies of one mesh in a single draw call. Each frame: Begin, Add per
/// instance, End. Transforms are written into one float buffer and uploaded in a single call,
/// so thousands of enemies cost almost nothing on the C# side.
///
/// Uses the imported model for <c>spec</c> when its .glb exists (keeping the model's own
/// materials), otherwise the placeholder mesh with the given material.
/// </summary>
public sealed class InstancedRenderer
{
    private const int Stride = 16; // 12 floats transform (3x4, row-major) + 4 floats colour

    private readonly MultiMesh _multiMesh;
    private readonly Transform3D _meshFit = Transform3D.Identity;
    private float[] _buffer = Array.Empty<float>();
    private int _capacity;
    private int _count;

    /// <summary>True when an imported model is drawn instead of the placeholder.</summary>
    public bool UsesModel { get; }

    public InstancedRenderer(Node parent, PrimitiveMesh placeholder, Material placeholderMaterial,
        ModelSpec? spec = null, bool castShadows = true)
    {
        Mesh mesh = placeholder;
        if (spec is { } modelSpec && ModelLibrary.LoadMesh(modelSpec) is { } model)
        {
            mesh = model.Mesh;
            _meshFit = model.Fit;
            UsesModel = true;
        }
        else
        {
            placeholder.Material = placeholderMaterial;
        }

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

    /// <summary>Adds one instance rotated around Y by <paramref name="yaw"/> with per-axis scale.
    /// The colour tints placeholder materials only.</summary>
    public void Add(Vector3 position, float yaw, Vector3 scale, Color color)
    {
        if (_count == _capacity)
            Grow();

        var basis = new Basis(Vector3.Up, yaw) * Basis.FromScale(scale);
        var t = UsesModel ? new Transform3D(basis, position) * _meshFit : new Transform3D(basis, position);

        int o = _count * Stride;
        Write(o, t.Basis.Row0, t.Origin.X);
        Write(o + 4, t.Basis.Row1, t.Origin.Y);
        Write(o + 8, t.Basis.Row2, t.Origin.Z);
        _buffer[o + 12] = color.R;
        _buffer[o + 13] = color.G;
        _buffer[o + 14] = color.B;
        _buffer[o + 15] = color.A;
        _count++;
    }

    public void End()
    {
        if (_capacity > 0)
            RenderingServer.MultimeshSetBuffer(_multiMesh.GetRid(), _buffer);
        _multiMesh.VisibleInstanceCount = _count;
    }

    private void Write(int offset, Vector3 row, float origin)
    {
        _buffer[offset] = row.X;
        _buffer[offset + 1] = row.Y;
        _buffer[offset + 2] = row.Z;
        _buffer[offset + 3] = origin;
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
