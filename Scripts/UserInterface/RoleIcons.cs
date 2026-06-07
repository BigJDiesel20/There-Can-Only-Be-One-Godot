using Godot;

/// <summary>
/// Loads the team-role icon textures copied from Unity (res://Art/HumanFigure.png and
/// res://Art/Crowns/crown_01.png). Null-safe until the editor imports the PNGs.
/// </summary>
public static class RoleIcons
{
    static Texture2D _figure, _crown;

    public static Texture2D Figure
    {
        get
        {
            if (_figure == null && ResourceLoader.Exists("res://Art/HumanFigure.png"))
                _figure = GD.Load<Texture2D>("res://Art/HumanFigure.png");
            return _figure;
        }
    }

    public static Texture2D Crown
    {
        get
        {
            if (_crown == null && ResourceLoader.Exists("res://Art/Crowns/crown_01.png"))
                _crown = GD.Load<Texture2D>("res://Art/Crowns/crown_01.png");
            return _crown;
        }
    }
}
