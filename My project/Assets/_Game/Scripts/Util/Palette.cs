using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// The PICO-8 palette and the role each colour plays, shared by the runtime level builder
    /// and the editor tools so placeholder art stays consistent everywhere.
    /// </summary>
    public static class Palette
    {
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        public static readonly Color Black = Hex("#000000");
        public static readonly Color DarkBlue = Hex("#1D2B53");
        public static readonly Color Brown = Hex("#AB5236");
        public static readonly Color DarkGrey = Hex("#5F574F");
        public static readonly Color LightGrey = Hex("#C2C3C7");
        public static readonly Color White = Hex("#FFF1E8");
        public static readonly Color Red = Hex("#FF004D");
        public static readonly Color Orange = Hex("#FFA300");
        public static readonly Color Yellow = Hex("#FFEC27");
        public static readonly Color Green = Hex("#00E436");
        public static readonly Color Blue = Hex("#29ADFF");
        public static readonly Color Lavender = Hex("#83769C");

        public static readonly Color Ground = Brown;
        public static readonly Color GroundEdge = DarkBlue;
        public static readonly Color OneWay = White;
        public static readonly Color Moving = Blue;
        public static readonly Color Hazard = Red;
        public static readonly Color Gate = LightGrey;
        public static readonly Color Breakable = Orange;
        public static readonly Color Player = Red;
        public static readonly Color Checkpoint = Green;
        public static readonly Color Switch = Lavender;
    }
}
