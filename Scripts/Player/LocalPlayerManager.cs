using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Per-player hub — port of Unity's LocalPlayerManager. Owns every per-player
/// controller and pumps the PlayerEvents loop.
///
/// PORT SCOPE: wires the controllers ported so far (StateMachine, Stat, Camera,
/// Movement, Attack). Not-yet-ported pieces are marked TODO: TeamController,
/// UserInterfaceController, PlayerMenuController, PlayerSymbolLibrary, the Aura Field
/// + ObjectTriggerDetection, and the floating display name.
///
/// EVENT PUMP (replaces Unity's Update/FixedUpdate/LateUpdate):
///   _Process        → set GameTime.Delta, Poll input, fire OnUpdate then OnLateUpdate
///   _PhysicsProcess → set GameTime.FixedDelta, fire OnFixedUpdate
/// Poll() runs before OnUpdate so edge-triggered input is fresh; OnLateUpdate (camera)
/// runs in _Process after physics so the camera follows the post-move position.
/// </summary>
public partial class LocalPlayerManager : Node3D
{
    public static List<LocalPlayerManager> ActivePlayers = new();

    public PlayerEvents       playerEvents = new();
    public PlayerInput        playerInput;
    public PlayerStateMachine stateMachine = new();

    public int    deviceId;
    public string playerName;

    /// <summary>CharacterSelect: currently browsed/chosen roster index, and whether locked in.</summary>
    public int  colorIndex;
    public bool characterConfirmed;

    public StatController    statManager;
    public CameraControler   cameraControler;
    public MovementController movementController;
    public AttackController  attackController;
    public TeamController    teamController;
    public UserInterfaceController userInterfaceController;
    public PlayerMenuController    playerMenuController;

    public Node3D character;

    /// <summary>Floating billboard name tag above the character's head (visible to every viewport).</summary>
    Label3D _nameLabel;

    /// <summary>Trigger volume around the character that detects other players (aura-drain loop).</summary>
    ObjectTriggerDetection auraField;

    /// <summary>This player's split-screen SubViewport (set by PreGame). Null = render to main view.</summary>
    public SubViewport viewport;

    /// <summary>The character's moveset — set in CharacterSelect (TODO) or the test scene.</summary>
    public CharacterAttackConfig attackConfig;

    /// <summary>This player's team status (Solo/Leader/Follower), proxied to its TeamController.</summary>
    public TeamController.Status CurrentTeamStatus
    {
        get => teamController.CurrentStatus;
        set => teamController.CurrentStatus = value;
    }

    public PlayerSymbolEntry personalSymbol;

    /// <summary>
    /// Symbol shown as this player's cursor: own symbol when Solo/Leader, the team
    /// leader's symbol when a Follower.
    /// </summary>
    public PlayerSymbolEntry ActiveSymbol
    {
        get
        {
            if (teamController == null || teamController.CurrentStatus != TeamController.Status.Follower)
                return personalSymbol;
            LocalPlayerManager leader = teamController.team?.GetLeader();
            return leader != null ? leader.personalSymbol : personalSymbol;
        }
    }

    /// <summary>Debug gate — when true the event pump is suspended.</summary>
    public bool test = false;

    // ── Staged character refs (set in CharacterSelect, consumed in PreGame) ──────
    private Node3D _pendingCharacter;
    private Node3D _pendingCursor;

