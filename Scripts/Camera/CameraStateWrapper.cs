using Godot;

/// <summary>
/// Shared mutable bridge between the camera and movement controllers (port of the
/// Unity class of the same name). The camera writes the active mode plus the
/// movement reference frame each frame; MovementController reads them so left-stick
/// input stays relative to the camera/target rather than the raw model facing.
///
/// NOTE: Godot's Vector3.Forward is -Z and Unity's was +Z (handedness differs).
/// Defaults below are placeholders only — the camera overwrites them every frame —
/// but the forward-axis convention is one of the things to verify in-engine.
/// </summary>
public class CameraStateWrapper
{
	public enum CameraState { Orbit, Follow, FightingSide }

	public CameraState CurrentState = CameraState.Orbit;

	/// <summary>True while Follow camera aim-lock (R1) is active.</summary>
	public bool IsFollowAimLock = false;

	/// <summary>Flat unit vector from the owner toward the opponent in FightingSide mode.</summary>
	public Vector3 FightAxis = Vector3.Forward;

	/// <summary>Movement-reference forward/right for Orbit mode (set pre-combat-framing offset).</summary>
	public Vector3 MovementForward = Vector3.Forward;
	public Vector3 MovementRight   = Vector3.Right;
}
