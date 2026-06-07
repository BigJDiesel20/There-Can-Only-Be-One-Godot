using Godot;

/// <summary>
/// Per-character moveset definition. Unity ScriptableObject → Godot Resource.
///
/// • One or more AttackData per button (Light / Heavy / Special / Launcher).
///   The first entry in each list is the standalone move; the rest are reserved
///   for combo steps.
/// • A list of ComboData chains available to this character.
///
/// Assign the .tres to a player in CharacterSelect (port of LocalPlayerManager.attackConfig).
/// </summary>
[GlobalClass]
public partial class CharacterAttackConfig : Resource
{
	[ExportGroup("Standalone Attacks")]
	[Export] public Godot.Collections.Array<AttackData> lightAttacks    = new();
	[Export] public Godot.Collections.Array<AttackData> heavyAttacks    = new();
	[Export] public Godot.Collections.Array<AttackData> specialAttacks  = new();
	[Export] public Godot.Collections.Array<AttackData> launcherAttacks = new();

	[ExportGroup("Combo Chains")]
	[Export] public Godot.Collections.Array<ComboData> combos = new();

	/// <summary>
	/// Builds a basic in-code default moveset so combat works without an authored .tres.
	/// X = light, Y = heavy, B = special; A (Launcher) is reachable only inside a combo.
	/// GameManager saves the result as res://DefaultConfig.tres on first run for editing.
	/// </summary>
	public static CharacterAttackConfig BuildDefault()
	{
		AttackData Make(string name, HitBoxTriggerEvents.AttackType type, Vector3 size,
						float startup, float active, float recovery, float lunge, float push) => new()
		{
			attackName     = name,
			type           = type,
			hitBoxName     = name + " HB",
			hitBoxPosition = new Vector3(0f, 0.18f, 0.9f),
			hitBoxEuler    = Vector3.Zero,
			hitBoxScale    = size,
			startupLength     = startup,
			animationLength   = active,
			attackBlockLength = recovery,
			lungeDistance     = lunge,
			pushBackDistance  = push,
		};

		var light    = Make("Light",    HitBoxTriggerEvents.AttackType.Light,    new Vector3(0.6f, 0.4f, 1.0f), 0.05f, 0.07f, 0.18f, 2f, 1.0f);
		var heavy    = Make("Heavy",    HitBoxTriggerEvents.AttackType.Heavy,    new Vector3(0.9f, 0.6f, 1.3f), 0.12f, 0.10f, 0.30f, 3f, 2.5f);
		var special  = Make("Special",  HitBoxTriggerEvents.AttackType.Special,  new Vector3(1.2f, 0.8f, 1.4f), 0.15f, 0.12f, 0.35f, 1f, 3.0f);
		var launcher = Make("Launcher", HitBoxTriggerEvents.AttackType.Launcher, new Vector3(0.9f, 1.0f, 1.2f), 0.10f, 0.10f, 0.35f, 1f, 8.0f);

		var config = new CharacterAttackConfig();
		config.lightAttacks.Add(light);
		config.heavyAttacks.Add(heavy);
		config.specialAttacks.Add(special);
		// launcherAttacks intentionally empty — Launcher only fires as a combo step.

		ComboData Combo(string name, params (HitBoxTriggerEvents.AttackType input, AttackData atk)[] steps)
		{
			var c = new ComboData { comboName = name };
			foreach (var (input, atk) in steps)
				c.steps.Add(new ComboStep { inputType = input, attack = atk });
			return c;
		}

		config.combos.Add(Combo("LightLightHeavy",
			(HitBoxTriggerEvents.AttackType.Light, light),
			(HitBoxTriggerEvents.AttackType.Light, light),
			(HitBoxTriggerEvents.AttackType.Heavy, heavy)));

		config.combos.Add(Combo("LightHeavyLauncher",
			(HitBoxTriggerEvents.AttackType.Light, light),
			(HitBoxTriggerEvents.AttackType.Heavy, heavy),
			(HitBoxTriggerEvents.AttackType.Launcher, launcher)));

		return config;
	}
}
