using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Enchanted-forest tile art, generated as low-fidelity 16x16 pixel tiles with deterministic
    /// dithering so a tiled sprite reads as organic rock and moss rather than a flat colour.
    /// Everything is point-filtered and built from a tight palette for an arcade look.
    /// </summary>
    public static class TileArt
    {
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        // Forest palette: damp earth, deep moss, glowing motes.
        public static readonly Color Earth = Hex("#3E2C25");
        public static readonly Color EarthDark = Hex("#241A18");
        public static readonly Color EarthLight = Hex("#4F3A2F");
        public static readonly Color Moss = Hex("#1B6B4A");
        public static readonly Color MossBright = Hex("#2FA968");
        public static readonly Color GrassTop = Hex("#3FD17A");
        public static readonly Color Wood = Hex("#6B4A2F");
        public static readonly Color WoodLight = Hex("#8C6440");
        public static readonly Color Stone = Hex("#4A4A5A");
        public static readonly Color StoneLight = Hex("#6E6E82");
        public static readonly Color Crystal = Hex("#7BE8FF");
        public static readonly Color CrystalDeep = Hex("#2A7FA8");
        public static readonly Color Thorn = Hex("#8C1B3A");
        public static readonly Color ThornDark = Hex("#4A0E20");
        public static readonly Color Glow = Hex("#FFEC27");

        const int T = 16;

        /// <summary>Mossy earth: the main solid tile body.</summary>
        public static Sprite EarthTile(int seed = 7)
        {
            var tex = New(T, T);
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                float n = PixelArt.Noise(x, y, seed);
                Color c = n > 0.82f ? EarthLight
                        : n > 0.62f ? Earth
                        : n > 0.18f ? Earth * 0.92f
                        : EarthDark;
                c.a = 1f;

                // A few moss clumps clinging to the rock.
                if (PixelArt.Noise(x, y, seed + 91) > 0.93f) c = Moss;
                tex.SetPixel(x, y, c);
            }
            // Darken the outer edge so stacked tiles still show block seams.
            for (int i = 0; i < T; i++)
            {
                tex.SetPixel(i, 0, EarthDark);
                tex.SetPixel(0, i, Mul(tex.GetPixel(0, i), 0.8f));
                tex.SetPixel(T - 1, i, Mul(tex.GetPixel(T - 1, i), 0.8f));
            }
            return Finish(tex);
        }

        /// <summary>Grass + hanging vines, laid along the top edge of solid ground.</summary>
        public static Sprite GrassCap(int seed = 13)
        {
            const int H = 8;
            var tex = New(T, H);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < T; x++)
                tex.SetPixel(x, y, Color.clear);

            for (int x = 0; x < T; x++)
            {
                // Ragged grass line: 2-3 px of bright top, moss beneath.
                int top = H - 1;
                int thickness = 2 + (PixelArt.Noise(x, 3, seed) > 0.55f ? 1 : 0);
                for (int i = 0; i < thickness; i++)
                {
                    int y = top - i;
                    if (y < 0) continue;
                    tex.SetPixel(x, y, i == 0 ? GrassTop : MossBright);
                }

                // Occasional vine dangling down into the rock.
                if (PixelArt.Noise(x, 9, seed) > 0.72f)
                {
                    int len = 2 + Mathf.FloorToInt(PixelArt.Noise(x, 11, seed) * 3f);
                    for (int i = 0; i < len; i++)
                    {
                        int y = top - thickness - i;
                        if (y >= 0) tex.SetPixel(x, y, Moss);
                    }
                }

                // Rare glowing spore.
                if (PixelArt.Noise(x, 17, seed) > 0.94f)
                    tex.SetPixel(x, Mathf.Max(0, top - thickness - 1), Glow);
            }
            return Finish(tex);
        }

        /// <summary>Weathered planks for one-way platforms.</summary>
        public static Sprite PlankTile(int seed = 23)
        {
            var tex = New(T, T);
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                bool seam = y % 6 == 0;
                float n = PixelArt.Noise(x, y, seed);
                Color c = seam ? Mul(Wood, 0.6f) : n > 0.7f ? WoodLight : Wood;
                if (PixelArt.Noise(x, y, seed + 5) > 0.95f) c = Moss;   // moss creeping over
                c.a = 1f;
                tex.SetPixel(x, y, c);
            }
            for (int x = 0; x < T; x++)
            {
                tex.SetPixel(x, T - 1, MossBright);      // mossy top lip
                tex.SetPixel(x, 0, Mul(Wood, 0.5f));
            }
            return Finish(tex);
        }

        /// <summary>Carved runestone for gates.</summary>
        public static Sprite RunestoneTile(int seed = 31)
        {
            var tex = New(T, T);
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                float n = PixelArt.Noise(x, y, seed);
                Color c = n > 0.8f ? StoneLight : n > 0.3f ? Stone : Mul(Stone, 0.75f);
                c.a = 1f;
                tex.SetPixel(x, y, c);
            }
            // A glowing rune etched in the middle.
            for (int i = 4; i < 12; i++)
            {
                tex.SetPixel(8, i, Crystal);
                tex.SetPixel(i, 8, CrystalDeep);
            }
            tex.SetPixel(8, 8, Crystal);
            return Finish(tex);
        }

        /// <summary>Cracked crystal for breakable walls.</summary>
        public static Sprite CrystalTile(int seed = 37)
        {
            var tex = New(T, T);
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                float n = PixelArt.Noise(x, y, seed);
                Color c = n > 0.86f ? Crystal : n > 0.5f ? CrystalDeep : Mul(CrystalDeep, 0.7f);
                c.a = 1f;
                // Fracture lines running through it.
                if (Mathf.Abs((x + y) % 7 - 3) == 0) c = Mul(Crystal, 0.85f);
                tex.SetPixel(x, y, c);
            }
            return Finish(tex);
        }

        /// <summary>Bramble spikes for hazards: points along the top, dark tangle below.</summary>
        public static Sprite ThornTile(int seed = 41)
        {
            var tex = New(T, T);
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
                tex.SetPixel(x, y, Color.clear);

            // Tangled base.
            for (int y = 0; y < 6; y++)
            for (int x = 0; x < T; x++)
            {
                float n = PixelArt.Noise(x, y, seed);
                if (n > 0.35f) tex.SetPixel(x, y, n > 0.75f ? Thorn : ThornDark);
            }

            // Four triangular spikes.
            for (int s = 0; s < 4; s++)
            {
                int cx = s * 4 + 2;
                for (int i = 0; i < 8; i++)
                {
                    int half = (8 - i) / 3;
                    for (int dx = -half; dx <= half; dx++)
                    {
                        int x = cx + dx;
                        if (x >= 0 && x < T) tex.SetPixel(x, 5 + i, i > 5 ? Thorn : ThornDark);
                    }
                }
                tex.SetPixel(cx, 13, Color.white);   // glinting tip
            }
            return Finish(tex);
        }

        /// <summary>A glowing shrine marker for checkpoints.</summary>
        public static Sprite ShrineTile(int seed = 53)
        {
            var tex = New(T, T);
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(7.5f, 7.5f));
                Color c = Color.clear;
                if (d < 3f) c = Crystal;
                else if (d < 5f) c = CrystalDeep;
                else if (d < 7f && PixelArt.Noise(x, y, seed) > 0.55f) c = Mul(CrystalDeep, 0.7f);
                tex.SetPixel(x, y, c);
            }
            return Finish(tex);
        }

        /// <summary>A distant tree silhouette used for parallax backdrop layers.</summary>
        public static Sprite TreeSilhouette(int width, int height, Color color, int seed)
        {
            var tex = New(width, height);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                tex.SetPixel(x, y, Color.clear);

            int trunkX = width / 2;
            int trunkW = Mathf.Max(1, width / 8);
            int canopyBase = Mathf.RoundToInt(height * 0.35f);

            for (int y = 0; y < canopyBase; y++)
            for (int x = trunkX - trunkW; x <= trunkX + trunkW; x++)
                if (x >= 0 && x < width) tex.SetPixel(x, y, color);

            // Layered canopy blobs narrowing toward the top.
            for (int y = canopyBase; y < height; y++)
            {
                float t = (y - canopyBase) / (float)Mathf.Max(1, height - canopyBase);
                int half = Mathf.RoundToInt(Mathf.Lerp(width * 0.5f, width * 0.06f, t * t));
                for (int x = trunkX - half; x <= trunkX + half; x++)
                {
                    if (x < 0 || x >= width) continue;
                    if (PixelArt.Noise(x, y, seed) > 0.18f) tex.SetPixel(x, y, color);
                }
            }
            return Finish(tex);
        }

        // ---------------------------------------------------------------- helpers
        static Color Mul(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a);

        static Texture2D New(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        static Sprite Finish(Texture2D tex)
        {
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                 new Vector2(0.5f, 0.5f), SpriteFactory.PPU, 0, SpriteMeshType.FullRect);
        }
    }
}
