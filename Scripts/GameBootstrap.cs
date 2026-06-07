using Godot;

/// <summary>
/// TEMPORARY first-playable bootstrap (replaced by GameManager + game states later).
/// Spawns a single LocalPlayerManager driving a CharacterBody3D character, so the
/// ported movement / camera / jump can be validated in-engine. Gamepad on device 0
/// required — PlayerInput has no keyboard fallback yet.
/// </summary>
public partial class GameBootstrap : Node3D
{
	[Export] public PackedScene PlayerScene;

	public override void _Ready()
	{
		if (PlayerScene == null)
		{
			GD.PushError("[Bootstrap] PlayerScene not assigned.");
			return;
		}

		var lpm = new LocalPlayerManager { Name = "Player1", playerName = "Player1" };
		AddChild(lpm);
		lpm.InitializePlayer(0);                 // gamepad device 0

		Node3D character = PlayerScene.Instantiate<Node3D>();
		AddChild(character);
		character.GlobalPosition = new Vector3(0, 1, 0);

		lpm.StageCharacter(character, null);     // no cursor yet
		lpm.BuildCharacter();
		lpm.EnterBattle();                       // make input live

		GD.Print("[Bootstrap] Player spawned. Move = left stick, jump = A, camera = right stick. Needs a gamepad on device 0.");
	}
}
