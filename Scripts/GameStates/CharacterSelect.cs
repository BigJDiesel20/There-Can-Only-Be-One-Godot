using Godot;

/// <summary>
/// CharacterSelect — each joined player browses a 16-swatch roster grid (D-Pad / arrows),
/// confirms with A/Enter, deconfirms with B/Backspace. Colors are exclusive (browsing skips
/// taken ones). Each player has a colored cursor border; confirming spawns that player's tinted
/// capsule + stages it. When everyone has confirmed → PreGame.
///
/// Device 0's player also responds to the keyboard so the flow is testable without a pad.
/// </summary>
public class CharacterSelect : IGameState
{
    public GameManager gameManager { get; set; }

    // ── Layout ──────────────────────────────────────────────────────────────────
    const int   Cols       = 8;
    const float SwatchSize = 84f;
    const float Gap        = 14f;
    const float LabelH     = 22f;

    static readonly Color[] PlayerColors =
    {
        new Color(1f, 1f, 1f),       new Color(1f, 0.84f, 0f),
        new Color(0.2f, 0.9f, 1f),   new Color(1f, 0.3f, 0.8f),
        new Color(0.4f, 1f, 0.4f),   new Color(1f, 0.5f, 0.2f),
        new Color(0.6f, 0.6f, 1f),   new Color(1f, 1f, 0.4f),
    };
    static Color PlayerColor(int i) => PlayerColors[i % PlayerColors.Length];

    CanvasLayer _ui;
    Vector2[]   _swatchPos;
    Panel[]     _cursors;
    Label[]     _statusLabels;

    public CharacterSelect(GameManager gameManager) { this.gameManager = gameManager; }

    public void OnLoad()
    {
        foreach (LocalPlayerManager p in gameManager.playerSlot)
            p.characterConfirmed = false;

        BuildUI();
        UpdateCursors();
    }

    public void OnUpdate()
    {
        foreach (LocalPlayerManager p in gameManager.playerSlot)
        {
            int  dev = p.deviceId;
            bool kbd = dev == 0;

            if (!p.characterConfirmed)
            {
                if (MenuInput.PadJustPressed(dev, JoyButton.DpadLeft)  || (kbd && MenuInput.KeyJustPressed(Key.Left)))  Browse(p, -1);
                if (MenuInput.PadJustPressed(dev, JoyButton.DpadRight) || (kbd && MenuInput.KeyJustPressed(Key.Right))) Browse(p, +1);
                if (MenuInput.PadJustPressed(dev, JoyButton.A)         || (kbd && MenuInput.KeyJustPressed(Key.Enter))) Confirm(p);
            }
            else if (MenuInput.PadJustPressed(dev, JoyButton.B) || (kbd && MenuInput.KeyJustPressed(Key.Backspace)))
            {
                Deconfirm(p);
            }
        }

        UpdateCursors();

        if (AllConfirmed())
            gameManager.ChangeState("PreGame");
    }

    public void OnExit() { _ui?.QueueFree(); _ui = null; }

    // ── Selection logic ─────────────────────────────────────────────────────────
    void Browse(LocalPlayerManager p, int dir)
    {
        int total = CharacterRoster.Count;
        for (int step = 0; step < total; step++)
        {
            p.colorIndex = (p.colorIndex + dir + total) % total;
            if (!IsTaken(p.colorIndex, p)) break;
        }
    }

    void Confirm(LocalPlayerManager p)
    {
        if (IsTaken(p.colorIndex, p))
        {
            Browse(p, +1);
            if (IsTaken(p.colorIndex, p)) return;
        }

        Node3D character = gameManager.PlayerScene.Instantiate<Node3D>();
        gameManager.AddChild(character);
        Tint(character, CharacterRoster.Characters[p.colorIndex].color);

        p.StageCharacter(character, null);
        p.characterConfirmed = true;
    }

    void Deconfirm(LocalPlayerManager p)
    {
        p.DeactivatePlayerCharacter();
        p.characterConfirmed = false;
    }

    bool IsTaken(int index, LocalPlayerManager self)
    {
        foreach (LocalPlayerManager p in gameManager.playerSlot)
            if (p != self && p.characterConfirmed && p.colorIndex == index)
                return true;
        return false;
    }

