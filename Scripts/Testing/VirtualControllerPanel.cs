using Godot;
using System.Collections.Generic;

/// <summary>
/// In-game GUI for the virtual controllers — the Godot rebuild of Unity's
/// VirtualControllerManagerWindow. A collapsible pane pinned to the top-right: collapsed it is
/// just a small button in the corner; expanded it slides a control panel down the right side
/// (add/remove players, full lobby flow, patrol radius/speed, per-player patrol / jump / remove).
/// Talks to VirtualControllerManager (which owns the actual input driving).
/// </summary>
public partial class VirtualControllerPanel : Control
{
    VirtualControllerManager _mgr;

    Button        _toggleBtn;
    PanelContainer _body;

    VBoxContainer _rowsBox;
    Label   _activeLabel, _radiusVal, _speedVal;
    Button  _patrolAllBtn;

    readonly Dictionary<int, Label>  _rowStatus = new();
    readonly Dictionary<int, Button> _rowPatrol = new();
    string _rowSig = "";
    bool _collapsed = true;

    const float Width   = 320f;
    const float ToggleW = 150f;
    const float ToggleH = 30f;
    const float BodyH   = 470f;

    public VirtualControllerPanel(VirtualControllerManager mgr) { _mgr = mgr; }
    public VirtualControllerPanel() { }

    public override void _Ready()
    {
        // Full-screen pass-through host. A Control directly under a CanvasLayer is NOT auto-sized
        // to the viewport, so we keep its size synced in _Process (children anchor to it).
        SetAnchorsPreset(LayoutPreset.TopLeft);
        Position    = Vector2.Zero;
        MouseFilter = MouseFilterEnum.Ignore;
        Size        = GetViewport().GetVisibleRect().Size;

        // Always-visible toggle button pinned to the top-right corner (fixed rect).
        _toggleBtn = new Button { Text = "Controllers ▸", FocusMode = FocusModeEnum.None };
        _toggleBtn.AnchorLeft = 1f; _toggleBtn.AnchorRight = 1f; _toggleBtn.AnchorTop = 0f; _toggleBtn.AnchorBottom = 0f;
        _toggleBtn.OffsetRight = -8f;        _toggleBtn.OffsetLeft   = -8f - ToggleW;
        _toggleBtn.OffsetTop   = 8f;         _toggleBtn.OffsetBottom = 8f + ToggleH;
        _toggleBtn.Pressed += ToggleCollapsed;
        AddChild(_toggleBtn);

        // Body panel below the button (fixed rect down the right side).
        _body = new PanelContainer();
        _body.AnchorLeft = 1f; _body.AnchorRight = 1f; _body.AnchorTop = 0f; _body.AnchorBottom = 0f;
        _body.OffsetRight = -8f;             _body.OffsetLeft   = -8f - Width;
        _body.OffsetTop   = 8f + ToggleH + 4f; _body.OffsetBottom = 8f + ToggleH + 4f + BodyH;
        var style = new StyleBoxFlat { BgColor = new Color(0.08f, 0.09f, 0.11f, 0.94f) };
        style.SetCornerRadiusAll(6);
        style.SetContentMarginAll(8);
        style.BorderColor = new Color(0.3f, 0.5f, 0.3f); style.SetBorderWidthAll(1);
        _body.AddThemeStyleboxOverride("panel", style);
        AddChild(_body);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 5);
        _body.AddChild(col);

        // Global controls.
        var addRow = new HBoxContainer();
        addRow.AddChild(MakeButton("+ Add", () => _mgr.AddController(), expand: true));
        addRow.AddChild(MakeButton("Remove All", () => _mgr.RemoveAll(), expand: true));
        col.AddChild(addRow);

        var spawnRow = new HBoxContainer();
        spawnRow.AddChild(new Label { Text = "Spawn:", VerticalAlignment = VerticalAlignment.Center });
        var spin = new SpinBox { MinValue = 1, MaxValue = VirtualControllers.Max, Value = _mgr.SpawnCount, Step = 1 };
        spin.FocusMode = FocusModeEnum.None;
        if (spin.GetLineEdit() is LineEdit le) le.FocusMode = FocusModeEnum.None;
        spin.ValueChanged += v => _mgr.SpawnCount = (int)v;
        spawnRow.AddChild(spin);
        spawnRow.AddChild(MakeButton("▶ Full Lobby Flow", () => _mgr.FullFlow(), expand: true));
        col.AddChild(spawnRow);

        col.AddChild(new HSeparator());

        _patrolAllBtn = MakeButton("Patrol All", () => _mgr.SetPatrolAll(!_mgr.AnyPatrolling), expand: true);
        col.AddChild(_patrolAllBtn);

