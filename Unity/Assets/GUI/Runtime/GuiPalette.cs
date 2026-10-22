using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The GUI's colours (sRGB, as picked), taken from the toon models: the warm dark ink of their outline, the cream of the amanita
    /// spots, the reds, oranges and greens of caps and moss, and the cyan of the subsurface spore glow.
    /// </summary>
    public static class GuiPalette
    {
        public static readonly Color Ink = Hex(0x2B1C1F);
        public static readonly Color Cream = Hex(0xFFF2D9);
        public static readonly Color Paper = Hex(0xF4DFB4);
        public static readonly Color Amanita = Hex(0xE2453C);
        public static readonly Color AmanitaDark = Hex(0xA62E2E);
        public static readonly Color Chanterelle = Hex(0xF5A63B);
        public static readonly Color Gold = Hex(0xFFD54F);
        public static readonly Color Moss = Hex(0x86BC4A);
        public static readonly Color MossDark = Hex(0x4D7D2E);
        public static readonly Color Spore = Hex(0x8FF0DC);
        public static readonly Color SporeDeep = Hex(0x2FB59E);
        public static readonly Color Violet = Hex(0x8C66CC);
        public static readonly Color Sky = Hex(0x86C5E8);
        public static readonly Color Night = Hex(0x3A2D47);
        public static readonly Color Dusk = Hex(0x5B4870);

        /// <summary>The colours the players are told apart by, in the order they are handed out.</summary>
        public static readonly Color[] Players =
        {
            Amanita, Chanterelle, Moss, Sky, Violet, Hex(0xD95F8C), Hex(0xF07FB0), SporeDeep, Hex(0xC9875A), Hex(0x6F8FE0), Hex(0xB8D44A), Hex(0xE06A3A),
        };

        public static Color Hex(int rgb) => new Color((rgb >> 16 & 0xFF) / 255f, (rgb >> 8 & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        public static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        public static string HexOf(Color color) => ColorUtility.ToHtmlStringRGB(color);
    }
}
