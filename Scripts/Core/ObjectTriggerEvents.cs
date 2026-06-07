using Godot;
using System;

/// <summary>
/// Trigger event hub for non-attack volumes (currently the Aura Field).
/// Ported from Unity: UnityAction → System.Action, Collider → Area3D.
/// </summary>
public class ObjectTriggerEvents
{
    public enum Type { AuraField }

    public Action<Node3D> BroadCastOnTriggerEnter;
    public Action<Node3D> BroadCastOnTriggerExit;
}
