using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// A single attack move. Ported from Unity.
///
/// FRAME-PERFECT DESIGN (unchanged): integer frame counters at 60 Hz, three phases
/// Startup → Active → Recovery, manual hit detection each active frame (not signals).
///
/// GODOT ADAPTATIONS
/// ─────────────────
/// • Hitbox prefab + BoxCollider → a code-built MeshInstance3D (BoxMesh) child of the
///   character. It is never a live physics body — it only supplies a transform + size
///   for the manual overlap query and a colored visual for debugging.
/// • Physics.OverlapBoxNonAlloc → PhysicsDirectSpaceState3D.IntersectShape with a
///   BoxShape3D (zero standing PhysX cost, same as the disabled-collider trick).
/// • Lunge Physics.SphereCast → a forward ray query (IntersectRay). ⚠ This loses the
///   sphere radius and is an approximation; CharacterBody3D.MoveAndSlide already blocks
///   wall penetration, so the manual stop is partly redundant — verify in-engine.
/// • hitbox color states (blue/red/green) → StandardMaterial3D.AlbedoColor.
///
/// ⚠ LUNGE↔MOVEMENT: SetLungeDirection writes rb.Velocity, but MovementController also
/// writes rb.Velocity every physics frame (and only it calls MoveAndSlide). The Unity
/// ordering that made the lunge "win" must be re-verified in Godot; this is a known
/// playtest item.
/// </summary>
public class Attack
{
    // ── Identity ──────────────────────────────────────────────────────────
    LocalPlayerManager player;
    HitBoxTriggerEvents.AttackType _type;
    AttackData _data;

    // ── Frame counters (60 fps) ───────────────────────────────────────────
    int _startupFrames, _startupFrame;
    int _animationFrames, _animationFrame;
    int _coolDownFrames, _coolDownFrame;

    private const int HitStunFrames = 4;
    int _hitStunFrame;

    // ── Hitbox / hurtbox ──────────────────────────────────────────────────
    MeshInstance3D hitBox;
    Vector3 _hitBoxSize;              // full box size for the overlap query
    Node3D  hurtBox;
    bool    isHitConfirm = false;
    StandardMaterial3D hitboxMaterial;
    bool isAttackAnimationActive = false;
    Action onAttack;
    Action onMiss;

    // ── State ─────────────────────────────────────────────────────────────
    bool isStartupActive    = false;
    bool isCoolDownActive   = false;
    bool _isHitConfirmPause = false;
    Color tempColor;
    float _pushBackDistance;
    float _pushBackSpeed;

    PlayerEvents playerEvents;

    // ── Lunge ─────────────────────────────────────────────────────────────
    Node3D         _character;
    CharacterBody3D _rb;
    float   _lungeDistance;
    float   _lungePerFrame;
    Vector3 _lungeDirection;
    bool    _lungeActive;
    float   _lungeRadius;
    uint    _obstructionMask;
    float   _lungeStopGap = 0.3f;

    // ── Timestamped animation callbacks ───────────────────────────────────
    public List<(double time, Action action)> onAnimation = new();

    // ── Properties ────────────────────────────────────────────────────────
    public double AnimationLength
    {
        get => _animationFrames / 60.0;
        set => _animationFrames = Mathf.RoundToInt((float)value * 60f);
    }

    public double CoolDownLength
    {
        get => _coolDownFrames / 60.0;
        set => _coolDownFrames = Mathf.RoundToInt((float)value * 60f);
    }

    public MeshInstance3D Hitbox => hitBox;
    public HitBoxTriggerEvents.AttackType Type { get => _type; set => _type = value; }
    public AttackData Data => _data;
    public bool IsAttackActive    => isAttackAnimationActive;
    public bool IsHitConfirmPause => _isHitConfirmPause;

