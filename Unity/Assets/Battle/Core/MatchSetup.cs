using System;

namespace SdfBattle
{
    /// <summary>
    /// What one campaign battle asks of the match (ticket 32.3): team sizes per team, the bots' skill per team, the names, the win condition,
    /// one round only, spores on or off and blue's respawn wait. Plain data; the campaign's <c>BattleSetup</c> (SdfCampaign.Core, which
    /// references this assembly and not the other way round) converts to it. <see cref="Match"/> reads <see cref="Pending"/> at its start;
    /// with none, every number stays the scene's own and the scene plays alone.
    /// </summary>
    [Serializable]
    public sealed class MatchSetup
    {
        /// <summary>The setup of the scene being loaded, set by whoever loads it and cleared by them; null plays the scene's defaults.</summary>
        public static MatchSetup Pending { get; set; }

        public int redSize = 4;
        public int blueSize = 4;
        /// <summary>The red bots' skill, <see cref="BotSkill.level"/>.</summary>
        public float redSkill = 0.5f;
        /// <summary>The blue bots' skill, <see cref="BotSkill.level"/>.</summary>
        public float blueSkill = 0.5f;
        /// <summary>Names in roster order, the first red one is you; missing ones fall back to <see cref="Cast"/>.</summary>
        public string[] redNames = Array.Empty<string>();
        public string[] blueNames = Array.Empty<string>();
        public int capturesToWin = 3;
        public float timeLimitSeconds = 600;
        /// <summary>One round only: the result ends the match for good.</summary>
        public bool singleRound = true;
        /// <summary>What wins the match; the boss fight (ticket 33) is its own.</summary>
        public MatchObjective objective;
        public bool sporesOn = true;
        /// <summary>The boss fight's difficulty, 0 Super Easy, 1 Easy, 2 Normal, 3 Hard (<see cref="BossDifficulty"/>, ticket 33.6); the campaign's difficulty by number.</summary>
        public int bossDifficulty = 2;
        /// <summary>RETRY EASIER: the boss fight takes Easy's damage and wind-up rows.</summary>
        public bool retryEasier;
        /// <summary>The boss fight's first phase, 1 to 3 (RETRY PHASE).</summary>
        public int bossStartPhase = 1;
        /// <summary>Seconds a dead blue player waits; red keeps the rules' own.</summary>
        public float blueRespawnSeconds = 5;
        /// <summary>The match starts held (ticket 32.7): a story beat plays first, and whoever plays it lets the match go.</summary>
        public bool held;
        /// <summary>The controls warm-up runs first: the match starts held, and the warm-up lets it go (you may move and fire meanwhile).</summary>
        public bool warmUp;

        public float SkillOf(Team team) => team == Team.Red ? redSkill : blueSkill;

        /// <summary>The name of a team's <paramref name="number"/>th player (from 1): the setup's, else the cast's.</summary>
        public string NameOf(Team team, int number)
        {
            var names = team == Team.Red ? redNames : blueNames;
            return names != null && number >= 1 && number <= names.Length && !string.IsNullOrEmpty(names[number - 1])
                ? names[number - 1]
                : Cast.Name(team, number);
        }

        /// <summary>Writes the match's numbers into <paramref name="rules"/> (the rest of the rules stays as it is).</summary>
        public void Apply(MatchRules rules)
        {
            rules.teamSize = Math.Max(1, redSize);
            rules.blueTeamSize = Math.Max(1, blueSize);
            rules.capturesToWin = Math.Max(1, capturesToWin);
            rules.roundSeconds = timeLimitSeconds;
            rules.singleRound = singleRound;
            rules.objective = objective;
            rules.blueRespawnSeconds = blueRespawnSeconds;
        }
    }
}
