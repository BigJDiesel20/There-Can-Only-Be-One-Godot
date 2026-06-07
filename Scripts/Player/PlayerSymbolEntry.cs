using Godot;

/// <summary>
/// Per-player cursor symbol appearance. Minimal port of the Unity PlayerSymbolEntry.
/// The full PlayerSymbolLibrary and the HDRP SymbolGlow shader are deferred to a later
/// pass; these are the fields the camera cursor code reads.
/// </summary>
[GlobalClass]
public partial class PlayerSymbolEntry : Resource
{
	[Export] public Texture2D sprite;
	[Export] public Color   symbolColor    = Colors.White;
	[Export] public Color   glowColor      = Colors.White;
	[Export] public float   glowIntensity  = 1f;
	[Export] public Vector3 positionOffset = new Vector3(0f, 2f, 0f);
	[Export] public float   scale          = 1f;
}