    public double StartupProgress   => _startupFrames   == 0 ? 1.0 : (double)_startupFrame   / _startupFrames;
    public double AnimationProgress => _animationFrames == 0 ? 1.0 : (double)_animationFrame / _animationFrames;
    public double CoolDownProgress  => _coolDownFrames  == 0 ? 1.0 : (double)_coolDownFrame  / _coolDownFrames;

    // ── Execute (called from physics tick by AttackController) ────────────
    public void Execute(uint playerMask)
    {
        if (_isHitConfirmPause)
        {
            TickHitStunPause();
            return;
        }

        ActivateAttack();

        if (isCoolDownActive && isStartupActive)
        {
            DoWhileStartupIsActive();
            DeactivateStartupOnComplete();
        }
        else if (isCoolDownActive && isAttackAnimationActive)
        {
            DoWhileAnimationIsActive(playerMask);
            DeactivateAttackAnimationOnComplete();
        }
        else if (isCoolDownActive && !isStartupActive && !isAttackAnimationActive)
        {
            DoWhileAttackBlockIsActive();
            DeactivateAttackBlockOnComplete();
        }
    }

    // ── Hit-stun pause ────────────────────────────────────────────────────
    private void TickHitStunPause()
    {
        if (hitboxMaterial != null) hitboxMaterial.AlbedoColor = Colors.Green;
        _hitStunFrame++;

        if (_hitStunFrame < HitStunFrames) return;

        if (hitboxMaterial != null) hitboxMaterial.AlbedoColor = tempColor;
        _hitStunFrame      = 0;
        _isHitConfirmPause = false;
        playerEvents.OnHitConfirmPauseEnd?.Invoke((hitBox, hurtBox));

        if (hurtBox == null) return;

        PlayerDetection pd = NodeUtil.GetComponentInChildren<PlayerDetection>(hurtBox)
                             ?? NodeUtil.GetComponentInParent<PlayerDetection>(hurtBox);
        if (pd == null) return;

        switch (_type)
        {
            case HitBoxTriggerEvents.AttackType.Launcher:
                pd.PlayerEvents.OnPush?.Invoke(Vector3.Up * _pushBackSpeed);
                break;
            default:
                Vector3 dir = (hurtBox.GlobalPosition - hitBox.GlobalPosition).Normalized();
                dir.Y = 0f;
                pd.PlayerEvents.OnPush?.Invoke(dir * _pushBackSpeed);
                break;
        }
    }

    // ── Attack lifecycle ──────────────────────────────────────────────────
    private void ActivateAttack()
    {
        if (CoolDownProgress != 0.0 || AnimationProgress != 0.0) return;
        if (isStartupActive || isAttackAnimationActive) return;

        isCoolDownActive = true;
        onAttack?.Invoke();

        if (_startupFrames > 0)
        {
            isStartupActive = true;
            if (hitBox != null) hitBox.Visible = true;
            if (hitboxMaterial != null) hitboxMaterial.AlbedoColor = Colors.Blue;
        }
        else
        {
            isAttackAnimationActive = true;
            if (hitBox != null) hitBox.Visible = true;
            if (hitboxMaterial != null) hitboxMaterial.AlbedoColor = Colors.Red;
        }
    }

    private void DoWhileStartupIsActive()
    {
        _startupFrame++;
        ApplyLunge();
    }

    private void ApplyLunge()
    {
        if (!_lungeActive || _rb == null || _lungePerFrame == 0f || hitBox == null) return;

        var space = hitBox.GetWorld3D().DirectSpaceState;
        Vector3 origin = _character.GlobalPosition + Vector3.Up * (_lungeRadius + 0.05f);
        var rayQuery = PhysicsRayQueryParameters3D.Create(
            origin, origin + _lungeDirection * _lungePerFrame, _obstructionMask);
        rayQuery.Exclude = new Godot.Collections.Array<Rid> { _rb.GetRid() };

        var hit = space.IntersectRay(rayQuery);
        if (hit.Count > 0)
        {
            float hitDist = origin.DistanceTo(hit["position"].AsVector3());
            float safeDistance = Mathf.Max(0f, hitDist - _lungeStopGap);
            if (safeDistance > 0f)
                _rb.GlobalPosition += _lungeDirection * safeDistance;

            _rb.Velocity = new Vector3(0f, _rb.Velocity.Y, 0f);
            _lungeActive = false;
            return;
        }
        // Clear path: velocity carries the lunge. NOTE lunge↔movement ordering (see header).
    }

