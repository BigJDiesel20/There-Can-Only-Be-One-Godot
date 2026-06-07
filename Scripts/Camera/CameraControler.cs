using Godot;
using System.Collections.Generic;

/// <summary>
/// Per-player camera (orbit / follow / fighting-side) with lock-on targeting and a
/// floating cursor. Ported from Unity. THIS IS PASS 1 of 2.
///
/// PASS 1 (this file): node setup, mode dispatch, full Orbit + Follow camera math,
/// anchor follow, directional auto-follow, the CameraStateWrapper.MovementForward/Right
/// writes, hit-confirm pause. Targeting, fighting-side framing, follow aim-lock target
/// acquisition, the cursor symbol, and the debug overlays are STUBBED (marked
/// "PASS 2") so the class compiles; they are filled in next.
///
/// GODOT ADAPTATIONS
/// • Camera (component) + cameraObject (GameObject) → a single Camera3D.
/// • cameraLocation / cameraAnchor GameObjects → Node3D helpers created under the player.
/// • Transform.LookAt → Node3D.LookAt (Godot −Z faces the target — matches a camera).
/// • Split-screen via Camera.rect → deferred to SubViewport scene setup (Pass 2+).
/// • [Header]/[Tooltip] inspector fields → plain public fields (class is built at runtime).
///
/// ⚠ HANDEDNESS: Unity forward = +Z, Godot forward = −Z. Forward/right/shoulder-offset
/// signs are marked NOTE and must be verified in-engine.
/// </summary>
public class CameraControler
{
	// ── References ────────────────────────────────────────────────────────────
	public PlayerInput GamePad;
	public Camera3D camera;
	public Node3D cursor;
	public string PlayerName;

	// ── Runtime nodes (created in Initialize) ─────────────────────────────────
	public Node3D cameraLocation;
	public Node3D cameraAnchor;
	public Node3D cameraTarget;

	// ── Orbit shape ────────────────────────────────────────────────────────────
	public float x;
	public float y;
	public float height = 3f;
	public float radius = 7f;
	public float degrees = 0f;
	public float degreeOffset = 90f;

	// ── Orbit pitch ──────────────────────────────────────────────────────────
	public float pitch = 20f;
	public float minPitch = -5f;
	public float maxPitch = 75f;

	// ── Orbit input speeds ─────────────────────────────────────────────────────
	public float horizontalOrbitSpeed = 5f;
	public float verticalOrbitSpeed = 2f;

	// ── Anchor follow ──────────────────────────────────────────────────────────
	public float frontThreshold = 3f;
	public float backThreshold = 3f;
	public float deadzone = 0.05f;
	public float anchorLerpSpeed = 3.5f;

	// ── Directional auto-follow ────────────────────────────────────────────────
	public float directionalFollowSpeed = 90f;

	// ── Orbit look offset ──────────────────────────────────────────────────────
	public float lookOffsetX = 0f;
	public float lookOffsetY = 0f;

	// ── Follow camera ──────────────────────────────────────────────────────────
	public float sholderHeight = 1.74f;
	public float sholderDistance = -5.25f;  // NOTE: Unity −Z = behind; Godot behind = +Z
	public float sholderOffset = 2.19f;

	public float followPitch = 0f;
	public float followMinPitch = -30f;
	public float followMaxPitch = 45f;
	public float followTiltSpeed = 60f;
	public float followTiltReturnSpeed = 120f;

	// ── Follow aim lock ────────────────────────────────────────────────────────
	public float aimHorizontalLimit = 60f;
	public float aimVerticalLimit = 30f;
	public float aimRotateSpeed = 90f;
	public float aimCenterX = 0.07f;
	public float aimCenterY = 0.15f;

	// ── Targeting ──────────────────────────────────────────────────────────────
	public float targetingRange = 20f;
	public float targetingOrbitSpeed = 120f;
	public float offScreenEdgePadding = 0.05f;
	public float doubleTapInterval = 0.35f;
	public float cycleDzone = 0.3f;
	public float losTimeout = 2f;

	// ── Combat framing ─────────────────────────────────────────────────────────
	public float attackViewAngleOffset = 60f;
	public float attackViewLerpSpeed = 90f;

	// ── State ──────────────────────────────────────────────────────────────────
	public bool isSwitched = false;

	// ── Side view ──────────────────────────────────────────────────────────────
	public float sideViewHeight = 2f;
	public float sideViewDistanceMultiplier = 0.8f;
	public float sideViewMinDistance = 5f;
	public float sideViewMaxDistance = 22f;
	public float sideViewSmoothing = 6f;
	public float sideViewFOV = 55f;
	public float sideViewHUDClearance = 0.32f;
	public float sideViewFramingSpeed = 4f;

	// ── Private state ──────────────────────────────────────────────────────────
	private Node3D playerTransform;

