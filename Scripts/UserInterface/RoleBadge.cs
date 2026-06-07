using Godot;

/// <summary>
/// A team-role chip: a human-figure silhouette with a crown overlaid on its head.
/// Port of Unity's name-label / team-list role icon composite:
///   Leader   → figure + crown
///   Follower → figure only
///   Solo     → hidden
/// A light rounded backing keeps the solid-black figure readable over the HUD.
/// </summary>
public partial class RoleBadge : Control
{
    TextureRect _figure, _crown;

    public RoleBadge()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        var bg = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0.92f, 0.92f, 0.95f, 0.9f) };
        style.SetCornerRadiusAll(6);
        bg.AddThemeStyleboxOverride("panel", style);
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        _figure = NewIcon(RoleIcons.Figure);
        _figure.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_figure);

        _crown = NewIcon(RoleIcons.Crown);
        // Sit the crown on the figure's head: upper band, slightly poking above the top edge.
        _crown.AnchorLeft = 0.12f; _crown.AnchorRight = 0.88f;
        _crown.AnchorTop  = -0.20f; _crown.AnchorBottom = 0.42f;
        AddChild(_crown);

        Visible = false;
    }

    static TextureRect NewIcon(Texture2D tex) => new TextureRect
    {
        Texture     = tex,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
        MouseFilter = MouseFilterEnum.Ignore,
    };

    public void SetRole(TeamController.Status role)
    {
        bool teamed = role != TeamController.Status.Solo;
        Visible = teamed;
        // Refresh textures in case the PNGs imported after this badge was constructed.
        if (_figure != null) _figure.Texture = RoleIcons.Figure;
        if (_crown  != null) { _crown.Texture = RoleIcons.Crown; _crown.Visible = role == TeamController.Status.Leader; }
    }
}
