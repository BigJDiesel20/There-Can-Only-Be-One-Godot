using Godot;

/// <summary>
/// Tiles the window into one SubViewport per player for local split-screen. Every
/// SubViewport shares the main World3D, so all players see the same 3D scene from their
/// own camera. Each player's camera + HUD are parented into their SubViewport.
///
/// The SubViewportContainers live under a CanvasLayer so they lay out against the window
/// (a Control parented under Node3D nodes would not get a viewport-relative rect).
/// Replaces Unity's per-camera Camera.rect (Godot composites SubViewports instead).
/// </summary>
public partial class SplitScreenRig : Node
{
    public SubViewport[] Viewports;

    public void Build(int count, World3D sharedWorld)
    {
        var layer = new CanvasLayer { Name = "SplitScreenLayer" };
        AddChild(layer);

        Viewports = new SubViewport[count];
        int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt((float)count / cols);

        // Cell pixel size — set on each SubViewport up front so per-player HUD scaling can read a
        // correct size immediately (the container's Stretch only assigns it on the next layout pass).
        Vector2 win = GetTree().Root.GetVisibleRect().Size;
        var cellSize = new Vector2I(Mathf.Max(1, (int)(win.X / cols)), Mathf.Max(1, (int)(win.Y / rows)));

        for (int i = 0; i < count; i++)
        {
            int col = i % cols;
            int row = i / cols;

            var container = new SubViewportContainer
            {
                Stretch     = true,
                // Stop (not Ignore) so mouse clicks forward into the SubViewport's GUI — this lets
                // the teaming dialog choices be confirmed by clicking. Gamepad input is polled, so
                // it is unaffected by this change.
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            // Anchor to this player's grid cell (fractions of the window).
            container.AnchorLeft   = (float)col       / cols;
            container.AnchorRight  = (float)(col + 1) / cols;
            container.AnchorTop    = (float)row       / rows;
            container.AnchorBottom = (float)(row + 1) / rows;
            container.OffsetLeft = container.OffsetTop = container.OffsetRight = container.OffsetBottom = 0;
            layer.AddChild(container);

            var vp = new SubViewport
            {
                World3D                = sharedWorld,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                // true so SubViewportContainer-forwarded mouse events reach in-viewport GUI (the
                // teaming dialog buttons). Gamepad/keyboard are polled, not event-driven, so this
                // doesn't affect player input routing.
                HandleInputLocally     = true,
                Size                   = cellSize,
            };
            container.AddChild(vp);
            Viewports[i] = vp;

            // Thin border around this cell so split-screen views read as separate panels
            // (Godot port of Unity's CameraViewportBorder). Drawn on the same layer, over the
            // viewport texture; transparent fill so only the outline shows.
            if (count > 1)
            {
                var border = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
                border.AnchorLeft = container.AnchorLeft; border.AnchorRight = container.AnchorRight;
                border.AnchorTop  = container.AnchorTop;  border.AnchorBottom = container.AnchorBottom;
                border.OffsetLeft = border.OffsetTop = border.OffsetRight = border.OffsetBottom = 0;
                var bstyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = Colors.Black };
                bstyle.SetBorderWidthAll(2);
                border.AddThemeStyleboxOverride("panel", bstyle);
                layer.AddChild(border);
            }
        }
    }
}
