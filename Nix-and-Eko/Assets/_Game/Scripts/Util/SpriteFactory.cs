using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Generates simple point-filtered placeholder sprites at runtime/edit time so levels can be
    /// blocked out with a crisp "retro" look before real pixel art exists.
    /// </summary>
    public static class SpriteFactory
    {
        /// <summary>Pixels-per-unit used for generated sprites (keep consistent for pixel-perfect).</summary>
        public const int PPU = 16;

        /// <summary>A square flat color tile with a subtle darker border — a chunky retro block.</summary>
        public static Sprite SolidTile(Color fill, int pixels = 16, Color? border = null)
            => SolidRect(fill, pixels, pixels, border);

        /// <summary>A rectangular flat color tile with a subtle darker border.</summary>
        public static Sprite SolidRect(Color fill, int w = 16, int h = 16, Color? border = null)
        {
            var tex = NewTex(w, h);
            Color b = border ?? fill * 0.65f;
            b.a = 1f;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool edge = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                tex.SetPixel(x, y, edge ? b : fill);
            }
            tex.Apply();
            return ToSprite(tex);
        }

        /// <summary>
        /// A flat-color bar with a left-edge pivot, so scaling its transform down on X shrinks
        /// it from the right while the left edge stays put — for a fuel/resource meter that
        /// drains toward zero without needing to reposition it every frame.
        /// </summary>
        public static Sprite Bar(Color fill, int w = 16, int h = 4)
        {
            var tex = NewTex(w, h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, fill);
            tex.Apply();
            return ToSprite(tex, new Vector2(0f, 0.5f));
        }

        /// <summary>
        /// A gently bowed bar — a shallow arc like a Skyrim status meter, center-pivoted so
        /// scaling its transform down on X shrinks it symmetrically toward the middle from both
        /// ends. <paramref name="arc"/> is how many pixels the ends rise above the center;
        /// <paramref name="thickness"/> is the strip's vertical width.
        /// </summary>
        public static Sprite CurvedBar(Color fill, int w = 24, int thickness = 3, int arc = 3, Color? border = null)
        {
            int h = thickness + arc + 2;               // room for the bow + a 1px margin top/bottom
            var tex = NewTex(w, h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, Color.clear);

            Color b = border ?? fill * 0.55f;
            b.a = fill.a;

            float baseY = 1f + thickness * 0.5f;       // vertical center of the strip at mid-span
            for (int x = 0; x < w; x++)
            {
                float t = w <= 1 ? 0f : (x / (float)(w - 1)) * 2f - 1f;   // -1..1 across the span
                float centerY = baseY + arc * (t * t);                    // ends bow upward
                int lo = Mathf.RoundToInt(centerY - thickness * 0.5f);
                int hi = lo + thickness - 1;
                for (int y = lo; y <= hi; y++)
                {
                    if (y < 0 || y >= h) continue;
                    bool edge = y == lo || y == hi || x == 0 || x == w - 1;
                    tex.SetPixel(x, y, edge ? b : fill);
                }
            }
            tex.Apply();
            return ToSprite(tex);   // center pivot → symmetric shrink
        }

        /// <summary>A filled circle — anti-aliased at the edge by keeping the ring boundary crisp
        /// at Point filtering and dropping any pixel whose centre is outside the radius. Used for
        /// the Eko-ball and its trail particles so both read as round blue orbs, not pixel dice.</summary>
        public static Sprite SolidCircle(Color fill, int pixels = 8, Color? border = null)
        {
            var tex = NewTex(pixels, pixels);
            Vector2 c = new Vector2((pixels - 1) / 2f, (pixels - 1) / 2f);
            float r = pixels * 0.5f;
            Color b = border ?? fill;
            for (int y = 0; y < pixels; y++)
            for (int x = 0; x < pixels; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                if (d > r) tex.SetPixel(x, y, Color.clear);
                else if (d > r - 1f) tex.SetPixel(x, y, b);
                else tex.SetPixel(x, y, fill);
            }
            tex.Apply();
            return ToSprite(tex);
        }

        /// <summary>A hollow circle outline — used to mark where a drag gesture started.</summary>
        public static Sprite Circle(Color stroke, int pixels = 16, float thickness = 0.22f)
        {
            var tex = NewTex(pixels, pixels);
            Vector2 c = new Vector2((pixels - 1) / 2f, (pixels - 1) / 2f);
            float r = pixels * 0.5f;
            float inner = 1f - Mathf.Clamp01(thickness);

            for (int y = 0; y < pixels; y++)
            for (int x = 0; x < pixels; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / r;
                tex.SetPixel(x, y, d <= 1f && d >= inner ? stroke : Color.clear);
            }
            tex.Apply();
            return ToSprite(tex);
        }

        /// <summary>A concentric-ring target sprite for switches.</summary>
        public static Sprite Target(Color ring, Color center, int pixels = 16)
        {
            var tex = NewTex(pixels, pixels);
            Vector2 c = new Vector2((pixels - 1) / 2f, (pixels - 1) / 2f);
            float r = pixels * 0.5f;
            for (int y = 0; y < pixels; y++)
            for (int x = 0; x < pixels; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / r;
                Color col;
                if (d > 1f) col = Color.clear;
                else if (d > 0.66f) col = ring;
                else if (d > 0.33f) col = center;
                else col = ring;
                tex.SetPixel(x, y, col);
            }
            tex.Apply();
            return ToSprite(tex);
        }

        /// <summary>
        /// A sleek dart pointing +X: a thin 1px shaft, a long tapered arrowhead, and a small swept
        /// fletching at the nock — reads as a dart rather than a chunky bolt.
        /// </summary>
        public static Sprite Arrow(Color shaft, Color head, int w = 18, int h = 7)
        {
            var tex = NewTex(w, h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, Color.clear);

            int midY = h / 2;

            // Long tapered head: about the front third, widening from the tip back to a base.
            int headLen = Mathf.Clamp(Mathf.RoundToInt(w * 0.32f), 4, w - 4);
            int headStart = w - headLen;
            // Keep the head base slim (not the full sprite height) so it reads as a sleek dart tip.
            int maxHalf = Mathf.Clamp((h - 1) / 2, 1, 2);
            for (int x = headStart; x < w; x++)
            {
                float f = (float)(x - headStart) / Mathf.Max(1, w - 1 - headStart); // 0 at base → 1 at tip
                int half = Mathf.RoundToInt((1f - f) * maxHalf);
                for (int y = midY - half; y <= midY + half; y++)
                    if (y >= 0 && y < h) tex.SetPixel(x, y, head);
            }

            // Thin single-pixel shaft from the nock up to the head base.
            for (int x = 1; x < headStart; x++)
                tex.SetPixel(x, midY, shaft);

            // Swept fletching at the nock: a couple of pixels flaring out above and below.
            tex.SetPixel(0, midY, shaft);
            if (midY + 1 < h) tex.SetPixel(1, midY + 1, head);
            if (midY - 1 >= 0) tex.SetPixel(1, midY - 1, head);
            if (midY + 1 < h) tex.SetPixel(0, midY + 1, head);
            if (midY - 1 >= 0) tex.SetPixel(0, midY - 1, head);

            tex.Apply();
            return ToSprite(tex, new Vector2(0.1f, 0.5f)); // pivot near the nock
        }

        static Texture2D NewTex(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            return tex;
        }

        static Sprite ToSprite(Texture2D tex, Vector2? pivot = null)
        {
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                pivot ?? new Vector2(0.5f, 0.5f),
                PPU,
                0,
                SpriteMeshType.FullRect);
            return sprite;
        }
    }
}