    private void DeactivateStartupOnComplete()
    {
        if (_startupFrame < _startupFrames) return;
        isStartupActive         = false;
        isAttackAnimationActive = true;
        if (hitboxMaterial != null) hitboxMaterial.AlbedoColor = Colors.Red;

        if (_lungeActive && _rb != null)
        {
            _rb.Velocity = new Vector3(0f, _rb.Velocity.Y, 0f);
            _lungeActive = false;
        }
    }

    private void DoWhileAnimationIsActive(uint playerMask)
    {
        _animationFrame++;
        CheckHits(playerMask);

        for (int i = 0; i < onAnimation.Count; i++)
            if (AnimationProgress >= onAnimation[i].time)
                onAnimation[i].action?.Invoke();
    }

    private void DoWhileAttackBlockIsActive() => _coolDownFrame++;

    private void DeactivateAttackAnimationOnComplete()
    {
        if (_animationFrame < _animationFrames) return;
        playerEvents.OnAnimationComplete?.Invoke();
        if (hitBox != null) hitBox.Visible = false;
        isAttackAnimationActive = false;
    }

    private void DeactivateAttackBlockOnComplete()
    {
        if (_coolDownFrame < _coolDownFrames) return;
        playerEvents.OnCoolDownComplete?.Invoke();
        isCoolDownActive = false;
    }

    // ── Hit detection ─────────────────────────────────────────────────────
    private void CheckHits(uint playerMask)
    {
        if (isHitConfirm || hitBox == null) return;

        var space = hitBox.GetWorld3D().DirectSpaceState;
        var shape = new BoxShape3D { Size = _hitBoxSize };
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape             = shape,
            Transform         = new Transform3D(hitBox.GlobalBasis.Orthonormalized(), hitBox.GlobalPosition),
            CollisionMask     = playerMask,
            CollideWithBodies = true,
            CollideWithAreas  = false,
        };

