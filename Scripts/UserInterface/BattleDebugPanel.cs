using Godot;

/// <summary>
/// In-battle debug overlay — Godot port of Unity's BattleDebugUI. A collapsible pane pinned to the
/// TOP-LEFT: pick a player number and Force Win (drives that player's aura to max → PostGame), so
/// the Battle → PostGame → Lobby loop is testable without grinding aura. Created by Battle.OnLoad,
/// freed on OnExit. Mouse-driven; all controls are non-focusable so the gamepad never grabs them.
/// </summary>
public partial class BattleDebugPanel : Control
{
    GameManager _gm;
    Button   _toggleBtn;
    PanelContainer _body;
    SpinBox  _playerSpin;
    bool _collapsed;

    const float ToggleW = 132f, ToggleH = 28f, BodyW = 220f, BodyH = 96f;

    public BattleDebugPanel(GameManager gm) { _gm = gm; }
    public BattleDebugPanel() { }

    public override void _Ready()
    {
        // Pass-through host; children anchor to the top-left corner so the host size is irrelevant.
        SetAnchorsPreset(LayoutPreset.TopLeft);
        Position    = Vector2.Zero;
        MouseFilter = MouseFilterEnum.Ignore;

        _toggleBtn = new Button { Text = "DEBUG ▾", FocusMode = FocusModeEnum.None };
        Place(_toggleBtn, 8f, 8f, ToggleW, ToggleH);
        var tStyle = new StyleBoxFlat { BgColor = new Color(0.06f, 0.06f, 0.06f, 0.94f) };
        tStyle.SetCornerRadiusAll(4); tStyle.SetContentMarginAll(4);
        _toggleBtn.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.1f));
        _toggleBtn.Pressed += () => SetCollapsed(!_collapsed);
        AddChild(_toggleBtn);

        _body = new PanelContainer();
        var bStyle = new StyleBoxFlat { BgColor = new Color(0.06f, 0.06f, 0.06f, 0.94f) };
        bStyle.SetCornerRadiusAll(5); bStyle.SetContentMarginAll(8);
        bStyle.BorderColor = new Color(1f, 0.6f, 0.1f, 0.5f); bStyle.SetBorderWidthAll(1);
        _body.AddThemeStyleboxOverride("panel", bStyle);
        Place(_body, 8f, 8f + ToggleH + 4f, BodyW, BodyH);
        AddChild(_body);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        _body.AddChild(col);

        var title = new Label { Text = "DEBUG  |  Force Win" };
        title.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.1f));
        col.AddChild(title);

        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Player #", VerticalAlignment = VerticalAlignment.Center });
        _playerSpin = new SpinBox { MinValue = 1, MaxValue = Mathf.Max(1, _gm.playerSlot.Count), Value = 1, Step = 1,
                                    FocusMode = FocusModeEnum.None };
        if (_playerSpin.GetLineEdit() is LineEdit le) le.FocusMode = FocusModeEnum.None;
        _playerSpin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(_playerSpin);
        col.AddChild(row);

        var win = new Button { Text = "FORCE WIN", FocusMode = FocusModeEnum.None };
        var wStyle = new StyleBoxFlat { BgColor = new Color(0.70f, 0.12f, 0.08f) };
        wStyle.SetCornerRadiusAll(4); wStyle.SetContentMarginAll(4);
        win.AddThemeStyleboxOverride("normal", wStyle);
        win.AddThemeColorOverride("font_color", Colors.White);
        win.Pressed += ForceWin;
        col.AddChild(win);

        SetCollapsed(false);
    }

    void ForceWin()
    {
        int idx = (int)_playerSpin.Value - 1;
        if (idx < 0 || idx >= _gm.playerSlot.Count) { GD.Print("[BattleDebug] No such player."); return; }

        // Drain everyone else's aura to zero first (as if the winner absorbed it all), then push
        // the winner to max — which fires OnValueMaximum → Battle's win → PostGame.
        for (int i = 0; i < _gm.playerSlot.Count; i++)
            if (i != idx) _gm.playerSlot[i].statManager?.DebugDrainAura();
        _gm.playerSlot[idx].statManager?.DebugForceWin();
    }

    void SetCollapsed(bool collapsed)
    {
        _collapsed = collapsed;
        if (_body != null) _body.Visible = !collapsed;
        if (_toggleBtn != null) _toggleBtn.Text = collapsed ? "DEBUG ▸" : "DEBUG ▾";
    }

    static void Place(Control c, float x, float y, float w, float h)
    {
        c.AnchorLeft = 0f; c.AnchorRight = 0f; c.AnchorTop = 0f; c.AnchorBottom = 0f;
        c.OffsetLeft = x; c.OffsetRight = x + w; c.OffsetTop = y; c.OffsetBottom = y + h;
    }
}
