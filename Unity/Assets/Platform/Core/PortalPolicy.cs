namespace SdfPlatform
{
    public enum GameplayChange
    {
        None,
        Start,
        Stop,
    }

    /// <summary>
    /// Turns "is the player playing right now" into the portal's gameplayStart / gameplayStop events: one event per change, never two starts
    /// or two stops in a row. Playing means a round is live and nothing covers it (no menu, comic, load, ad or pause).
    /// </summary>
    public sealed class GameplayTracker
    {
        public bool Playing { get; private set; }

        public GameplayChange Set(bool playing)
        {
            if (playing == Playing)
                return GameplayChange.None;
            Playing = playing;
            return playing ? GameplayChange.Start : GameplayChange.Stop;
        }

        /// <summary>The state the portal has seen is lost (it was not ready yet): the next <see cref="Set"/> reports the state again.</summary>
        public void Forget() => Playing = false;
    }

    /// <summary>
    /// When a midgame ad may be asked for. The portal's SDK spaces ads itself (at most one every 3 minutes, none right at the start) and
    /// drops a request that comes too early, so this only keeps the game's own rules: an ad only at a natural break after a battle that
    /// was really played (a few seconds of a warm-up or an instant retry earn none), and not twice in a row without play between.
    /// </summary>
    public sealed class AdBreakPolicy
    {
        /// <summary>Seconds of battle played since the last ad before the next one is earned.</summary>
        public float MinPlayedSeconds = 45;

        /// <summary>Real seconds between two asks; the portal enforces 180 on its own, this stays a little under.</summary>
        public float MinSecondsBetween = 150;

        private float played;
        private float lastAsk = float.NegativeInfinity;

        /// <summary>Seconds of battle played since the last ad.</summary>
        public float Played => played;

        /// <summary>A battle just ended after <paramref name="seconds"/> of play.</summary>
        public void BattleEnded(float seconds)
        {
            if (seconds > 0)
                played += seconds;
        }

        /// <summary>Whether to ask for an ad at this break; <paramref name="now"/> is real time in seconds.</summary>
        public bool ShouldAsk(float now) => played >= MinPlayedSeconds && now - lastAsk >= MinSecondsBetween;

        /// <summary>An ad was asked for (shown or not): the play counted towards it is spent.</summary>
        public void Asked(float now)
        {
            lastAsk = now;
            played = 0;
        }
    }
}
