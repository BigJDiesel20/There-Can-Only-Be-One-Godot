using Godot;
using System;

/// <summary>
/// Drives a player's locomotion, jump game-feel, push/launch, prone, and attack
/// snap-to-target. Ported from Unity.
///
/// PHYSICS ADAPTATION (Rigidbody → CharacterBody3D)
/// ────────────────────────────────────────────────
/// • Unity drove a Rigidbody by writing linearVelocity every FixedUpdate with custom
///   gravity. The Godot equivalent is a CharacterBody3D: we set Velocity and call
///   MoveAndSlide() once at the end of the physics tick. Gravity stays custom (driven
///   through Jump()), exactly like the original.
/// • rb.MoveRotation(Quaternion.Euler(deg)) → set rb.Rotation (radians) via SetEulerDeg.
/// • rb.transform.eulerAngles → rb.RotationDegrees; rb.rotation (quat) → rb.Quaternion.
/// • rb.transform.TransformDirection(v) → rb.GlobalBasis * v.
/// • Ground check: Unity SphereCast → CharacterBody3D.IsOnFloor() (after MoveAndSlide).
/// • Halt()/Resume(): CharacterBody3D is always kinematic, so a _halted flag freezes
///   the tick instead of toggling Rigidbody.isKinematic.
///
/// ⚠ HANDEDNESS: Unity forward = +Z, Godot forward = −Z. Spots that build directions
/// from local Z or Vector3.Forward are marked NOTE and must be verified in-engine.
/// All facing/movement math is otherwise a structural 1:1 port and will need playtesting.
/// </summary>
public class MovementController
{
    public PlayerInput gamePad;
    public CharacterBody3D rb;
    public float signX;
    public float signY;
    public Node3D camera;          // retained for parity; movement now uses cameraStateWrapper
    public Node3D cameraLocation;  // retained for parity
    CameraStateWrapper cameraStateWrapper;
    public float pitch = 0;
    public float yaw = 0;
    public Node3D character;

    Vector3 currentForceDirection = Vector3.Zero;
    Vector3 forceDirection = Vector3.Zero;
    bool isPushed = false;
    bool isLaunched = false;
    (bool x, bool z) isZero = (true, true);
    int isPushCompleted = 0;
    Vector3 currentRoation = Vector3.Zero;
    Vector3 fallPosition = Vector3.Zero;
    float startTime = 0;
    float fallStartTime = 0;
    Vector3 fallStartRotation = Vector3.Zero;
    float fallDuration = 0.5f;
    float standUpDuration = 1f;

    private bool isInitialized = false;
    public bool IsInitialized => isInitialized;
    bool _isHitConfirmPause = false;
    private PlayerEvents playerEvents;
    public bool debugLogs = false;

    private bool _halted = false;

    // Movement speeds per camera mode
    public float orbitMoveSpeed    = 20f;
    public float followMoveSpeed   =  5f;
    public float sideViewMoveSpeed =  5f;

    // Attack snap-to-target
    private bool       isAttackSnapping = false;
    private Quaternion attackSnapTarget = Quaternion.Identity;
    private float      _snapThreshold   = 60f;   // arc degrees — mirrored from AttackController via event
    public  float      attackSnapSpeed  = 720f;  // degrees per second

    // ── Edge-triggered jump input (read in Update, consumed in physics tick) ──
    void OnInputUpdate()
    {
        _jumpPressed  |= gamePad.GetButtonDown("A");
        _jumpReleased |= gamePad.GetButtonUp("A");
    }

    public void OnUpdate()
    {
        if (_halted || rb == null) return;

        GroundDetection();

        switch (cameraStateWrapper.CurrentState)
        {
            case CameraStateWrapper.CameraState.Orbit:        Orbit();            break;
            case CameraStateWrapper.CameraState.Follow:       Follow();           break;
            case CameraStateWrapper.CameraState.FightingSide: FightingSideMove(); break;
        }

        // Attack snap-to-target: runs after movement so it always wins the rotation.
        if (isAttackSnapping)
        {
            Quaternion next = QuatRotateTowards(rb.Quaternion, attackSnapTarget, attackSnapSpeed * GameTime.Delta);
            rb.Quaternion = next;
        }

        rb.MoveAndSlide();
    }

