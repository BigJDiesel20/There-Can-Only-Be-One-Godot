using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Payload for PlayerEvents.OnMessageRequested — a message, a duration, and 1..4 labelled
/// choices each carrying the callback to run when that button is chosen.
/// </summary>
public class MessageRequest
{
	public string Message;
	public double Duration;
	public List<(string label, Action action)> Choices = new();
}

/// <summary>
/// Central per-player event hub. Ported from Unity (UnityAction → System.Action,
/// Collider → Area3D, Quaternion/Vector3 → Godot equivalents).
///
/// Controllers subscribe to and fire these instead of polling each other. The
/// three dictionaries fan out per-type sub-hubs (hitbox triggers, object triggers,
/// per-stat events) exactly as in the Unity project.
///
/// NOTE on the Unity Update/FixedUpdate/LateUpdate trio: in Godot these map to
/// _Process (OnUpdate), _PhysicsProcess (OnFixedUpdate), and a late phase driven
/// after _Process (OnLateUpdate). LocalPlayerManager raises them in that order.
/// </summary>
public class PlayerEvents
{
	public Action OnUpdate;
	public Action OnFixedUpdate;
	public Action OnLateUpdate;

	public Action<Quaternion, float> OnAttackRotate;   // rotation, arcThreshold

	/// <summary>Fired when the first attack of a new chain is committed (comboCounter 0 → 1).</summary>
	public Action OnAttackStart;
	public Action OnAttackEnd;

	public Action<(Node3D hitbox, Node3D hurtbox)> OnHitConfirm;
	public Action<(Node3D hitbox, Node3D hurtbox)> OnHitConfirmPauseEnd;

	public Action<Vector3> OnPush;
	public Action OnAnimationComplete;
	public Action OnCoolDownComplete;
	public Action<LocalPlayerManager, bool> OnOrbitTargetChanged;
	public Action<Damage> OnDamageReceived;
	public Action OnAuraDrain;
	public Action OnAuraReplenish;
	public Action OnTeamChanged;
	public Action<bool> OnInvulnerabilityActive;
	public Action<bool> OnProneActive;

	/// <summary>Fired when a dialog/message box opens (true) or closes (false).</summary>
	public Action<bool> OnDialogStateChanged;

	/// <summary>Fired when the Player Menu opens (true) / closes (false).</summary>
	public Action<bool> OnMenuStateChanged;

	/// <summary>
	/// Fired when any system wants a dialog/message box shown on THIS player's UI. The UI
	/// subscribes; senders use the RequestMessage helpers below. Cross-player dialogs fire on
	/// the recipient's events (e.g. otherPlayer.playerEvents.RequestMessage(...)). No subscriber
	/// = no-op, exactly like the other events.
	/// </summary>
	public Action<MessageRequest> OnMessageRequested;

	public void RequestMessage(string message, Action confirmX, string confirmXButtonText, double messageDuration)
		=> OnMessageRequested?.Invoke(new MessageRequest { Message = message, Duration = messageDuration,
			   Choices = new() { (confirmXButtonText, confirmX) } });

	public void RequestMessage(string message, Action confirmX, Action rejectB, (string confirmX, string rejectB) buttonText, double messageDuration)
		=> OnMessageRequested?.Invoke(new MessageRequest { Message = message, Duration = messageDuration,
			   Choices = new() { (buttonText.confirmX, confirmX), (buttonText.rejectB, rejectB) } });

	public void RequestMessage(string message, Action confirmX, Action confirmY, Action rejectB, (string confirmX, string confirmY, string rejectB) buttonText, double messageDuration)
		=> OnMessageRequested?.Invoke(new MessageRequest { Message = message, Duration = messageDuration,
			   Choices = new() { (buttonText.confirmX, confirmX), (buttonText.confirmY, confirmY), (buttonText.rejectB, rejectB) } });

	public void RequestMessage(string message, Action confirmX, Action confirmY, Action confirmA, Action rejectB, (string confirmX, string confirmY, string confirmA, string rejectB) buttonText, double messageDuration)
		=> OnMessageRequested?.Invoke(new MessageRequest { Message = message, Duration = messageDuration,
			   Choices = new() { (buttonText.confirmX, confirmX), (buttonText.confirmY, confirmY), (buttonText.confirmA, confirmA), (buttonText.rejectB, rejectB) } });

	public Dictionary<HitBoxTriggerEvents.AttackType, HitBoxTriggerEvents> hitboxTriggerEventCollection = new()
	{
		{ HitBoxTriggerEvents.AttackType.None,     new HitBoxTriggerEvents() },
		{ HitBoxTriggerEvents.AttackType.Light,    new HitBoxTriggerEvents() },
		{ HitBoxTriggerEvents.AttackType.Heavy,    new HitBoxTriggerEvents() },
		{ HitBoxTriggerEvents.AttackType.Special,  new HitBoxTriggerEvents() },
		{ HitBoxTriggerEvents.AttackType.Launcher, new HitBoxTriggerEvents() }
	};

	public Dictionary<ObjectTriggerEvents.Type, ObjectTriggerEvents> objectTriggerEventCollection = new()
	{
		{ ObjectTriggerEvents.Type.AuraField, new ObjectTriggerEvents() }
	};

	public Dictionary<StatEvents.Type, StatEvents> statEventsCoclection = new()
	{
		{ StatEvents.Type.Health,             new StatEvents() },
		{ StatEvents.Type.HealthRegeneration, new StatEvents() },
		{ StatEvents.Type.Stamina,            new StatEvents() },
		{ StatEvents.Type.StaminaRecovery,    new StatEvents() },
		{ StatEvents.Type.Aura,               new StatEvents() },
		{ StatEvents.Type.Armor,              new StatEvents() },
		{ StatEvents.Type.ToughHide,          new StatEvents() }
	};
}
