using System.Collections.Generic;
using BulletHeaven.Numerics;

namespace BulletHeaven.Levels;

/// <summary>
/// What a level looks like, as plain data: the engine front-ends (Godot, Unity) turn these into
/// scene objects. Positions are in world space (x, height, y-on-the-ground-plane), sizes are
/// (width, height, depth). Models are files named in assets/models; when one is missing the
/// front-end draws the primitive fallback instead (or nothing, for <see cref="Shape.None"/>).
/// </summary>
public sealed class LevelLayout
{
    public FloorVisual Floor;
    public WallsVisual Walls;
    public readonly List<PropVisual> Props = new();
    public readonly List<PlankVisual> Planks = new();
    public readonly List<LightVisual> Lights = new();
}

public enum Shape { None, Box, Sphere, Cylinder, Wedge, Plane }

/// <summary>Surface for primitives: plastic-like colour, or a glowing (emissive) one when <see cref="Glow"/> &gt; 0.</summary>
public readonly record struct Look(Color Color, float Roughness = 0.6f, float Metallic = 0f, float Glow = 0f);

/// <summary>
/// One object standing on <see cref="Position"/> (bottom centre). The model is stretched to
/// <see cref="Size"/> when given, else used at its natural size. The fallback primitive gets
/// <see cref="FallbackSize"/> (a wedge rises towards +X before the yaw; a cylinder's top radius is
/// scaled by <see cref="TopScale"/>). With a <see cref="Fade"/> footprint the object turns see-through
/// while it hides the player from the camera.
/// </summary>
public sealed record PropVisual(
    string Model,
    Vector3 Position,
    Vector3? Size,
    Shape Fallback,
    Vector3 FallbackSize,
    Look FallbackLook,
    float YawDegrees = 0f,
    Color? Tint = null,
    Rect2? Fade = null,
    float FadeTop = 0f,
    float TopScale = 1f,
    bool CastShadows = true);

/// <summary>A board leaning at an angle (cutting board, ruler): centred on <see cref="Center"/>, turned by
/// <see cref="YawDegrees"/>, then tilted up towards its local +X by <see cref="TiltRadians"/>.</summary>
public sealed record PlankVisual(string Model, Vector3 Center, float YawDegrees, float TiltRadians, Vector3 BoardSize, Look FallbackLook);

/// <summary>Floor from a baked texture set (textures/{set}_albedo/roughness/normal.jpg), or a flat colour.</summary>
public sealed record FloorVisual(string TextureSet, Color Fallback, float ToneVariation, float SheenVariation);

/// <summary>
/// Room walls just outside the play area (back, sides, low skirting at the front). With a
/// <see cref="PanelModel"/> that exists, the back and sides are lined with that model every
/// <see cref="PanelSpacing"/> instead.
/// </summary>
public sealed record WallsVisual(Look Look, float Height = 50f, string PanelModel = null, float PanelSpacing = 16f);

public sealed record LightVisual(Vector3 Position, Color Color, float Energy, float Range, bool Shadows = false, bool Flicker = false);

/// <summary>Mood overrides on top of the default daylight; null fields keep the default.</summary>
public sealed class Atmosphere
{
    public Color? SkyTop, SkyHorizon, GroundHorizon, GroundBottom;
    public float? AmbientEnergy;
    public float? VolumetricFogDensity;
    public Color VolumetricFogAlbedo = Color.White;
    public float? FogDensity;
    public Color FogColor = Color.White;
    public float? SunEnergy;
    public Color? SunColor;
    /// <summary>Euler degrees (pitch, yaw, roll) of the sun, as in Godot.</summary>
    public Vector3? SunRotationDegrees;
}
