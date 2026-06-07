using Godot;

/// <summary>
/// Marker/back-reference node attached as a child of a character body so that hit
/// queries can resolve which LocalPlayerManager (and PlayerEvents) a struck body
/// belongs to. Ported from Unity's PlayerDetection MonoBehaviour.
///
/// In Unity this was a component on the character GameObject and was found via
/// GetComponent on the struck Collider. In Godot a node can hold only one script,
/// so it lives as a child Node of the body and is resolved via NodeUtil
/// (GetComponentInChildren / InParent).
/// </summary>
public partial class PlayerDetection : Node
{
    private LocalPlayerManager _player;
    private PlayerEvents playerEvents;

    public LocalPlayerManager Player => _player;
    public PlayerEvents PlayerEvents => playerEvents;

    public void Initialize(LocalPlayerManager localPlayerManager, PlayerEvents playerEvents)
    {
        _player = localPlayerManager;
        this.playerEvents = playerEvents;
    }

    public void Deactivate()
    {
        _player = null;
        playerEvents = null;
    }
}
