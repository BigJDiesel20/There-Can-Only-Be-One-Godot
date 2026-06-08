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

		// The 9 frames in AuraFlame.png are NOT registered — each draws the flame at a different
		// spot AND a different height inside its 100×100 cell. Find each frame's solid bounding box…
		var bx = new int[9]; var by = new int[9]; var bw = new int[9]; var bh = new int[9];
		int maxH = 1;
		for (int i = 0; i < 9; i++)
		{
			int cx = (i % 3) * 100, cy = (i / 3) * 100;
			int mnx = 100, mny = 100, mxx = -1, mxy = -1;
			for (int y = 0; y < 100; y++)
				for (int x = 0; x < 100; x++)
					if (sheet.GetPixel(cx + x, cy + y).A > 0.3f) // solid threshold: ignore faint wisps
					{ if (x < mnx) mnx = x; if (x > mxx) mxx = x; if (y < mny) mny = y; if (y > mxy) mxy = y; }
			if (mxx < mnx) { mnx = 0; mny = 0; mxx = 99; mxy = 99; }
			bx[i] = cx + mnx; by[i] = cy + mny; bw[i] = mxx - mnx + 1; bh[i] = mxy - mny + 1;
			if (bh[i] > maxH) maxH = bh[i];
		}

		// …then scale every frame to the SAME height (preserving aspect) and bottom-align it, so all
		// 9 share one vertical envelope — base on the bottom line, tip on the top line. Merely
		// bottom-aligning the raw bboxes (the previous approach) still let the height swing ~27% per
		// frame, so the tip kept "popping up" and the animation looked like it wrapped bottom-to-top.
		// Locking the envelope makes the flame flicker in shape only, rising from a fixed base.
		var sw = new int[9];
		int canvasW = 1;
		for (int i = 0; i < 9; i++)
		{
			sw[i] = Mathf.Max(1, Mathf.RoundToInt(bw[i] * (float)maxH / bh[i]));
			if (sw[i] > canvasW) canvasW = sw[i];
		}

		_frames = new Texture2D[9];
		for (int i = 0; i < 9; i++)
		{
			var sub = Image.CreateEmpty(bw[i], bh[i], false, Image.Format.Rgba8);
			sub.BlitRect(sheet, new Rect2I(bx[i], by[i], bw[i], bh[i]), Vector2I.Zero);
			sub.Resize(sw[i], maxH, Image.Interpolation.Bilinear); // uniform height, aspect-preserved

			var canvas = Image.CreateEmpty(canvasW, maxH, false, Image.Format.Rgba8);
			canvas.Fill(new Color(0, 0, 0, 0));
			canvas.BlitRect(sub, new Rect2I(0, 0, sw[i], maxH), new Vector2I((canvasW - sw[i]) / 2, 0));
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
