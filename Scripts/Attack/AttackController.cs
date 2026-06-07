using Godot;
using System.Collections.Generic;

/// <summary>
/// Player attack state machine: input buffer, combo matching, attack selection,
/// snap-to-target, and per-tick execution. Ported from Unity (includes the
/// launcher-combo-only + new-chain guard fixes made in the Unity version).
///
/// GODOT ADAPTATIONS
/// ─────────────────
/// • Config lists are Godot.Collections.Array&lt;T&gt; (not List&lt;T&gt;).
/// • Physics.OverlapSphereNonAlloc (snap search) → space-state IntersectShape with a
///   SphereShape3D.
/// • Physics.IgnoreCollision / SetupPhysicsIgnore is GONE — the Godot hitbox is a plain
///   MeshInstance3D, never a physics collider, so there is nothing to ignore.
/// • Quaternion.LookRotation → Basis.LookingAt (Godot −Z forward; see QuatLookingAt).
/// • Collider[] overlap buffer dropped (space-state allocates its own results).
///
/// ⚠ HANDEDNESS: snap forward uses −character.GlobalBasis.Z (Godot forward). Verify the
/// imported model's facing in-engine.
/// </summary>
public class AttackController
{
    // ── References ────────────────────────────────────────────────────────
    LocalPlayerManager    player;
    PlayerInput           gamePad;
    PlayerEvents          playerEvents;
    CharacterAttackConfig _config;

    // ── Runtime attack pool ───────────────────────────────────────────────
    readonly Dictionary<AttackData, Attack> _attacks = new();
    Attack _noAttack;
    Attack AttackCommand;

    // ── Combo state ───────────────────────────────────────────────────────
    readonly List<ComboData> _activeCombos = new();
    int _comboStep;
    int comboCounter;

    // ── Input buffer ──────────────────────────────────────────────────────
    readonly Queue<HitBoxTriggerEvents.AttackType> _inputBuffer = new(4);

    // ── Attack snap ───────────────────────────────────────────────────────
    public float attackSnapRange                    = 8f;
    float        attackSnapAngleThreshold           = 60f;

    Node3D    lastSnapTarget;
    uint      _playerMask;

    // ── State ─────────────────────────────────────────────────────────────
    bool isInitialized;
    bool _isHitConfirmPause;

    public bool IsInitialized => isInitialized;

    // ── Update — input capture only ───────────────────────────────────────
    void OnUpdate()
    {
        if (!isInitialized || _isHitConfirmPause) return;

        if      (gamePad.GetButtonDown("X")) _inputBuffer.Enqueue(HitBoxTriggerEvents.AttackType.Light);
        else if (gamePad.GetButtonDown("Y")) _inputBuffer.Enqueue(HitBoxTriggerEvents.AttackType.Heavy);
        else if (gamePad.GetButtonDown("B")) _inputBuffer.Enqueue(HitBoxTriggerEvents.AttackType.Special);
        else if (gamePad.GetButtonDown("A")) _inputBuffer.Enqueue(HitBoxTriggerEvents.AttackType.Launcher);
    }

    // ── FixedUpdate — deterministic execution ─────────────────────────────
    void OnFixedUpdate()
    {
        if (!isInitialized) return;

        if (!gamePad.IsCombatInputActive)
            _inputBuffer.Clear();
        else if (!AttackCommand.IsHitConfirmPause && _inputBuffer.Count > 0)
            QueNextAttack(_inputBuffer.Dequeue());

        AttackCommand.Execute(_playerMask);

        if (AttackCommand.CoolDownProgress >= 1.0 && comboCounter > 0)
        {
            comboCounter   = 0;
            lastSnapTarget = null;
            _activeCombos.Clear();
            _comboStep = 0;
            playerEvents.OnAttackEnd?.Invoke();
        }
    }

