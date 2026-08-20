using System.Collections.Generic;
using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Builds point-filtered sprites from ASCII art, so small pixel sprites can be authored
    /// legibly in code (one character per pixel) instead of imported as textures.
    /// Rows are given top-down, the way they read on screen.
    /// </summary>
    public static class PixelArt
    {
        /// <summary>Deterministic value noise in 0..1 — used for dithering tiles.</summary>
        public static float Noise(int x, int y, int seed = 0)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
            }
        }

        /// <summary>
        /// Turn ASCII rows into a sprite. '.' and ' ' are transparent; every other character is
        /// looked up in <paramref name="palette"/>.
        /// </summary>
        public static Sprite FromRows(string[] rows, Dictionary<char, Color> palette,
                                      float pixelsPerUnit = SpriteFactory.PPU, Vector2? pivot = null)
        {
            int h = rows.Length;
            int w = 0;
            foreach (string r in rows) w = Mathf.Max(w, r.Length);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            for (int y = 0; y < h; y++)
            {
                // Rows are authored top-down; texture space runs bottom-up.
                string row = rows[h - 1 - y];
                for (int x = 0; x < w; x++)
                {
                    char c = x < row.Length ? row[x] : '.';
                    Color col = c == '.' || c == ' ' ? Color.clear
                              : palette.TryGetValue(c, out var p) ? p
                              : Color.magenta;   // unmapped character: loud on purpose
                    tex.SetPixel(x, y, col);
                }
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h),
                                 pivot ?? new Vector2(0.5f, 0.5f),
                                 pixelsPerUnit, 0, SpriteMeshType.FullRect);
        }
    }
}
