using System;

namespace BulletHeaven.Numerics;

/// <summary>The handful of Godot Mathf helpers gameplay uses.</summary>
public static class Mathf
{
    public const float Pi = MathF.PI;
    public const float Tau = MathF.PI * 2f;

    public static float Lerp(float from, float to, float weight) => from + (to - from) * weight;
    public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    public static float Abs(float value) => MathF.Abs(value);
    public static float DegToRad(float degrees) => degrees * (Pi / 180f);
    public static float RadToDeg(float radians) => radians * (180f / Pi);

    public static float MoveToward(float from, float to, float delta) =>
        MathF.Abs(to - from) <= delta ? to : from + MathF.Sign(to - from) * delta;

    /// <summary>Wraps into [min, max).</summary>
    public static float Wrap(float value, float min, float max)
    {
        float range = max - min;
        return range == 0f ? min : min + ((value - min) % range + range) % range;
    }

    /// <summary>Interpolates angles the short way round.</summary>
    public static float LerpAngle(float from, float to, float weight)
    {
        float diff = (to - from) % Tau;
        float distance = (2f * diff) % Tau - diff;
        return from + distance * weight;
    }
}
