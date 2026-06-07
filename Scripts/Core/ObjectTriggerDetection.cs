using Godot;

/// <summary>
/// The aura-field trigger volume — an Area3D that detects other players' bodies entering/leaving
/// and broadcasts them through an ObjectTriggerEvents hub. Port of Unity's ObjectTriggerDetection
/// (OnTriggerEnter/Exit → Godot BodyEntered/BodyExited signals; tag check → "Player" group).
///
/// The field's own owner is filtered out downstream in StatController (it shares the Player layer).
/// </summary>
public partial class ObjectTriggerDetection : Area3D
{
    ObjectTriggerEvents _events;

    public void Initialize(ObjectTriggerEvents events)
    {
        _events = events;
        BodyEntered += OnBodyEntered;
        BodyExited  += OnBodyExited;
    }

    public void Deactivate()
    {
        BodyEntered -= OnBodyEntered;
        BodyExited  -= OnBodyExited;
        _events = null;
    }

    void OnBodyEntered(Node3D body)
    {
        if (body.IsInGroup("Player")) _events?.BroadCastOnTriggerEnter?.Invoke(body);
    }

    void OnBodyExited(Node3D body)
    {
        if (body.IsInGroup("Player")) _events?.BroadCastOnTriggerExit?.Invoke(body);
    }
}