	// Follow the player's smoothly interpolated transform (valid when physics
	// interpolation is enabled) so a render-rate camera doesn't sample the 60 Hz-
	// stepped body — fixes the radial jitter when running toward/away from camera.
	private Vector3 PlayerPos => playerTransform.GetGlobalTransformInterpolated().Origin;

	private bool isAnchorLerping = false;
	private bool isHit;

	private bool isInitialized = false;
	public bool IsInitialized => isInitialized;

	CameraStateWrapper cameraStateWapper = new();
	private bool _isHitConfirmPause;
	private PlayerEvents playerEvents;

	// Side-view private state
	private bool    _isSideView = false;
	private int     _sideViewSign = 1;
	private Vector3 _sideViewSmoothPos;
	private Vector3 _lastFightAxis = Vector3.Forward;
	private float   _defaultFOV = 60f;
	private float   _sideViewLookOffsetY = 0f;

	// Follow aim-lock private state
	private bool               _isFollowAimLock = false;
	private float              _aimLockYaw = 0f;
	private float              _aimLockPitch = 0f;
	private float              _aimYawOffset = 0f;
	private float              _aimPitchOffset = 0f;
	private LocalPlayerManager _followAimTarget = null;

	// Orbit targeting private state
	private LocalPlayerManager _owner = null;
	private bool               _isTargeting = false;
	private bool               _isR1Held = false;
	private LocalPlayerManager _currentTarget = null;
	private readonly List<LocalPlayerManager> _sortedTargets = new();
	private int                _targetIndex = 0;
	private float              _lastR1TapTime = -999f;
	private bool               _stickCycleReady = true;
	private float              _losTimer = 0f;
	private float              _attackViewOffset = 0f;

	// ══════════════════════════════════════════════════════════════════════════
	//  Per-frame tick (subscribed to OnLateUpdate)
	// ══════════════════════════════════════════════════════════════════════════
	public void OnUpdate()
	{
		cameraStateWapper.CurrentState = CameraStateWrapper.CameraState.Orbit;
		if (!isInitialized) return;

		if (GamePad.GetButtonDown("D-Pad Down"))
		{
			isSwitched = !isSwitched;
			if (!isSwitched && _isFollowAimLock) ExitFollowAimLock();
			if ( isSwitched && _isTargeting)     ExitTargeting();
			if (_isSideView)                     ExitSideView();
		}

		if (GamePad.GetButtonDown("D-Pad Right"))
		{
			if (_isSideView) ExitSideView();
			else             EnterSideView();
		}

		if (_isSideView)
		{
			cameraStateWapper.CurrentState = CameraStateWrapper.CameraState.FightingSide;
			if (_currentTarget == null || _currentTarget.character == null)
				ExitSideView();
		}
		else
		{
			cameraStateWapper.CurrentState = isSwitched
				? CameraStateWrapper.CameraState.Follow
				: CameraStateWrapper.CameraState.Orbit;
		}

		if (cameraStateWapper.CurrentState == CameraStateWrapper.CameraState.Orbit)
			UpdateTargeting();

		if (camera != null)
		{
			switch (cameraStateWapper.CurrentState)
			{
				case CameraStateWrapper.CameraState.Orbit:
					if (_isTargeting)        UpdateOffScreenIndicator();
					else if (cursor != null) cursor.Visible = false;
					Orbit();
					break;

				case CameraStateWrapper.CameraState.Follow:
					if (_isFollowAimLock)    UpdateFollowAimCursor();
					else if (cursor != null) cursor.Visible = false;
					Follow();
					break;

				case CameraStateWrapper.CameraState.FightingSide:
					if (cursor != null) cursor.Visible = false;
					FightingSide();
					break;
			}
		}
	}

