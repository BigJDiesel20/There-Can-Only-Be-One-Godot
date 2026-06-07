using Godot;
using System.Collections.Generic;

/// <summary>
/// Debug helper that recreates the Unity "Virtual Controller Manager" tooling for Godot: spawn
/// emulated players, auto-pilot them through the Lobby + CharacterSelect, and drive a wandering
/// patrol so you get live moving opponents to test multiplayer with.
///
/// Driven by an in-game GUI (VirtualControllerPanel) AND keyboard hotkeys:
///   F1 add · F2 remove-last · F3 toggle patrol-all · F4 bots jump
///
/// Virtual controllers feed input through VirtualControllers → MenuInput / PlayerInput, so they
/// behave exactly like real gamepads everywhere downstream. Auto-pilot only presses buttons in
/// menus (join / confirm / optionally Start); gameplay input comes from patrol or the GUI.
/// </summary>
public partial class VirtualControllerManager : Node
{
    GameManager _gm;
    VirtualControllerPanel _panel;

    // One-shot button pulses (device,button) → frames remaining held.
    readonly Dictionary<(int dev, JoyButton btn), int> _pulses = new();
    // Per-device cooldown (frames) to avoid re-spamming auto-pilot presses.
    readonly Dictionary<int, int> _cooldown = new();

    // Patrol (per device).
    readonly HashSet<int> _patrolling = new();
    readonly Dictionary<int, PatrolData> _patrol = new();
    class PatrolData { public Vector3 origin, target; }

    public float PatrolRadius = 6f;
    public float PatrolSpeed  = 0.8f;
    public int   SpawnCount   = 2;
    bool _autoAdvance;

    // Hotkey edge tracking.
    readonly Dictionary<Key, bool> _keyPrev = new();

    const int PulseFrames = 5;
    const int CooldownFrames = 20;
    const float ArriveThreshold = 1.5f;

    public VirtualControllerManager(GameManager gm) { _gm = gm; }
    public VirtualControllerManager() { } // Godot needs a parameterless ctor

    public GameManager Game => _gm;

    public override void _Ready()
    {
        var layer = new CanvasLayer { Layer = 50 };
        AddChild(layer);
        _panel = new VirtualControllerPanel(this);
        layer.AddChild(_panel);
    }

    public override void _Process(double delta)
    {
        if (_gm == null) return;

        HandleHotkeys();

        foreach (var kv in VirtualControllers.States) kv.Value.ResetButtons();
        TickCooldowns();
        AutoPilot();
        ApplyPulses();
        TickPatrol();
        _panel?.Refresh();
    }

    // ── Public API (used by the GUI) ──────────────────────────────────────────────
    public int AddController()
    {
        int id = VirtualControllers.Add();
        if (id < 0) GD.Print("[VirtualControllers] Full (max reached).");
        return id;
    }

    public void RemoveDevice(int id)
    {
        if (id < 0) return;
        LocalPlayerManager lpm = _gm.playerSlot.Find(p => p.deviceId == id);
        if (lpm != null)
        {
            lpm.DeactivatePlayerCharacter();
            _gm.playerSlot.Remove(lpm);
            lpm.QueueFree();
            _gm.SetPlayerNames();
        }
        _patrolling.Remove(id);
        _patrol.Remove(id);
        VirtualControllers.Remove(id);
    }

    public void RemoveLast() => RemoveDevice(VirtualControllers.Last());

    public void RemoveAll()
    {
        foreach (int id in new List<int>(VirtualControllers.States.Keys)) RemoveDevice(id);
    }

    /// <summary>Spawn up to SpawnCount controllers and auto-drive the whole pre-game flow.</summary>
    public void FullFlow()
    {
        while (VirtualControllers.Count < Mathf.Clamp(SpawnCount, 1, VirtualControllers.Max))
            if (AddController() < 0) break;
        _autoAdvance = true;
    }

    public bool IsPatrolling(int dev) => _patrolling.Contains(dev);
    public bool AnyPatrolling => _patrolling.Count > 0;

    public void SetPatrol(int dev, bool on)
    {
        if (on) { _patrolling.Add(dev); _patrol.Remove(dev); }
        else    { _patrolling.Remove(dev); _patrol.Remove(dev); ZeroSticks(dev); }
    }

    public void SetPatrolAll(bool on)
    {
        foreach (int dev in VirtualControllers.States.Keys) SetPatrol(dev, on);
    }

    public void PulseButton(int dev, JoyButton b) => StartPulse(dev, b);

    static void ZeroSticks(int dev)
    {
        var s = VirtualControllers.Get(dev);
        if (s != null) { s.LeftX = 0f; s.LeftY = 0f; }
    }

    // ── Hotkeys ─────────────────────────────────────────────────────────────────
    void HandleHotkeys()
    {
        if (KeyDown(Key.F1)) AddController();
        if (KeyDown(Key.F2)) RemoveLast();
        if (KeyDown(Key.F3)) SetPatrolAll(!AnyPatrolling);
        if (KeyDown(Key.F4)) foreach (int d in VirtualControllers.States.Keys) StartPulse(d, JoyButton.A);
    }

    bool KeyDown(Key key)
    {
        bool now = Input.IsPhysicalKeyPressed(key);
        bool was = _keyPrev.GetValueOrDefault(key);
        _keyPrev[key] = now;
        return now && !was;
    }

