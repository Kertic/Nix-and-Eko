using System.Collections.Generic;
using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Procedural glyph textures for controller inputs, so the pause menu (and any other UI that
    /// needs to show a button prompt) can render the actual PlayStation shape or Xbox letter
    /// rather than the raw path string like "buttonWest". Textures are pixel-art, built once and
    /// cached — same generated-art approach as the rest of the placeholder visuals, so no
    /// imported sprites are needed.
    ///
    /// <see cref="For"/> returns null for controls we don't have a graphic for (uncommon
    /// bindings, XR devices, an unrecognised control name) — callers should draw the text label
    /// as a fallback in that case.
    /// </summary>
    public static class ControllerGlyphs
    {
        public enum Kind { PS, Xbox, Generic }

        // Cache keyed by "path|kind" so two rows binding the same control share one texture.
        static readonly Dictionary<string, Texture2D> _cache = new();

        // Authentic-ish PS face-button colours.
        static readonly Color PsPink   = new Color(1f,   0.45f, 0.75f);
        static readonly Color PsGreen  = new Color(0.45f, 0.95f, 0.55f);
        static readonly Color PsRed    = new Color(1f,   0.30f, 0.30f);
        static readonly Color PsBlue   = new Color(0.35f, 0.65f, 1f);

        // Neutral chip colours for triggers / shoulders / dpad / start / select.
        static readonly Color Chip     = new Color(0.10f, 0.12f, 0.18f);
        static readonly Color ChipEdge = new Color(0.75f, 0.80f, 0.90f);
        static readonly Color XboxA    = new Color(0.50f, 0.90f, 0.40f);
        static readonly Color XboxB    = new Color(1f,   0.40f, 0.35f);
        static readonly Color XboxX    = new Color(0.40f, 0.65f, 1f);
        static readonly Color XboxY    = new Color(1f,   0.85f, 0.30f);

        const int Size = 28;
        static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);

        /// <summary>Get (or build and cache) the glyph for an effective binding path. Returns
        /// null for controls without a graphic — draw the text fallback.</summary>
        public static Texture2D For(string effectivePath, Kind kind)
        {
            if (string.IsNullOrEmpty(effectivePath)) return null;
            string key = effectivePath + "|" + kind;
            if (_cache.TryGetValue(key, out var tex) && tex != null) return tex;

            tex = Build(effectivePath, kind);
            if (tex != null) _cache[key] = tex;
            return tex;
        }

        static Texture2D Build(string effectivePath, Kind kind)
        {
            int slash = effectivePath.IndexOf('/');
            if (slash < 0) return null;
            string device = effectivePath.Substring(0, slash);
            string control = effectivePath.Substring(slash + 1);

            if (device.Contains("Gamepad")) return BuildGamepad(control, kind);
            // Keyboard/mouse glyphs are drawn as text pills by the pause menu itself — no
            // procedural texture needed.
            return null;
        }

        static Texture2D BuildGamepad(string control, Kind kind)
        {
            switch (control)
            {
                case "buttonSouth": return kind == Kind.PS ? PsCross()    : XboxLetter('A', XboxA);
                case "buttonEast":  return kind == Kind.PS ? PsCircle()   : XboxLetter('B', XboxB);
                case "buttonWest":  return kind == Kind.PS ? PsSquare()   : XboxLetter('X', XboxX);
                case "buttonNorth": return kind == Kind.PS ? PsTriangle() : XboxLetter('Y', XboxY);
                case "leftShoulder":    return Chip2Line(kind == Kind.PS ? "L" : "L", kind == Kind.PS ? "1" : "B");
                case "rightShoulder":   return Chip2Line(kind == Kind.PS ? "R" : "R", kind == Kind.PS ? "1" : "B");
                case "leftTrigger":     return Chip2Line(kind == Kind.PS ? "L" : "L", kind == Kind.PS ? "2" : "T");
                case "rightTrigger":    return Chip2Line(kind == Kind.PS ? "R" : "R", kind == Kind.PS ? "2" : "T");
                case "leftStickPress":  return Chip2Line("L", kind == Kind.PS ? "3" : "S");
                case "rightStickPress": return Chip2Line("R", kind == Kind.PS ? "3" : "S");
                case "start":           return DPadOrIcon(icon: PsStartIcon(kind));
                case "select":          return DPadOrIcon(icon: PsSelectIcon(kind));
                case "dpad":            return DPadTexture(all: true);
                case "dpad/up":         return DPadTexture(up: true);
                case "dpad/down":       return DPadTexture(down: true);
                case "dpad/left":       return DPadTexture(left: true);
                case "dpad/right":      return DPadTexture(right: true);
            }
            return null;
        }

        // ================================================================== PS face buttons
        static Texture2D PsSquare()
        {
            var tex = BlankTex();
            // Filled dark disc backing + magenta square outline. Two-pixel stroke for weight.
            DrawFilledCircle(tex, Size / 2, Size / 2, 12, Chip);
            DrawCircleOutline(tex, Size / 2, Size / 2, 12, ChipEdge, 1);
            DrawRectOutline(tex, Size / 2 - 6, Size / 2 - 6, 12, 12, PsPink, 2);
            tex.Apply();
            return tex;
        }

        static Texture2D PsTriangle()
        {
            var tex = BlankTex();
            DrawFilledCircle(tex, Size / 2, Size / 2, 12, Chip);
            DrawCircleOutline(tex, Size / 2, Size / 2, 12, ChipEdge, 1);
            // Equilateral-ish triangle outline pointing up.
            DrawTriangleOutline(tex, Size / 2, Size / 2 - 6, 12, PsGreen, 2);
            tex.Apply();
            return tex;
        }

        static Texture2D PsCircle()
        {
            var tex = BlankTex();
            DrawFilledCircle(tex, Size / 2, Size / 2, 12, Chip);
            DrawCircleOutline(tex, Size / 2, Size / 2, 12, ChipEdge, 1);
            DrawCircleOutline(tex, Size / 2, Size / 2, 6, PsRed, 2);
            tex.Apply();
            return tex;
        }

        static Texture2D PsCross()
        {
            var tex = BlankTex();
            DrawFilledCircle(tex, Size / 2, Size / 2, 12, Chip);
            DrawCircleOutline(tex, Size / 2, Size / 2, 12, ChipEdge, 1);
            // Two diagonals meeting in the middle.
            DrawLine(tex, Size / 2 - 5, Size / 2 - 5, Size / 2 + 5, Size / 2 + 5, PsBlue, 2);
            DrawLine(tex, Size / 2 - 5, Size / 2 + 5, Size / 2 + 5, Size / 2 - 5, PsBlue, 2);
            tex.Apply();
            return tex;
        }

        // ================================================================== Xbox face buttons
        static Texture2D XboxLetter(char letter, Color color)
        {
            var tex = BlankTex();
            DrawFilledCircle(tex, Size / 2, Size / 2, 12, Chip);
            DrawCircleOutline(tex, Size / 2, Size / 2, 12, color, 2);
            DrawLetter(tex, Size / 2, Size / 2, letter, color);
            tex.Apply();
            return tex;
        }

        // ================================================================== Shoulders / triggers / stick clicks
        static Texture2D Chip2Line(string top, string bottom)
        {
            var tex = BlankTex();
            DrawRoundedRect(tex, 2, 6, Size - 4, Size - 12, Chip, ChipEdge);
            DrawLetter(tex, Size / 2, Size / 2 + 4, top[0],    ChipEdge);
            DrawLetter(tex, Size / 2, Size / 2 - 4, bottom[0], ChipEdge);
            tex.Apply();
            return tex;
        }

        // ================================================================== Start / Select
        static Texture2D DPadOrIcon(Texture2D icon) => icon;

        static Texture2D PsStartIcon(Kind kind)
        {
            var tex = BlankTex();
            DrawFilledCircle(tex, Size / 2, Size / 2, 12, Chip);
            DrawCircleOutline(tex, Size / 2, Size / 2, 12, ChipEdge, 1);
            // Three short horizontal bars (menu / options / hamburger). Universal enough that
            // both PS and Xbox players recognise it.
            for (int i = -4; i <= 4; i += 4)
                DrawLine(tex, Size / 2 - 5, Size / 2 + i, Size / 2 + 5, Size / 2 + i, ChipEdge, 1);
            tex.Apply();
            return tex;
        }

        static Texture2D PsSelectIcon(Kind kind)
        {
            var tex = BlankTex();
            DrawFilledCircle(tex, Size / 2, Size / 2, 12, Chip);
            DrawCircleOutline(tex, Size / 2, Size / 2, 12, ChipEdge, 1);
            // Two overlapping panes — the "share / create / view" idiom.
            DrawRectOutline(tex, Size / 2 - 6, Size / 2 - 3, 8, 6, ChipEdge, 1);
            DrawRectOutline(tex, Size / 2 - 2, Size / 2 - 3, 8, 6, ChipEdge, 1);
            tex.Apply();
            return tex;
        }

        // ================================================================== D-Pad
        static Texture2D DPadTexture(bool all = false, bool up = false, bool down = false, bool left = false, bool right = false)
        {
            var tex = BlankTex();
            DrawFilledCircle(tex, Size / 2, Size / 2, 12, Chip);
            DrawCircleOutline(tex, Size / 2, Size / 2, 12, ChipEdge, 1);
            // Plus-shape.
            Color hi = ChipEdge, lo = new Color(ChipEdge.r * 0.35f, ChipEdge.g * 0.35f, ChipEdge.b * 0.35f);
            DrawRectFilled(tex, Size / 2 - 2, Size / 2 - 8, 4, 16, all ? hi : lo);
            DrawRectFilled(tex, Size / 2 - 8, Size / 2 - 2, 16, 4, all ? hi : lo);
            // Highlight the pressed direction.
            if (up)    DrawRectFilled(tex, Size / 2 - 2, Size / 2,     4, 8, hi);
            if (down)  DrawRectFilled(tex, Size / 2 - 2, Size / 2 - 8, 4, 8, hi);
            if (right) DrawRectFilled(tex, Size / 2,     Size / 2 - 2, 8, 4, hi);
            if (left)  DrawRectFilled(tex, Size / 2 - 8, Size / 2 - 2, 8, 4, hi);
            tex.Apply();
            return tex;
        }

        // ================================================================== Primitive drawing
        static Texture2D BlankTex()
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            var buf = new Color[Size * Size];
            for (int i = 0; i < buf.Length; i++) buf[i] = Transparent;
            tex.SetPixels(buf);
            return tex;
        }

        static void DrawFilledCircle(Texture2D tex, int cx, int cy, int r, Color c)
        {
            int r2 = r * r;
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= r2) SetPx(tex, cx + x, cy + y, c);
        }

        static void DrawCircleOutline(Texture2D tex, int cx, int cy, int r, Color c, int thickness)
        {
            int inner = (r - thickness) * (r - thickness);
            int outer = r * r;
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    int d = x * x + y * y;
                    if (d <= outer && d >= inner) SetPx(tex, cx + x, cy + y, c);
                }
        }

        static void DrawRectOutline(Texture2D tex, int x0, int y0, int w, int h, Color c, int thickness)
        {
            for (int t = 0; t < thickness; t++)
            {
                for (int x = x0; x < x0 + w; x++)
                {
                    SetPx(tex, x, y0 + t, c);
                    SetPx(tex, x, y0 + h - 1 - t, c);
                }
                for (int y = y0; y < y0 + h; y++)
                {
                    SetPx(tex, x0 + t, y, c);
                    SetPx(tex, x0 + w - 1 - t, y, c);
                }
            }
        }

        static void DrawRectFilled(Texture2D tex, int x0, int y0, int w, int h, Color c)
        {
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                    SetPx(tex, x, y, c);
        }

        static void DrawTriangleOutline(Texture2D tex, int apexX, int apexY, int height, Color c, int thickness)
        {
            // Triangle apex at (apexX, apexY), base half-width = height * ~0.87 (equilateral-ish).
            int halfBase = Mathf.RoundToInt(height * 0.87f);
            int baseY = apexY - height;
            // Left edge apex → bottom-left.
            DrawLine(tex, apexX, apexY, apexX - halfBase, baseY, c, thickness);
            // Right edge apex → bottom-right.
            DrawLine(tex, apexX, apexY, apexX + halfBase, baseY, c, thickness);
            // Base line.
            DrawLine(tex, apexX - halfBase, baseY, apexX + halfBase, baseY, c, thickness);
        }

        static void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, Color c, int thickness)
        {
            // Bresenham + per-pixel thickness square (small enough that a stamp is fine).
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            while (true)
            {
                for (int ty = -thickness / 2; ty <= thickness / 2; ty++)
                    for (int tx = -thickness / 2; tx <= thickness / 2; tx++)
                        SetPx(tex, x0 + tx, y0 + ty, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = err * 2;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx)  { err += dx; y0 += sy; }
            }
        }

        /// <summary>Very small 5x5 letter for A/B/X/Y/L/R/1/2/3/B/T/S. Not a full font — just the
        /// letters we actually need for controller labels.</summary>
        static void DrawLetter(Texture2D tex, int cx, int cy, char letter, Color c)
        {
            string[] rows = LetterRows(letter);
            if (rows == null) return;
            int h = rows.Length;
            int w = rows[0].Length;
            int startX = cx - w / 2;
            int startY = cy + h / 2 - 1;
            for (int r = 0; r < h; r++)
                for (int col = 0; col < w; col++)
                    if (rows[r][col] == '#') SetPx(tex, startX + col, startY - r, c);
        }

        /// <summary>Draw a rounded rect chip (pill-ish) with a background fill and 1px edge.</summary>
        static void DrawRoundedRect(Texture2D tex, int x0, int y0, int w, int h, Color fill, Color edge)
        {
            DrawRectFilled(tex, x0 + 1, y0, w - 2, h, fill);
            DrawRectFilled(tex, x0, y0 + 1, w, h - 2, fill);
            // 1px edge.
            for (int x = x0 + 1; x < x0 + w - 1; x++)
            {
                SetPx(tex, x, y0, edge);
                SetPx(tex, x, y0 + h - 1, edge);
            }
            for (int y = y0 + 1; y < y0 + h - 1; y++)
            {
                SetPx(tex, x0, y, edge);
                SetPx(tex, x0 + w - 1, y, edge);
            }
        }

        static void SetPx(Texture2D tex, int x, int y, Color c)
        {
            if ((uint)x >= (uint)tex.width || (uint)y >= (uint)tex.height) return;
            tex.SetPixel(x, y, c);
        }

        // 5x5 pixel letter shapes for the ones we need. `#` = pixel on.
        static string[] LetterRows(char letter) => letter switch
        {
            'A' => new[] { ".###.", "#...#", "#####", "#...#", "#...#" },
            'B' => new[] { "####.", "#...#", "####.", "#...#", "####." },
            'X' => new[] { "#...#", ".#.#.", "..#..", ".#.#.", "#...#" },
            'Y' => new[] { "#...#", ".#.#.", "..#..", "..#..", "..#.." },
            'L' => new[] { "#....", "#....", "#....", "#....", "#####" },
            'R' => new[] { "####.", "#...#", "####.", "#..#.", "#...#" },
            'S' => new[] { ".####", "#....", ".###.", "....#", "####." },
            'T' => new[] { "#####", "..#..", "..#..", "..#..", "..#.." },
            '1' => new[] { "..#..", ".##..", "..#..", "..#..", ".###." },
            '2' => new[] { ".###.", "#...#", "...#.", "..#..", "#####" },
            '3' => new[] { "####.", "....#", ".###.", "....#", "####." },
            _   => null,
        };
    }
}
