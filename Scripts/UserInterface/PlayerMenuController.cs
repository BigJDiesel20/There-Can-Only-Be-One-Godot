using Godot;
using System.Text;

/// <summary>
/// Per-player in-game menu (combo list, team actions, etc.). This is NOT a pause — the match
/// keeps running for everyone; opening it only puts THIS player into the Menu input context
/// (so they can't act while it's open). Opened with Start during Battle/Prone — the
/// PlayerStateMachine handles that (GetMenuOpenButtonDown → EnterMenu → PlayerState_Menu fires
/// OnMenuStateChanged(true)). This controller shows its panel on that event and, while open,
/// closes on B (GetMenuButtonDown is gated to the Menu context, so it can't fire on the same
/// frame Start opened it). Closing fires OnMenuStateChanged(false) → the state machine returns
/// the player to their previous state.
///
/// Renders into the player's own SubViewport (split-screen) as a contained box — it does not
/// dim the view, since gameplay is still live behind it.
/// </summary>
public class PlayerMenuController
{
    LocalPlayerManager _owner;
    PlayerInput        _gamePad;
    PlayerEvents       _playerEvents;

    CanvasLayer _ui;
    bool _open;

    public void Initialize(LocalPlayerManager owner, Node renderParent, PlayerInput gamePad, PlayerEvents playerEvents)
    {
        _owner        = owner;
        _gamePad      = gamePad;
        _playerEvents = playerEvents;

        BuildUI(renderParent);

        _playerEvents.OnMenuStateChanged += OnMenuStateChanged;
        _playerEvents.OnUpdate           += OnUpdate;
    }

    public void Deactivate()
    {
        if (_playerEvents != null)
        {
            _playerEvents.OnMenuStateChanged -= OnMenuStateChanged;
            _playerEvents.OnUpdate           -= OnUpdate;
            _playerEvents = null;
        }
        _ui?.QueueFree();
        _ui = null;
    }

    void OnMenuStateChanged(bool open)
    {
        _open = open;
        if (_ui != null) _ui.Visible = open;
    }

    void OnUpdate()
    {
        if (!_open) return;
        if (_gamePad.GetMenuButtonDown("B"))
            _playerEvents.OnMenuStateChanged?.Invoke(false); // close
    }

    void BuildUI(Node parent)
    {
        _ui = new CanvasLayer { Name = "PlayerMenu", Layer = 15, Visible = false };
        parent.AddChild(_ui);

        // A contained box centered in the player's view — no full-screen dim, since the match
        // is still live behind it.
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        center.MouseFilter = Control.MouseFilterEnum.Ignore;
        _ui.AddChild(center);

        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor             = new Color(0.05f, 0.06f, 0.09f, 0.92f),
            BorderColor         = new Color(0.5f, 0.7f, 1f, 0.9f),
            ContentMarginLeft   = 24, ContentMarginRight = 24,
            ContentMarginTop    = 18, ContentMarginBottom = 18,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        };
        style.SetBorderWidthAll(2);
        panel.AddThemeStyleboxOverride("panel", style);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        var title = new Label { Text = "MENU", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        vbox.AddChild(title);

        var combos = new Label { Text = BuildComboList(), HorizontalAlignment = HorizontalAlignment.Center };
        combos.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(combos);

        var hint = new Label { Text = "Press B to Close", HorizontalAlignment = HorizontalAlignment.Center };
        hint.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(hint);
    }

    string BuildComboList()
    {
        var sb = new StringBuilder("— Combos —\n");
        var config = _owner?.attackConfig;
        if (config?.combos == null || config.combos.Count == 0)
            return sb.Append("(none)").ToString();

        foreach (ComboData combo in config.combos)
        {
            if (combo?.steps == null) continue;
            sb.Append(combo.comboName).Append(":  ");
            for (int i = 0; i < combo.steps.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(ButtonLetter(combo.steps[i].inputType));
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    static string ButtonLetter(HitBoxTriggerEvents.AttackType t) => t switch
    {
        HitBoxTriggerEvents.AttackType.Light    => "X",
        HitBoxTriggerEvents.AttackType.Heavy    => "Y",
        HitBoxTriggerEvents.AttackType.Special  => "B",
        HitBoxTriggerEvents.AttackType.Launcher => "A",
        _                                       => "?",
    };
}