    // ── Auto-pilot through the menus ──────────────────────────────────────────────
    void AutoPilot()
    {
        bool inLobby  = _gm.currentState is Lobby;
        bool inSelect = _gm.currentState is CharacterSelect;
        if (!inLobby) _autoAdvance = false;
        if (!inLobby && !inSelect) return;

        bool allJoined = VirtualControllers.Count > 0;
        foreach (int dev in VirtualControllers.States.Keys)
        {
            if (_cooldown.GetValueOrDefault(dev) > 0) { allJoined = false; continue; }
            LocalPlayerManager lpm = _gm.playerSlot.Find(p => p.deviceId == dev);

            if (inLobby && lpm == null) { StartPulse(dev, JoyButton.A); allJoined = false; }       // join
            else if (inSelect && lpm != null && !lpm.characterConfirmed) StartPulse(dev, JoyButton.A); // confirm
            else if (lpm == null) allJoined = false;
        }

        // Full-flow: once every virtual has joined, press Start to advance Lobby → CharacterSelect.
        if (inLobby && _autoAdvance && allJoined && _gm.playerSlot.Count > 0)
        {
            int first = VirtualControllers.Last();
            if (first >= 0 && _cooldown.GetValueOrDefault(first) <= 0) StartPulse(first, JoyButton.Start);
        }
    }

    // ── Pulse plumbing ────────────────────────────────────────────────────────────
    void StartPulse(int dev, JoyButton btn)
    {
        _pulses[(dev, btn)] = PulseFrames;
        _cooldown[dev] = CooldownFrames;
    }

    void ApplyPulses()
    {
        if (_pulses.Count == 0) return;
        var done = new List<(int, JoyButton)>();
        foreach (var key in new List<(int dev, JoyButton btn)>(_pulses.Keys))
        {
            var state = VirtualControllers.Get(key.dev);
            if (state != null) SetButton(state, key.btn, true);
            int left = _pulses[key] - 1;
            if (left <= 0) done.Add(key); else _pulses[key] = left;
        }
        foreach (var k in done) _pulses.Remove(k);
    }

    void TickCooldowns()
    {
        foreach (int dev in new List<int>(_cooldown.Keys))
        {
            int v = _cooldown[dev] - 1;
            if (v <= 0) _cooldown.Remove(dev); else _cooldown[dev] = v;
        }
    }

    static void SetButton(VirtualControllerState s, JoyButton b, bool v)
    {
        switch (b)
        {
            case JoyButton.A: s.A = v; break;
            case JoyButton.B: s.B = v; break;
            case JoyButton.X: s.X = v; break;
            case JoyButton.Y: s.Y = v; break;
            case JoyButton.Start: s.Start = v; break;
            case JoyButton.RightShoulder: s.R1 = v; break;
            case JoyButton.LeftShoulder:  s.L1 = v; break;
            case JoyButton.RightStick: s.R3 = v; break;
            case JoyButton.LeftStick:  s.L3 = v; break;
            case JoyButton.DpadUp: s.DpadUp = v; break;
            case JoyButton.DpadDown: s.DpadDown = v; break;
            case JoyButton.DpadLeft: s.DpadLeft = v; break;
            case JoyButton.DpadRight: s.DpadRight = v; break;
        }
    }

    // ── Patrol ────────────────────────────────────────────────────────────────────
    void TickPatrol()
    {
        if (_patrolling.Count == 0 || _gm.currentState is not Battle) return;

        foreach (int dev in _patrolling)
        {
            var state = VirtualControllers.Get(dev);
            LocalPlayerManager lpm = _gm.playerSlot.Find(p => p.deviceId == dev);
            if (state == null || lpm?.character == null) continue;

            if (!_patrol.TryGetValue(dev, out var data))
            {
                Vector3 o = lpm.character.GlobalPosition;
                data = new PatrolData { origin = o, target = RandomWaypoint(o, PatrolRadius) };
                _patrol[dev] = data;
            }

            Vector3 pos = lpm.character.GlobalPosition;
            Vector3 toTarget = data.target - pos; toTarget.Y = 0f;
            float dist = toTarget.Length();
            if (dist < ArriveThreshold) { data.target = RandomWaypoint(data.origin, PatrolRadius); continue; }

            SetStickToward(state, lpm, toTarget / dist, PatrolSpeed);
        }
    }

    static void SetStickToward(VirtualControllerState state, LocalPlayerManager lpm, Vector3 worldDir, float speed)
    {
        Camera3D cam = lpm.cameraControler?.GetCamera();
        Vector3 fwd, right;
        if (cam != null)
        {
            Transform3D t = cam.GlobalTransform;
            fwd = -t.Basis.Z; fwd.Y = 0f;          // Godot camera looks down -Z
            right = t.Basis.X; right.Y = 0f;
            fwd   = fwd.LengthSquared()   > 0.001f ? fwd.Normalized()   : Vector3.Forward;
            right = right.LengthSquared() > 0.001f ? right.Normalized() : Vector3.Right;
        }
        else { fwd = Vector3.Forward; right = Vector3.Right; }

        float moveRight   = Mathf.Clamp(worldDir.Dot(right), -1f, 1f) * speed;
        float moveForward = Mathf.Clamp(worldDir.Dot(fwd),   -1f, 1f) * speed;
        // Store in Godot raw convention: PlayerInput flips LeftY (sign -1) back to "forward".
        state.LeftX = moveRight;
        state.LeftY = -moveForward;
    }

    static Vector3 RandomWaypoint(Vector3 origin, float radius)
    {
        float angle = (float)GD.RandRange(0.0, Mathf.Tau);
        float dist  = (float)GD.RandRange(radius * 0.35f, radius);
        return origin + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
    }
}