	// ══════════════════════════════════════════════════════════════════════════
	//  Orbit
	// ══════════════════════════════════════════════════════════════════════════
	public void Orbit()
	{
		UpdateAnchor();

		if (_isTargeting && _isR1Held)
		{
			UpdateTargetingOrbit();
		}
		else
		{
			UpdateDirectionalFollow();
			degrees += (Mathf.Abs(GamePad.GetAxis("Right Stick X")) > 0.2f)
				? GamePad.GetAxis("Right Stick X") * horizontalOrbitSpeed : 0;
		}

		degrees %= 360;

		pitch += (Mathf.Abs(GamePad.GetAxis("Right Stick Y")) > 0.2f) ? -GamePad.GetAxis("Right Stick Y") * verticalOrbitSpeed : 0;
		pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

		float pitchRad = pitch * MathU.Deg2Rad;
		float horizontalRadius = Mathf.Abs(radius) * Mathf.Cos(pitchRad);
		float verticalOffset = Mathf.Abs(radius) * Mathf.Sin(pitchRad);

		y = horizontalRadius * Mathf.Sin((degrees - degreeOffset) * MathU.Deg2Rad);
		x = horizontalRadius * Mathf.Cos((degrees - degreeOffset) * MathU.Deg2Rad);

		camera.GlobalPosition = new Vector3(x, verticalOffset, y) + cameraAnchor.GlobalPosition;

		Vector3 lookTarget = cameraAnchor.GlobalPosition
			+ camera.GlobalBasis.X * lookOffsetX
			+ camera.GlobalBasis.Y * lookOffsetY;

		if (camera.GlobalPosition.DistanceTo(lookTarget) > 0.001f)
			camera.LookAt(lookTarget, Vector3.Up);

		// Keep cameraLocation in sync (parity with Unity) and publish movement frame.
		cameraLocation.GlobalPosition = camera.GlobalPosition;
		cameraLocation.GlobalRotation = new Vector3(0, camera.GlobalRotation.Y, camera.GlobalRotation.Z);

		if (!(_isTargeting && _isR1Held))
		{
			Vector3 camFwd = -camera.GlobalBasis.Z; camFwd.Y = 0; camFwd = camFwd.Normalized();      // NOTE handedness
			Vector3 camRight = camera.GlobalBasis.X; camRight.Y = 0; camRight = camRight.Normalized();
			cameraStateWapper.MovementForward = camFwd;
			cameraStateWapper.MovementRight   = camRight;
		}
	}

	// ══════════════════════════════════════════════════════════════════════════
	//  Follow
	// ══════════════════════════════════════════════════════════════════════════
	public void Follow()
	{
		if (GamePad.GetButtonDown("Right Shoulder"))    EnterFollowAimLock();
		else if (GamePad.GetButtonUp("Right Shoulder")) ExitFollowAimLock();

		cameraAnchor.GlobalPosition = PlayerPos;

		if (_isFollowAimLock)
		{
			float stickX = GamePad.GetAxis("Right Stick X");
			if (Mathf.Abs(stickX) > deadzone)
			{
				_aimYawOffset -= stickX * aimRotateSpeed * GameTime.Delta;
				_aimYawOffset  = Mathf.Clamp(_aimYawOffset, -aimHorizontalLimit, aimHorizontalLimit);
			}

			float stickY = GamePad.GetAxis("Right Stick Y");
			if (Mathf.Abs(stickY) > deadzone)
			{
				_aimPitchOffset -= stickY * aimRotateSpeed * GameTime.Delta;
				_aimPitchOffset  = Mathf.Clamp(_aimPitchOffset, -aimVerticalLimit, aimVerticalLimit);
			}

			float finalYaw   = _aimLockYaw  + _aimYawOffset;
			float finalPitch = _aimLockPitch + _aimPitchOffset;

			cameraAnchor.GlobalRotation = new Vector3(0, playerTransform.GlobalRotation.Y, 0); // NOTE handedness
			camera.GlobalPosition = cameraAnchor.ToGlobal(new Vector3(sholderOffset, sholderHeight, -sholderDistance));
			camera.GlobalRotation = new Vector3(Mathf.DegToRad(-finalPitch), Mathf.DegToRad(finalYaw), 0);

			UpdateFollowAimTarget();
		}
		else
		{
			cameraAnchor.GlobalRotation = new Vector3(0, playerTransform.GlobalRotation.Y, 0); // NOTE handedness
			camera.GlobalPosition = cameraAnchor.ToGlobal(new Vector3(sholderOffset, sholderHeight, -sholderDistance));

			float rightStickY = GamePad.GetAxis("Right Stick Y");
			if (Mathf.Abs(rightStickY) > deadzone)
			{
				followPitch -= rightStickY * followTiltSpeed * GameTime.Delta;
				followPitch  = Mathf.Clamp(followPitch, followMinPitch, followMaxPitch);
			}
			else
			{
				followPitch = MathU.MoveTowards(followPitch, 0f, followTiltReturnSpeed * GameTime.Delta);
			}

			camera.GlobalRotation = new Vector3(Mathf.DegToRad(-followPitch), cameraAnchor.GlobalRotation.Y, 0);
		}
	}

	void EnterFollowAimLock()
	{
		if (_isFollowAimLock) return;
		_isFollowAimLock = true;
		cameraStateWapper.IsFollowAimLock = true;
		_aimLockYaw     = Mathf.RadToDeg(playerTransform.GlobalRotation.Y);
		_aimLockPitch   = followPitch;
		_aimYawOffset   = 0f;
		_aimPitchOffset = 0f;
		_followAimTarget = null;
	}

