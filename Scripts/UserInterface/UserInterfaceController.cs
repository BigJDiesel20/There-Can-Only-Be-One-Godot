using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Per-player UI — the dialog/message box (HUD stat bars are a later pass). Godot port of
/// Unity's UserInterfaceController dialog half, rebuilt for the decoupled event model:
/// it subscribes to PlayerEvents.OnMessageRequested and renders the MessageRequest.
///
/// FLOW
/// • A MessageRequest arrives (from Team, etc.) → show the box, fire OnDialogStateChanged(true).
///   The PlayerStateMachine flips to the Dialog context, which is what enables GetUIButtonDown.
/// • Face buttons map to choices: confirms on X/Y/A in order, the final "reject" choice on B
///   (matching the Unity cross layout). Pressing one runs that choice's callback then closes.
/// • Auto-closes after the request's duration.
/// • Closing fires OnDialogStateChanged(false) → state machine restores the prior context
///   (with one-frame combat suppression, already handled there).
///
/// Built in code (CanvasLayer + Control) so no .tscn prefab is needed. Single-player for now;
/// split-screen will parent each player's CanvasLayer into its SubViewport later.
/// </summary>
public class UserInterfaceController
{
    PlayerInput  gamePad;
    PlayerEvents playerEvents;

    CanvasLayer _layer;
    Panel       _box;
    Label       _message;
    Label       _choicesLabel;

    PlayerStatBarUI _statBarUI;

    bool   _isOpen;
    double _timer;
    double _messageDuration;
    List<(string label, Action action)> _choices;

    // Face button per choice index: confirms fill X,Y,A in order; the last choice (reject)
    // goes to B when there is more than one choice. Mirrors the Unity X/B/Y/A cross.
    static readonly string[] ConfirmButtons = { "X", "Y", "A" };

    static string ButtonForChoice(int index, int count)
    {
        if (count <= 1)        return "X";
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

        this.playerEvents.OnUpdate          += OnUpdate;
        this.playerEvents.OnMessageRequested += HandleMessageRequest;
    }

    public void Deactivate()
    {
        if (playerEvents != null)
        {
            playerEvents.OnUpdate          -= OnUpdate;
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

        _box = new Panel { Name = "MessageBox", Visible = false };
        _box.SetAnchorsPreset(Control.LayoutPreset.Center);
        _box.CustomMinimumSize = new Vector2(420, 200);
        // Center the panel on screen.
        _box.AnchorLeft = _box.AnchorRight = 0.5f;
        _box.AnchorTop  = _box.AnchorBottom = 0.5f;
        _box.OffsetLeft = -210; _box.OffsetRight = 210;
        _box.OffsetTop  = -100; _box.OffsetBottom = 100;
        _layer.AddChild(_box);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vbox.OffsetLeft = 16; vbox.OffsetTop = 16; vbox.OffsetRight = -16; vbox.OffsetBottom = -16;
        vbox.AddThemeConstantOverride("separation", 18);
        _box.AddChild(vbox);

        _message = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode        = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical   = Control.SizeFlags.ExpandFill,
        };
        vbox.AddChild(_message);

        _choicesLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        vbox.AddChild(_choicesLabel);
    }

    void HandleMessageRequest(MessageRequest req)
    {
        if (req == null || req.Choices == null || req.Choices.Count == 0) return;

        _choices         = req.Choices;
        _messageDuration = req.Duration > 0 ? req.Duration : 10;
        _timer           = 0;

        _message.Text = req.Message;

        // Build the "(X) Accept   (B) Decline" prompt line.
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _choices.Count; i++)
        {
            if (i > 0) sb.Append("    ");
            sb.Append($"({ButtonForChoice(i, _choices.Count)}) {_choices[i].label}");
        }
        _choicesLabel.Text = sb.ToString();

        _box.Visible = true;
        _isOpen      = true;
        playerEvents.OnDialogStateChanged?.Invoke(true);
    }

    void OnUpdate()
    {
        if (!_isOpen) return;

        // GetUIButtonDown only passes while Context == Dialog, so these can't cross-fire combat.
        for (int i = 0; i < _choices.Count; i++)
        {
            if (gamePad.GetUIButtonDown(ButtonForChoice(i, _choices.Count)))
            {
                Action chosen = _choices[i].action;
                Clear();
                chosen?.Invoke();
                return;
            }
        }

        if ((_timer += GameTime.Delta / _messageDuration) >= 1.0)
            Clear();
    }

    void Clear()
    {
        if (!_isOpen) return;
        _isOpen      = false;
        _box.Visible = false;
        _choices     = null;
        playerEvents.OnDialogStateChanged?.Invoke(false);
    }
}
