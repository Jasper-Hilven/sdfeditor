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
        /// <summary>Players per team, you included.</summary>
        public int teamSize = 4;
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
        /// <summary>Seconds without damage after spawning; firing ends it early.</summary>
        public float spawnProtectionSeconds = 2;
        /// <summary>A player's health when they spawn (the health itself is ticket 22's).</summary>
        public float maxHealth = 100;
        /// <summary>Whether a team's own players and machines can hurt each other; off.</summary>
        public bool friendlyFire;
    }
}
