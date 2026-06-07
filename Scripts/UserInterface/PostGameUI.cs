using Godot;

/// <summary>
/// Results overlay for the PostGame state — Godot port of Unity's PostGameUI. Full-screen dark
/// panel with a "MATCH OVER" header, a winner banner (crown + name), and four navigable rows
/// (Replay / Choose Characters / Leave / Quit). PostGame drives selection; this just renders.
/// </summary>
public class PostGameUI
{
    CanvasLayer _layer;
    Label[]     _rows;

    public static readonly string[] Labels = { "Replay", "Choose Characters", "Leave", "Quit" };
    static readonly Color Selected = new Color(1f, 0.85f, 0.2f);
    static readonly Color Normal   = new Color(0.80f, 0.80f, 0.84f);

    public void Initialize(Node parent, string winner)
    {
        _layer = new CanvasLayer { Name = "PostGameUI", Layer = 100 };
        parent.AddChild(_layer);

        var bg = new ColorRect { Color = new Color(0.05f, 0.05f, 0.06f, 0.92f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(bg);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(center);

        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 14);
        center.AddChild(col);

        var header = new Label { Text = "MATCH OVER", HorizontalAlignment = HorizontalAlignment.Center };
        header.AddThemeFontSizeOverride("font_size", 48);
        col.AddChild(header);

        if (RoleIcons.Crown != null)
        {
            var crown = new TextureRect
            {
                Texture = RoleIcons.Crown,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
                CustomMinimumSize = new Vector2(72, 52),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            };
            col.AddChild(crown);
        }

        var win = new Label
        {
            Text = string.IsNullOrEmpty(winner) ? "WINNER!" : $"{winner.ToUpper()}  WINS!",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        win.AddThemeFontSizeOverride("font_size", 34);
        win.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
        col.AddChild(win);

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14) }); // spacer

        _rows = new Label[Labels.Length];
        for (int i = 0; i < Labels.Length; i++)
        {
            var l = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            l.AddThemeFontSizeOverride("font_size", 26);
            col.AddChild(l);
            _rows[i] = l;
        }

        var hint = new Label { Text = "Up/Down move    A/Enter select", HorizontalAlignment = HorizontalAlignment.Center };
        hint.AddThemeFontSizeOverride("font_size", 16);
        hint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.64f));
        col.AddChild(hint);
    }

    public void Refresh(int selected)
    {
        if (_rows == null) return;
        for (int i = 0; i < _rows.Length; i++)
        {
            bool sel = i == selected;
            _rows[i].Text = (sel ? "► " : "   ") + Labels[i];
            _rows[i].AddThemeColorOverride("font_color", sel ? Selected : Normal);
        }
    }

    public void Destroy()
    {
        _layer?.QueueFree();
        _layer = null;
        _rows = null;
    }
}