    public void Orbit()
    {
        if (!isProne)
        {
            if (!_isHitConfirmPause)
            {
                signX = orbitMoveSpeed * gamePad.GetAxis("Move Horizontal");
                signY = orbitMoveSpeed * gamePad.GetAxis("Move Vertical");

                // Movement is relative to the camera's pre-combat-framing reference frame.
                Vector3 relativeDirection = signY * cameraStateWrapper.MovementForward + signX * cameraStateWrapper.MovementRight;

                if (isZero.x == false)
                {
                    currentForceDirection.X = MathU.MoveTowards(currentForceDirection.X, 0, Mathf.Abs(forceDirection.X) * 2 * GameTime.Delta);
                    if (currentForceDirection.X == 0) isZero.x = true;
                }

                if (isZero.z == false)
                {
                    currentForceDirection.Z = MathU.MoveTowards(currentForceDirection.Z, 0, Mathf.Abs(forceDirection.Z) * 2 * GameTime.Delta);
                    if (currentForceDirection.Z == 0) isZero.z = true;
                }

                if (debugLogs) GD.Print($"isPushCompleted >= 2: {isPushCompleted >= 2} isPushed: {isPushed}");
                if (isZero == (true, true)) isPushed = false;

                if (!isPushed)
                {
                    float standUpRecoveryTime = MathU.Clamp01((GameTime.Time - startTime) / standUpDuration);

                    if (standUpRecoveryTime < 1f)
                    {
                        float targetY = (relativeDirection != Vector3.Zero)
                            ? Mathf.Atan2(relativeDirection.X, relativeDirection.Z) * MathU.Rad2Deg
                            : currentRoation.Y;
                        SetEulerDeg(
                            MathU.SmoothStep(fallPosition.X, 0, standUpRecoveryTime),
                            MathU.SmoothStep(fallPosition.Y, targetY, standUpRecoveryTime),
                            MathU.SmoothStep(fallPosition.Z, 0, standUpRecoveryTime));
                        rb.Velocity = new Vector3(0, Jump(), 0);
                    }
                    else
                    {
                        if (relativeDirection != Vector3.Zero)
                        {
                            // Break the snap lock if the stick deviates beyond the arc threshold.
                            if (isAttackSnapping)
                            {
                                Vector3 snapForward = attackSnapTarget * Vector3.Forward; // NOTE handedness
                                snapForward.Y = 0f;
                                float stickAngle = Mathf.RadToDeg(relativeDirection.Normalized().AngleTo(snapForward.Normalized()));
                                if (stickAngle > _snapThreshold) isAttackSnapping = false;
                            }

                            if (!isAttackSnapping)
                                SetEulerDeg(0, Mathf.Atan2(relativeDirection.X, relativeDirection.Z) * MathU.Rad2Deg, 0);
                        }

                        // Leaving the ground always restores full movement.
                        if (isAttackSnapping && jumpState != JumpState.Grounded) isAttackSnapping = false;

                        rb.Velocity = isAttackSnapping
                            ? new Vector3(0, Jump(), 0)
                            : relativeDirection + new Vector3(0, Jump(), 0);
                    }
                }
                else
                {
                    SetEulerDeg(currentRoation);
                    rb.Velocity = new Vector3(currentForceDirection.X, Jump(), currentForceDirection.Z);
                }
            }
            else
            {
                SetEulerDeg(currentRoation);
                rb.Velocity = Vector3.Zero;
            }
        }
        else
        {
            float fallProgress = MathU.Clamp01((GameTime.Time - fallStartTime) / fallDuration);
            SetEulerDeg(
                MathU.SmoothStep(fallStartRotation.X, -90, fallProgress),
                currentRoation.Y,
                MathU.SmoothStep(fallStartRotation.Z, 0, fallProgress));
            fallPosition = rb.RotationDegrees;
            if (fallPosition.X > 180f) fallPosition.X -= 360f;
            rb.Velocity = new Vector3(0, Jump(), 0);
        }
    }