        col.AddChild(SliderRow("Radius", 1f, 30f, _mgr.PatrolRadius, v => _mgr.PatrolRadius = (float)v, out _radiusVal, "m"));
        col.AddChild(SliderRow("Speed",  0.1f, 1f, _mgr.PatrolSpeed, v => _mgr.PatrolSpeed = (float)v, out _speedVal, ""));

        col.AddChild(new HSeparator());

        _activeLabel = new Label { Text = "active: 0" };
        col.AddChild(_activeLabel);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 168) };
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        col.AddChild(scroll);
        _rowsBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rowsBox.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_rowsBox);

        col.AddChild(new Label
        {
            Text = "hotkeys:  F1 add · F2 remove · F3 patrol · F4 jump",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1, 1, 1, 0.6f),
        });

        SetCollapsed(true);
    }

    public override void _Process(double delta)
    {
        // Keep the pass-through host filling the viewport so the corner-anchored controls land
        // on-screen (and follow window resizes).
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        if (Size != vp) Size = vp;
    }

    void ToggleCollapsed() => SetCollapsed(!_collapsed);

    void SetCollapsed(bool collapsed)
    {
        _collapsed = collapsed;
        if (_body != null) _body.Visible = !collapsed;
        if (_toggleBtn != null) _toggleBtn.Text = collapsed ? "Controllers ▸" : "Controllers ▾";
    }

    /// <summary>Called each frame by the manager — keep labels + the dynamic row list in sync.</summary>
    public void Refresh()
    {
        if (_activeLabel == null) return;

        string state = _mgr.Game?.currentState?.GetType().Name ?? "?";
        _activeLabel.Text = $"active: {VirtualControllers.Count}    state: {state}";
        if (_patrolAllBtn != null) _patrolAllBtn.Text = _mgr.AnyPatrolling ? "Stop All Patrol" : "Patrol All";

        // Rebuild rows only when the set of devices changes.
        string sig = string.Join(",", VirtualControllers.States.Keys);
        if (sig != _rowSig) { RebuildRows(); _rowSig = sig; }

        // Live status per row.
        foreach (var kv in _rowStatus)
        {
            int dev = kv.Key;
            LocalPlayerManager lpm = _mgr.Game.playerSlot.Find(p => p.deviceId == dev);
            string status = lpm == null ? "not joined" : (lpm.characterConfirmed ? "ready" : "joining");
            kv.Value.Text = $"dev {dev}  ·  {status}";
            if (_rowPatrol.TryGetValue(dev, out var pb))
                pb.Modulate = _mgr.IsPatrolling(dev) ? new Color(0.5f, 1f, 0.5f) : Colors.White;
        }
    }

    void RebuildRows()
    {
        foreach (Node c in _rowsBox.GetChildren()) c.QueueFree();
        _rowStatus.Clear();
        _rowPatrol.Clear();

        foreach (int dev in VirtualControllers.States.Keys)
        {
            int d = dev;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);

            var lbl = new Label { Text = $"dev {d}", SizeFlagsHorizontal = SizeFlags.ExpandFill,
                                  VerticalAlignment = VerticalAlignment.Center };
            row.AddChild(lbl);
            _rowStatus[d] = lbl;

            var patrol = MakeButton("Patrol", () => _mgr.SetPatrol(d, !_mgr.IsPatrolling(d)));
            row.AddChild(patrol);
            _rowPatrol[d] = patrol;

            row.AddChild(MakeButton("Jump", () => _mgr.PulseButton(d, JoyButton.A)));
            row.AddChild(MakeButton("✕",     () => _mgr.RemoveDevice(d)));

            _rowsBox.AddChild(row);
        }
    }

    // ── UI helpers ────────────────────────────────────────────────────────────────
    static Button MakeButton(string text, System.Action onPressed, bool expand = false)
    {
        // FocusMode None so the gamepad never lands focus here — otherwise the controller's
        // "accept" button would activate this GUI button instead of driving the player.
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
        if (expand) b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        b.Pressed += () => onPressed();
        return b;
    }

    HBoxContainer SliderRow(string label, float min, float max, float value,
                            System.Action<double> onChanged, out Label valueLabel, string suffix)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(54, 0),
                                 VerticalAlignment = VerticalAlignment.Center });
        var slider = new HSlider { MinValue = min, MaxValue = max, Value = value, Step = 0.05f,
                                   FocusMode = FocusModeEnum.None,
                                   SizeFlagsHorizontal = SizeFlags.ExpandFill,
                                   SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var vl = new Label { Text = $"{value:0.0}{suffix}", CustomMinimumSize = new Vector2(40, 0),
                             HorizontalAlignment = HorizontalAlignment.Right };
        slider.ValueChanged += v => { onChanged(v); vl.Text = $"{v:0.0}{suffix}"; };
        row.AddChild(slider);
        row.AddChild(vl);
        valueLabel = vl;
        return row;
    }
}
