using Godot;
using System;

/// <summary>
/// Per-attack-type trigger event hub. Ported from Unity (UnityAction → System.Action,
/// Collider → Area3D). The nested AttackType enum is the canonical attack classifier
/// referenced across AttackData, ComboData, and the input buffer.
///
/// Enum order is preserved from the Unity project so serialized integer values in
/// migrated combo data still line up: None=0, Light=1, Heavy=2, Special=3, Launcher=4.
/// </summary>
public class HitBoxTriggerEvents
{
    public enum AttackType { None, Light, Heavy, Special, Launcher }

    public Action<Node3D> BroadCastOnTriggerEnter;
    public Action<Node3D> BroadCastOnTriggerExit;
}
