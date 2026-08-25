using System.Collections.Generic;
using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// The archer's sprite set, drawn in code as ASCII pixel art: a small hooded figure with a
    /// bow, facing right (the renderer flips for facing). Roughly 11x13 px — at 16 PPU that is
    /// about 0.7 x 0.8 world units, so she reads as a chunky arcade sprite next to 1-unit tiles.
    /// </summary>
    public static class ArcherSprites
    {
        // H hood  h hood shadow  F face  E eye  B tunic  b tunic shadow
        // L legs  l boot  W bow  q quiver/fletching
        static readonly Dictionary<char, Color> Pal = new Dictionary<char, Color>
        {
            ['H'] = Hex("#1B6B4A"),   // hood: deep forest green
            ['h'] = Hex("#0E3B2C"),   // hood shadow
            ['F'] = Hex("#FFCCAA"),   // face
            ['E'] = Hex("#1D2B53"),   // eye
            ['B'] = Hex("#2E8B57"),   // tunic
            ['b'] = Hex("#17513A"),   // tunic shadow
            ['L'] = Hex("#5F574F"),   // legs
            ['l'] = Hex("#3A342F"),   // boots
            ['W'] = Hex("#AB5236"),   // bow stave
            ['q'] = Hex("#FFEC27"),   // fletching
            ['G'] = Hex("#F2A23C"),   // glider canopy
            ['g'] = Hex("#B96C25"),   // glider canopy underside / tips
            ['r'] = Hex("#6B6156"),   // glider rigging lines
        };

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        // ---- Idle: two frames, a single-pixel breath bob ----
        static readonly string[] Idle0 =
        {
            "...hHHh....",
            "..hHHHHh...",
            "..hHFFFh.W.",
            "..hHFEFh.W.",
            "...hFFh..W.",
            "..qbBBBb.W.",
            ".qBBBBBBbW.",
            "..bBBBBb.W.",
            "...BBBB..W.",
            "...LL.LL...",
            "...LL.LL...",
            "...ll.ll...",
            "...........",
        };

        static readonly string[] Idle1 =
        {
            "...........",
            "...hHHh....",
            "..hHHHHh.W.",
            "..hHFFFh.W.",
            "..hHFEFh.W.",
            "...hFFh..W.",
            "..qbBBBb.W.",
            ".qBBBBBBbW.",
            "...BBBB..W.",
            "...LL.LL...",
            "...LL.LL...",
            "...ll.ll...",
            "...........",
        };

        // ---- Walk: 4-frame cycle (contact, pass, contact, pass) ----
        static readonly string[] Walk0 =
        {
            "...hHHh....",
            "..hHHHHh...",
            "..hHFFFh.W.",
            "..hHFEFh.W.",
            "...hFFh..W.",
            "..qbBBBb.W.",
            ".qBBBBBBbW.",
            "..bBBBBb.W.",
            "...BBBB..W.",
            "..LL..LL...",
            "..LL...LL..",
            "..ll...ll..",
            "...........",
        };

        static readonly string[] Walk1 =
        {
            "...........",
            "...hHHh....",
            "..hHHHHh.W.",
            "..hHFFFh.W.",
            "..hHFEFh.W.",
            "...hFFh..W.",
            "..qbBBBb.W.",
            ".qBBBBBBbW.",
            "...BBBB..W.",
            "...LLLL....",
            "...LL.LL...",
            "...ll..ll..",
            "...........",
        };

        static readonly string[] Walk2 =
        {
            "...hHHh....",
            "..hHHHHh...",
            "..hHFFFh.W.",
            "..hHFEFh.W.",
            "...hFFh..W.",
            "..qbBBBb.W.",
            ".qBBBBBBbW.",
            "..bBBBBb.W.",
            "...BBBB..W.",
            "...LL..LL..",
            "..LL....LL.",
            "..ll....ll.",
            "...........",
        };

        static readonly string[] Walk3 =
        {
            "...........",
            "...hHHh....",
            "..hHHHHh.W.",
            "..hHFFFh.W.",
            "..hHFEFh.W.",
            "...hFFh..W.",
            "..qbBBBb.W.",
            ".qBBBBBBbW.",
            "...BBBB..W.",
            "...LLLL....",
            "..LL..LL...",
            "..ll...ll..",
            "...........",
        };

        // ---- Jump: tucked, cloak lifted ----
        static readonly string[] JumpUp =
        {
            "...hHHh..W.",
            "..hHHHHh.W.",
            "..hHFFFh.W.",
            "..hHFEFh.W.",
            "...hFFh..W.",
            "..qbBBBb.W.",
            ".qBBBBBBbW.",
            "..bBBBBb...",
            "...BBBB....",
            "..LLL.LLL..",
            "..ll....ll.",
            "...........",
            "...........",
        };

        // ---- Fall: legs trailing, cloak flared ----
        static readonly string[] FallDown =
        {
            "...........",
            "...hHHh..W.",
            "..hHHHHh.W.",
            "..hHFFFh.W.",
            "..hHFEFh.W.",
            "...hFFh..W.",
            ".qqbBBBb.W.",
            ".qBBBBBBbW.",
            "..bBBBBb...",
            "...LL.LL...",
            "..LL...LL..",
            ".ll.....ll.",
            "...........",
        };

        // ---- Glide: hanging from a delta-wing glider, cloak spread, legs trailing together ----
        static readonly string[] Glide =
        {
            ".GGGGGGGGGGG.",
            "gGGGGGGGGGGGg",
            "....r...r....",
            "....hHHh.....",
            "...hHHHHh....",
            "...hHFFFh....",
            "...hHFEFh..W.",
            "...bBBBBb....",
            ".bBBBBBBBBb..",
            "..bBBBBBBb...",
            "...bBBBBb....",
            "....LLLL.....",
            "....llll.....",
        };

        // ---- Wall slide: pressed flat against the wall, one arm up ----
        static readonly string[] WallSlide =
        {
            "....hHHh...",
            "...hHHHHh..",
            "...hHFFFh..",
            "...hHFEFh..",
            "....hFFh.W.",
            "...qbBBBbW.",
            "..qBBBBBBW.",
            "...bBBBBbW.",
            "....BBBB.W.",
            "....LL.LL..",
            "....LL.LL..",
            "....ll.ll..",
            "...........",
        };

        // ---- Crouch: hunched, bow low ----
        static readonly string[] Crouch =
        {
            "...........",
            "...........",
            "...hHHh....",
            "..hHHHHh...",
            "..hHFFFh...",
            "..hHFEFh.W.",
            "...hFFh..W.",
            "..qbBBBb.W.",
            ".qBBBBBBbW.",
            "..bBBBBBb..",
            "..LLL.LLL..",
            "..lll.lll..",
            "...........",
        };

        // ---- Hurt: knocked back, arms out ----
        static readonly string[] Hurt =
        {
            "...........",
            "..hHHhh....",
            ".hHHHHHh...",
            ".hHFFFFh...",
            ".hHFEEFh...",
            "..hFFFh....",
            "W.bBBBb.q..",
            ".WBBBBBBq..",
            "..bBBBBb...",
            "..LL...LL..",
            ".LL.....LL.",
            ".ll.....ll.",
            "...........",
        };

        // ---- Melee: 3-hit combo poses (horizontal swipe, forward thrust, overhead) ----
        static readonly string[] Melee0 =   // swipe: arm sweeping across, low-to-high
        {
            "...hHHh.....",
            "..hHHHHh....",
            "..hHFFFh....",
            "..hHFEFhWWW.",
            "...hFFhWW...",
            "..qbBBBbW...",
            ".qBBBBBBb...",
            "..bBBBBb....",
            "...BBBB.....",
            "...LL.LL....",
            "...LL.LL....",
            "...ll.ll....",
            "............",
        };

        static readonly string[] Melee1 =   // thrust: arrow driven straight forward
        {
            "...hHHh.....",
            "..hHHHHh....",
            "..hHFFFh....",
            "..hHFEFh....",
            "...hFFh.....",
            "..qbBBBbWWWW",
            ".qBBBBBBbWWq",
            "..bBBBBb....",
            "...BBBB.....",
            "...LL.LL....",
            "..LL...LL...",
            "..ll...ll...",
            "............",
        };

        static readonly string[] Melee2 =   // overhead: big swing coming down from up high
        {
            ".......WW...",
            "...hHHhWW...",
            "..hHHHHhW...",
            "..hHFFFh.W..",
            "..hHFEFh....",
            "...hFFh.....",
            "..qbBBBb....",
            ".qBBBBBBb...",
            "..bBBBBb....",
            "...BBBB.....",
            "..LL...LL...",
            "..ll...ll...",
            "............",
        };

        // ---- Roll: tucked ball, two spin frames ----
        static readonly string[] Roll0 =
        {
            "............",
            "............",
            "...hHHHh....",
            "..hHFFBBb...",
            ".hHFEBBBBb..",
            ".hFBBBBBBL..",
            ".bBBBBBBLL..",
            ".lLBBBBLl...",
            "..llLLll....",
            "............",
            "............",
        };

        static readonly string[] Roll1 =
        {
            "............",
            "............",
            "...bBBBb....",
            "..lLBBHHh...",
            ".lLBBHFFHh..",
            ".LBBBBFEFb..",
            ".LLBBBBBBb..",
            "..lLBBBBl...",
            "...llLLl....",
            "............",
            "............",
        };

        static Sprite[] _idle, _walk, _jump, _fall, _glide, _slide, _crouch, _hurt, _melee, _roll;

        static Sprite Frame(string[] rows) => PixelArt.FromRows(rows, Pal);

        /// <summary>Build a clip from one or more ASCII frames.</summary>
        static Sprite[] Clip(params string[][] frames)
        {
            var result = new Sprite[frames.Length];
            for (int i = 0; i < frames.Length; i++) result[i] = Frame(frames[i]);
            return result;
        }

        public static Sprite[] IdleFrames => _idle ??= Clip(Idle0, Idle1);
        public static Sprite[] WalkFrames => _walk ??= Clip(Walk0, Walk1, Walk2, Walk3);
        public static Sprite[] JumpFrames => _jump ??= Clip(JumpUp);
        public static Sprite[] FallFrames => _fall ??= Clip(FallDown);
        public static Sprite[] GlideFrames => _glide ??= Clip(Glide);
        public static Sprite[] WallSlideFrames => _slide ??= Clip(WallSlide);
        public static Sprite[] CrouchFrames => _crouch ??= Clip(Crouch);
        public static Sprite[] HurtFrames => _hurt ??= Clip(Hurt);
        /// <summary>Three combo poses: [0] swipe, [1] thrust, [2] overhead.</summary>
        public static Sprite[] MeleeFrames => _melee ??= Clip(Melee0, Melee1, Melee2);
        public static Sprite[] RollFrames => _roll ??= Clip(Roll0, Roll1);
    }
}
