using Godot;

namespace BulletHeaven.Rendering;

public static class Materials
{
    public static StandardMaterial3D Plastic(Color color, float roughness = 0.45f) => new()
    {
        AlbedoColor = color,
        Roughness = roughness,
    };

    public static StandardMaterial3D Glow(Color color, float energy = 2f) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
        VertexColorUseAsAlbedo = true,
    };

    /// <summary>Colour comes from the per-instance colour of a MultiMesh.</summary>
    public static StandardMaterial3D InstanceColored(float roughness = 0.5f) => new()
    {
        VertexColorUseAsAlbedo = true,
        Roughness = roughness,
    };
}
