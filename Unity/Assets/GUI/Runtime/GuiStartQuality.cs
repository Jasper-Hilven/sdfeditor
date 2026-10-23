using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The first look at the machine, once a launch, on the title: after <see cref="Settle"/> seconds there, the frames of
    /// <see cref="Measure"/> seconds are timed and the game starts on the best level <see cref="GuiQualityRule"/> would climb to with them
    /// (95 % of them in time at that level's rate), up to <see cref="Highest"/> at most: the title is cheaper than an arena, so it does not
    /// start on Ultra Epic; the HUD's countdown climbs there in play if the arena allows. Late frames take it down as the rule does (one
    /// level at 50 a second or slower, two at 30). The HUD goes on adjusting in play.
    /// </summary>
    public static class GuiStartQuality
    {
        public const float Settle = 1, Measure = 2;
        public const string Highest = SdfMushrooms.QualityExtras.Epic;

        private static bool done;
        private static float time;
        private static readonly System.Collections.Generic.List<float> frames = new System.Collections.Generic.List<float>();

        /// <summary>The title went before the measuring was over (a quick click): no change.</summary>
        public static void Stop() => done = true;

        /// <summary>Once a frame while the title shows, in play.</summary>
        public static void Tick()
        {
            if (done)
                return;
            var dt = Time.unscaledDeltaTime;
            time += dt;
            if (time < Settle)
                return;
            frames.Add(dt);
            if (time < Settle + Measure)
                return;
            done = true;

            var names = QualitySettings.names;
            var level = QualitySettings.GetQualityLevel();
            var highest = Mathf.Max(0, System.Array.IndexOf(names, Highest));
            var to = level;
            // Up while the level above's rate was met, best first
            while (to - 1 >= highest && Share(f => GuiQualityRule.InTime(f, GuiQualityRule.UpFps(to - 1))) >= GuiQualityRule.Share)
                to--;
            if (to == level && Share(f => f >= 1 / GuiQualityRule.DownFps) >= GuiQualityRule.Share)
                to = Mathf.Min(names.Length - 1, level + (Share(f => f >= 1 / GuiQualityRule.TwoDownFps) >= GuiQualityRule.Share ? 2 : 1));
            if (to == level)
                return;
            Debug.Log($"Start quality: {frames.Count / Measure:0} frames a second on the title, so {names[to]} (was {names[level]})");
            QualitySettings.SetQualityLevel(to, true);
        }

        private static float Share(System.Predicate<float> counts) => frames.Count == 0 ? 0 : (float)frames.FindAll(counts).Count / frames.Count;
    }
}
