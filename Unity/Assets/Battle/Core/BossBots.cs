using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>A circle on the floor Prime's attack is about to hit (a wind-up's marker, ticket 33): bots step out of it.</summary>
    public readonly struct DangerZone
    {
        public DangerZone(Vector2 centre, float radius)
        {
            Centre = centre;
            Radius = radius;
        }

        /// <summary>Its middle on the floor (x, z).</summary>
        public Vector2 Centre { get; }

        public float Radius { get; }
    }

    /// <summary>
    /// "Stay out of markers" (ticket 33, section 10): a bot standing in an active marker steps out of it, by circle tests against the
    /// planner's list (cheap: a handful of circles). Plain C#.
    /// </summary>
    public static class BotDanger
    {
        /// <summary>Metres beyond a circle's edge a bot wants to be.</summary>
        public const float Margin = 1.2f;

        /// <summary>
        /// Whether <paramref name="position"/> (x, z) is in a circle or its margin, and then the flat way out: away from the middle of
        /// the deepest one it is in (along +x when it stands right on the middle).
        /// </summary>
        public static bool Escape(Vector2 position, IReadOnlyList<DangerZone> zones, out Vector2 direction)
        {
            direction = Vector2.zero;
            if (zones == null)
                return false;
            var deepest = 0f;
            for (var i = 0; i < zones.Count; i++)
            {
                var away = position - zones[i].Centre;
                var depth = zones[i].Radius + Margin - away.magnitude;
                if (depth <= deepest)
                    continue;
                deepest = depth;
                direction = away.sqrMagnitude > 1e-6f ? away.normalized : Vector2.right;
            }
            return deepest > 0;
        }
    }

    /// <summary>
    /// The red bots' and the blue crew's temperaments in each phase of the boss fight (ticket 33, section 7), by their place in their
    /// team (0 first): the weights <see cref="BotBrain"/> scores its goals with.
    /// <list type="bullet">
    /// <item>Red, phase 1: one raider (the blue flag), one guardian (the red flag), one hunter (the crew); phase 2: all hunt (the sacs and
    /// the tanks) and fetch (the electro-gun); phase 3: one raider for the crown, the others harass Prime from range.</item>
    /// <item>Blue, phase 1: it plays capture the flag too (a raider, a guardian, hunters); phases 2 and 3: hunters, and in phase 3 one
    /// guards the crown.</item>
    /// </list>
    /// </summary>
    public static class BossTemperaments
    {
        public static Temperament Red(BossPhase phase, int place)
        {
            switch (phase)
            {
                case BossPhase.Phase2:
                case BossPhase.Transition23:
                    return new Temperament(0, 0, 0, 1.4f, 1.2f);
                case BossPhase.Phase3:
                    return place == 0 ? Temperament.Of(BotStyle.Raider) : new Temperament(0.3f, 0.2f, 0.5f, 1.4f, 0.8f);
                default:
                    return place switch
                    {
                        0 => Temperament.Of(BotStyle.Raider),
                        1 => Temperament.Of(BotStyle.Guardian),
                        2 => Temperament.Of(BotStyle.Hunter),
                        _ => Temperament.Of(BotStyle.AllRounder),
                    };
            }
        }

        public static Temperament Blue(BossPhase phase, int place)
        {
            switch (phase)
            {
                case BossPhase.Phase2:
                case BossPhase.Transition23:
                    return new Temperament(0, 0, 0, 1.3f, 0.6f);
                case BossPhase.Phase3:
                    return place == 0 ? new Temperament(0, 1.4f, 1.2f, 0.8f, 0.4f) : new Temperament(0, 0, 0.3f, 1.4f, 0.6f);
                default:
                    return place switch
                    {
                        0 => Temperament.Of(BotStyle.Raider),
                        1 => Temperament.Of(BotStyle.Guardian),
                        _ => Temperament.Of(BotStyle.Hunter),
                    };
            }
        }

        /// <summary>The temperament of a team's bot in a phase.</summary>
        public static Temperament Of(Team team, BossPhase phase, int place) => team == Team.Red ? Red(phase, place) : Blue(phase, place);
    }
}
