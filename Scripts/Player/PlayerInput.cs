using Godot;
using System.Collections.Generic;

/// <summary>
/// Gated input wrapper — the Godot replacement for the Rewired-backed Unity PlayerInput.
///
/// REWIRED → GODOT
/// ───────────────
/// • Each player is bound to a physical gamepad by its Godot joypad device id (for
///   16-player local play every LocalPlayerManager owns one of these around a device).
/// • Action strings keep their Unity/Rewired names ("A", "Move Horizontal", …) and are
///   mapped to Godot JoyButton / JoyAxis here, so the ported controllers need no changes.
/// • Godot's raw joypad API exposes only current state (Input.IsJoyButtonPressed), with
///   no GetButtonDown/Up edge events. Poll() snapshots button state once per rendered
///   frame so GetButtonDown/GetButtonUp can derive edges — call it at the very start of
///   the owner's _Process, before any controller reads input.
///
/// Context gating (CombatInputActive / MovementInputActive / one-frame suppression) is
/// ported verbatim from Unity.
/// </summary>
public class PlayerInput
{
	readonly int _device;

	public PlayerInputContext Context { get; set; } = PlayerInputContext.Disabled;

	/// <summary>True when Context is anything other than Disabled (back-compat).</summary>
	public bool IsEnabled
	{
		get => Context != PlayerInputContext.Disabled;
		set => Context = value ? PlayerInputContext.Battle : PlayerInputContext.Disabled;
	}

	/// <summary>The bound joypad device id.</summary>
	public int PlayerId => _device;

	// ── Action → device-input maps ────────────────────────────────────────────
	static readonly Dictionary<string, JoyButton> ButtonMap = new()
	{
		{ "A",             JoyButton.A },
		{ "B",             JoyButton.B },
		{ "X",             JoyButton.X },
		{ "Y",             JoyButton.Y },
		{ "Start",         JoyButton.Start },
		{ "Right Shoulder", JoyButton.RightShoulder },
		{ "Left Shoulder",  JoyButton.LeftShoulder },
		{ "Right Stick Button", JoyButton.RightStick }, // team invite/action menu (TeamController)
		{ "Left Stick Button",  JoyButton.LeftStick },
		{ "D-Pad Up",      JoyButton.DpadUp },
		{ "D-Pad Down",    JoyButton.DpadDown },
		{ "D-Pad Left",    JoyButton.DpadLeft },
		{ "D-Pad Right",   JoyButton.DpadRight },
	};

	// axis name → (Godot axis, sign). Godot stick-Y is +down, so Move/Right-Stick Y
	// are inverted to match Unity's +up convention used throughout the ported logic.
	static readonly Dictionary<string, (JoyAxis axis, float sign)> AxisMap = new()
	{
		{ "Move Horizontal", (JoyAxis.LeftX,   1f) },
		{ "Move Vertical",   (JoyAxis.LeftY,  -1f) },
		{ "Right Stick X",   (JoyAxis.RightX,  1f) },
		{ "Right Stick Y",   (JoyAxis.RightY, -1f) },
	};

	readonly Dictionary<string, bool> _curr = new();
	readonly Dictionary<string, bool> _prev = new();

	bool _suppressCombatThisFrame;

	public PlayerInput(int device)
	{
		_device = device;
		foreach (var key in ButtonMap.Keys) { _curr[key] = false; _prev[key] = false; }
	}

	/// <summary>
	/// Snapshot all mapped button states for edge detection. Call once per rendered
	/// frame at the start of the owner's _Process, before controllers read input.
	/// </summary>
	public void Poll()
	{
		var v = VirtualControllers.IsVirtual(_device) ? VirtualControllers.Get(_device) : null;
		foreach (var kv in ButtonMap)
		{
			_prev[kv.Key] = _curr[kv.Key];
			_curr[kv.Key] = v != null ? v.GetButton(kv.Value) : Input.IsJoyButtonPressed(_device, kv.Value);
		}
	}

	// ── One-frame combat suppression (dialog dismiss-bleed guard) ─────────────
	public void SuppressCombatThisFrame() => _suppressCombatThisFrame = true;
	public void ClearFrameSuppression()   => _suppressCombatThisFrame = false;

	// ── Gating ────────────────────────────────────────────────────────────────
	bool CombatInputActive =>
		!_suppressCombatThisFrame              &&
		Context != PlayerInputContext.Disabled &&
		Context != PlayerInputContext.Dialog   &&
		Context != PlayerInputContext.Menu     &&
		Context != PlayerInputContext.Spectate;

	public bool IsCombatInputActive => CombatInputActive;

	bool MovementInputActive =>
		CombatInputActive && Context != PlayerInputContext.Comboing;

	// ── Combat input ──────────────────────────────────────────────────────────
	// Godot's Input.GetJoyAxis returns the raw axis (incl. controller resting drift).
	// Rewired applied a deadzone; we replicate it here with a rescaled radial cutoff so
	// an idle stick reads exactly 0 (no drift) and full deflection still reaches 1.
	const float AxisDeadzone = 0.2f;

	public float GetAxis(string action)
	{
		if (!MovementInputActive) return 0f;
		if (!AxisMap.TryGetValue(action, out var a)) return 0f;

		float rawDevice = VirtualControllers.IsVirtual(_device)
			? (VirtualControllers.Get(_device)?.GetAxis(a.axis) ?? 0f)
			: Input.GetJoyAxis(_device, a.axis);
		float raw = rawDevice * a.sign;
		float mag = Mathf.Abs(raw);
		if (mag < AxisDeadzone) return 0f;
		return Mathf.Sign(raw) * (mag - AxisDeadzone) / (1f - AxisDeadzone);
	}

	public bool GetButton(string action)
		=> CombatInputActive && _curr.GetValueOrDefault(action);

	public bool GetButtonDown(string action)
		=> CombatInputActive && _curr.GetValueOrDefault(action) && !_prev.GetValueOrDefault(action);

	public bool GetButtonUp(string action)
		=> CombatInputActive && !_curr.GetValueOrDefault(action) && _prev.GetValueOrDefault(action);

	// ── UI / dialog / menu input (bypass combat gating) ───────────────────────
	public bool GetUIButtonDown(string action)
		=> Context == PlayerInputContext.Dialog
		   && _curr.GetValueOrDefault(action) && !_prev.GetValueOrDefault(action);

	public bool GetMenuButtonDown(string action)
		=> Context == PlayerInputContext.Menu
		   && _curr.GetValueOrDefault(action) && !_prev.GetValueOrDefault(action);

	public bool GetMenuOpenButtonDown()
		=> (Context == PlayerInputContext.Battle || Context == PlayerInputContext.Prone)
		   && _curr.GetValueOrDefault("Start") && !_prev.GetValueOrDefault("Start");
}
