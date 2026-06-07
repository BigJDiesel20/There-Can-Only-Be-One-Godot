using Godot;
using System;

/// <summary>
/// Main menu — Godot port of Unity's Menu state. Top level: Online (unimplemented) / Offline /
/// Quit. Offline opens a submenu: Game Mode (◄► cycles) / Start (→ Lobby) / Back.
///
/// Up/Down (D-Pad or arrows) move the cursor, Left/Right cycle the game mode, A/Enter confirms,
/// B/Backspace backs out. Driven by the keyboard or any connected pad.
/// </summary>
public class Menu : IGameState
{
    public GameManager gameManager { get; set; }

    CanvasLayer   _ui;
    VBoxContainer _topBox, _subBox;
    Label[]       _topRows, _subRows;
    Label         _hint;

    int  _row, _subRow;
    bool _inSub;

    static readonly string[] TopLabels = { "Online", "Offline", "Quit" };
    const int RowOnline = 0, RowOffline = 1, RowQuit = 2, TopCount = 3;
    const int SubGameMode = 0, SubStart = 1, SubBack = 2, SubCount = 3;

    static readonly Color Selected = new Color(1f, 0.85f, 0.2f);
    static readonly Color Normal   = new Color(0.75f, 0.78f, 0.82f);
    static readonly Color Disabled = new Color(0.4f, 0.42f, 0.46f);

    static readonly GameManager.GameMode[] GameModes = (GameManager.GameMode[])Enum.GetValues(typeof(GameManager.GameMode));

    public Menu(GameManager gameManager) { this.gameManager = gameManager; }

    public void OnLoad()
    {
        _row = 0; _subRow = 0; _inSub = false;
        BuildUI();
        Refresh();
    }

    public void OnExit() { _ui?.QueueFree(); _ui = null; }

    public void OnUpdate()
    {
        if (_inSub) UpdateSubmenu();
        else        UpdateTopLevel();
    }

    // ── Top level ─────────────────────────────────────────────────────────────────
    void UpdateTopLevel()
    {
        if (NavUp())   { _row = (_row - 1 + TopCount) % TopCount; Refresh(); }
        if (NavDown()) { _row = (_row + 1) % TopCount;           Refresh(); }

        if (Confirm())
        {
            switch (_row)
            {
                case RowOnline:  break; // not yet available
                case RowOffline: _inSub = true; _subRow = 0; Refresh(); break;
                case RowQuit:    gameManager.GetTree().Quit(); break;
            }
        }
    }

    // ── Offline submenu ───────────────────────────────────────────────────────────
    void UpdateSubmenu()
    {
        if (NavUp())   { _subRow = (_subRow - 1 + SubCount) % SubCount; Refresh(); }
        if (NavDown()) { _subRow = (_subRow + 1) % SubCount;           Refresh(); }

        if (_subRow == SubGameMode && GameModes.Length > 1)
        {
            int idx = Array.IndexOf(GameModes, gameManager.currentGameMode);
            if (idx < 0) idx = 0;
            if (NavLeft())  { gameManager.currentGameMode = GameModes[(idx - 1 + GameModes.Length) % GameModes.Length]; Refresh(); }
            if (NavRight()) { gameManager.currentGameMode = GameModes[(idx + 1) % GameModes.Length];                   Refresh(); }
        }

        if (Confirm())
        {
            switch (_subRow)
            {
                case SubGameMode: break;
                case SubStart:    gameManager.ChangeState("Lobby"); return;
                case SubBack:     _inSub = false; Refresh(); break;
            }
        }

        if (Back()) { _inSub = false; Refresh(); }
    }

    // ── Rendering ──────────────────────────────────────────────────────────────────
    void Refresh()
    {
        _topBox.Visible = !_inSub;
        _subBox.Visible = _inSub;

        for (int i = 0; i < _topRows.Length; i++)
        {
            bool sel = !_inSub && _row == i;
            _topRows[i].Text = (sel ? "► " : "   ") + TopLabels[i] + (i == RowOnline ? "   (soon)" : "");
            _topRows[i].AddThemeColorOverride("font_color",
                i == RowOnline ? Disabled : (sel ? Selected : Normal));
        }

        string[] subText =
        {
            $"Game Mode:  {gameManager.currentGameMode}" + (GameModes.Length > 1 ? "   ◄ ►" : ""),
            "Start",
            "Back",
        };
        for (int i = 0; i < _subRows.Length; i++)
        {
            bool sel = _inSub && _subRow == i;
            _subRows[i].Text = (sel ? "► " : "   ") + subText[i];
            _subRows[i].AddThemeColorOverride("font_color", sel ? Selected : Normal);
        }

        _hint.Text = _inSub
            ? "Up/Down move    Left/Right change mode    A/Enter select    B/Esc back"
            : "Up/Down move    A/Enter select";
    }

    void BuildUI()
    {
        _ui = new CanvasLayer { Name = "MainMenuUI", Layer = 20 };
        gameManager.AddChild(_ui);

        var bg = new ColorRect { Color = new Color(0.08f, 0.10f, 0.14f, 1f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(bg);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(center);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 24);
        center.AddChild(col);

        var title = new Label { Text = "MAIN MENU", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 40);
        col.AddChild(title);

        _topBox = MakeRowBox(out _topRows, TopCount);
        _subBox = MakeRowBox(out _subRows, SubCount);
        col.AddChild(_topBox);
        col.AddChild(_subBox);

        _hint = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _hint.AddThemeFontSizeOverride("font_size", 16);
        _hint.AddThemeColorOverride("font_color", new Color(0.6f, 0.62f, 0.66f));
        col.AddChild(_hint);
    }

    static VBoxContainer MakeRowBox(out Label[] rows, int count)
    {
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.AddThemeConstantOverride("separation", 10);
        rows = new Label[count];
        for (int i = 0; i < count; i++)
        {
            var l = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            l.AddThemeFontSizeOverride("font_size", 26);
            box.AddChild(l);
            rows[i] = l;
        }
        return box;
    }

    // ── Input (keyboard + any connected pad) ───────────────────────────────────────
    static bool AnyPad(JoyButton b)
    {
        foreach (int d in Input.GetConnectedJoypads())
            if (MenuInput.PadJustPressed(d, b)) return true;
        return false;
    }

    static bool NavUp()    => MenuInput.KeyJustPressed(Key.Up)    || AnyPad(JoyButton.DpadUp);
    static bool NavDown()  => MenuInput.KeyJustPressed(Key.Down)  || AnyPad(JoyButton.DpadDown);
    static bool NavLeft()  => MenuInput.KeyJustPressed(Key.Left)  || AnyPad(JoyButton.DpadLeft);
    static bool NavRight() => MenuInput.KeyJustPressed(Key.Right) || AnyPad(JoyButton.DpadRight);
    static bool Confirm()  => MenuInput.KeyJustPressed(Key.Enter) || MenuInput.KeyJustPressed(Key.Space) || AnyPad(JoyButton.A);
    static bool Back()     => MenuInput.KeyJustPressed(Key.Backspace) || MenuInput.KeyJustPressed(Key.Escape) || AnyPad(JoyButton.B);
}
