using Godot;

/// <summary>
/// A flickering flame icon — cycles the 9 frames of res://Art/AuraFlame.png (a 3×3, 100×100 sheet
/// with a 10px inset, sliced per Unity's AuraFlameImporter). Godot port of Unity's
/// AuraFlameAnimator (the heat-distortion material is omitted). Null-safe until the PNG imports.
///
/// Flicker matches Unity: it plays slowly when idle and faster while its segment is actively
/// draining; each icon gets a small random speed multiplier + a random start frame/phase so the
/// row never flickers in lock-step.
/// </summary>
public partial class AnimatedFlame : TextureRect
{
	static Texture2D   _sheet;
	static Texture2D[] _frames;

	public float SlowFps = 12f;   // idle flicker (Unity slowFps)
	public float FastFps = 24f;   // flicker while this segment is draining (Unity fastFps)
	public bool  Draining;        // set by the HUD on the segment that's actively changing

	float _t;          // frames-elapsed accumulator
	int   _frame;
	float _speedMul = 1f;

	public override void _Ready()
	{
		// Stretch the frame to fill the (tall) cell — Unity uses preserveAspect=false at full pie
		// height. The frame's built-in transparent padding keeps adjacent flames separate.
		StretchMode = StretchModeEnum.Scale;
		ExpandMode  = ExpandModeEnum.IgnoreSize;
		MouseFilter = MouseFilterEnum.Ignore;

		EnsureFrames();
		if (_frames != null && _frames.Length > 0)
		{
			_frame  = (int)(GD.Randi() % (uint)_frames.Length); // random start frame (desync)
			Texture = _frames[_frame];
		}
		_speedMul = 1f + (float)GD.RandRange(-0.06, 0.06);       // per-icon ±6% speed variance
		_t        = (float)GD.RandRange(0.0, 1.0);               // random phase offset
	}

	public override void _Process(double delta)
	{
		if (_frames == null || _frames.Length == 0) { EnsureFrames(); return; }

		float fps = (Draining ? FastFps : SlowFps) * _speedMul;
		_t += (float)delta * fps;
		while (_t >= 1f)
		{
			_t -= 1f;
			_frame  = (_frame + 1) % _frames.Length;
			Texture = _frames[_frame];
		}
	}

	static void EnsureFrames()
	{
		if (_frames != null) return;
		if (!ResourceLoader.Exists("res://Art/AuraFlame.png")) return;

		_sheet = GD.Load<Texture2D>("res://Art/AuraFlame.png");

		Image sheet = _sheet.GetImage();
		if (sheet == null || (sheet.IsCompressed() && sheet.Decompress() != Error.Ok))
		{
			_frames = SimpleRegions(); // fallback: raw cells (may wobble, but no crash)
			return;
		}
		if (sheet.GetFormat() != Image.Format.Rgba8) sheet.Convert(Image.Format.Rgba8);

		// The 9 frames in AuraFlame.png are NOT registered — each draws the flame in a different
		// spot inside its 100×100 cell, so playing them raw makes the flame jump around. Re-render
		// each frame into a common canvas, horizontally centered and bottom-aligned, so it animates
		// in place (the flame's base stays put, only its shape flickers).
		var bx = new int[9]; var by = new int[9]; var bw = new int[9]; var bh = new int[9];
		int maxW = 1, maxH = 1;
		for (int i = 0; i < 9; i++)
		{
			int cx = (i % 3) * 100, cy = (i / 3) * 100;
			int mnx = 100, mny = 100, mxx = -1, mxy = -1;
			for (int y = 0; y < 100; y++)
				for (int x = 0; x < 100; x++)
					if (sheet.GetPixel(cx + x, cy + y).A > 0.1f)
					{ if (x < mnx) mnx = x; if (x > mxx) mxx = x; if (y < mny) mny = y; if (y > mxy) mxy = y; }
			if (mxx < mnx) { mnx = 0; mny = 0; mxx = 99; mxy = 99; }
			bx[i] = cx + mnx; by[i] = cy + mny; bw[i] = mxx - mnx + 1; bh[i] = mxy - mny + 1;
			if (bw[i] > maxW) maxW = bw[i];
			if (bh[i] > maxH) maxH = bh[i];
		}

		_frames = new Texture2D[9];
		for (int i = 0; i < 9; i++)
		{
			var canvas = Image.CreateEmpty(maxW, maxH, false, Image.Format.Rgba8);
			canvas.Fill(new Color(0, 0, 0, 0));
			int dx = (maxW - bw[i]) / 2; // center horizontally
			int dy = maxH - bh[i];       // bottom-align (flames rise from a common baseline)
			canvas.BlitRect(sheet, new Rect2I(bx[i], by[i], bw[i], bh[i]), new Vector2I(dx, dy));
			_frames[i] = ImageTexture.CreateFromImage(canvas);
		}
	}

	static Texture2D[] SimpleRegions()
	{
		var fr = new Texture2D[9];
		for (int i = 0; i < 9; i++)
		{
			int row = i / 3, col = i % 3;
			fr[i] = new AtlasTexture { Atlas = _sheet, Region = new Rect2(col * 100 + 10, row * 100 + 10, 80, 80) };
		}
		return fr;
	}
}