    public void Follow()
    {
        if (!isProne)
        {
            if (!_isHitConfirmPause)
            {
                signX = followMoveSpeed * gamePad.GetAxis("Move Horizontal");
                signY = followMoveSpeed * gamePad.GetAxis("Move Vertical");

                if (!cameraStateWrapper.IsFollowAimLock)
                    yaw -= 200f * gamePad.GetAxis("Right Stick X") * GameTime.FixedDelta;

                if (!isPushed)
                {
                    rb.Velocity = rb.GlobalBasis * new Vector3(signX, Jump(), -signY); // Godot −Z = forward
                    SetEulerDeg(0, yaw, 0);
                }
                else
                {
                    rb.Velocity = new Vector3(currentForceDirection.X, Jump(), currentForceDirection.Z);
                    SetEulerDeg(currentRoation);
                }
            }
            else
            {
                SetEulerDeg(currentRoation);
                rb.Velocity = Vector3.Zero;
            }
        }
    }

    void FightingSideMove()
    {
        if (!isProne)
        {
            if (!_isHitConfirmPause)
            {
                Vector3 fightAxis = cameraStateWrapper.FightAxis;
                if (fightAxis.LengthSquared() > 0.01f)
                    yaw = Mathf.Atan2(fightAxis.X, fightAxis.Z) * MathU.Rad2Deg;

                signX = sideViewMoveSpeed * gamePad.GetAxis("Move Horizontal");
                signY = sideViewMoveSpeed * gamePad.GetAxis("Move Vertical");

                if (!isPushed)
                {
                    rb.Velocity = rb.GlobalBasis * new Vector3(-signY, Jump(), signX); // NOTE handedness
                    SetEulerDeg(0, yaw, 0);
                }
                else
                {
                    rb.Velocity = new Vector3(currentForceDirection.X, Jump(), currentForceDirection.Z);
                    SetEulerDeg(currentRoation);
                }
            }
            else
            {
                SetEulerDeg(currentRoation);
                rb.Velocity = Vector3.Zero;
            }
        }
    }

    public void Initialize(LocalPlayerManager player, Node3D camera, Node3D cameraLocation,
                           CameraStateWrapper cameraStateWrapper, Node3D charater,
                           ref bool isMovementInitialized, PlayerEvents playerEvents)
    {
        gamePad = player.playerInput;
        rb = charater as CharacterBody3D;
        // Rigidbody flags (useGravity/continuous/interpolate) have no direct CharacterBody3D
        // equivalent — gravity is custom and CharacterBody3D is always kinematic.
        this.camera = camera;
        this.cameraLocation = cameraLocation;
        this.character = charater;
        this.cameraStateWrapper = cameraStateWrapper;
        isMovementInitialized = isInitialized = true;

        this.playerEvents = playerEvents;
        this.playerEvents.OnUpdate      += OnInputUpdate; // edge-triggered input (Update rate)
        this.playerEvents.OnFixedUpdate += OnUpdate;      // physics (FixedUpdate rate)
        this.playerEvents.OnHitConfirm += OnHitConfirm;
        this.playerEvents.OnHitConfirmPauseEnd += OnHitConfirmPauseEnd;
        this.playerEvents.OnPush += OnPush;
        this.playerEvents.OnInvulnerabilityActive += OnInvulnerabilityActive;
        this.playerEvents.OnProneActive           += OnProneActive;
        this.playerEvents.OnAttackRotate          += OnAttackRotate;
        this.playerEvents.OnAttackEnd             += OnAttackEnd;
    }

    /// <summary>Freezes the character (CharacterBody3D analogue of going kinematic).</summary>
    public void Halt()
    {
        if (rb == null) return;
        _halted = true;
        rb.Velocity = Vector3.Zero;

        currentForceDirection = Vector3.Zero;
        forceDirection        = Vector3.Zero;
        isPushed              = false;
        isLaunched            = false;
        isZero                = (true, true);
        _jumpPressed          = false;
        _jumpReleased         = false;
        charge                = 0f;
        _jumpBufferFrame      = 0;
        _coyoteFrame          = 0;
        jumpState             = JumpState.Grounded;
        isAttackSnapping      = false;
    }

