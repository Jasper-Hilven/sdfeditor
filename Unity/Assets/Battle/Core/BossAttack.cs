using System.Collections.Generic;

namespace SdfBattle
{
    /// <summary>The life of one of Prime's attacks (ticket 33, section 10).</summary>
    public enum BossAttackStage
    {
        /// <summary>Not started (an attack in cooldown or waiting its turn).</summary>
        Ready,
        /// <summary>The telegraph: body pose, eye glow and the ground marker; nothing hurts yet.</summary>
        WindUp,
        /// <summary>The effect.</summary>
        Active,
        /// <summary>Afterwards; the slam's is the stuck cap.</summary>
        Recover,
    }

    /// <summary>
    /// One attack in progress: <c>Ready → WindUp → Active → Recover</c> with the seconds of its <see cref="Spec"/> (already scaled by the
    /// difficulty and Sore Loser), and who it is aimed at. Pure data: the world draws the marker and does the damage from it.
    /// </summary>
    public sealed class BossAttack
    {
        private readonly List<PlayerState> targets = new List<PlayerState>();

        internal BossAttack(BossAttackSpec spec)
        {
            Spec = spec;
            Stage = BossAttackStage.WindUp;
            TimeLeft = spec.windUp;
        }

        public BossAttackKind Kind => Spec.kind;

        public BossAttackSpec Spec { get; }

        public BossAttackStage Stage { get; private set; }

        /// <summary>Seconds left in the current stage.</summary>
        public float TimeLeft { get; private set; }

        /// <summary>The first target (the glare's, the slam's nearest, the grab's); null for an attack that needs none.</summary>
        public PlayerState Target => targets.Count > 0 ? targets[0] : null;

        /// <summary>Everyone aimed at: the mortar's up to three.</summary>
        public IReadOnlyList<PlayerState> Targets => targets;

        /// <summary>The hand that grabs (0 left, 1 right); alternates.</summary>
        public int Hand { get; internal set; }

        /// <summary>How far through the wind-up it is, 0 to 1: the marker pulses faster as it nears.</summary>
        public float WindUpProgress => Stage == BossAttackStage.WindUp
            ? (Spec.windUp <= 0 ? 1 : 1 - TimeLeft / Spec.windUp)
            : 1;

        public bool Finished { get; private set; }

        internal void AddTarget(PlayerState player) => targets.Add(player);

        /// <summary>
        /// Moves on by <paramref name="seconds"/>, at most to the end of the stage it is in; returns the seconds left over, and whether
        /// the stage changed (or the attack ended). The planner calls it again with what is left, so no stage is skipped unseen.
        /// </summary>
        internal float Advance(float seconds, out bool changed)
        {
            changed = false;
            if (Finished)
                return 0;
            if (seconds < TimeLeft - 1e-6f)
            {
                TimeLeft -= seconds;
                return 0;
            }
            var left = System.Math.Max(0, seconds - TimeLeft);
            changed = true;
            switch (Stage)
            {
                case BossAttackStage.WindUp:
                    Stage = BossAttackStage.Active;
                    TimeLeft = Spec.active;
                    break;
                case BossAttackStage.Active:
                    Stage = BossAttackStage.Recover;
                    TimeLeft = Spec.recover;
                    break;
                default:
                    Finished = true;
                    TimeLeft = 0;
                    break;
            }
            return left;
        }
    }
}