    // ══════════════════════════════════════════════════════════════════════════
    //  Event pump
    // ══════════════════════════════════════════════════════════════════════════
    public override void _Process(double delta)
    {
        if (test) return;
        GameTime.Delta = (float)delta;
        playerInput?.Poll();
        playerEvents.OnUpdate?.Invoke();
        playerEvents.OnLateUpdate?.Invoke();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (test) return;
        GameTime.FixedDelta = (float)delta;
        playerEvents.OnFixedUpdate?.Invoke();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Initialization
    // ══════════════════════════════════════════════════════════════════════════
    public void InitializePlayer(int deviceId)
    {
        this.deviceId = deviceId;
        playerInput   = new PlayerInput(deviceId);
        // State machine stays dormant (Disabled) until Battle/test calls EnterBattle().
        stateMachine.Initialize(this);
    }

    public void InitializePlayerName(string playerName)
    {
        Name = this.playerName = playerName;
        cameraControler?.SetCameraName(playerName);
        if (_nameLabel != null) _nameLabel.Text = playerName ?? "";
    }

    /// <summary>Store the instantiated character + cursor without wiring controllers yet.</summary>
    public void StageCharacter(Node3D character, Node3D cursor)
    {
        _pendingCharacter = character;
        _pendingCursor    = cursor;
    }

    /// <summary>Finish character setup from the staged refs (called from PreGame).</summary>
    public void BuildCharacter()
    {
        if (_pendingCharacter == null)
        {
            GD.PushWarning($"[LocalPlayerManager] BuildCharacter called but no character staged for {playerName}.");
            return;
        }
        InitializePlayerCharacter(_pendingCharacter, _pendingCursor);
        _pendingCharacter = null;
        _pendingCursor    = null;
    }

    public void InitializePlayerCharacter(Node3D character, Node3D cursor)
    {
        this.character = character;

        // Resolve / back-reference for hit queries.
        var pd = new PlayerDetection { Name = "PlayerDetection" };
        character.AddChild(pd);
        pd.Initialize(this, playerEvents);

        // Tag + physics layers so attack/aura queries find this body.
        character.AddToGroup("Player");
        if (character is CollisionObject3D co)
        {
            co.CollisionLayer = PhysicsLayers.Player;
            co.CollisionMask  = PhysicsLayers.World | PhysicsLayers.Player;
        }

        // Stats
        statManager = new StatController();
        statManager.Initialize((100, 100), (1, 100), (100, 100), (1, 100), (1000, 1000), (1, 100), (1, 100),
                               this, playerEvents, playerInput);

        // This player's identity symbol (shown on the aura pie / lock-on cursor). Keyed by the
        // chosen character slot so each player gets a distinct one.
        personalSymbol = PlayerSymbolLibrary.GetEntry(colorIndex);

        // Aura field — a trigger sphere around the character that detects OTHER players.
        // Holding B near a prone player inside it drains their aura into yours (win loop).
        auraField = new ObjectTriggerDetection
        {
            Name            = "AuraField",
            CollisionLayer  = PhysicsLayers.AuraField,
            CollisionMask   = PhysicsLayers.Player,   // detect player bodies; own is filtered in StatController
            Monitoring      = true,
        };
        auraField.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 2.5f } });
        character.AddChild(auraField);
        auraField.Initialize(playerEvents.objectTriggerEventCollection[ObjectTriggerEvents.Type.AuraField]);

        // Camera + UI render into this player's SubViewport (split-screen) or, when none was
        // assigned, into this LocalPlayerManager (single full-window view).
        Node renderParent = viewport != null ? (Node)viewport : this;

        // Lock-on cursor: a billboarded Sprite3D that floats over this player's current target,
        // showing the target's symbol. Split-screen shares one World3D, so we isolate it to the
        // OWNER's view via a private render layer (keyed to the player's unique colorIndex) and a
        // matching camera cull mask — layer 1 stays the shared world everyone sees.
        uint privateBit = 1u << (Mathf.Clamp(colorIndex, 0, 18) + 1);
        var lockCursor = new Sprite3D
        {
            Name        = "LockOnCursor",
            Billboard   = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            PixelSize   = 0.01f,
            Visible     = false,
            Layers      = privateBit,
        };
        renderParent.AddChild(lockCursor);

        // Camera
        cameraControler = new CameraControler();
        cameraControler.Initialize(character, renderParent, this, playerInput, lockCursor, "P1Visible", playerEvents);
        var cam = cameraControler.GetCamera();
        if (cam != null) cam.CullMask = 1u | privateBit; // shared world + only this player's cursor

        // Movement
        movementController = new MovementController();
        bool mInit = false;
        movementController.Initialize(this, cameraControler.GetCamera(), cameraControler.GetCameraLocation(),
                                      cameraControler.GetCameraState(), character, ref mInit, playerEvents);

        // Auto-set uncharged jump height to the model's visual height (full charge = 2×).
        float h = ComputeModelHeight(character);
        if (h > 0f) movementController.height = h;

        // Floating name tag above the head — billboarded so it reads from any camera/viewport
        // (this is how teammates' names show up too: every player carries their own tag).
        Color tint = (colorIndex >= 0 && colorIndex < CharacterRoster.Count)
            ? CharacterRoster.Characters[colorIndex].color : Colors.White;
        _nameLabel = new Label3D
        {
            Name            = "NameTag",
            Text            = playerName ?? "",
            Billboard       = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize        = 64,
            PixelSize       = 0.0025f,
            Modulate        = tint,
            OutlineSize     = 16,
            OutlineModulate = new Color(0f, 0f, 0f, 0.9f),
            NoDepthTest     = true,
            Position        = new Vector3(0f, (h > 0f ? h : 1.8f) + 0.7f, 0f),
        };
        character.AddChild(_nameLabel);

        // Team
        teamController = new TeamController();
        teamController.Initialize(playerInput, this, playerEvents);

        // Attack
        attackController = new AttackController();
        attackController.Initialize(playerInput, this, character, playerEvents, attackConfig);

        // UI (dialog/message box + HUD bars) — into the same viewport as the camera.
        userInterfaceController = new UserInterfaceController();
        userInterfaceController.Initialize(playerInput, playerEvents, renderParent, this);

        // In-game pause menu (combo list) — opened with Start during Battle/Prone.
        playerMenuController = new PlayerMenuController();
        playerMenuController.Initialize(this, renderParent, playerInput, playerEvents);

        if (!ActivePlayers.Contains(this))
            ActivePlayers.Add(this);

        InitializePlayerName(playerName);
    }

    /// <summary>Visual height of the character's tallest mesh (for jump-height scaling).</summary>
    static float ComputeModelHeight(Node3D character)
    {
        var meshes = NodeUtil.GetComponentsInChildren<MeshInstance3D>(character);
        float maxY = 0f;
        foreach (var m in meshes)
        {
            float y = m.GetAabb().Size.Y; // local-space AABB — NOTE ignores node scale; tune in-engine
            if (y > maxY) maxY = y;
        }
        return maxY;
    }

    /// <summary>Convenience: make this player's input live (normally driven by the Battle game state).</summary>
    public void EnterBattle() => stateMachine.EnterBattle();

    public void RefreshCursorSymbol() { /* cursor is target-driven; no owner push needed */ }

    // ══════════════════════════════════════════════════════════════════════════
    //  Teardown
    // ══════════════════════════════════════════════════════════════════════════
    public void DeactivatePlayerCharacter()
    {
        if (statManager == null)
        {
            _pendingCharacter?.QueueFree();
            _pendingCursor?.QueueFree();
            _pendingCharacter = null;
            _pendingCursor    = null;
            return;
        }

        ActivePlayers.Remove(this);

        stateMachine.Deactivate();

        cameraControler?.Deactivate();   cameraControler = null;
        movementController?.Deactivate(); movementController = null;
        attackController?.Deactivate();   attackController = null;
        teamController?.Deactivate();     teamController = null;
        userInterfaceController?.Deactivate(); userInterfaceController = null;
        playerMenuController?.Deactivate();    playerMenuController = null;
        auraField?.Deactivate();          auraField = null;
        statManager?.Deactivate();        statManager = null;

        if (character != null)
        {
            character.QueueFree();
            character = null;
        }
    }
}
