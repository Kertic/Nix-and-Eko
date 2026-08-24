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

        /// <summary>A thin arrow/bolt shape pointing +X.</summary>
        public static Sprite Arrow(Color shaft, Color head, int w = 16, int h = 6)
        {
            var tex = NewTex(w, h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, Color.clear);

            int midY = h / 2;
            // shaft
            for (int x = 0; x < w - 4; x++)
            {
                tex.SetPixel(x, midY, shaft);
                if (h > 4) tex.SetPixel(x, midY - 1, shaft);
            }
            // arrowhead triangle
            for (int i = 0; i < 4; i++)
                for (int y = midY - i; y <= midY - 1 + i; y++)
                    if (y >= 0 && y < h) tex.SetPixel(w - 4 + i, y, head);

            tex.Apply();
            return ToSprite(tex, new Vector2(0.15f, 0.5f)); // pivot near the nock
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