	void ExitFollowAimLock()
	{
		if (!_isFollowAimLock) return;
		ClearFollowAimHighlight();
		_isFollowAimLock = false;
		cameraStateWapper.IsFollowAimLock = false;
		_followAimTarget = null;
		followPitch     = Mathf.Clamp(_aimLockPitch + _aimPitchOffset, followMinPitch, followMaxPitch);
		_aimYawOffset   = 0f;
		_aimPitchOffset = 0f;
		cameraTarget = null;
		isHit        = false;
		playerEvents.OnOrbitTargetChanged?.Invoke(null, false);
	}

	// ══════════════════════════════════════════════════════════════════════════
	//  Anchor + directional follow (Orbit helpers)
	// ══════════════════════════════════════════════════════════════════════════
	void UpdateAnchor()
	{
		Vector3 flatForward = -camera.GlobalBasis.Z; flatForward.Y = 0; flatForward = flatForward.Normalized(); // NOTE handedness
		Vector3 anchorToPlayer = PlayerPos - cameraAnchor.GlobalPosition;
		float forwardOffset = anchorToPlayer.Dot(flatForward);

		bool beyondFront = forwardOffset > frontThreshold;
		bool beyondBack  = forwardOffset < -backThreshold;

		if (beyondFront || beyondBack)
			isAnchorLerping = true;

		if (isAnchorLerping)
		{
			float distanceToPlayer = cameraAnchor.GlobalPosition.DistanceTo(PlayerPos);
			if (distanceToPlayer <= deadzone)
				isAnchorLerping = false;
			else
				cameraAnchor.GlobalPosition = cameraAnchor.GlobalPosition.Lerp(PlayerPos, anchorLerpSpeed * GameTime.Delta);
		}
	}

	void UpdateDirectionalFollow()
	{
		bool rightStickActive = Mathf.Abs(GamePad.GetAxis("Right Stick X")) > 0.2f
							 || Mathf.Abs(GamePad.GetAxis("Right Stick Y")) > 0.2f;

		float horizontalInput = GamePad.GetAxis("Move Horizontal");

		if (rightStickActive || Mathf.Abs(horizontalInput) <= 0.2f) return;

		Vector3 flatCamRight = camera.GlobalBasis.X; flatCamRight.Y = 0; flatCamRight = flatCamRight.Normalized();
		Vector3 moveDir = flatCamRight * Mathf.Sign(horizontalInput);

		float targetDegrees = Mathf.Atan2(-moveDir.Z, -moveDir.X) * MathU.Rad2Deg + degreeOffset;
		degrees = MathU.MoveTowardsAngle(degrees, targetDegrees, directionalFollowSpeed * GameTime.Delta);
	}

	// ══════════════════════════════════════════════════════════════════════════
	//  Lifecycle
	// ══════════════════════════════════════════════════════════════════════════
	// renderParent is where the camera + helper nodes are added — the player's SubViewport
	// for split-screen, or the LocalPlayerManager for single-view. It need not be a Node3D
	// (the camera is transform-driven by world coordinates each frame regardless). owner is
	// the LocalPlayerManager for targeting/self-identification.
	public void Initialize(Node3D character, Node renderParent, LocalPlayerManager owner,
						   PlayerInput gamePad, Node3D cursor, string cameraCullingMask, PlayerEvents playerEvents)
	{
		cameraLocation = new Node3D { Name = "cameraLocation" };
		renderParent.AddChild(cameraLocation);

		camera = new Camera3D { Name = "myCamera" };
		renderParent.AddChild(camera);
		camera.Current = true; // makes this the active camera of its (Sub)Viewport

		this.cursor = cursor;
		this.playerTransform = character;

		cameraAnchor = new Node3D { Name = "CameraOrbitAnchor" };
		renderParent.AddChild(cameraAnchor);
		cameraAnchor.GlobalPosition = character.GlobalPosition;

		// Camera nodes are positioned every render frame, so exclude them from physics
		// interpolation — only the physics-stepped player body should interpolate.
		camera.PhysicsInterpolationMode         = Node.PhysicsInterpolationModeEnum.Off;
		cameraLocation.PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off;
		cameraAnchor.PhysicsInterpolationMode   = Node.PhysicsInterpolationModeEnum.Off;

		this.GamePad = gamePad;
		_owner = owner;
		_defaultFOV = camera.Fov;

		this.playerEvents = playerEvents;
		this.playerEvents.OnLateUpdate          += OnUpdate;
		this.playerEvents.OnHitConfirm          += OnHitConfirm;
		this.playerEvents.OnHitConfirmPauseEnd  += OnHitConfirmPauseEnd;
		isInitialized = true;
		// NOTE: cameraCullingMask / per-player culling handled by SubViewport split-screen later.
	}

