namespace SdfMushrooms
{
    /// <summary>
    /// When the HUD offers "Press H to increase quality": the game runs steadily, around the display's 60 a second (a vsync or the
    /// browser's frame cap), on a level whose next one up asks for more than that (80 a second for Epic, 100 for Ultra
    /// Epic: see GuiQualityRule.UpFps), so the automatic rule never climbs. It uses the automatic rule's window and its way of measuring:
    /// the frames of <see cref="For"/> seconds, at least <see cref="Share"/> of them in time at <see cref="SteadyFps"/> (with the
    /// <see cref="Jitter"/> leeway on a frame's time). The band is therefore from about <see cref="SteadyFps"/> up to the next level's rate,
    /// where the rule would climb by itself (<c>upDue</c> hides the hint then). Leaving the band hides the hint at once (the window starts
    /// over, and it takes a whole new window to show again). Nothing is offered when the player or the rule just stepped down from the level
    /// above (it would only step back down), until the level changes upwards. The HUD takes the step: one level up, as the rule would.
    /// Quality levels go best first, so up is a lower index.
    /// </summary>
    public sealed class QualityHintRule
    {
        public const float SteadyFps = 55, For = 6, Share = 0.95f, Jitter = 1.1f;

        private float time;
        private int frames, late;
        private int lastLevel = -1;
        private int steppedDownFrom = -1;

        /// <summary>Whether the hint shows now.</summary>
        public bool Visible { get; private set; }

        /// <summary>Whether a frame of <paramref name="dt"/> seconds came at <see cref="SteadyFps"/> a second or faster, with the leeway.</summary>
        public static bool Steady(float dt) => dt <= Jitter / SteadyFps;

        /// <summary>The window starts over and the hint goes: after a step, a pause, a menu.</summary>
        public void Reset()
        {
            (time, frames, late) = (0, 0, 0);
            Visible = false;
        }

        /// <summary>
        /// One frame of <paramref name="dt"/> seconds at quality <paramref name="level"/> (best first, 0 the top). <paramref name="eligible"/>:
        /// auto quality is on, the level is not the top and its next one up asks for more than the steady rate. <paramref name="upDue"/>: the
        /// automatic rule is counting towards a step up. Returns <see cref="Visible"/>.
        /// </summary>
        public bool Tick(float dt, int level, bool eligible, bool upDue)
        {
            if (lastLevel >= 0 && level != lastLevel)
            {
                // A step down from the level above this one: do not offer it back. Any step up forgets that.
                steppedDownFrom = level > lastLevel ? lastLevel : -1;
                Reset();
            }
            lastLevel = level;
            if (!eligible || upDue || steppedDownFrom == level - 1)
            {
                Reset();
                return false;
            }
            time += dt;
            frames++;
            if (!Steady(dt))
                late++;
            // More late frames than the share allows over a whole window at the steady rate: out of the band
            if (late > (1 - Share) * For * SteadyFps)
            {
                Reset();
                return false;
            }
            // A window done in the band shows the hint; the next one starts then (a late frame in it hides the hint, as above)
            if (time >= For)
            {
                Visible = late <= (1 - Share) * frames;
                (time, frames, late) = (0, 0, 0);
            }
            return Visible;
        }
    }
}
