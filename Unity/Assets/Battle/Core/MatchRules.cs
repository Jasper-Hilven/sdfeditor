using System;

namespace SdfBattle
{
    /// <summary>
    /// The numbers a match plays by (ticket 20, from the user): 4 vs 4, first to 3 captures, a 10 minute clock, sudden death on a tie,
    /// a 3 s countdown before each round and the result for 10 s after it, 5 s dead before a respawn, 2 s of spawn protection. Plain
    /// data, serialized on the scene's <c>Match</c> component so they can be tuned in the inspector.
    /// </summary>
    [Serializable]
    public sealed class MatchRules
    {
        /// <summary>Players per team, you included; blue's own size is <see cref="blueTeamSize"/> when set.</summary>
        public int teamSize = 4;
        /// <summary>Blue's players when it differs from <see cref="teamSize"/> (a 4 v 5 match); 0 or less means the same.</summary>
        public int blueTeamSize;
        /// <summary>Captures that win a round.</summary>
        public int capturesToWin = 3;
        /// <summary>The round's clock; when it runs out the team with more captures wins, on a tie the next capture does.</summary>
        public float roundSeconds = 600;
        /// <summary>The wait at the start of a round, everyone at their spawn and unable to move.</summary>
        public float countdownSeconds = 3;
        /// <summary>How long the result shows before the next round starts with everything reset.</summary>
        public float resultSeconds = 10;
        /// <summary>Seconds dead before a player is back at the team's spawn zone.</summary>
        public float respawnSeconds = 5;
        /// <summary>Seconds a dead blue player waits when it differs from <see cref="respawnSeconds"/>; negative means the same.</summary>
        public float blueRespawnSeconds = -1;
        /// <summary>Seconds without damage after spawning; firing ends it early.</summary>
        public float spawnProtectionSeconds = 2;
        /// <summary>A player's health when they spawn (the health itself is ticket 22's).</summary>
        public float maxHealth = 100;
        /// <summary>Whether a team's own players and machines can hurt each other; off.</summary>
        public bool friendlyFire;
        /// <summary>One round only (the campaign, ticket 32): the result ends the match, nothing starts a next round.</summary>
        public bool singleRound;

        /// <summary>Players in a team, you included.</summary>
        public int SizeOf(Team team) => team == Team.Blue && blueTeamSize > 0 ? blueTeamSize : teamSize;

        /// <summary>Seconds a dead player of a team waits before respawning.</summary>
        public float RespawnOf(Team team) => team == Team.Blue && blueRespawnSeconds >= 0 ? blueRespawnSeconds : respawnSeconds;
    }
}