	public void Deactivate()
	{
		if (_isFollowAimLock) ExitFollowAimLock();
		if (_isTargeting)     ExitTargeting();
		_owner = null;
		_sortedTargets.Clear();

		this.playerEvents.OnLateUpdate          -= OnUpdate;
		this.playerEvents.OnHitConfirm          -= OnHitConfirm;
		this.playerEvents.OnHitConfirmPauseEnd  -= OnHitConfirmPauseEnd;
		this.playerEvents = null;

		if (cameraAnchor   != null) { cameraAnchor.QueueFree();   cameraAnchor = null; }
		if (cameraLocation != null) { cameraLocation.QueueFree(); cameraLocation = null; }
		if (cursor != null) { cursor.QueueFree(); cursor = null; }
		if (camera != null) { camera.QueueFree(); camera = null; }
		isInitialized = false;
	}

	public Camera3D GetCamera() => camera;
	public Node3D GetCameraLocation() => cameraLocation;
	public CameraStateWrapper GetCameraState() => cameraStateWapper;

	public void SetCameraName(string playerName)
	{
		PlayerName = playerName;
		if (camera != null) camera.Name = $"{playerName} myCamera";
		if (cursor != null) cursor.Name = $"{playerName} Cursor";
	}

	public void SetDisplayName(string playerName) { }

	public void OnHitConfirm((Node3D hitbox, Node3D hurtbox) hitInfo)         => _isHitConfirmPause = true;
	public void OnHitConfirmPauseEnd((Node3D hitbox, Node3D hurtbox) hitInfo) => _isHitConfirmPause = false;

	// ══════════════════════════════════════════════════════════════════════════
	//  PASS 2 — targeting, side-view, aim-lock acquisition, cursor.
	// ══════════════════════════════════════════════════════════════════════════
	private List<MeshInstance3D> _targetRenderers;
	private List<MeshInstance3D> _followAimRenderers;
	private StandardMaterial3D   _highlightMat;
	private StandardMaterial3D   _aimHighlightMat;
	private PlayerSymbolEntry    _currentSymbolEntry;

	Vector3 CursorOffset => _currentSymbolEntry != null ? _currentSymbolEntry.positionOffset : new Vector3(0, 2, 0);
	float   CursorScale  => _currentSymbolEntry != null ? _currentSymbolEntry.scale          : 1f;

	// ── Targeting state machine ────────────────────────────────────────────────
	void UpdateTargeting()
	{
		bool r1Down = GamePad.GetButtonDown("Right Shoulder");
		bool r1Up   = GamePad.GetButtonUp("Right Shoulder");
		_isR1Held   = GamePad.GetButton("Right Shoulder");

		if (r1Up) _stickCycleReady = false;

		if (r1Down)
		{
			float elapsed  = GameTime.Time - _lastR1TapTime;
			_lastR1TapTime = GameTime.Time;

			if (_isTargeting && elapsed <= doubleTapInterval) { ExitTargeting(); return; }
			if (!_isTargeting) EnterTargeting();
		}

		if (_isTargeting)
		{
			if (_sortedTargets.Count == 0) BuildSortedTargets();
			CheckLineOfSight();
			if (_isR1Held) UpdateTargetCycling();
		}
	}

	void CheckLineOfSight()
	{
		if (losTimeout <= 0f || _currentTarget == null || _currentTarget.character == null) { _losTimer = 0f; return; }

		Vector3 from = playerTransform.GlobalPosition + Vector3.Up * 1.2f;
		Vector3 to   = _currentTarget.character.GlobalPosition + Vector3.Up * 1.2f;
		if (from.DistanceTo(to) < 0.01f) { _losTimer = 0f; return; }

		var space    = camera.GetViewport().FindWorld3D().DirectSpaceState;
		var rayQuery = PhysicsRayQueryParameters3D.Create(from, to);
		var exclude  = new Godot.Collections.Array<Rid>();
		AddBodyRids(playerTransform, exclude);
		AddBodyRids(_currentTarget.character, exclude);
		rayQuery.Exclude = exclude;

		bool blocked = space.IntersectRay(rayQuery).Count > 0;

		if (blocked) { _losTimer += GameTime.Delta; if (_losTimer >= losTimeout) ExitTargeting(); }
		else _losTimer = 0f;
	}

	void EnterTargeting()
	{
		BuildSortedTargets();
		if (_sortedTargets.Count == 0) return;
		_isTargeting     = true;
		_targetIndex     = 0;
		_stickCycleReady = true;
		SetCurrentTarget(_sortedTargets[0]);
	}

	void ExitTargeting()
	{
		_isTargeting      = false;
		_losTimer         = 0f;
		_attackViewOffset = 0f;
		ClearTargetHighlight();
		_currentTarget = null;
		_sortedTargets.Clear();
		cameraTarget = null;
		isHit        = false;
		playerEvents.OnOrbitTargetChanged?.Invoke(null, false);
	}