    /// <summary>Re-enables movement after a Halt().</summary>
    public void Resume()
    {
        if (rb == null) return;
        _halted = false;
        rb.Velocity = Vector3.Zero;
    }

    public void Deactivate()
    {
        gamePad = null;
        rb = null;
        this.playerEvents.OnUpdate      -= OnInputUpdate;
        this.playerEvents.OnFixedUpdate -= OnUpdate;
        this.playerEvents.OnHitConfirm -= OnHitConfirm;
        this.playerEvents.OnHitConfirmPauseEnd -= OnHitConfirmPauseEnd;
        this.playerEvents.OnPush -= OnPush;
        this.playerEvents.OnInvulnerabilityActive -= OnInvulnerabilityActive;
        this.playerEvents.OnProneActive           -= OnProneActive;
        this.playerEvents.OnAttackRotate          -= OnAttackRotate;
        this.playerEvents.OnAttackEnd             -= OnAttackEnd;
        this.playerEvents = null;
    }

    public float height     = 2f;  // auto-set from model bounds at build time (uncharged = 1×, full charge = 2×)
    public float chargeRate = 3f;
    public float charge     = 0f;
    public float launchForce = 0;
    public bool isGrounded;
    public bool isJumping = false;
    public float gravity = 14f;
    public enum JumpState { Grounded, Jumping, Falling, Launched }
    public JumpState jumpState;
    private bool isInvulnerabilityActive;
    private bool isProne;

    // ── Jump game-feel ────────────────────────────────────────────────
    private const int   CoyoteFrames        = 6;
    private const int   JumpBufferFrames    = 6;
    private const float MinJumpVelocity      = 3f;
    private const float ApexThreshold        = 1.0f;
    private const float ApexGravityScale     = 0.65f;
    private const float PostApexGravityScale = 4f;
    private int  _coyoteFrame     = 0;
    private int  _jumpBufferFrame = 0;
    private bool _hadApexHang     = false;

    private bool _jumpPressed  = false;
    private bool _jumpReleased = false;

    public float Jump()
    {
        bool jumpHeld = gamePad.GetButton("A");
        bool jumpDown = _jumpPressed;
        bool jumpUp   = _jumpReleased;

        if (jumpDown)                  _jumpBufferFrame = JumpBufferFrames;
        else if (_jumpBufferFrame > 0) _jumpBufferFrame--;

        switch (jumpState)
        {
            case JumpState.Grounded:
                if (!isProne)
                {
                    if (isGrounded) _coyoteFrame = CoyoteFrames;

                    if (isGrounded && jumpHeld)
                    {
                        charge += GameTime.FixedDelta * chargeRate;
                        charge  = MathU.Clamp01(charge);
                    }

                    if (!isGrounded && isLaunched)
                    {
                        currentForceDirection.Y = launchForce;
                        jumpState = JumpState.Launched;
                    }
                    else if (jumpUp && isGrounded && !isLaunched)
                    {
                        ExecuteJump();
                    }
                    else if (jumpDown && !isGrounded && _coyoteFrame > 0 && !isLaunched)
                    {
                        ExecuteJump();
                    }
                    else if (!isGrounded && !isLaunched)
                    {
                        if (_coyoteFrame > 0) _coyoteFrame--;
                        else                  jumpState = JumpState.Falling;
                    }
                }
                else
                {
                    if (!isGrounded) jumpState = JumpState.Falling;
                }
                break;

            case JumpState.Jumping:
                if (jumpUp && currentForceDirection.Y > MinJumpVelocity)
                    currentForceDirection.Y = MinJumpVelocity;

                bool inApex = jumpHeld && Mathf.Abs(currentForceDirection.Y) < ApexThreshold;
                if (inApex) _hadApexHang = true;
                float applyGravity = inApex ? gravity * ApexGravityScale : gravity;

                currentForceDirection.Y -= applyGravity * GameTime.FixedDelta;

                if (currentForceDirection.Y < 0) jumpState = JumpState.Falling;
                break;

            case JumpState.Launched:
                currentForceDirection.Y -= gravity * GameTime.FixedDelta;
                if (currentForceDirection.Y < 0)
                {
                    currentForceDirection.Y = 0;
                    isLaunched = false;
                    jumpState = JumpState.Falling;
                }
                break;

            case JumpState.Falling:
                float fallScale = _hadApexHang ? PostApexGravityScale : 3f;
                currentForceDirection.Y -= fallScale * gravity * GameTime.FixedDelta;

                if (currentForceDirection.Y > 0) jumpState = JumpState.Launched;

                if (isGrounded)
                {
                    currentForceDirection.Y = 0f;
                    charge    = 0f;
                    jumpState = JumpState.Grounded;
                    if (_jumpBufferFrame > 0) ExecuteJump();
                }
                break;
        }

        _jumpPressed  = false;
        _jumpReleased = false;

        return currentForceDirection.Y;
    }

