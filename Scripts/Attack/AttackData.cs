using Godot;

/// <summary>
/// Data asset describing a single attack move. Unity ScriptableObject → Godot Resource.
/// Create via the editor's "Create Resource" dialog (appears as AttackData thanks to
/// [GlobalClass]). One .tres file per move — e.g. LightAttack.tres.
///
/// GODOT ADAPTATIONS
/// ─────────────────
/// • ScriptableObject → [GlobalClass] partial Resource.
/// • [SerializeField]/[Header]/[Tooltip] → [Export] / [ExportGroup].
/// • UnityEngine.Vector3 → Godot.Vector3. NOTE: Godot is right-handed, Z-forward
///   differs from Unity; hitbox Z offsets keep the same numeric values for now and
///   will be retuned against the imported models.
/// </summary>
[GlobalClass]
public partial class AttackData : Resource
{
	[ExportGroup("Identity")]
	[Export] public string attackName = "New Attack";
	[Export] public HitBoxTriggerEvents.AttackType type = HitBoxTriggerEvents.AttackType.Light;

	[ExportGroup("Hitbox")]
	[Export] public string  hitBoxName     = "Hit Box";
	[Export] public Vector3 hitBoxPosition = new Vector3(0f, 0.18f, 0.9f);
	[Export] public Vector3 hitBoxEuler    = Vector3.Zero;
	[Export] public Vector3 hitBoxScale    = new Vector3(0.5f, 0.25f, 1f);

	[ExportGroup("Timing (seconds at 60 Hz)")]
	[Export] public float startupLength     = 0.05f;
	[Export] public float animationLength   = 0.067f;
	[Export] public float attackBlockLength = 0.233f;

	[ExportGroup("Movement")]
	[Export] public float lungeDistance    = 5f;
	[Export] public float pushBackDistance = 1.5f;
}