	void BuildSortedTargets()
	{
		_sortedTargets.Clear();
		if (_owner == null) return;

		Vector3 ownerPos = playerTransform.GlobalPosition;
		float   rangeSqr = targetingRange * targetingRange;

		foreach (var p in LocalPlayerManager.ActivePlayers)
		{
			if (p == _owner || p.character == null) continue;
			if ((p.character.GlobalPosition - ownerPos).LengthSquared() <= rangeSqr)
				_sortedTargets.Add(p);
		}

		_sortedTargets.Sort((a, b) =>
		{
			float dA = (a.character.GlobalPosition - ownerPos).LengthSquared();
			float dB = (b.character.GlobalPosition - ownerPos).LengthSquared();
			return dA.CompareTo(dB);
		});
	}

	void SetCurrentTarget(LocalPlayerManager target)
	{
		ClearTargetHighlight();
		_currentTarget = target;
		if (_currentTarget != null && _currentTarget.character != null)
		{
			_targetRenderers = NodeUtil.GetComponentsInChildren<MeshInstance3D>(_currentTarget.character);
			ApplyTargetHighlight();
		}
		cameraTarget = _currentTarget?.character;
		isHit        = _currentTarget != null;
		playerEvents.OnOrbitTargetChanged?.Invoke(_currentTarget, _currentTarget != null);
		if (_currentTarget != null) SetCursorSymbol(_currentTarget.ActiveSymbol);
	}

	void ApplyTargetHighlight()
	{
		if (_targetRenderers == null) return;
		_highlightMat ??= MakeHighlightMat(new Color(1f, 0.84f, 0f)); // gold
		foreach (var r in _targetRenderers) if (r != null) r.MaterialOverlay = _highlightMat;
	}

	void ClearTargetHighlight()
	{
		if (_targetRenderers == null) return;
		foreach (var r in _targetRenderers) if (r != null) r.MaterialOverlay = null;
		_targetRenderers = null;
	}

	void UpdateTargetCycling()
	{
		if (_sortedTargets.Count <= 1) return;
		float stickX = GamePad.GetAxis("Right Stick X");

		if (Mathf.Abs(stickX) > cycleDzone)
		{
			if (_stickCycleReady)
			{
				_stickCycleReady = false;
				int count = _sortedTargets.Count;
				if (stickX > 0f) _targetIndex = (_targetIndex + 1) % count;
				else             _targetIndex = (_targetIndex - 1 + count) % count;
				_targetIndex = Mathf.Clamp(_targetIndex, 0, _sortedTargets.Count - 1);
				SetCurrentTarget(_sortedTargets[_targetIndex]);
			}
		}
		else _stickCycleReady = true;
	}

	void UpdateTargetingOrbit()
	{
		if (_currentTarget == null || _currentTarget.character == null) return;

		Vector3 ownerPos  = playerTransform.GlobalPosition;
		Vector3 targetPos = _currentTarget.character.GlobalPosition;

		Vector3 camDir = ownerPos - targetPos; camDir.Y = 0f;
		if (camDir.LengthSquared() < 0.001f) return;
		camDir = camDir.Normalized();

		float targetDegrees = Mathf.Atan2(camDir.Z, camDir.X) * MathU.Rad2Deg + degreeOffset;

		float snapRange    = (_owner != null && _owner.attackController != null) ? _owner.attackController.attackSnapRange : 0f;
		float dist         = ownerPos.DistanceTo(targetPos);
		float targetOffset = dist <= snapRange ? attackViewAngleOffset : 0f;
		_attackViewOffset  = MathU.MoveTowards(_attackViewOffset, targetOffset, attackViewLerpSpeed * GameTime.Delta);
		targetDegrees     += _attackViewOffset;

		degrees = MathU.MoveTowardsAngle(degrees, targetDegrees, targetingOrbitSpeed * GameTime.Delta);

		// Movement reference uses the pre-offset owner→target direction (so stick stays target-relative).
		Vector3 toTarget = -camDir;                          // owner → target (flat)
		cameraStateWapper.MovementForward = toTarget;
		cameraStateWapper.MovementRight   = toTarget.Cross(Vector3.Up); // forward × up = right (Godot)
	}

