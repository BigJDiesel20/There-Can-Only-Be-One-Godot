/// <summary>
/// Central physics-layer bitmask constants, replacing Unity's named layers
/// ("Player", "AuraField", …) + LayerMask.GetMask. Godot uses 1..32 bit flags for
/// collision layers/masks. Character bodies set CollisionLayer to include Player;
/// attack queries use these masks. Keep in sync with layer names set in Project
/// Settings → Layer Names → 3D Physics if you assign them there.
/// </summary>
public static class PhysicsLayers
{
    public const uint World     = 1u << 0;  // layer 1 — ground / static geometry
    public const uint Player    = 1u << 1;  // layer 2 — player character bodies
    public const uint AuraField = 1u << 2;  // layer 3 — aura field trigger volumes
}