    // ── Combo queueing ────────────────────────────────────────────────────
    void QueNextAttack(HitBoxTriggerEvents.AttackType inputType)
    {
        bool queued     = false;
        bool isNewChain = false;

        bool canExtend = _activeCombos.Count > 0 &&
                         _activeCombos.Exists(c =>
                             _comboStep < c.steps.Count &&
                             c.steps[_comboStep].inputType == inputType);

        if (canExtend)
        {
            if (AttackCommand.AnimationProgress >= 1.0)
            {
                _activeCombos.RemoveAll(c =>
                    _comboStep >= c.steps.Count ||
                    c.steps[_comboStep].inputType != inputType);

                Attack nextAttack = GetOrBuildAttack(_activeCombos[0].steps[_comboStep].attack);
                comboCounter++;
                AttackCommand.ResetAttack();
                AttackCommand = nextAttack;
                _comboStep++;
                queued = true;
            }
        }
        else
        {
            if (AttackCommand.CoolDownProgress >= 1.0)
            {
                List<ComboData> starters = GetStartingCombos(inputType);

                // Ignore inputs that neither open a combo nor map to a standalone attack
                // (keeps Launcher combo-only; a bare A press stays a pure jump).
                if (starters.Count == 0 && GetStandaloneAttack(inputType) == _noAttack)
                    return;

                _activeCombos.Clear();
                _comboStep   = 0;
                isNewChain   = true;
                comboCounter = 1;

                Attack nextAttack;
                if (starters.Count > 0)
                {
                    foreach (var c in starters) _activeCombos.Add(c);
                    nextAttack = GetOrBuildAttack(starters[0].steps[0].attack);
                    _comboStep = 1;
                }
                else
                {
                    nextAttack = GetStandaloneAttack(inputType);
                }

                AttackCommand.ResetAttack();
                AttackCommand = nextAttack;
                queued = true;
            }
        }

        if (queued)
        {
            Vector3 lungeDir = SnapToNearestTarget();
            AttackCommand.SetLungeDirection(lungeDir);

            if (isNewChain)
                playerEvents.OnAttackStart?.Invoke();
        }
    }

    // ── Attack selection helpers ──────────────────────────────────────────
    Attack GetOrBuildAttack(AttackData data)
    {
        if (data == null) return _noAttack;
        if (_attacks.TryGetValue(data, out Attack existing)) return existing;

        var attack = new Attack();
        attack.Initialize(data, player, player.character, playerEvents);
        _attacks[data] = attack;
        return attack;
    }

    Attack GetStandaloneAttack(HitBoxTriggerEvents.AttackType type)
    {
        if (_config == null) return _noAttack;

        return type switch
        {
            HitBoxTriggerEvents.AttackType.Light    => GetFirst(_config.lightAttacks),
            HitBoxTriggerEvents.AttackType.Heavy    => GetFirst(_config.heavyAttacks),
            HitBoxTriggerEvents.AttackType.Special  => GetFirst(_config.specialAttacks),
            // Launcher is combo-only — no standalone move (a bare A press falls through to jump).
            HitBoxTriggerEvents.AttackType.Launcher => _noAttack,
            _                                       => _noAttack,
        };
    }

    Attack GetFirst(Godot.Collections.Array<AttackData> list)
    {
        if (list == null || list.Count == 0) return _noAttack;
        return GetOrBuildAttack(list[0]);
    }

    List<ComboData> GetStartingCombos(HitBoxTriggerEvents.AttackType inputType)
    {
        var result = new List<ComboData>();
        if (_config?.combos == null) return result;

        foreach (var combo in _config.combos)
            if (combo != null && combo.IsValid && combo.steps[0].inputType == inputType)
                result.Add(combo);
        return result;
    }

    // ── Attack snap (space-state sphere overlap) ──────────────────────────
    Vector3 SnapToNearestTarget()
    {
        Node3D  attacker    = player.character;
        Vector3 flatForward = -attacker.GlobalBasis.Z; // NOTE handedness (Godot forward = −Z)
        flatForward.Y = 0f;
        flatForward = flatForward.Normalized();

        if (lastSnapTarget != null && GodotObject.IsInstanceValid(lastSnapTarget))
        {
            Vector3 toLast = lastSnapTarget.GlobalPosition - attacker.GlobalPosition;
            toLast.Y = 0f;
            if (toLast.LengthSquared() > 0.001f &&
                Mathf.RadToDeg(flatForward.AngleTo(toLast.Normalized())) <= attackSnapAngleThreshold)
            {
                Vector3 dir = toLast.Normalized();
                playerEvents.OnAttackRotate?.Invoke(QuatLookingAt(dir), attackSnapAngleThreshold);
                return dir;
            }
            lastSnapTarget = null;
        }

        var space = attacker.GetViewport().FindWorld3D().DirectSpaceState;
        var shape = new SphereShape3D { Radius = attackSnapRange };
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape             = shape,
            Transform         = new Transform3D(Basis.Identity, attacker.GlobalPosition),
            CollisionMask     = _playerMask,
            CollideWithBodies = true,
            CollideWithAreas  = false,
        };

        var results    = space.IntersectShape(query, 32);
        float bestAngle = float.MaxValue;
        Node3D bestTarget = null;