	void UpdateOffScreenIndicator()
	{
		if (_currentTarget == null || _currentTarget.character == null || cursor == null)
		{ if (cursor != null) cursor.Visible = false; return; }

		SetCursorSymbol(_currentTarget.ActiveSymbol);

		Vector3 targetWorldPos = _currentTarget.character.GlobalPosition + CursorOffset;
		bool    behind  = camera.IsPositionBehind(targetWorldPos);
		Vector2 vpSize  = camera.GetViewport().GetVisibleRect().Size;
		Vector2 px      = camera.UnprojectPosition(targetWorldPos);
		Vector2 vp      = new Vector2(px.X / vpSize.X, 1f - px.Y / vpSize.Y); // bottom-up like Unity

		bool onScreen = !behind && vp.X >= 0f && vp.X <= 1f && vp.Y >= 0f && vp.Y <= 1f;
		cursor.Visible = true;

		if (onScreen)
		{
			cursor.GlobalPosition = targetWorldPos;
			cursor.Scale          = Vector3.One * CursorScale;
			return;
		}

		Vector2 dir = new Vector2(vp.X - 0.5f, vp.Y - 0.5f);
		if (behind) dir = -dir;
		float maxComp = Mathf.Max(Mathf.Abs(dir.X), Mathf.Abs(dir.Y));
		if (maxComp < 0.0001f) { cursor.Visible = false; return; }

		Vector2 edgeVP = dir / maxComp;
		edgeVP *= (0.5f - offScreenEdgePadding);
		edgeVP += new Vector2(0.5f, 0.5f);
		edgeVP.X = Mathf.Clamp(edgeVP.X, 0f, 1f);
		edgeVP.Y = Mathf.Clamp(edgeVP.Y, 0f, 1f);

		cursor.GlobalPosition = ViewportToWorld(edgeVP, 4f);
		cursor.Scale          = Vector3.One * CursorScale;
	}

	// ── Side view ──────────────────────────────────────────────────────────────
	public void EnterSideView()
	{
		if (_isSideView || !_isTargeting || _currentTarget == null) return;
		_isSideView   = true;
		_sideViewSign = -1;

		Vector3 playerPos = playerTransform.GlobalPosition;
		Vector3 targetPos = _currentTarget.character.GlobalPosition;
		Vector3 toTarget  = new Vector3(targetPos.X - playerPos.X, 0f, targetPos.Z - playerPos.Z);
		_lastFightAxis = toTarget.LengthSquared() > 0.01f ? toTarget.Normalized() : Vector3.Forward;

		if (camera != null) { _defaultFOV = camera.Fov; camera.Fov = sideViewFOV; _sideViewSmoothPos = camera.GlobalPosition; }
	}

	public void ExitSideView()
	{
		if (!_isSideView) return;
		_isSideView          = false;
		_sideViewLookOffsetY = 0f;
		if (camera != null) camera.Fov = _defaultFOV;
	}

	void FightingSide()
	{
		if (_currentTarget == null || _currentTarget.character == null) { ExitSideView(); return; }

		Vector3 playerPos = playerTransform.GlobalPosition;
		Vector3 targetPos = _currentTarget.character.GlobalPosition;
		Vector3 toTarget  = new Vector3(targetPos.X - playerPos.X, 0f, targetPos.Z - playerPos.Z);
		if (toTarget.LengthSquared() < 0.01f) return;

		Vector3 fightAxis  = toTarget.Normalized();
		float   separation = toTarget.Length();

		if (fightAxis.Dot(_lastFightAxis) < 0f) _sideViewSign *= -1;
		_lastFightAxis = fightAxis;

		Vector3 sideDir  = new Vector3(-fightAxis.Z, 0f, fightAxis.X) * _sideViewSign;
		Vector3 midpoint = (playerPos + targetPos) * 0.5f;
		float   camDist  = Mathf.Clamp(separation * sideViewDistanceMultiplier, sideViewMinDistance, sideViewMaxDistance);
		Vector3 targetCamPos = midpoint + sideDir * camDist + Vector3.Up * sideViewHeight;

		_sideViewSmoothPos = _sideViewSmoothPos.Lerp(targetCamPos, sideViewSmoothing * GameTime.Delta);
		camera.GlobalPosition = _sideViewSmoothPos;

		Vector3 baseLookTarget = midpoint + Vector3.Up * 1.5f;
		if (camera.GlobalPosition.DistanceTo(baseLookTarget) > 0.001f) camera.LookAt(baseLookTarget, Vector3.Up);

		Vector3 lowerFeetRef = playerPos.Y <= targetPos.Y ? playerPos : targetPos;
		float   vpSizeY      = camera.GetViewport().GetVisibleRect().Size.Y;
		float   currentFeetVP = 1f - camera.UnprojectPosition(lowerFeetRef).Y / vpSizeY;

		float dist          = _sideViewSmoothPos.DistanceTo(baseLookTarget);
		float halfFrustumH  = Mathf.Tan(Mathf.DegToRad(camera.Fov) * 0.5f);
		float targetLookOffsetY = (currentFeetVP - sideViewHUDClearance) * 2f * dist * halfFrustumH;

		_sideViewLookOffsetY = Mathf.Lerp(_sideViewLookOffsetY, targetLookOffsetY, sideViewFramingSpeed * GameTime.Delta);

		Vector3 finalLook = baseLookTarget + Vector3.Up * _sideViewLookOffsetY;
		if (camera.GlobalPosition.DistanceTo(finalLook) > 0.001f) camera.LookAt(finalLook, Vector3.Up);

		cameraLocation.GlobalPosition = camera.GlobalPosition;
		cameraLocation.GlobalRotation = new Vector3(0f, camera.GlobalRotation.Y, 0f);
		cameraStateWapper.FightAxis   = fightAxis;
	}

