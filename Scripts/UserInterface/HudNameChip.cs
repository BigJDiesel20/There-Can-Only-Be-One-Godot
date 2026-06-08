using Godot;

/// <summary>
/// A HUD name label, ported from Unity's PlayerStatBarUI name label / team-list row:
/// a dark box holding an optional role-icon slot (human figure + leader crown) followed by the
/// player's name. Used three ways, exactly like Unity:
///   • the local player's name (icon on the left, bold white)
///   • each teammate stacked as a row above it (icon on the left, light grey)
///   • the locked target's name (no icon, right-aligned)
///
/// The figure PNG is a solid-black silhouette, so it's recoloured to a light silhouette via a
/// tiny canvas_item shader (Unity tinted it; a plain Modulate can't lighten black) so it reads on
/// the semi-transparent black box. The crown keeps its own gold colours.
/// </summary>
public partial class HudNameChip : PanelContainer
{
    readonly Label _label;
    TextureRect _figure, _crown;

    static ShaderMaterial _silhouette;
    static ShaderMaterial Silhouette()
    {
        if (_silhouette != null) return _silhouette;
        _silhouette = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = "shader_type canvas_item;\n" +
                       "void fragment() {\n" +
                       "    float a = texture(TEXTURE, UV).a;\n" +
                       "    COLOR = vec4(0.93, 0.93, 0.96, a);\n" + // light silhouette
                       "}\n",
            },
        };
        return _silhouette;
    }

    public HudNameChip(float height, float fontSize, bool showIcon, Color textColor, bool bold)
    {
        MouseFilter        = MouseFilterEnum.Ignore;
        CustomMinimumSize  = new Vector2(0f, height);

        var style = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.55f) };
        style.ContentMarginLeft = style.ContentMarginRight = 4f;
        style.ContentMarginTop  = style.ContentMarginBottom = 0f;
        AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 3);
        AddChild(row);

        // Square role-icon slot on the leading (left) edge — always reserved, like Unity, so the
        // name stays aligned whether or not the role icon is showing.
        if (showIcon)
            row.AddChild(BuildIconSlot(height));

        _label = new Label
        {
            Text              = "",
            MouseFilter       = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _label.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(Mathf.Max(8f, fontSize)));
        _label.AddThemeColorOverride("font_color", textColor);
        _label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        _label.AddThemeConstantOverride("outline_size", Mathf.RoundToInt(Mathf.Max(2f, fontSize * 0.12f)));
        if (bold) _label.AddThemeConstantOverride("outline_size", Mathf.RoundToInt(Mathf.Max(3f, fontSize * 0.16f)));
        AddThemeToBold(_label, bold);
        row.AddChild(_label);
    }

    static void AddThemeToBold(Label label, bool bold)
    {
        // Godot has no built-in bold flag on Label; emulate weight with a slightly heavier outline.
        if (!bold) return;
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 1f));
    }

    Control BuildIconSlot(float size)
    {
        var slot = new Control
        {
            CustomMinimumSize = new Vector2(size, size),
            MouseFilter       = MouseFilterEnum.Ignore,
        };

        // Figure — 0.75 scale within the slot, like Unity → inset ~12.5% each side.
        _figure = NewIcon(RoleIcons.Figure);
        _figure.Material = Silhouette();
        _figure.SetAnchorsPreset(LayoutPreset.FullRect);
        float inset = size * 0.125f;
        _figure.OffsetLeft = inset; _figure.OffsetTop = inset;
        _figure.OffsetRight = -inset; _figure.OffsetBottom = -inset;
        slot.AddChild(_figure);

        // Crown — sits snug on the figure's head: narrower span (smaller crown) and lowered so it
        // rests on the head instead of floating above it.
        _crown = NewIcon(RoleIcons.Crown);
        _crown.AnchorLeft = 0.24f; _crown.AnchorRight = 0.76f;
        _crown.AnchorTop  = 0.05f; _crown.AnchorBottom = 0.32f;
        slot.AddChild(_crown);

        _figure.Visible = false;
        _crown.Visible  = false;
        return slot;
    }

    static TextureRect NewIcon(Texture2D tex) => new TextureRect
    {
        Texture     = tex,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public void SetText(string text) => _label.Text = text ?? "";

    public void SetRole(TeamController.Status role)
    {
        if (_figure == null) return; // no icon slot (target chip)
        bool teamed = role != TeamController.Status.Solo;
        _figure.Texture = RoleIcons.Figure;            // refresh in case PNGs imported late
        _figure.Visible = teamed;
        _crown.Texture  = RoleIcons.Crown;
        _crown.Visible  = role == TeamController.Status.Leader;
    }
}
