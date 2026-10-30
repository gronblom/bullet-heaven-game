using System;
using Godot;

namespace BulletHeaven.Rendering;

/// <summary>
/// Draws any number of copies of one mesh in a single draw call. Each frame: Begin, Add per
/// instance, End. Transforms are written into one float buffer and uploaded in a single call,
/// so thousands of enemies cost almost nothing on the C# side.
///
/// Uses the imported model for <c>spec</c> when its .glb exists, otherwise the placeholder mesh
/// with the given material. With an <c>animationShader</c>, an imported model is drawn with that
/// shader (fed the model's own textures) and gets per-instance custom data for animation.
/// </summary>
public sealed class InstancedRenderer
{
    private readonly MultiMesh _multiMesh;
    private readonly Transform3D _meshFit = Transform3D.Identity;
    private readonly bool _colors;
    private readonly int _stride; // 12 floats transform (3x4, row-major) [+ 4 colour] + 4 custom
    private float[] _buffer = Array.Empty<float>();
    private int _capacity;
    private int _count;

    /// <summary>True when an imported model is drawn instead of the placeholder.</summary>
    public bool UsesModel { get; }

    public InstancedRenderer(Node parent, PrimitiveMesh placeholder, Material placeholderMaterial,
        ModelSpec? spec = null, bool castShadows = true, Shader animationShader = null)
    {
        Mesh mesh = placeholder;
        Material overrideMaterial = null;
        if (spec is { } modelSpec && ModelLibrary.LoadMesh(modelSpec) is { } model)
        {
            mesh = model.Mesh;
            _meshFit = model.Fit;
            UsesModel = true;
            if (animationShader != null && mesh.SurfaceGetMaterial(0) is BaseMaterial3D imported)
                overrideMaterial = AnimatedMaterial(animationShader, imported);
        }
        else
        {
            placeholder.Material = placeholderMaterial;
        }

        // Instance colours tint placeholders; animated models read vertex colours instead.
        _colors = overrideMaterial == null;
        _stride = 12 + (_colors ? 4 : 0) + 4;
        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = _colors,
            UseCustomData = true,
            Mesh = mesh,
            // Instances move every frame; a fixed generous bound avoids per-frame AABB work and culling surprises.
            CustomAabb = new Aabb(new Vector3(-200f, -50f, -200f), new Vector3(400f, 100f, 400f)),
        };

        parent.AddChild(new MultiMeshInstance3D
        {
            Multimesh = _multiMesh,
            MaterialOverride = overrideMaterial,
            CastShadow = castShadows
                ? GeometryInstance3D.ShadowCastingSetting.On
                : GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public void Begin() => _count = 0;

    /// <summary>
    /// Adds one instance: turned by <paramref name="yaw"/> around Y, then tipped nose-up by
    /// <paramref name="pitch"/> (models face +X), with per-axis scale. The colour tints placeholder
    /// materials; <paramref name="custom"/> goes to the animation shader (INSTANCE_CUSTOM).
    /// </summary>
    public void Add(Vector3 position, float yaw, Vector3 scale, Color color, float pitch = 0f, Color custom = default)
    {
        if (_count == _capacity)
            Grow();

        var basis = new Basis(Vector3.Up, yaw);
        if (pitch != 0f)
            basis *= new Basis(Vector3.Back, pitch);
        basis *= Basis.FromScale(scale);
        var t = UsesModel ? new Transform3D(basis, position) * _meshFit : new Transform3D(basis, position);

        int o = _count * _stride;
        Write(o, t.Basis.Row0, t.Origin.X);
        Write(o + 4, t.Basis.Row1, t.Origin.Y);
        Write(o + 8, t.Basis.Row2, t.Origin.Z);
        o += 12;
        if (_colors)
        {
            _buffer[o] = color.R;
            _buffer[o + 1] = color.G;
            _buffer[o + 2] = color.B;
            _buffer[o + 3] = color.A;
            o += 4;
        }
        _buffer[o] = custom.R;
        _buffer[o + 1] = custom.G;
        _buffer[o + 2] = custom.B;
        _buffer[o + 3] = custom.A;
        _count++;
    }

    public void End()
    {
        if (_capacity > 0)
            RenderingServer.MultimeshSetBuffer(_multiMesh.GetRid(), _buffer);
        _multiMesh.VisibleInstanceCount = _count;
    }

    private static ShaderMaterial AnimatedMaterial(Shader shader, BaseMaterial3D imported)
    {
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("albedo_texture", imported.AlbedoTexture);
        // glTF packs roughness and metallic into one texture; Godot's importer points both slots at it.
        material.SetShaderParameter("orm_texture", imported is OrmMaterial3D orm ? orm.OrmTexture
            : imported.RoughnessTexture ?? imported.MetallicTexture);
        material.SetShaderParameter("normal_texture", imported.NormalTexture);
        return material;
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
        var newBuffer = new float[newCapacity * _stride];
        Array.Copy(_buffer, newBuffer, _count * _stride);
        _buffer = newBuffer;
        _capacity = newCapacity;
        _multiMesh.InstanceCount = newCapacity; // reallocates; we upload the whole buffer in End anyway
    }
}
