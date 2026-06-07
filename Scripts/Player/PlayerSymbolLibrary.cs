using Godot;
using System.Collections.Generic;

/// <summary>
/// Loads the per-player symbol textures (copied from Unity into res://Art/Symbols/) and hands
/// out a PlayerSymbolEntry per index. Godot port of Unity's PlayerSymbolLibrary.
///
/// NOTE: the PNGs must be imported by the Godot editor first (open the project once) before
/// these load — until then GetEntry returns an entry with a null sprite (shows nothing, no crash).
/// </summary>
public static class PlayerSymbolLibrary
{
	static Texture2D[] _symbols;

	public static PlayerSymbolEntry GetEntry(int index)
	{
		EnsureLoaded();
		var entry = new PlayerSymbolEntry { symbolColor = Colors.White, glowColor = Colors.White };
		if (_symbols.Length > 0)
		{
			int i = ((index % _symbols.Length) + _symbols.Length) % _symbols.Length;
			entry.sprite = _symbols[i];
		}
		return entry;
	}

	static void EnsureLoaded()
	{
		if (_symbols != null) return;
		var list = new List<Texture2D>();
		for (int i = 0; i < 16; i++)
		{
			string path = $"res://Art/Symbols/symbol_{i:00}.png";
			if (ResourceLoader.Exists(path))
				list.Add(Trim(GD.Load<Texture2D>(path)));
		}
		_symbols = list.ToArray();
	}

	/// <summary>
	/// Crops a symbol texture to the bounding box of its opaque pixels (returns an AtlasTexture of
	/// that region). The source PNGs have uneven transparent padding and the glyph isn't always
	/// centered within the canvas, so trimming makes the glyph's true center the texture's center —
	/// which is what lets the HUD center it perfectly in the pie's hole at any size. Best-effort:
	/// falls back to the original texture if the image can't be read.
	/// </summary>
	static Texture2D Trim(Texture2D tex)
	{
		if (tex == null) return null;
		Image img = tex.GetImage();
		if (img == null) return tex;
		if (img.IsCompressed() && img.Decompress() != Error.Ok) return tex;

		int w = img.GetWidth(), h = img.GetHeight();

		// Bound to the SOLID glyph core (alpha > 0.4) only — the PNGs have a faint semi-transparent
		// glow/halo baked around the glyph; including it made the box ~2× too big so the real glyph
		// rendered small inside it. Solid pixels = the actual symbol; their centroid centers it and
		// their bbox sizes it to fill the hole.
		const float Solid = 0.4f;
		double sx = 0, sy = 0; int n = 0;
		int minX = w, minY = h, maxX = -1, maxY = -1;
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				if (img.GetPixel(x, y).A < Solid) continue;
				sx += x; sy += y; n++;
				if (x < minX) minX = x; if (x > maxX) maxX = x;
				if (y < minY) minY = y; if (y > maxY) maxY = y;
			}
		if (n == 0 || maxX < minX) return tex; // nothing solid — leave as-is

		float cx = (float)(sx / n), cy = (float)(sy / n);

		// Square region centered on the centroid, sized to the glyph's larger bbox dimension so the
		// glyph fills the rect. Square → KeepAspectCentered adds no uneven padding.
		float half = Mathf.Max(maxX - minX, maxY - minY) * 0.5f + 1f;
		half = Mathf.Min(half, Mathf.Min(Mathf.Min(cx, w - cx), Mathf.Min(cy, h - cy))); // keep in bounds

		return new AtlasTexture { Atlas = tex, Region = new Rect2(cx - half, cy - half, half * 2f, half * 2f) };
	}
}
