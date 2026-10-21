using System;

namespace SdfBattle
{
    /// <summary>
    /// The numbers capture the flag plays by (ticket 21, from the user): the carrier walks 10% slower, a dropped flag nobody touches goes
    /// home after 30 s. Plain data, serialized on the scene's <c>CaptureTheFlag</c> component so they can be tuned in the inspector; the
    /// captures to win, the clock and sudden death are the match's (<see cref="MatchRules"/>).
    /// </summary>
    [Serializable]
    public sealed class FlagRules
    {
        /// <summary>The carrier's walking speed as a share of everyone's; they keep all their weapons.</summary>
        public float carrierSpeed = 0.9f;
        /// <summary>Seconds a dropped flag lies before it goes home by itself, if no player of its team touches it first.</summary>
        public float returnSeconds = 30;
    }
}