	// ── Follow aim-lock acquisition ────────────────────────────────────────────
	void UpdateFollowAimTarget()
	{
		LocalPlayerManager newTarget = null;
		float   closestDist = float.MaxValue;
		Vector2 vpSize      = camera.GetViewport().GetVisibleRect().Size;

		foreach (var p in LocalPlayerManager.ActivePlayers)
		{
			if (p == _owner || p.character == null) continue;
			Vector3 wp = p.character.GlobalPosition + Vector3.Up * 1f;
			if (camera.IsPositionBehind(wp)) continue;

			Vector2 px = camera.UnprojectPosition(wp);
			Vector2 vp = new Vector2(px.X / vpSize.X, 1f - px.Y / vpSize.Y);
			if (Mathf.Abs(vp.X - 0.5f) > aimCenterX) continue;
			if (Mathf.Abs(vp.Y - 0.5f) > aimCenterY) continue;

			float d = camera.GlobalPosition.DistanceTo(wp);
			if (d < closestDist) { closestDist = d; newTarget = p; }
		}

		if (newTarget != _followAimTarget)
		{
			ClearFollowAimHighlight();
			_followAimTarget = newTarget;
			if (_followAimTarget != null)
			{
				_followAimRenderers = NodeUtil.GetComponentsInChildren<MeshInstance3D>(_followAimTarget.character);
				ApplyFollowAimHighlight();
			}
			cameraTarget = _followAimTarget?.character;
			isHit        = _followAimTarget != null;
			playerEvents.OnOrbitTargetChanged?.Invoke(_followAimTarget, _followAimTarget != null);
			if (_followAimTarget != null) SetCursorSymbol(_followAimTarget.ActiveSymbol);
		}
	}

	void UpdateFollowAimCursor()
	{
		if (_followAimTarget == null || _followAimTarget.character == null) { if (cursor != null) cursor.Visible = false; return; }
		if (cursor != null)
		{
			SetCursorSymbol(_followAimTarget.ActiveSymbol);
			cursor.Visible        = true;
			cursor.GlobalPosition = _followAimTarget.character.GlobalPosition + CursorOffset;
			cursor.Scale          = Vector3.One * CursorScale;
		}
	}

	void ApplyFollowAimHighlight()
	{
		if (_followAimRenderers == null) return;
		_aimHighlightMat ??= MakeHighlightMat(Colors.Cyan);
		foreach (var r in _followAimRenderers) if (r != null) r.MaterialOverlay = _aimHighlightMat;
	}

	void ClearFollowAimHighlight()
	{
		if (_followAimRenderers == null) return;
		foreach (var r in _followAimRenderers) if (r != null) r.MaterialOverlay = null;
		_followAimRenderers = null;
	}

	// ── Cursor symbol ──────────────────────────────────────────────────────────
	public void SetCursorSymbol(PlayerSymbolEntry entry)
	{
		if (cursor == null || entry == null) return;
		_currentSymbolEntry = entry;

		Sprite3D sprite = cursor as Sprite3D ?? NodeUtil.GetComponentInChildren<Sprite3D>(cursor);
		if (sprite != null)
		{
			if (entry.sprite != null) sprite.Texture = entry.sprite;
			sprite.Modulate = entry.symbolColor;
		}
	}

	public void RefreshCursorAppearance()
	{
		if (_currentSymbolEntry != null) SetCursorSymbol(_currentSymbolEntry);
	}

	// ── Godot helpers ──────────────────────────────────────────────────────────
	Vector3 ViewportToWorld(Vector2 vpNorm, float depth)
	{
		Vector2 vpSize = camera.GetViewport().GetVisibleRect().Size;
		Vector2 px     = new Vector2(vpNorm.X * vpSize.X, (1f - vpNorm.Y) * vpSize.Y);
		return camera.ProjectPosition(px, depth);
	}

	static void AddBodyRids(Node3D root, Godot.Collections.Array<Rid> list)
	{
		if (root is CollisionObject3D co) list.Add(co.GetRid());
		foreach (var child in root.GetChildren())
			if (child is Node3D n) AddBodyRids(n, list);
	}

	static StandardMaterial3D MakeHighlightMat(Color c) => new StandardMaterial3D
	{
		ShadingMode     = BaseMaterial3D.ShadingModeEnum.Unshaded,
		Transparency    = BaseMaterial3D.TransparencyEnum.Alpha,
		AlbedoColor     = new Color(c.R, c.G, c.B, 0.35f),
		EmissionEnabled = true,
		Emission        = c,
	};
}
