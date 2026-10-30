using System;
using System.Collections.Generic;
using Godot;

namespace BulletHeaven.Rendering;

/// <summary>
/// Fades tall props to see-through while they stand between the camera and the player, so the
/// player never disappears behind the counter or a cereal box. Props register a footprint and top
/// height (the same numbers as their collision), plus the materials to fade.
/// </summary>
public sealed class OcclusionFader
{
    private const int Samples = 16;
    private const float FadedAlpha = 0.3f;
    private const float SmallFadedAlpha = 0.55f;

    private sealed class Occluder
    {
        public Rect2 Footprint;
        public float Top;
        public BaseMaterial3D[] Materials;
        public float Alpha = 1f;
        public bool Transparent;
    }

    private readonly List<Occluder> _occluders = new();

    public void Register(Rect2 footprint, float top, params BaseMaterial3D[] materials) =>
        _occluders.Add(new Occluder { Footprint = footprint, Top = top, Materials = materials });

    public void Update(Vector3 camera, Vector3 target, float dt)
    {
        float blend = 1f - MathF.Exp(-10f * dt);
        foreach (var occluder in _occluders)
        {
            // Small props (mugs, fruit) only hide part of you: fade them less so they don't vanish up close.
            bool small = occluder.Footprint.Size.X * occluder.Footprint.Size.Y < 30f;
            float goal = Blocks(occluder, camera, target) ? (small ? SmallFadedAlpha : FadedAlpha) : 1f;
            if (goal == 1f && occluder.Alpha == 1f && !occluder.Transparent)
                continue; // untouched this frame
            occluder.Alpha = Mathf.Lerp(occluder.Alpha, goal, blend);
            if (MathF.Abs(occluder.Alpha - goal) < 0.01f)
                occluder.Alpha = goal;

            bool transparent = occluder.Alpha < 0.99f;
            occluder.Transparent = transparent;
            foreach (var material in occluder.Materials)
            {
                // Dithered (alpha hash) rather than blended: still writes depth, so close-up surfaces
                // don't vanish or sort wrongly, and shadows stay. Opaque again once unfaded.
                material.Transparency = transparent ? BaseMaterial3D.TransparencyEnum.AlphaHash : BaseMaterial3D.TransparencyEnum.Disabled;
                var color = material.AlbedoColor;
                color.A = occluder.Alpha;
                material.AlbedoColor = color;
            }
        }
    }

    private static bool Blocks(Occluder occluder, Vector3 camera, Vector3 target)
    {
        for (int i = 1; i < Samples; i++)
        {
            var p = target.Lerp(camera, i / (float)Samples);
            if (p.Y < occluder.Top && occluder.Footprint.HasPoint(new Vector2(p.X, p.Z)))
                return true;
        }
        return false;
    }
}
