using System;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// How good the bots are (ticket 23): one setting, <see cref="level"/> from 0 (clumsy) to 1 (sharp), from which every number of
    /// their aim and sight follows: the reaction time before the first shot at a new target, the aim error that starts wide and
    /// shrinks while they keep tracking it, the extra error a target crossing the view adds, how fast they turn, how wide they see.
    /// Even at 1 they react in a fifth of a second and never aim perfectly, so they stay beatable.
    /// </summary>
    [Serializable]
    public sealed class BotSkill
    {
        [Tooltip("0 clumsy, 1 sharp: reaction time, aim error, turn speed and view width follow from it")]
        [Range(0, 1)] public float level = 0.5f;

        public BotSkill()
        {
        }

        public BotSkill(float level) => this.level = Mathf.Clamp01(level);

        private float L(float atZero, float atOne) => Mathf.Lerp(atZero, atOne, Mathf.Clamp01(level));

        /// <summary>Seconds from first seeing a new target until the first shot at it.</summary>
        public float ReactionSeconds => L(0.65f, 0.2f);

        /// <summary>Degrees off the target the aim starts at, as the bot starts tracking it.</summary>
        public float StartError => L(14, 5);

        /// <summary>Degrees off the target the aim settles to after tracking it a while; never 0.</summary>
        public float SteadyError => L(3.2f, 0.7f);

        /// <summary>Seconds of tracking in which the error shrinks by about two thirds of the way to steady.</summary>
        public float SettleSeconds => L(1.6f, 0.55f);

        /// <summary>Seconds the aim lags a target crossing the view: its angular speed times this is added to the error.</summary>
        public float TrackingLag => L(0.14f, 0.04f);

        /// <summary>Degrees a second the bot turns its view at most.</summary>
        public float TurnSpeed => L(240, 540);

        /// <summary>Half the view cone's angle, degrees: what it sees without turning.</summary>
        public float ViewHalfAngle => L(55, 70);

        /// <summary>Metres it sees an enemy across, in its view cone and line of sight.</summary>
        public float SightRange => L(70, 110);

        /// <summary>Metres within which it notices an enemy whatever way it looks (hears them).</summary>
        public float HearingRange => L(4, 7);

        /// <summary>Seconds it remembers where it last saw an enemy.</summary>
        public float MemorySeconds => L(3, 5);
    }
}
