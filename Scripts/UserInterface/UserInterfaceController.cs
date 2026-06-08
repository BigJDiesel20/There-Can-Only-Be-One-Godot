using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Per-player dialog/message box. Godot port of Unity's UserInterfaceController dialog half,
/// rebuilt for the decoupled event model: it subscribes to PlayerEvents.OnMessageRequested and
/// renders the MessageRequest.
///
/// LAYOUT (matches Unity) — a centred MessageBox with the message panel on top and the choice
/// buttons arranged in a D-pad / cross that mirrors the physical face buttons:
///     X = left   ·   B = right   ·   Y = top   ·   A = bottom   ·   (single choice = centred)
/// Button captions read "{label}: ({BUTTON})", e.g. "Team Up: (X)".
///
/// The whole box is scaled to the player's SubViewport cell, so it stays proportional whether the
/// view is full-screen (1 player) or a small cell of a 16-way split.
///
/// FLOW
/// • A MessageRequest arrives → show the box, fire OnDialogStateChanged(true). The state machine
///   flips to the Dialog context (which enables GetUIButtonDown).
/// • A choice fires from a mouse click on its button OR its mapped face button; either runs the
///   callback then closes. Auto-closes after the request's duration.
/// • Closing fires OnDialogStateChanged(false) → state machine restores the prior context.
/// </summary>
public class UserInterfaceController
{
    PlayerInput  gamePad;
    PlayerEvents playerEvents;

    CanvasLayer _layer;
    Panel       _box;
    Panel       _msgBg;
    Label       _message;
    readonly List<Button> _buttons = new();

    PlayerStatBarUI _statBarUI;

    bool   _isOpen;
    double _timer;
    double _messageDuration;
    List<(string label, Action action)> _choices;

    // Scaled layout metrics (computed once from the viewport-cell size).
    float _boxW, _boxH, _btnW, _btnWide, _btnH, _armH, _armV, _crossY, _fontBtn;

    // Face button per choice index: confirms fill X,Y,A in order; the last choice (reject) goes to
    // B when there is more than one choice. Mirrors the Unity X/B/Y/A cross assignment.
    static readonly string[] ConfirmButtons = { "X", "Y", "A" };

    static string ButtonForChoice(int index, int count)
    {
        if (count <= 1)         return "X";
        if (index == count - 1) return "B";           // reject
        return ConfirmButtons[Mathf.Min(index, 2)];   // confirms: X, Y, A
    }

    public void Initialize(PlayerInput gamePad, PlayerEvents playerEvents, Node parent, LocalPlayerManager owner)
    {
        this.gamePad      = gamePad;
        this.playerEvents = playerEvents;

        BuildUI(parent);

        _statBarUI = new PlayerStatBarUI();
        _statBarUI.Initialize(_layer, playerEvents, owner);

        this.playerEvents.OnUpdate           += OnUpdate;
        this.playerEvents.OnMessageRequested += HandleMessageRequest;
    }

    public void Deactivate()
    {
        if (playerEvents != null)
        {
            playerEvents.OnUpdate           -= OnUpdate;
            playerEvents.OnMessageRequested -= HandleMessageRequest;
            playerEvents = null;
        }
        _statBarUI?.Deactivate();
        _statBarUI = null;
        _layer?.QueueFree();
        _layer = null;
    }