    /// <summary>
    /// Shared launch logic. Peak height scales with v² so velocity = √(2·g·h·(1+charge))
    /// gives a clean 2:1 height ratio: uncharged reaches one model height, full charge 2×.
    /// </summary>
    private void ExecuteJump()
    {
        float launchVelocity = Mathf.Sqrt(2f * gravity * height * (1f + charge));
        currentForceDirection.Y = Mathf.Max(launchVelocity, MinJumpVelocity);
        charge           = 0f;
        _jumpBufferFrame = 0;
        _coyoteFrame     = 0;
        _hadApexHang     = false;
        jumpState        = JumpState.Jumping;
    }

    void GroundDetection()
    {
        // CharacterBody3D reports floor contact from the last MoveAndSlide().
        isGrounded = rb.IsOnFloor();
        if (debugLogs) GD.Print($"grounded: {isGrounded}");
    }

    public void OnHitConfirm((Node3D hitbox, Node3D hurtbox) hitInfo)
    {
        if (isProne)
        {
            _isHitConfirmPause = true;
            currentRoation.Y = rb.RotationDegrees.Y;
        }
    }

    public void OnHitConfirmPauseEnd((Node3D hitbox, Node3D hurtbox) hitInfo)
    {
        _isHitConfirmPause = false;
    }

    public void OnPush(Vector3 direction)
    {
        if (!isInvulnerabilityActive)
        {
            forceDirection.X = currentForceDirection.X = direction.X;
            forceDirection.Z = currentForceDirection.Z = direction.Z;
            launchForce = direction.Y;
            if (debugLogs) GD.Print($"direction: {direction}  currentForceDirection: {currentForceDirection}");
            isPushed = true;
            isLaunched = true;
            isZero = (false, false);
        }
    }

    void OnInvulnerabilityActive(bool isActive) => isInvulnerabilityActive = isActive;

    private void OnProneActive(bool isActive)
    {
        isProne = isActive;
        if (isProne)
        {
            fallStartTime = GameTime.Time;
            fallStartRotation = rb.RotationDegrees;
            currentRoation.Y = rb.RotationDegrees.Y;
        }
        else
        {
            currentRoation.Y = rb.RotationDegrees.Y;
            startTime = GameTime.Time;
        }
    }

    void OnAttackRotate(Quaternion targetRotation, float arcThreshold)
    {
        attackSnapTarget = targetRotation;
        _snapThreshold   = arcThreshold;
        isAttackSnapping = true;
    }

    void OnAttackEnd() => isAttackSnapping = false;

    // ── Godot helpers ─────────────────────────────────────────────────────────
    void SetEulerDeg(float x, float y, float z)
        => rb.Rotation = new Vector3(Mathf.DegToRad(x), Mathf.DegToRad(y), Mathf.DegToRad(z));

    void SetEulerDeg(Vector3 deg) => SetEulerDeg(deg.X, deg.Y, deg.Z);

    /// <summary>Quaternion equivalent of Unity's Quaternion.RotateTowards (maxDelta in degrees).</summary>
    static Quaternion QuatRotateTowards(Quaternion from, Quaternion to, float maxDegrees)
    {
        float angleDeg = Mathf.RadToDeg(from.AngleTo(to));
        if (angleDeg < 1e-4f) return to;
        float t = Mathf.Min(1f, maxDegrees / angleDeg);
        return from.Slerp(to, t);
    }
}
