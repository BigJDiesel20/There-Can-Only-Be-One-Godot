using Godot;

/// <summary>
/// Tiny helper to build a full-screen menu overlay (CanvasLayer + dark background + a
/// centered title/info pair). Used by SplashScreen / Lobby / CharacterSelect. Code-built so
/// no .tscn is needed; the state frees the returned CanvasLayer on exit.
/// </summary>
public static class MenuUI
{
    public static (CanvasLayer layer, Label title, Label info) Create(Node parent, string titleText)
    {
        var layer = new CanvasLayer { Name = "MenuUI", Layer = 20 };
        parent.AddChild(layer);

        var bg = new ColorRect { Color = new Color(0.08f, 0.10f, 0.14f, 1f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(bg);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(center);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 28);
        center.AddChild(vbox);

        var title = new Label { Text = titleText, HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 40);
        vbox.AddChild(title);

        var info = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        info.AddThemeFontSizeOverride("font_size", 20);
        vbox.AddChild(info);

        return (layer, title, info);
    }
}
