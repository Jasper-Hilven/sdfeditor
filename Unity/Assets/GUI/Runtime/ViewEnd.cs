using System;
namespace SdfGui
{
    /// <summary>
    /// How the match ended, as the banner, Victory and Defeat show it (ticket 32.6). The match fills the plain numbers
    /// (SdfBattle.MatchHud); the showcase round makes a sample (<see cref="MatchView.SampleEnd"/>).
    /// </summary>
    [Serializable]
    public sealed class ViewEnd
    {
        /// <summary>The match is over and the rest is valid.</summary>
        public bool set;
        [UnityEngine.Tooltip("Your team won: Victory rather than Defeat")]
        public bool won;
        [UnityEngine.Tooltip("The winning team is blue, for the banner")]
        public bool winnerBlue;
        public int redScore;
        public int blueScore;
        [UnityEngine.Tooltip("Your captures, kills and deaths")]
        public int captures;
        public int kills;
        public int deaths;
        public float seconds;
        [UnityEngine.Tooltip("The win came on kills (no captures at time-up, or sudden death's first kill), not on captures")]
        public bool onKills;

        public void Clear()
        {
            set = won = winnerBlue = onKills = false;
            redScore = blueScore = captures = kills = deaths = 0;
            seconds = 0;
        }
    }
}