    void BuildUI(Node parent)
    {
        _layer = new CanvasLayer { Name = "PlayerUI", Layer = 10 };
        parent.AddChild(_layer);

        // ── Scale to the player's viewport cell (so it shrinks with the split count) ──────────
        float ch = GetCellSize().Y;
        float s  = Mathf.Clamp(ch / 648f, 0.4f, 1.1f);

        float pad   = 16f * s;
        float gap   = 12f * s;
        float msgW  = 340f * s;
        float msgH  = 64f  * s;
        _btnW    = 138f * s;
        _btnWide = 190f * s;   // single centred confirm
        _btnH    = 42f  * s;
        _armH    = 118f * s;   // centre → left/right button centre
        _armV    = 54f  * s;   // centre → top/bottom button centre
        _fontBtn = Mathf.Clamp(16f * s, 8f, 18f);
        float fontMsg = Mathf.Clamp(21f * s, 11f, 23f);

        float crossH = 2f * _armV + _btnH;
        _boxW = Mathf.Max(msgW, 2f * (_armH + _btnW * 0.5f)) + 2f * pad;
        _boxH = pad + msgH + gap + crossH + pad;
        _crossY = pad + msgH + gap + crossH * 0.5f; // cross centre (from box top)

        // ── Box (centred in the cell) ────────────────────────────────────────────────────────
        _box = new Panel { Name = "MessageBox", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        var boxStyle = new StyleBoxFlat { BgColor = new Color(0.10f, 0.11f, 0.16f, 0.96f) };
        boxStyle.SetCornerRadiusAll(Mathf.RoundToInt(10f * s));
        boxStyle.SetBorderWidthAll(Mathf.Max(1, Mathf.RoundToInt(2f * s)));
        boxStyle.BorderColor = new Color(0.40f, 0.62f, 0.95f, 0.85f);
        _box.AddThemeStyleboxOverride("panel", boxStyle);
        _box.AnchorLeft = _box.AnchorRight = 0.5f;
        _box.AnchorTop  = _box.AnchorBottom = 0.5f;
        _box.OffsetLeft = -_boxW * 0.5f; _box.OffsetRight  = _boxW * 0.5f;
        _box.OffsetTop  = -_boxH * 0.5f; _box.OffsetBottom = _boxH * 0.5f;
        _layer.AddChild(_box);

        // ── Message panel (top) ──────────────────────────────────────────────────────────────
        _msgBg = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        var msgStyle = new StyleBoxFlat { BgColor = new Color(0.16f, 0.22f, 0.34f, 1f) };
        msgStyle.SetCornerRadiusAll(Mathf.RoundToInt(6f * s));
        _msgBg.AddThemeStyleboxOverride("panel", msgStyle);
        _msgBg.AnchorLeft = _msgBg.AnchorRight = 0.5f; _msgBg.AnchorTop = _msgBg.AnchorBottom = 0f;
        _msgBg.OffsetLeft = -msgW * 0.5f; _msgBg.OffsetRight = msgW * 0.5f;
        _msgBg.OffsetTop  = pad; _msgBg.OffsetBottom = pad + msgH;
        _box.AddChild(_msgBg);

        _message = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            AutowrapMode        = TextServer.AutowrapMode.WordSmart,
            MouseFilter         = Control.MouseFilterEnum.Ignore,
        };
        _message.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _message.OffsetLeft = 6f * s; _message.OffsetRight = -6f * s;
        _message.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(fontMsg));
        _msgBg.AddChild(_message);
    }

    void HandleMessageRequest(MessageRequest req)
    {
        if (req == null || req.Choices == null || req.Choices.Count == 0) return;

        _choices         = req.Choices;
        _messageDuration = req.Duration > 0 ? req.Duration : 10;
        _timer           = 0;

        _message.Text = req.Message;

        // Rebuild the choice buttons, laid out as a face-button cross.
        foreach (var b in _buttons) b.QueueFree();
        _buttons.Clear();

        int count = _choices.Count;
        float cx = _boxW * 0.5f;
        for (int i = 0; i < count; i++)
        {
            int idx = i;
            string face = ButtonForChoice(i, count);
            var btn = new Button
            {
                Text        = $"{_choices[i].label}: ({face})",
                FocusMode   = Control.FocusModeEnum.None, // mouse-only; gamepad uses the mapped face button
                ClipText    = true,
            };
            btn.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(_fontBtn));
            btn.Pressed += () => Choose(idx);
            _box.AddChild(btn);

            // Place by the physical face-button slot (single choice → centred & wider).
            if (count == 1)            PlaceButton(btn, cx,          _crossY,        _btnWide);
            else switch (face)
            {
                case "X": PlaceButton(btn, cx - _armH, _crossY,         _btnW); break; // left
                case "B": PlaceButton(btn, cx + _armH, _crossY,         _btnW); break; // right
                case "Y": PlaceButton(btn, cx,         _crossY - _armV, _btnW); break; // top
                case "A": PlaceButton(btn, cx,         _crossY + _armV, _btnW); break; // bottom
            }
            _buttons.Add(btn);
        }

        _box.Visible = true;
        _isOpen      = true;
        playerEvents.OnDialogStateChanged?.Invoke(true);
    }

    void PlaceButton(Button b, float centerX, float centerY, float w)
    {
        b.AnchorLeft = b.AnchorRight = b.AnchorTop = b.AnchorBottom = 0f;
        b.OffsetLeft   = centerX - w * 0.5f;  b.OffsetRight  = centerX + w * 0.5f;
        b.OffsetTop    = centerY - _btnH * 0.5f; b.OffsetBottom = centerY + _btnH * 0.5f;
    }

    void OnUpdate()
    {
        if (!_isOpen) return;

        // GetUIButtonDown only passes while Context == Dialog, so these can't cross-fire combat.
        for (int i = 0; i < _choices.Count; i++)
        {
            if (gamePad.GetUIButtonDown(ButtonForChoice(i, _choices.Count)))
            {
                Choose(i);
                return;
            }
        }

        if ((_timer += GameTime.Delta / _messageDuration) >= 1.0)
            Clear();
    }

    // Confirm a choice (from a mouse click on its button, or its mapped face button).
    void Choose(int index)
    {
        if (!_isOpen || _choices == null || index < 0 || index >= _choices.Count) return;
        Action chosen = _choices[index].action;
        Clear();
        chosen?.Invoke();
    }

    void Clear()
    {
        if (!_isOpen) return;
        _isOpen      = false;
        _box.Visible = false;
        _choices     = null;
        playerEvents.OnDialogStateChanged?.Invoke(false);
    }

    /// <summary>This player's SubViewport cell size in pixels (dialog metrics are scaled by it).</summary>
    Vector2 GetCellSize()
    {
        var vp = _layer.GetViewport();
        Vector2 sz = vp != null ? vp.GetVisibleRect().Size : Vector2.Zero;
        if (sz.X <= 0f || sz.Y <= 0f)
        {
            var root = _layer.GetTree()?.Root;
            sz = root != null ? root.GetVisibleRect().Size : new Vector2(1152f, 648f);
        }
        return sz;
    }
}