    bool AllConfirmed()
    {
        if (gameManager.playerSlot.Count == 0) return false;
        foreach (LocalPlayerManager p in gameManager.playerSlot)
            if (!p.characterConfirmed) return false;
        return true;
    }

    static void Tint(Node3D character, Color color)
    {
        var mesh = NodeUtil.GetComponentInChildren<MeshInstance3D>(character);
        if (mesh != null)
            mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.6f };
    }

    // ── UI ──────────────────────────────────────────────────────────────────────
    void BuildUI()
    {
        _ui = new CanvasLayer { Name = "CharSelectUI", Layer = 20 };
        gameManager.AddChild(_ui);

        var bg = new ColorRect { Color = new Color(0.08f, 0.10f, 0.14f, 1f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(bg);

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(root);

        var title = new Label { Text = "CHOOSE YOUR FIGHTER", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 36);
        title.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        title.OffsetTop = 28; title.OffsetBottom = 78;
        root.AddChild(title);

        Vector2 screen = gameManager.GetViewport().GetVisibleRect().Size;
        int rows = (CharacterRoster.Count + Cols - 1) / Cols;
        float gridW = Cols * SwatchSize + (Cols - 1) * Gap;
        float gridH = rows * (SwatchSize + LabelH) + (rows - 1) * Gap;
        float startX = (screen.X - gridW) * 0.5f;
        float startY = (screen.Y - gridH) * 0.5f;

        _swatchPos = new Vector2[CharacterRoster.Count];
        for (int i = 0; i < CharacterRoster.Count; i++)
        {
            int col = i % Cols, row = i / Cols;
            float x = startX + col * (SwatchSize + Gap);
            float y = startY + row * (SwatchSize + LabelH + Gap);
            _swatchPos[i] = new Vector2(x, y);

            var sw = new ColorRect
            {
                Color    = CharacterRoster.Characters[i].color,
                Position = new Vector2(x, y),
                Size     = new Vector2(SwatchSize, SwatchSize),
            };
            root.AddChild(sw);

            var nm = new Label
            {
                Text                = CharacterRoster.Characters[i].name,
                HorizontalAlignment = HorizontalAlignment.Center,
                Position            = new Vector2(x, y + SwatchSize),
                Size                = new Vector2(SwatchSize, LabelH),
            };
            nm.AddThemeFontSizeOverride("font_size", 12);
            root.AddChild(nm);
        }

        int n = gameManager.playerSlot.Count;
        _cursors = new Panel[n];
        _statusLabels = new Label[n];
        for (int p = 0; p < n; p++)
        {
            var cur = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            var box = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = PlayerColor(p) };
            box.SetBorderWidthAll(3);
            cur.AddThemeStyleboxOverride("panel", box);
            root.AddChild(cur);
            _cursors[p] = cur;

            var st = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            st.AddThemeFontSizeOverride("font_size", 18);
            st.AddThemeColorOverride("font_color", PlayerColor(p));
            st.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
            st.OffsetTop = -118 + p * 26; st.OffsetBottom = -92 + p * 26;
            root.AddChild(st);
            _statusLabels[p] = st;
        }
    }

    void UpdateCursors()
    {
        if (_cursors == null) return;
        for (int p = 0; p < gameManager.playerSlot.Count; p++)
        {
            LocalPlayerManager pl = gameManager.playerSlot[p];
            float   inset = p * 3f;            // nest overlapping cursors so each stays visible
            Vector2 pos   = _swatchPos[pl.colorIndex];
            _cursors[p].Position = pos - new Vector2(4 + inset, 4 + inset);
            _cursors[p].Size     = new Vector2(SwatchSize + 8 + inset * 2, SwatchSize + 8 + inset * 2);

            var box = (StyleBoxFlat)_cursors[p].GetThemeStylebox("panel");
            box.SetBorderWidthAll(pl.characterConfirmed ? 6 : 3);

            _statusLabels[p].Text = $"{pl.playerName}:  {CharacterRoster.Characters[pl.colorIndex].name}  " +
                                    (pl.characterConfirmed ? "— LOCKED" : "— choosing ( < > )");
        }
    }
}
