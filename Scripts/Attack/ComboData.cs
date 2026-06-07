using Godot;

/// <summary>
/// One step of a combo chain. In Unity this was a [Serializable] nested class;
/// in Godot, sub-resources in an exported array must themselves be Resources,
/// so ComboStep is its own [GlobalClass] Resource.
/// </summary>
[GlobalClass]
public partial class ComboStep : Resource
{
	[Export] public HitBoxTriggerEvents.AttackType inputType = HitBoxTriggerEvents.AttackType.Light;
	[Export] public AttackData attack;
}

/// <summary>
/// Data asset defining one combo chain as an ordered sequence of steps.
/// Unity ScriptableObject → Godot Resource.
///
/// Each step specifies the button the player must press (inputType) and the
/// specific AttackData that plays at that step.
/// </summary>
[GlobalClass]
public partial class ComboData : Resource
{
	[Export] public string comboName = "New Combo";
	[Export] public Godot.Collections.Array<ComboStep> steps = new();

	/// <summary>True when the combo has at least one step and every step has an attack assigned.</summary>
	public bool IsValid
	{
		get
		{
			if (steps == null || steps.Count == 0) return false;
			foreach (var s in steps)
				if (s == null || s.attack == null) return false;
			return true;
		}
	}
}
