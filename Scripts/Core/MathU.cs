using Godot;

/// <summary>
/// Unity-compatible math helpers that Godot's Mathf does not provide (or names
/// differently). Ported code keeps using Godot.Mathf for the common functions
/// (Abs, Clamp, Sign, Sin, Cos, Atan2, Lerp, Sqrt, Inf) and switches only these
/// Unity-specific ones to MathU, so the translation stays close to the original.
/// </summary>
public static class MathU
{
    /// <summary>Unity Mathf.Rad2Deg constant (Godot only has the RadToDeg() function).</summary>
    public const float Rad2Deg = 57.295779513f;

    /// <summary>Unity Mathf.Deg2Rad constant (Godot only has the DegToRad() function).</summary>
    public const float Deg2Rad = 0.0174532925f;

    /// <summary>Unity Mathf.Clamp01.</summary>
    public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    /// <summary>Unity Mathf.SmoothStep (eased 0..1 interpolation between from and to).</summary>
    public static float SmoothStep(float from, float to, float t)
    {
        t = Clamp01(t);
        t = t * t * (3f - 2f * t);
        return from + (to - from) * t;
    }

    /// <summary>Unity Mathf.MoveTowards (Godot's is named MoveToward; provided here for parity).</summary>
    public static float MoveTowards(float current, float target, float maxDelta)
    {
        if (Mathf.Abs(target - current) <= maxDelta) return target;
        return current + Mathf.Sign(target - current) * maxDelta;
    }

    /// <summary>Shortest signed angular difference in degrees, range (-180, 180].</summary>
    public static float DeltaAngle(float current, float target)
    {
        float delta = (target - current) % 360f;
        if (delta >  180f) delta -= 360f;
        if (delta < -180f) delta += 360f;
        return delta;
    }

    /// <summary>Unity Mathf.MoveTowardsAngle — moves an angle toward a target taking the short way around.</summary>
    public static float MoveTowardsAngle(float current, float target, float maxDelta)
    {
        float delta = DeltaAngle(current, target);
        if (-maxDelta < delta && delta < maxDelta) return target;
        target = current + delta;
        return MoveTowards(current, target, maxDelta);
    }
}
