using Godot;

/// <summary>
/// Temporary sanity-check node for the WIP port. Confirms the C# assembly compiled
/// and loads at runtime. Remove once the real GameManager entry point exists.
/// </summary>
public partial class BootCheck : Node
{
	public override void _Ready()
	{
		GD.Print("[There Can Only Be One] Godot port assembly loaded — C# compiled and running OK.");
		GD.Print($"  Active players list initialized: {LocalPlayerManager.ActivePlayers != null}");
	}
}
