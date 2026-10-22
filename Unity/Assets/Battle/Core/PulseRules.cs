using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// A line the data pulses run along (ticket 30.9): the corners of a level.json pulse line (x, z in Unity's coordinates) and its width.
    /// Arc length is metres from the first corner, the way the overlay mesh's UV.x counts it.
    /// </summary>
    public sealed class PulseLine
    {
        private readonly Vector2[] corners;
        private readonly float[] starts;

        public PulseLine(IReadOnlyList<Vector2> corners, float width)
        {
            if (corners == null || corners.Count < 2)
                throw new ArgumentException("A pulse line needs two corners or more", nameof(corners));
            this.corners = new Vector2[corners.Count];
            starts = new float[corners.Count];
            var length = 0f;
            for (var i = 0; i < this.corners.Length; i++)
            {
                this.corners[i] = corners[i];
                if (i > 0)
                    length += Vector2.Distance(corners[i - 1], corners[i]);
                starts[i] = length;
            }
            Length = length;
            Width = width;
        }

        public float Length { get; }

        public float Width { get; }

        /// <summary>The nearest point of the line to <paramref name="p"/>: its arc length from the first corner, and how far off the line p is.</summary>
        public void Project(Vector2 p, out float arc, out float distance)
        {
            arc = 0;
            distance = float.MaxValue;
            for (var i = 0; i + 1 < corners.Length; i++)
            {
                var a = corners[i];
                var ab = corners[i + 1] - a;
                var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(Vector2.Dot(ab, ab), 1e-12f));
                var d = Vector2.Distance(p, a + ab * t);
                if (d < distance)
                {
                    distance = d;
                    arc = starts[i] + t * Mathf.Sqrt(Vector2.Dot(ab, ab));
                }
            }
        }
    }

    /// <summary>
    /// The data pulses of the computer level (ticket 30.9): bands that leave the first corner of each pulse line every
    /// <see cref="every"/> seconds, run <see cref="speed"/> metres per second and are <see cref="length"/> metres long, the head in front.
    /// A scout on a line within <see cref="Reach"/> metres of a band gets <see cref="boost"/> times its move speed for
    /// <see cref="boostSeconds"/> seconds (<see cref="PulseBoost"/>). Pure, so the rule is tested without a scene; the SdfPulse shader
    /// draws the same bands from the same formula (<see cref="Behind"/>). The numbers are level.json's "pulses".
    /// <para>
    /// Time is one clock for the whole level, the scene's time, and pulses were already running at time 0 (the formula is periodic in
    /// time): the band at arc u is the one whose head passed that point <see cref="Behind"/> metres ago.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class PulseRules
    {
        /// <summary>How near a band counts as with it, in metres along the line, ahead of its head and behind its tail.</summary>
        public const float Reach = 1.5f;

        /// <summary>How far past a trace's edge counts as on it, in metres: a bus is three traces 0.8 m wide and 0.8 m apart, so this closes the gaps.</summary>
        public const float Margin = 0.4f;

        [Tooltip("Seconds between one pulse and the next")]
        public float every = 4;
        [Tooltip("Metres per second")]
        public float speed = 18;
        [Tooltip("A pulse's length in metres")]
        public float length = 3;
        [Tooltip("The move speed factor of a scout with a pulse")]
        public float boost = 1.35f;
        [Tooltip("Seconds the boost lasts after the pulse, refreshed by the next")]
        public float boostSeconds = 2.5f;

        /// <summary>Metres between one pulse's head and the next one's.</summary>
        public float Period => every * speed;

        /// <summary>
        /// Metres from the head of the nearest pulse ahead of arc <paramref name="arc"/>, back to it, at <paramref name="time"/>: 0 at a head,
        /// up to <see cref="Period"/>. The shader's <c>frac((speed * t - u) / period) * period</c>.
        /// </summary>
        public float Behind(float arc, float time)
        {
            var period = Mathf.Max(Period, 1e-3f);
            var x = (speed * time - arc) / period;
            return (x - Mathf.Floor(x)) * period;
        }

        /// <summary>Whether a pulse covers arc <paramref name="arc"/> at <paramref name="time"/>: the band the shader draws.</summary>
        public bool Covers(float arc, float time) => Behind(arc, time) < length;

        /// <summary>Whether a pulse is within <see cref="Reach"/> metres of arc <paramref name="arc"/> (along the line) at <paramref name="time"/>.</summary>
        public bool IsNear(float arc, float time) => length + 2 * Reach >= Period || Behind(arc - Reach, time) <= length + 2 * Reach;

        /// <summary>Whether a point is on a line (within its half width and <see cref="Margin"/> of its centre) and a pulse is near there.</summary>
        public bool IsBoosted(PulseLine line, Vector2 p, float time)
        {
            line.Project(p, out var arc, out var distance);
            return distance <= line.Width * 0.5f + Margin && IsNear(arc, time);
        }

        /// <summary>Whether a point is boosted on any of the lines.</summary>
        public bool IsBoosted(IReadOnlyList<PulseLine> lines, Vector2 p, float time)
        {
            for (var i = 0; i < lines.Count; i++)
                if (IsBoosted(lines[i], p, time))
                    return true;
            return false;
        }
    }

    /// <summary>
    /// One scout's boost (ticket 30.9): being with a pulse sets it to run out <see cref="PulseRules.boostSeconds"/> from now, so the next
    /// pulse refreshes it and nothing stacks. Plain data, one per player.
    /// </summary>
    public struct PulseBoost
    {
        private float until;

        /// <summary>Time the boost ends; before any boost, never.</summary>
        public float Until => until;

        public bool Active(float time) => time < until;

        /// <summary>Seconds left, 0 when there is no boost.</summary>
        public float Remaining(float time) => Mathf.Max(0, until - time);

        /// <summary>The scout is with a pulse at <paramref name="time"/>: the boost lasts <paramref name="seconds"/> from now (never shorter than it was).</summary>
        public void Refresh(float time, float seconds) => until = Mathf.Max(until, time + seconds);

        /// <summary>The factor on the scout's move speed: <paramref name="boost"/> while it lasts, else 1.</summary>
        public float Multiplier(float time, float boost) => Active(time) ? boost : 1;

        /// <summary>Ends it (the scout died).</summary>
        public void Clear() => until = 0;
    }
}
