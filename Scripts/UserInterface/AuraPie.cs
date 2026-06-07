using Godot;

/// <summary>
/// Radial aura gauge — a ring that fills clockwise from the top with the aura percentage.
/// Custom-drawn (DrawColoredPolygon fan + inner hole), so it needs no textures — the Godot
/// equivalent of Unity's Image radial-fill pie. The fancy glow halo + pulse animation from
/// the Unity original are omitted for now.
/// </summary>
public partial class AuraPie : Control
{
    float _pct = 1f;

    public Color FillColor    = new Color(0.53f, 0.81f, 0.98f);
    public Color BgColor      = new Color(0.85f, 0.08f, 0.08f);
    public Color OutlineColor = new Color(0f, 0f, 0f, 1f);
    public Color HoleColor    = new Color(0.06f, 0.06f, 0.10f, 1f);

    // Pulse (port of Unity's AuraPiePulse): fill brightness + a soft halo behind the ring
    // oscillate together at `PulseHz`. A subtle "this gauge is alive" glow.
    public bool  Pulse   = true;
    const float  PulseHz = 1.2f;
    float _t;

    public override void _Process(double delta)
    {
        if (!Pulse) return;
        _t += (float)delta;
        QueueRedraw();
    }

    public void SetPercent(float p)
    {
        _pct = Mathf.Clamp(p, 0f, 1f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 c = Size * 0.5f;
        float   r = Mathf.Min(Size.X, Size.Y) * 0.5f;
        if (r <= 0f) return;

        // 0→1→0 sinusoid driving the fill brightness pulse (kept entirely inside the ring — no
        // external halo, so nothing spills past the pie).
        float t = Pulse ? (Mathf.Sin(_t * PulseHz * Mathf.Tau) + 1f) * 0.5f : 1f;

        DrawCircle(c, r, OutlineColor);     // outline
        DrawCircle(c, r - 2f, BgColor);     // depleted background

        if (_pct > 0.0001f)
        {
            // Fill brightness pulses between a dim and a bright tint of FillColor.
            float b = Mathf.Lerp(0.78f, 1f, t);
            var fill = new Color(FillColor.R * b, FillColor.G * b, FillColor.B * b, FillColor.A);
            int seg = Mathf.Max(3, (int)(_pct * 64));
            var pts = new Vector2[seg + 2];
            pts[0] = c;
            float start = -Mathf.Pi * 0.5f;            // start at the top
            for (int i = 0; i <= seg; i++)
            {
                float a = start + _pct * Mathf.Tau * (i / (float)seg); // clockwise (Y is down)
                pts[i + 1] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r - 2f);
            }
            DrawColoredPolygon(pts, fill);
        }

        DrawCircle(c, (r - 2f) * 0.62f, HoleColor); // inner hole → ring look (sized to hold the symbol)
    }
}
