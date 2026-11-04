using System;

namespace SdfMushrooms
{
    /// <summary>
    /// What a touch device (phone, tablet; ticket 55) gets in quality (ticket 58), apart from the desktop's, which it never changes. Quality
    /// levels go best first: a touch device starts on <see cref="StartLevel"/>, one above the lowest (the user: a first look at the lowest
    /// level scares players away), and the automatic rule moves from there as the frames allow, down to <see cref="CutLevel"/> on a slow
    /// phone. On <see cref="CutLevel"/> a touch device also turns off the ink outline, the level's fixed sun shadow and the
    /// post-processing, thins the foliage (<see cref="FoliageThinning"/>, <see cref="FoliageNearing"/>) and keeps <see cref="GoreShare"/> of
    /// the gore. The render target is capped to <see cref="MaxRenderHeight"/> pixels tall at every level.
    /// </summary>
    public static class MobileQualityRule
    {
        /// <summary>The level a touch device starts on.</summary>
        public const string StartLevel = "Integrated GPU / Web";
        /// <summary>The lowest level, the one that gets the touch cuts (where the automatic rule takes a slow phone).</summary>
        public const string CutLevel = "Ultra Low";
        /// <summary>The most pixels tall the 3D view renders on a touch device: a 1080p phone does not draw 1080p of ink line.</summary>
        public const int MaxRenderHeight = 540;
        /// <summary>The share of the foliage the low preset draws that the lowest touch level keeps, and its draw distance factor.</summary>
        public const float FoliageThinning = 0.6f, FoliageNearing = 0.8f;
        /// <summary>The share of the blood pools and corpses kept on the lowest touch level.</summary>
        public const float GoreShare = 0.5f;
        /// <summary>Turns the start-up choice off (a page address containing it): the comparison runs of ticket 58.</summary>
        public const string OptOut = "nomobilequality";

        /// <summary>Whether the lowest touch level's cuts apply: a touch device on <see cref="CutLevel"/>.</summary>
        public static bool Cuts(bool touch, string level) => touch && level == CutLevel;

        /// <summary>The render scale on a touch device: the preset's, capped so the view is at most <see cref="MaxRenderHeight"/> tall, at least 0.35.</summary>
        public static float RenderScale(float preset, int screenHeight)
        {
            if (screenHeight <= 0)
                return preset;
            return Math.Min(preset, Math.Max(0.35f, (float)MaxRenderHeight / screenHeight));
        }

        /// <summary>The index of the level a touch device starts on, or -1 when the names lack it.</summary>
        public static int StartIndex(string[] names) => Array.IndexOf(names, StartLevel);
    }
}
