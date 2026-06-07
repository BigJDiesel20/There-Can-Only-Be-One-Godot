/// <summary>
/// Ambient time shim replacing Unity's static Time.deltaTime / Time.fixedDeltaTime /
/// Time.time. Godot delivers delta as a parameter to _Process/_PhysicsProcess instead
/// of exposing it statically, so LocalPlayerManager writes Delta/FixedDelta here at the
/// top of each frame (before firing OnUpdate/OnFixedUpdate) and the ported controllers
/// read them exactly where they used Time.deltaTime / Time.fixedDeltaTime.
///
/// Time mirrors Unity's Time.time (seconds since launch) via the engine clock, so it is
/// correct regardless of how many players write Delta each frame.
/// </summary>
public static class GameTime
{
    /// <summary>Last _Process delta in seconds (Unity Time.deltaTime).</summary>
    public static float Delta;

    /// <summary>Last _PhysicsProcess delta in seconds (Unity Time.fixedDeltaTime).</summary>
    public static float FixedDelta;

    /// <summary>Seconds since the engine started (Unity Time.time).</summary>
    public static float Time => Godot.Time.GetTicksMsec() / 1000f;
}
