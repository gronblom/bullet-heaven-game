using System;
using System.Collections.Generic;
using Godot;

namespace BulletHeaven.Rendering;

/// <summary>
/// Optional imported models. Put a .glb in assets/models/ with one of the names below and it
/// replaces the placeholder primitives; delete it and the primitives come back.
/// tools/blender/generate_models.py generates all of them.
///
/// Characters and bugs (the specs below) are auto-fitted: scaled so their longest horizontal side (or their height, for characters)
/// matches <see cref="ModelSpec.Size"/>, placed with their lowest point on the ground, and turned by <see cref="ModelSpec.YawDegrees"/>.
/// The game expects models to face +X; glTF models usually face +Z, hence the default 90°.
/// If a model drives sideways or backwards, change its YawDegrees here (try 0, 180 or -90).
/// </summary>
public static class ModelLibrary
{
    public const string Folder = "res://assets/models/";

    public static readonly ModelSpec Runner = new("runner", Size: 1.35f, YawDegrees: 90f, FitHeight: true); // incl. hat
    public static readonly ModelSpec Car = new("car", Size: 1.4f, YawDegrees: 90f);
    public static readonly ModelSpec Ant = new("ant", Size: 1.4f, YawDegrees: 90f);       // incl. antennae
    public static readonly ModelSpec Beetle = new("beetle", Size: 1.3f, YawDegrees: 90f);
    public static readonly ModelSpec Gem = new("gem", Size: 1f, YawDegrees: 0f);

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

    /// <summary>
    /// Instantiates a prop model, or returns null if assets/models/{name}.glb isn't there. Props are
    /// modelled at in-game size with their origin at the bottom centre, so by default they're used
    /// as-is; pass <paramref name="exactSize"/> to stretch box-like props to an exact size instead.
    /// Materials are copied per instance so each prop can be tinted and faded on its own.
    /// </summary>
    public static (Node3D Node, BaseMaterial3D[] Materials)? LoadProp(string name, Vector3? exactSize = null, Color? tint = null)
    {
        string path = Folder + name + ".glb";
        if (!ResourceLoader.Exists(path))
            return null;

        var model = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        if (exactSize is { } size && MeasureBounds(model, Transform3D.Identity) is { } box)
        {
            var scale = new Vector3(size.X / box.Size.X, size.Y / box.Size.Y, size.Z / box.Size.Z);
            var center = box.GetCenter();
            var basis = Basis.FromScale(scale);
            model.Transform = new Transform3D(basis, basis * new Vector3(-center.X, -box.Position.Y, -center.Z)) * model.Transform;
        }

        var materials = new List<BaseMaterial3D>();
        OwnMaterials(model, materials, tint);
        var root = new Node3D { Name = name };
        root.AddChild(model);
        return (root, materials.ToArray());
    }

    /// <summary>Nodes with this name anywhere under root (e.g. the runner's "LegL"), or null.</summary>
    public static Node3D FindNode(Node root, string name)
    {
        if (root.Name == name && root is Node3D found)
            return found;
        foreach (var child in root.GetChildren())
        {
            if (FindNode(child, name) is { } hit)
                return hit;
        }
        return null;
    }

    private static void OwnMaterials(Node node, List<BaseMaterial3D> materials, Color? tint)
    {
        if (node is MeshInstance3D { Mesh: not null } instance)
        {
            for (int i = 0; i < instance.Mesh.GetSurfaceCount(); i++)
            {
                if (instance.Mesh.SurfaceGetMaterial(i) is not BaseMaterial3D source)
                    continue;
                var copy = (BaseMaterial3D)source.Duplicate();
                if (tint is { } color)
                    copy.AlbedoColor = color; // multiplies the texture: paint over bare wood
                instance.SetSurfaceOverrideMaterial(i, copy);
                materials.Add(copy);
            }
        }
        foreach (var child in node.GetChildren())
            OwnMaterials(child, materials, tint);
    }

    /// <summary>Loads an optional texture from assets/textures/, or null.</summary>
    public static Texture2D LoadTexture(string name)
    {
        string path = "res://assets/textures/" + name;
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    private static PackedScene Load(ModelSpec spec) =>
        ResourceLoader.Exists(spec.Path) ? GD.Load<PackedScene>(spec.Path) : null;

    private static Transform3D Fit(ModelSpec spec, Aabb box)
    {
        float measured = spec.FitHeight ? box.Size.Y : MathF.Max(box.Size.X, box.Size.Z);
        float scale = measured > 1e-4f ? spec.Size / measured : 1f;
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

public readonly record struct ModelSpec(string Name, float Size, float YawDegrees, bool FitHeight = false)
{
    public string Path => ModelLibrary.Folder + Name + ".glb";
}
