namespace SdfGui
{
    /// <summary>The words that name a key or a mouse button (ticket 56): on a touch device they name the touch control instead.</summary>
    public static class TouchWording
    {
        public static bool Touch => SdfPlatform.Device.IsTouch;

        /// <summary>The desktop's words, or the touch device's.</summary>
        public static string Pick(string keys, string touch) => Touch ? touch : keys;

        public const string ScoreboardTip = "Hold the SCORE button to see the scoreboard.";
        public const string FlagFightTip = "Hold the SCORE button to see who is winning the flag fight.";
        public const string HeadKeys = "Tap the arrows to change head";
        public const string ClickToPlay = "TAP TO PLAY";
        public const string ScoreboardHint = "hold SCORE";
        public const string SkipHint = "Hold to skip";
        /// <summary>A tip as this device reads it: the keys named in it become the touch controls.</summary>
        public static string Tip(string tip)
        {
            if (!Touch)
                return tip;
            return tip switch
            {
                "Hold Tab to see the scoreboard." => ScoreboardTip,
                "Hold Tab to see who is winning the flag fight." => FlagFightTip,
                "On the end card, A and D change your head." => HeadTip,
                _ => tip,
            };
        }

        public const string HeadTip = "On the end card, tap the arrows to change your head.";
    }
}
