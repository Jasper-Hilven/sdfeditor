using System;

namespace SdfCampaign
{
    /// <summary>
    /// The numbers of one battle (ticket 32, section 2) at Normal difficulty: team sizes, the bots' skill per team, the names, the win
    /// condition and the mutators. Weapons, machines and hazards come from the scene, not from here. Plain data.
    /// </summary>
    [Serializable]
    public sealed class BattleSetup
    {
        /// <summary>Players in the red team, you included.</summary>
        public int redSize = 4;
        /// <summary>Players in the blue team.</summary>
        public int blueSize = 4;
        /// <summary>Skill of the blue bots (BotSkill.level: 0 clumsy, 1 sharp).</summary>
        public float blueSkill = 0.5f;
        /// <summary>Skill of the red teammates; difficulty does not change it.</summary>
        public float redSkill = 0.5f;
        /// <summary>Names of the red players in order (the first is you).</summary>
        public string[] redNames = Array.Empty<string>();
        /// <summary>Names of the blue players in order.</summary>
        public string[] blueNames = Array.Empty<string>();
        /// <summary>Captures that win the match.</summary>
        public int capturesToWin = 3;
        /// <summary>The match clock in seconds; at zero the team with more captures wins, a tie goes to sudden death.</summary>
        public float timeLimitSeconds = 600;
        /// <summary>One round only: the result ends the match, there is no next round.</summary>
        public bool singleRound = true;
        /// <summary>Whether the controls warm-up runs first (the meadow, first play only; the runner clears it on retries).</summary>
        public bool warmUp;
        /// <summary>Whether the spore powers (25) are on.</summary>
        public bool sporesOn = true;
        /// <summary>Seconds a dead blue player waits before respawning (red uses the match rules' own).</summary>
        public float blueRespawnSeconds = 5;

        /// <summary>The numbers the match plays by (SdfBattle.Core cannot see this assembly, so it has its own plain copy).</summary>
        public SdfBattle.MatchSetup ToMatchSetup() => new SdfBattle.MatchSetup
        {
            redSize = redSize,
            blueSize = blueSize,
            redSkill = redSkill,
            blueSkill = blueSkill,
            redNames = (string[])redNames.Clone(),
            blueNames = (string[])blueNames.Clone(),
            capturesToWin = capturesToWin,
            timeLimitSeconds = timeLimitSeconds,
            singleRound = singleRound,
            sporesOn = sporesOn,
            blueRespawnSeconds = blueRespawnSeconds,
            warmUp = warmUp,
        };

        public BattleSetup Clone()
        {
            var copy = (BattleSetup)MemberwiseClone();
            copy.redNames = (string[])redNames.Clone();
            copy.blueNames = (string[])blueNames.Clone();
            return copy;
        }
    }
}
