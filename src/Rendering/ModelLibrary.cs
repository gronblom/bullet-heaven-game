using System;
using Godot;

namespace BulletHeaven.Rendering;

/// <summary>
/// Optional imported models. Put a .glb in assets/models/ with one of the names below and it
/// replaces the placeholder primitives; delete it and the primitives come back.
///
/// Models are auto-fitted: scaled so their longest horizontal side matches <see cref="ModelSpec.Length"/>,
/// placed with their lowest point on the ground, and turned by <see cref="ModelSpec.YawDegrees"/>.
/// The game expects models to face +X; glTF models usually face +Z, hence the default 90°.
/// If a model drives sideways or backwards, change its YawDegrees here (try 0, 180 or -90).
/// </summary>
public static class ModelLibrary
{
    public const string Folder = "res://assets/models/";

    public static readonly ModelSpec Car = new("car", Length: 1.4f, YawDegrees: 90f);
    public static readonly ModelSpec Ant = new("ant", Length: 1f, YawDegrees: 90f);
    public static readonly ModelSpec Beetle = new("beetle", Length: 1f, YawDegrees: 90f);
    public static readonly ModelSpec Gem = new("gem", Length: 1f, YawDegrees: 0f);

    /// <summary>Instantiates the model as a fitted node tree, or returns null if the file isn't there.</summary>
    public static Node3D LoadScene(ModelSpec spec)
    {
        var scene = Load(spec);
        if (scene == null)
            return null;

        var model = scene.Instantiate<Node3D>();
        var bounds = MeasureBounds(model, Transform3D.Identity);
        if (bounds is not { } box)
            return model;

        var fit = Fit(spec, box);
        var root = new Node3D { Name = spec.Name };
        model.Transform = fit * model.Transform;
        root.AddChild(model);
        return root;
    }

    /// <summary>
    /// First mesh in the model plus the transform that fits it, for MultiMesh batches (which draw a
    /// single mesh). Returns null if the file isn't there or holds no mesh.
    /// </summary>
    public static (Mesh Mesh, Transform3D Fit)? LoadMesh(ModelSpec spec)
    {
        var scene = Load(spec);
        if (scene == null)
            return null;

        var model = scene.Instantiate<Node3D>();
        try
        {
            if (FindMesh(model, Transform3D.Identity) is not { } found)
            {
                GD.PushWarning($"{spec.Path} contains no mesh; using the placeholder.");
                return null;
            }

            var meshBounds = found.Transform * found.Mesh.GetAabb();
            return (found.Mesh, Fit(spec, meshBounds) * found.Transform);
        }
        finally
        {
            model.Free();
        }
    }

    private static PackedScene Load(ModelSpec spec) =>
        ResourceLoader.Exists(spec.Path) ? GD.Load<PackedScene>(spec.Path) : null;

    private static Transform3D Fit(ModelSpec spec, Aabb box)
    {
        float longest = MathF.Max(box.Size.X, box.Size.Z);
        float scale = longest > 1e-4f ? spec.Length / longest : 1f;
        var basis = new Basis(Vector3.Up, Mathf.DegToRad(spec.YawDegrees)) * Basis.FromScale(Vector3.One * scale);

        // Centre horizontally and stand it on the ground before turning.
        var center = box.GetCenter();
        var offset = new Vector3(-center.X, -box.Position.Y, -center.Z);
        return new Transform3D(basis, basis * offset);
    }

    private static Aabb? MeasureBounds(Node node, Transform3D parent)
    {
        var transform = node is Node3D n3 ? parent * n3.Transform : parent;
        Aabb? total = null;
        if (node is MeshInstance3D { Mesh: not null } instance)
            total = transform * instance.Mesh.GetAabb();

        foreach (var child in node.GetChildren())
        {
            if (MeasureBounds(child, transform) is { } childBox)
                total = total is { } t ? t.Merge(childBox) : childBox;
        }
        return total;
    }

    private static (Mesh Mesh, Transform3D Transform)? FindMesh(Node node, Transform3D parent)
    {
        var transform = node is Node3D n3 ? parent * n3.Transform : parent;
        if (node is MeshInstance3D { Mesh: not null } instance)
            return (instance.Mesh, transform);

        foreach (var child in node.GetChildren())
        {
            if (FindMesh(child, transform) is { } found)
                return found;
        }
        return null;
    }
}

public readonly record struct ModelSpec(string Name, float Length, float YawDegrees)
{
    public string Path => ModelLibrary.Folder + Name + ".glb";
}
