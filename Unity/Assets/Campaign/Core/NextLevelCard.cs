using System;

namespace SdfCampaign
{
    /// <summary>
    /// The countdown on the end card after every match (ticket 48): 10 s of unscaled time, then the next level loads by itself. Space or
    /// Enter skips the rest. It stops while the pause menu is open. It finishes once: <see cref="Tick"/> and <see cref="Skip"/> return
    /// true on the one call that finishes it, and nothing after that.
    /// </summary>
    public sealed class NextLevelCountdown
    {
        public const float Seconds = 10;

        /// <summary>Seconds left before the next level (0 once done).</summary>
        public float Remaining { get; private set; } = Seconds;

        /// <summary>The countdown is over: the next level is due or loading.</summary>
        public bool Done { get; private set; }

        /// <summary>The whole seconds shown: 10 at the start, 1 for the last second, 0 once done.</summary>
        public int Shown => Done ? 0 : Math.Max(1, (int)Math.Ceiling(Remaining));

        /// <summary>Counts down by <paramref name="unscaledSeconds"/> unless paused; true on the call that finishes it.</summary>
        public bool Tick(float unscaledSeconds, bool paused)
        {
            if (Done || paused || unscaledSeconds <= 0)
                return false;
            Remaining = Math.Max(0, Remaining - unscaledSeconds);
            if (Remaining > 0)
                return false;
            Done = true;
            return true;
        }

        /// <summary>The skip key (Space or Enter): ends the countdown now unless paused; true on the call that finishes it.</summary>
        public bool Skip(bool paused)
        {
            if (Done || paused)
                return false;
            Remaining = 0;
            Done = true;
            return true;
        }
    }

    /// <summary>
    /// A thing that happens once per match end: <see cref="Arm"/> when a match goes live, then <see cref="Take"/> is true exactly once
    /// until it is armed again (the midgame ad is asked for once per match end, however the countdown ends).
    /// </summary>
    public sealed class OncePerMatch
    {
        private bool armed;

        public void Arm() => armed = true;

        public bool Take()
        {
            if (!armed)
                return false;
            armed = false;
            return true;
        }
    }

    /// <summary>The words and the rules of the end card that are not the screen's (ticket 48).</summary>
    public static class NextLevelCard
    {
        /// <summary>The line for a difficulty change between the match just ended and the next match; null when it did not change.</summary>
        public static string DifficultyLine(Difficulty before, Difficulty after)
        {
            if (after > before)
                return "Tougher opponents next";
            if (after < before)
                return "Easier opponents next";
            return null;
        }

        /// <summary>The countdown's line: "Next level in 10…".</summary>
        public static string CountdownLine(int shown) => $"Next level in {shown}…";

        /// <summary>A level's name as the card and the Loading screen show it: the scene's name in capitals.</summary>
        public static string LevelName(string level) => (level ?? "").ToUpperInvariant();
    }
}
