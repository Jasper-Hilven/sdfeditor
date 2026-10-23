using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>Where a group of heat rings is in its turn (ticket 28.10).</summary>
    public enum HeatPhase
    {
        /// <summary>Not its turn: cold glass.</summary>
        Cold,
        /// <summary>A dull ember glow, harmless; bots already keep off.</summary>
        Warming,
        /// <summary>Hot: it hurts whatever stands in it, and bots keep off.</summary>
        Hot,
        /// <summary>The glow fades, harmless.</summary>
        Cooling,
    }

    /// <summary>One ring of floor that heats (level.json "heat" rings): its centre (x, z in Unity's coordinates), radius and group.</summary>
    [Serializable]
    public struct HeatRing
    {
        public Vector2 centre;
        public float radius;
        [Tooltip("The index of the group it heats with; 0 takes the first turn")]
        public int group;

        public HeatRing(Vector2 centre, float radius, int group)
        {
            this.centre = centre;
            this.radius = radius;
            this.group = group;
        }

        /// <summary>Whether a point (x, z) is in the ring.</summary>
        public bool Contains(Vector2 p) => (p - centre).sqrMagnitude <= radius * radius;
    }

    /// <summary>
    /// The heat of a level (ticket 28.10, the kitchen's hob): rings in groups that heat in turns on the round clock. Group 0 starts to warm
    /// <see cref="first"/> seconds into a round, warms for <see cref="warm"/>, is hot for <see cref="hot"/> and cools for <see cref="cool"/>; the
    /// next group follows at once; once every group has had its turn the rest of the <see cref="cycle"/> is cold, and then it starts over.
    /// A group's rings are one, so a mirrored pair is hot together. Pure, so the rules are tested without a scene; the numbers are level.json's
    /// "heat" (the kitchen: 45 s cycle, 3 s warm, 12 s hot, 3 s cool, 10 damage a second, path cost x50).
    /// </summary>
    [Serializable]
    public sealed class HeatRules
    {
        /// <summary>The step of the damage: it comes in ticks like a burn's (<see cref="CombatState.TickSeconds"/>).</summary>
        public const float TickSeconds = 0.25f;

        [Tooltip("Seconds for every group to have had its turn and the cold glass after them, then again")]
        public float cycle = 45;
        [Tooltip("Seconds into a round the first group starts to warm")]
        public float first = 20;
        public float warm = 3;
        public float hot = 12;
        public float cool = 3;
        [Tooltip("Damage a second to a scout or tank in a hot ring")]
        public float damage = 10;
        [Tooltip("The factor on the path cost of a warming or hot ring, for bots and tanks")]
        public float cost = 50;
        [Tooltip("How the kill feed names a kill by it")]
        public string cause = "the heat";
        [Tooltip("How many groups take turns")]
        public int groups = 1;

        /// <summary>Seconds one group's turn takes.</summary>
        public float Span => warm + hot + cool;

        /// <summary>Where group <paramref name="group"/> is in its turn at round clock <paramref name="time"/>, and seconds into that phase.</summary>
        public HeatPhase PhaseOf(int group, float time, out float elapsed)
        {
            elapsed = 0;
            var t = time - first;
            if (t < 0 || cycle <= 0 || group < 0 || group >= groups)
                return HeatPhase.Cold;
            var into = t % cycle - group * Span;
            if (into < 0 || into >= Span)
                return HeatPhase.Cold;
            if (into < warm)
            {
                elapsed = into;
                return HeatPhase.Warming;
            }
            if (into < warm + hot)
            {
                elapsed = into - warm;
                return HeatPhase.Hot;
            }
            elapsed = into - warm - hot;
            return HeatPhase.Cooling;
        }

        public HeatPhase PhaseOf(int group, float time) => PhaseOf(group, time, out _);

        /// <summary>
        /// How hot the glass looks, 0 (cold) to 1 (white yellow): it warms to a dull ember (0.3), takes off to bright orange (0.65) and yellow
        /// (1) over the first half of the hot time, then the cooling takes it down to 0 again.
        /// </summary>
        public float Heat(int group, float time)
        {
            switch (PhaseOf(group, time, out var elapsed))
            {
                case HeatPhase.Warming:
                    return Mathf.Lerp(0, EmberHeat, Mathf.Clamp01(elapsed / Mathf.Max(warm, 1e-3f)));
                case HeatPhase.Hot:
                    return Mathf.Lerp(EmberHeat, 1, Mathf.Clamp01(elapsed / Mathf.Max(hot * 0.5f, 1e-3f)));
                case HeatPhase.Cooling:
                    return 1 - Mathf.Clamp01(elapsed / Mathf.Max(cool, 1e-3f));
                default:
                    return 0;
            }
        }

        /// <summary>The heat at the end of the warming: a dull ember.</summary>
        public const float EmberHeat = 0.3f;

        /// <summary>Whether a ring in this phase hurts: only a hot one (the warming glow warns, the cooling one is spent).</summary>
        public static bool Hurts(HeatPhase phase) => phase == HeatPhase.Hot;

        /// <summary>Whether bots and tanks keep off a ring in this phase: while it warms and is hot.</summary>
        public static bool Avoided(HeatPhase phase) => phase == HeatPhase.Warming || phase == HeatPhase.Hot;

        /// <summary>The navmesh cost factor of a group's rings now: <see cref="cost"/> while they warm and are hot, else 1.</summary>
        public float PathCost(int group, float time) => Avoided(PhaseOf(group, time)) ? Mathf.Max(1, cost) : 1;

        /// <summary>Whether a point stands in a hot ring at <paramref name="time"/>.</summary>
        public bool IsHot(IReadOnlyList<HeatRing> rings, Vector2 p, float time) => RingOn(rings, p, time, hotOnly: true) >= 0;

        /// <summary>Whether a point is in a ring bots keep off (warming or hot) at <paramref name="time"/>.</summary>
        public bool IsAvoided(IReadOnlyList<HeatRing> rings, Vector2 p, float time) => RingOn(rings, p, time, hotOnly: false) >= 0;

        /// <summary>The index of the first ring the point is in that hurts (<paramref name="hotOnly"/>) or is avoided, or -1.</summary>
        public int RingOn(IReadOnlyList<HeatRing> rings, Vector2 p, float time, bool hotOnly)
        {
            for (var i = 0; i < rings.Count; i++)
            {
                if (!rings[i].Contains(p))
                    continue;
                var phase = PhaseOf(rings[i].group, time);
                if (hotOnly ? Hurts(phase) : Avoided(phase))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Where a bot or tank standing in a ring steps to to be off it: the way out from the centre (any way from the very middle) and
        /// the metres to the ring's edge plus <paramref name="margin"/>.
        /// </summary>
        public static Vector2 StepOff(HeatRing ring, Vector2 p, float margin, out float distance)
        {
            var away = p - ring.centre;
            var length = away.magnitude;
            var direction = length > 1e-3f ? away / length : Vector2.right;
            distance = Mathf.Max(0, ring.radius - length) + margin;
            return direction;
        }
    }

    /// <summary>
    /// One scout's or tank's burn from a hot ring (ticket 28.10): it comes in ticks of <see cref="HeatRules.TickSeconds"/> while it stands in
    /// one, the rest of a tick is lost on leaving. Plain data, one per thing that can burn.
    /// </summary>
    public struct HeatBurn
    {
        private float clock;

        /// <summary>
        /// Lets <paramref name="dt"/> seconds pass; returns the damage to deal now (0 between ticks): <paramref name="perSecond"/> times the
        /// ticks that are full, so a second in a hot ring is a second's damage.
        /// </summary>
        public float Step(float dt, bool inHotRing, float perSecond)
        {
            if (!inHotRing)
            {
                clock = 0;
                return 0;
            }
            clock += dt;
            // A little slack, so that ticks do not slip a frame on float sums (0.05 five times is not quite 0.25)
            if (clock + 1e-4f < HeatRules.TickSeconds)
                return 0;
            var ticks = Mathf.Floor((clock + 1e-4f) / HeatRules.TickSeconds);
            clock = Mathf.Max(0, clock - ticks * HeatRules.TickSeconds);
            return ticks * HeatRules.TickSeconds * perSecond;
        }

        public void Clear() => clock = 0;
    }

    /// <summary>
    /// The glow of hot glass (ticket 28.10): a flat colour from the heat value of <see cref="HeatRules.Heat"/>, ember to orange to yellow
    /// and never scarlet (the red team's colour is the fight's), with how much of the burner's own colour it takes over.
    /// </summary>
    public static class HeatGlow
    {
        // Linear-ish display colours: a dull ember, bright orange, yellow
        private static readonly Color Ember = new Color(0.62f, 0.27f, 0.05f);
        private static readonly Color Orange = new Color(1.0f, 0.47f, 0.07f);
        private static readonly Color Yellow = new Color(1.0f, 0.84f, 0.25f);

        /// <summary>The glow's colour at <paramref name="heat"/> (0 to 1); alpha is the share it covers the burner's own colour with, 0 when cold.</summary>
        public static Color Of(float heat)
        {
            heat = Mathf.Clamp01(heat);
            Color colour;
            if (heat < HeatRules.EmberHeat)
                colour = Ember;
            else if (heat < 0.65f)
                colour = Color.Lerp(Ember, Orange, (heat - HeatRules.EmberHeat) / (0.65f - HeatRules.EmberHeat));
            else
                colour = Color.Lerp(Orange, Yellow, (heat - 0.65f) / 0.35f);
            // Cold steel to a dull glow in the warming, then all the way
            colour.a = Mathf.Lerp(0, 0.75f, Mathf.Clamp01(heat / HeatRules.EmberHeat)) + 0.25f * Mathf.Clamp01((heat - HeatRules.EmberHeat) / 0.35f);
            return colour;
        }
    }
}
