using System;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// A bot's aim at its target (ticket 23), as error in degrees off the true direction, so it is beatable: on a new target it first
    /// waits its reaction time, then aims with an error that starts at <see cref="BotSkill.StartError"/> and shrinks towards
    /// <see cref="BotSkill.SteadyError"/> the longer it keeps tracking, plus a lag while the target crosses its view. The error wanders
    /// slowly round the target rather than jumping each frame, so shots miss to one side for a while, as a person's would. Losing a target
    /// for good (<see cref="Lose"/>) and finding a new one starts over; seeing the same target again keeps what tracking it had.
    /// Plain C#: the scene's bot adds <see cref="Offset"/> to the direction to its target.
    /// </summary>
    public sealed class BotAim
    {
        private readonly System.Random random;
        private float wander;
        private float wanderSpeed;

        public BotAim(BotSkill skill, System.Random random = null)
        {
            Skill = skill ?? new BotSkill();
            this.random = random ?? new System.Random();
            Target = -1;
        }

        public BotSkill Skill { get; }

        /// <summary>The target's roster index; -1 for none.</summary>
        public int Target { get; private set; }

        /// <summary>Seconds left before the first shot at the target.</summary>
        public float ReactionLeft { get; private set; }

        /// <summary>Seconds the target has been tracked since the reaction (the error shrinks with it).</summary>
        public float Tracked { get; private set; }

        /// <summary>The aim's error now, degrees off the target.</summary>
        public float Error { get; private set; }

        /// <summary>The error as degrees of yaw (x, right) and pitch (y, down) to add to the true aim.</summary>
        public Vector2 Offset { get; private set; }

        /// <summary>Has a target and has reacted to it: may fire.</summary>
        public bool Ready => Target >= 0 && ReactionLeft <= 0;

        /// <summary>The error after tracking a target for <paramref name="tracked"/> seconds, with the target still.</summary>
        public float ErrorAfter(float tracked) =>
            Skill.SteadyError + (Skill.StartError - Skill.SteadyError) * Mathf.Exp(-Mathf.Max(0, tracked) / Mathf.Max(0.01f, Skill.SettleSeconds));

        /// <summary>Starts on <paramref name="target"/> (a roster index); the same target again changes nothing.</summary>
        public void Acquire(int target)
        {
            if (target == Target)
                return;
            Target = target;
            if (target < 0)
            {
                Lose();
                return;
            }
            ReactionLeft = Skill.ReactionSeconds * (0.8f + 0.4f * (float)random.NextDouble());
            Tracked = 0;
            wander = (float)(random.NextDouble() * Math.PI * 2);
            wanderSpeed = (float)(random.NextDouble() - 0.5) * 3;
            Error = Skill.StartError;
            Offset = Direction() * Error;
        }

        /// <summary>No target any more.</summary>
        public void Lose()
        {
            Target = -1;
            ReactionLeft = 0;
            Tracked = 0;
            Error = 0;
            Offset = Vector2.zero;
        }

        /// <summary>
        /// Lets <paramref name="dt"/> pass while tracking, the target moving across the view at <paramref name="angularSpeed"/> degrees a
        /// second; returns the offset to aim with.
        /// </summary>
        public Vector2 Tick(float dt, float angularSpeed = 0)
        {
            if (Target < 0)
                return Vector2.zero;
            if (ReactionLeft > 0)
                ReactionLeft = Mathf.Max(0, ReactionLeft - dt);
            else
                Tracked += dt;
            // The error's direction drifts round the target, now and then changing its way
            wanderSpeed = Mathf.Clamp(wanderSpeed + (float)(random.NextDouble() - 0.5) * 6 * dt, -2.5f, 2.5f);
            wander += wanderSpeed * dt;
            Error = ErrorAfter(Tracked) + Mathf.Abs(angularSpeed) * Skill.TrackingLag;
            Offset = Direction() * Error;
            return Offset;
        }

        private Vector2 Direction() => new Vector2(Mathf.Cos(wander), Mathf.Sin(wander) * 0.6f);
    }
}
