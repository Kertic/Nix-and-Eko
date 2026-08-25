using System.Collections.Generic;
using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Placeholder sprite sets for the two primitive enemies, drawn in code as ASCII pixel art the
    /// same way <see cref="ArcherSprites"/> is. A stubby patroller and a round slammer, each with a
    /// tiny walk/telegraph wobble.
    /// </summary>
    public static class EnemySprites
    {
        static readonly Dictionary<char, Color> Pal = new Dictionary<char, Color>
        {
            ['B'] = Hex("#7A3B9A"),   // body
            ['b'] = Hex("#4C2463"),   // body shadow
            ['E'] = Hex("#FFEC27"),   // eye
            ['e'] = Hex("#1D2B53"),   // pupil
            ['F'] = Hex("#D66A2E"),   // feet / spikes
            ['S'] = Hex("#C23B3B"),   // slammer body
            ['s'] = Hex("#7A1E1E"),   // slammer shadow
            ['W'] = Hex("#F4F4F4"),   // tooth / highlight
        };

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        // ---- Walker: squat, two-legged shuffle ----
        static readonly string[] Walk0 =
        {
            "..BBBBBB..",
            ".BBBBBBBB.",
            ".BEeBBEeB.",
            ".BBBBBBBB.",
            ".BbBBBBbB.",
            ".BBBBBBBB.",
            ".bBBBBBBb.",
            "..bBBBBb..",
            "..F....F..",
            ".FF....FF.",
        };

        static readonly string[] Walk1 =
        {
            "..BBBBBB..",
            ".BBBBBBBB.",
            ".BEeBBEeB.",
            ".BBBBBBBB.",
            ".BbBBBBbB.",
            ".BBBBBBBB.",
            ".bBBBBBBb.",
            "..bBBBBb..",
            "...F..F...",
            "..FF..FF..",
        };

        // ---- Slammer: round body, telegraph (squished) and slam (stretched) ----
        static readonly string[] Idle =
        {
            "...SSSS...",
            "..SSSSSS..",
            ".SSEeSSeES",
            ".SSSSSSSSS",
            ".SSSSSSSSS",
            ".sSSWWSSs.",
            "..sSSSSs..",
            "...ssss...",
        };

        static readonly string[] Wind =
        {
            "..........",
            "..........",
            "..SSSSSS..",
            ".SSEeeeSS.",
            ".SSSSSSSS.",
            ".sSSSSSSs.",
            ".sSSSSSSs.",
            "..ssssss..",
        };

        static readonly string[] Slam =
        {
            "...SSSS...",
            "..SSSSSS..",
            ".SSSSSSSS.",
            "SSSEeSSeES",
            "SSSSSSSSSS",
            "SssSSSSssS",
            "F.F.FF.F.F",
            "F.F.FF.F.F",
        };

        static Sprite F(string[] rows) => PixelArt.FromRows(rows, Pal);

        static Sprite[] _walk, _slIdle, _slWind, _slSlam;

        public static Sprite[] WalkerFrames => _walk ??= new[] { F(Walk0), F(Walk1) };
        public static Sprite SlammerIdle => (_slIdle ??= new[] { F(Idle) })[0];
        public static Sprite SlammerWind => (_slWind ??= new[] { F(Wind) })[0];
        public static Sprite SlammerSlam => (_slSlam ??= new[] { F(Slam) })[0];
    }
}
