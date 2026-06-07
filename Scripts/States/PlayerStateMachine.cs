using Godot;

/// <summary>
/// Per-player state machine. Ported from Unity, including the soft-lock fixes
/// (ReturnToPreviousState guard + OnAttackEnd overlay guard + SafeBaseState/_isProne)
/// so a dialog opening over a combo can no longer strand the player.
///
/// Pure logic apart from GD.Print (was Debug.Log). Pre-allocates every state so
/// transitions never allocate.
/// </summary>
public class PlayerStateMachine
{
    // ── Pre-allocated state instances ─────────────────────────────────────────
    public readonly PlayerState_Battle   Battle   = new();
    public readonly PlayerState_Prone    Prone    = new();
    public readonly PlayerState_Comboing Comboing = new();
    public readonly PlayerState_Dialog   Dialog   = new();
    public readonly PlayerState_Spectate Spectate = new();
    public readonly PlayerState_Menu     Menu     = new();

    // ── Internal state ────────────────────────────────────────────────────────
    private IPlayerState       _currentState;
    private IPlayerState       _previousState;
    private LocalPlayerManager _player;

    /// <summary>True while the player is knocked down (tracked via OnProneActive).</summary>
    private bool _isProne;

    public IPlayerState CurrentState => _currentState;

    /// <summary>
    /// The player's valid "resting" gameplay state — Prone if knocked down, else Battle.
    /// Fallback whenever an overlay (Dialog/Menu) or the transient Comboing state would
    /// otherwise be restored as the previous state.
    /// </summary>
    private IPlayerState SafeBaseState => _isProne ? (IPlayerState)Prone : Battle;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    public void Initialize(LocalPlayerManager player)
    {
        _player = player;
        player.playerEvents.OnUpdate             += OnUpdate;
        player.playerEvents.OnAttackStart        += OnAttackStart;
        player.playerEvents.OnAttackEnd          += OnAttackEnd;
        player.playerEvents.OnProneActive        += OnProneActive;
        player.playerEvents.OnDialogStateChanged += OnDialogStateChanged;
        player.playerEvents.OnMenuStateChanged   += OnMenuStateChanged;
    }

    public void Deactivate()
    {
        if (_player == null) return;

        _currentState?.OnExit(_player);
        _currentState  = null;
        _previousState = null;

        _player.playerEvents.OnUpdate             -= OnUpdate;
        _player.playerEvents.OnAttackStart        -= OnAttackStart;
        _player.playerEvents.OnAttackEnd          -= OnAttackEnd;
        _player.playerEvents.OnProneActive        -= OnProneActive;
        _player.playerEvents.OnDialogStateChanged -= OnDialogStateChanged;
        _player.playerEvents.OnMenuStateChanged   -= OnMenuStateChanged;
        _player = null;
    }

    // ── Per-frame tick ─────────────────────────────────────────────────────────
    private void OnUpdate()
    {
        _player.playerInput.ClearFrameSuppression();

        if (_player.playerInput.GetMenuOpenButtonDown())
        {
            GD.Print($"[StateMachine] {_player.playerName} Start pressed → EnterMenu. Context={_player.playerInput.Context}");
            EnterMenu();
        }
    }

    // ── State transitions ──────────────────────────────────────────────────────
    public void ChangeState(IPlayerState newState)
    {
        _previousState = _currentState;
        _currentState?.OnExit(_player);
        _currentState = newState;

        if (_currentState != null)
            _currentState.OnEnter(_player);
        else
            _player.playerInput.Context = PlayerInputContext.Disabled;
    }

    public void ReturnToPreviousState()
    {
        IPlayerState target = _previousState;

        // Never restore into an overlay (Dialog/Menu) or the transient Comboing state —
        // none are valid resting states. Prevents the dialog-over-combo soft-lock.
        if (target == Comboing || target == Dialog || target == Menu)
            target = SafeBaseState;

        ChangeState(target);
    }

    // ── Convenience entry points ────────────────────────────────────────────────
    public void EnterBattle()   => ChangeState(Battle);
    public void EnterProne()    => ChangeState(Prone);
    public void EnterComboing() => ChangeState(Comboing);
    public void EnterDialog()   => ChangeState(Dialog);
    public void EnterSpectate() => ChangeState(Spectate);
    public void EnterMenu()     => ChangeState(Menu);

    // ── Event-driven transitions ────────────────────────────────────────────────
    private void OnAttackStart()
    {
        if (_currentState == Battle || _currentState == Prone)
            ChangeState(Comboing);
    }

    private void OnAttackEnd()
    {
        if (_currentState == Comboing)
        {
            ReturnToPreviousState();
        }
        else if (_previousState == Comboing)
        {
            // Combo ended underneath an overlay (Dialog/Menu) that opened on top of
            // Comboing, so the normal Comboing→base transition was skipped. Rewrite the
            // stale previous-state slot so closing the overlay returns to Battle/Prone.
            _previousState = SafeBaseState;
        }
    }

    private void OnProneActive(bool isProne)
    {
        _isProne = isProne;
        if (isProne) ChangeState(Prone);
        else         ReturnToPreviousState();
    }

    private void OnDialogStateChanged(bool isOpen)
    {
        if (isOpen)
        {
            ChangeState(Dialog);
        }
        else
        {
            ReturnToPreviousState();
            _player.playerInput.SuppressCombatThisFrame();
        }
    }

    private void OnMenuStateChanged(bool isOpen)
    {
        if (!isOpen && _currentState == Menu)
        {
            ReturnToPreviousState();
            _player.playerInput.SuppressCombatThisFrame();
        }
    }
}
