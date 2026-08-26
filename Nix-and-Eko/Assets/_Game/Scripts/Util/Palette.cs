using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Faerie-night-forest palette (Ardenweald-ish): deep midnight indigo backdrops, glowing
    /// bioluminescent cyan / teal / violet highlights, warm magenta accents. Role fields map the
    /// visual language to concrete assets so the runtime level builder and the editor tools stay
    /// consistent everywhere — retune only the base hues here and every ground tile, indicator,
    /// switch, checkpoint and burst picks up the new palette on the next build.
    /// </summary>
    public static class Palette
    {
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        // Base hues. Names are kept for compatibility with existing callers even though the
        // colours have shifted — e.g. "Brown" is now a deep plum bark tone, not literal brown.
        public static readonly Color Black = Hex("#050718");        // near-black midnight — camera bg
        public static readonly Color DarkBlue = Hex("#1B1A47");     // rich indigo (edges, dusk mist)
        public static readonly Color Brown = Hex("#3B2A5E");        // deep plum bark
        public static readonly Color DarkGrey = Hex("#2E3B5C");     // dusky twilight blue-grey
        public static readonly Color LightGrey = Hex("#A9D6F0");    // cool icy blue-white
        public static readonly Color White = Hex("#E8F5FF");        // pale bioluminescent white
        public static readonly Color Red = Hex("#E5397D");          // hot magenta / faerie glow
        public static readonly Color Orange = Hex("#E88E4A");       // warm amber-copper accent
        public static readonly Color Yellow = Hex("#F0E27E");       // moonlight gold
        public static readonly Color Green = Hex("#4FE0C5");        // bright mint-teal glow
        public static readonly Color Blue = Hex("#3FC7FF");         // vivid cyan sprite glow
        public static readonly Color Lavender = Hex("#B675E0");     // glowing violet

        // Role aliases — the runtime consumers reference these, not the raw hues, so a redesign
        // that decouples e.g. Ground from Brown only edits the right-hand side.
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