        var results = space.IntersectShape(query, 16);
        for (int i = 0; i < results.Count; i++)
        {
            Node3D collider = results[i]["collider"].As<Node3D>();
            if (collider == null) continue;

            PlayerDetection pd = NodeUtil.GetComponentInChildren<PlayerDetection>(collider)
                                 ?? NodeUtil.GetComponentInParent<PlayerDetection>(collider);
            if (pd == null || pd.Player == player) continue;

            hurtBox   = collider;
            tempColor = hitboxMaterial != null ? hitboxMaterial.AlbedoColor : Colors.Red;

            GD.Print($"[Attack] Hit {hurtBox.Name} — type={_type}  frame={_animationFrame}/{_animationFrames}");
            isHitConfirm = _isHitConfirmPause = true;
            playerEvents.OnHitConfirm?.Invoke((hitBox, hurtBox));
            pd.PlayerEvents.OnDamageReceived?.Invoke(new Damage(10, Damage.AttackType.Smash));
            break;
        }
    }

    // ── Public API ────────────────────────────────────────────────────────
    public void SetLungeDirection(Vector3 direction)
    {
        _lungeDirection = direction;
        _lungeActive    = _lungePerFrame > 0f;

        if (_lungeActive && _rb != null)
        {
            float speed = _lungePerFrame * 60f;
            _rb.Velocity = new Vector3(_lungeDirection.X * speed, _rb.Velocity.Y, _lungeDirection.Z * speed);
        }
    }

    public void ResetAttack()
    {
        _startupFrame = _animationFrame = _coolDownFrame = _hitStunFrame = 0;
        isStartupActive = isCoolDownActive = isAttackAnimationActive = isHitConfirm = _isHitConfirmPause = false;
        hurtBox = null;
        if (hitBox != null) hitBox.Visible = false;

        if (_lungeActive && _rb != null)
            _rb.Velocity = new Vector3(0f, _rb.Velocity.Y, 0f);
        _lungeActive = false;
    }

    public void SetOnAnimaiton(Action action, double animationProgress)
        => onAnimation.Add((animationProgress, action));

    public void SetOnAnimaiton(Action action, float time = 0f)
    {
        double animLength = _animationFrames / 60.0;
        double progress   = animLength == 0 ? 0.0 : Clamp(time, 0, animLength) / animLength;
        onAnimation.Add((progress, action));
    }

    public void AddTimeStampedAction(int frame, Action action)
    {
        double progress = _animationFrames == 0 ? 0.0 : (double)frame / _animationFrames;
        onAnimation.Add((progress, action));
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────
    public void Initialize(
        LocalPlayerManager player,
        Node3D             character,
        string             hitBoxName,
        Vector3            hitBoxPosition,
        Vector3            hitBoxEulerAngle,
        Vector3            hitBoxScale,
        double             startupLength,
        double             animationLength,
        double             attackBlockLength,
        float              pushBackDistance,
        float              lungeDistance,
        HitBoxTriggerEvents.AttackType type,
        PlayerEvents       playerEvents)
    {
        this.player       = player;
        this.playerEvents = playerEvents;
        _pushBackDistance = pushBackDistance;
        _type             = type;

        const float pushBackTravelDuration = 0.2f;
        _pushBackSpeed = pushBackDistance > 0f ? pushBackDistance / pushBackTravelDuration : 0f;

        _character       = character;
        _lungeDistance   = lungeDistance;
        _rb              = character as CharacterBody3D;
        _obstructionMask = PhysicsLayers.World | PhysicsLayers.Player;
        _lungeRadius     = 0.3f; // NOTE: derived from CapsuleCollider in Unity — tune in-engine

        // Build the hitbox visual (also the query transform + size source).
        hitBox = new MeshInstance3D();
        hitBox.Mesh = new BoxMesh { Size = hitBoxScale };
        hitboxMaterial = new StandardMaterial3D { AlbedoColor = Colors.Blue };
        hitBox.MaterialOverride = hitboxMaterial;
        character.AddChild(hitBox);
        hitBox.Name           = hitBoxName;
        hitBox.Position       = hitBoxPosition;
        hitBox.RotationDegrees = hitBoxEulerAngle;
        hitBox.Visible        = false;
        _hitBoxSize           = hitBoxScale;

        _startupFrames   = Mathf.RoundToInt((float)startupLength    * 60f);
        _animationFrames = Mathf.RoundToInt((float)animationLength  * 60f);
        _coolDownFrames  = Mathf.RoundToInt((float)attackBlockLength * 60f);

        _lungePerFrame = (_startupFrames > 0 && _lungeDistance > 0f)
            ? _lungeDistance / _startupFrames
            : 0f;
    }

    public void Initialize(AttackData data, LocalPlayerManager player,
                           Node3D character, PlayerEvents playerEvents)
    {
        _data = data;
        Initialize(
            player, character,
            data.hitBoxName, data.hitBoxPosition, data.hitBoxEuler, data.hitBoxScale,
            data.startupLength, data.animationLength, data.attackBlockLength,
            data.pushBackDistance, data.lungeDistance, data.type, playerEvents);
    }

    public void Deactivate()
    {
        playerEvents = null;
        if (hitBox != null) { hitBox.QueueFree(); hitBox = null; }
    }

    private static double Clamp(double value, double min, double max)
        => value <= min ? min : value >= max ? max : value;
}