        for (int i = 0; i < results.Count; i++)
        {
            Node3D col = results[i]["collider"].As<Node3D>();
            if (col == null) continue;

            LocalPlayerManager other = NodeUtil.GetComponentInChildren<PlayerDetection>(col)?.Player;
            if (other == null || other == player || other.character == null) continue;

            Vector3 toTarget = other.character.GlobalPosition - attacker.GlobalPosition;
            toTarget.Y = 0f;
            if (toTarget.LengthSquared() < 0.001f) continue;

            float angle = Mathf.RadToDeg(flatForward.AngleTo(toTarget.Normalized()));
            if (angle <= attackSnapAngleThreshold && angle < bestAngle)
            {
                bestAngle  = angle;
                bestTarget = other.character;
            }
        }

        lastSnapTarget = bestTarget;

        if (bestTarget != null)
        {
            Vector3 dir = bestTarget.GlobalPosition - attacker.GlobalPosition;
            dir.Y = 0f;
            dir   = dir.Normalized();
            playerEvents.OnAttackRotate?.Invoke(QuatLookingAt(dir), attackSnapAngleThreshold);
            return dir;
        }

        return flatForward;
    }

    /// <summary>Quaternion whose forward (Godot −Z) points along dir — Godot's LookRotation.</summary>
    static Quaternion QuatLookingAt(Vector3 dir)
    {
        if (dir.LengthSquared() < 1e-6f) return Quaternion.Identity;
        return Basis.LookingAt(dir, Vector3.Up).GetRotationQuaternion();
    }

    // ── Initialization ────────────────────────────────────────────────────
    public void Initialize(PlayerInput gamePad, LocalPlayerManager player, Node3D character,
                           PlayerEvents playerEvents, CharacterAttackConfig config)
    {
        this.gamePad      = gamePad;
        this.player       = player;
        this.playerEvents = playerEvents;
        this._config      = config;
        _playerMask       = PhysicsLayers.Player;

        _noAttack = new Attack();
        _noAttack.Initialize(
            player, character,
            hitBoxName:        "No Hit Box",
            hitBoxPosition:    Vector3.Zero,
            hitBoxEulerAngle:  Vector3.Zero,
            hitBoxScale:       Vector3.Zero,
            startupLength:     0f,
            animationLength:   0f,
            attackBlockLength: 0f,
            pushBackDistance:  0f,
            lungeDistance:     0f,
            type:              HitBoxTriggerEvents.AttackType.Light,
            playerEvents:      playerEvents);

        AttackCommand = _noAttack;

        if (config != null)
            BuildAllAttacks(character);
        else
            GD.PushWarning($"[AttackController] No CharacterAttackConfig assigned to {player.playerName}. Attacks unavailable.");

        playerEvents.OnUpdate             += OnUpdate;
        playerEvents.OnFixedUpdate        += OnFixedUpdate;
        playerEvents.OnHitConfirm         += OnHitConfirm;
        playerEvents.OnHitConfirmPauseEnd += OnHitConfirmPauseEnd;

        isInitialized = true;
    }

    void BuildAllAttacks(Node3D character)
    {
        var allData = new HashSet<AttackData>();

        void AddList(Godot.Collections.Array<AttackData> list)
        {
            if (list == null) return;
            foreach (var d in list) if (d != null) allData.Add(d);
        }

        AddList(_config.lightAttacks);
        AddList(_config.heavyAttacks);
        AddList(_config.specialAttacks);
        AddList(_config.launcherAttacks);

        if (_config.combos != null)
            foreach (var combo in _config.combos)
                if (combo?.steps != null)
                    foreach (var step in combo.steps)
                        if (step?.attack != null) allData.Add(step.attack);

        foreach (var data in allData)
        {
            var attack = new Attack();
            attack.Initialize(data, player, character, playerEvents);
            _attacks[data] = attack;
        }
        // No SetupPhysicsIgnore — Godot hitbox is not a physics collider.
    }

    public void Deactivate()
    {
        _noAttack?.Deactivate();
        _noAttack = null;

        foreach (var attack in _attacks.Values)
            attack?.Deactivate();
        _attacks.Clear();
        _activeCombos.Clear();
        _inputBuffer.Clear();

        playerEvents.OnUpdate             -= OnUpdate;
        playerEvents.OnFixedUpdate        -= OnFixedUpdate;
        playerEvents.OnHitConfirm         -= OnHitConfirm;
        playerEvents.OnHitConfirmPauseEnd -= OnHitConfirmPauseEnd;

        playerEvents  = null;
        isInitialized = false;
    }

    // ── Event handlers ────────────────────────────────────────────────────
    public void OnHitConfirm((Node3D hitbox, Node3D hurtbox) hitInfo)
        => _isHitConfirmPause = true;

    public void OnHitConfirmPauseEnd((Node3D hitbox, Node3D hurtbox) hitInfo)
        => _isHitConfirmPause = false;
}
