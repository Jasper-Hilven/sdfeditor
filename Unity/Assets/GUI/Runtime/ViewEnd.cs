using System;

namespace SdfGui
{
    /// <summary>
    /// How the match ended, as the banner, Victory and Defeat show it (ticket 32.6). A real match fills the plain numbers
    /// (SdfBattle.MatchHud) and the campaign adds the medal, the taunt and whether RETRY EASIER is offered (CampaignRunner); the showcase
    /// round makes a sample (<see cref="MatchView.SampleEnd"/>).
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
        [UnityEngine.Tooltip("0 none, 1 bronze, 2 silver, 3 gold (a win in the campaign)")]
        public int medal;
        public bool newBest;
        [UnityEngine.Tooltip("Prime's taunt on the Defeat screen")]
        public string taunt = "";
        [UnityEngine.Tooltip("RETRY EASIER is offered on the Defeat screen")]
        public bool canRetryEasier;

        [UnityEngine.Header("The final boss (ticket 33.7)")]
        [UnityEngine.Tooltip("The match was the boss fight: the screens show lives and damage instead of the score, and RETRY PHASE / RESTART FIGHT")]
        public bool boss;
        [UnityEngine.Tooltip("The phase reached, 1 to 3")]
        public int bossPhase = 1;
        [UnityEngine.Tooltip("Prime's health left, 0 to 1 of the whole")]
        public float bossHealthLeft;
        public int livesLost;
        public int damageDealt;
        [UnityEngine.Tooltip("The tip for the phase reached, in place of the shared ones")]
        public string tip = "";

        public void Clear()
        {
            set = won = winnerBlue = newBest = canRetryEasier = boss = false;
            redScore = blueScore = captures = kills = deaths = medal = livesLost = damageDealt = 0;
            bossPhase = 1;
            bossHealthLeft = seconds = 0;
            taunt = tip = "";
        }
    }
}
