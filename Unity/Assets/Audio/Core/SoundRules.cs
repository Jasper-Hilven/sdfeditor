using System;

namespace SdfAudio
{
    /// <summary>
    /// A walker's footsteps as plain numbers: one step for every <see cref="Stride"/> metres walked on the ground. Standing still or
    /// being in the air lets the count rest, and the first step after a rest comes a little early, so starting to walk is heard at once.
    /// </summary>
    public sealed class StrideCounter
    {
        /// <summary>Metres walked between two steps.</summary>
        public float Stride { get; }

        private float walked;

        public StrideCounter(float stride = 2f)
        {
            Stride = Math.Max(0.1f, stride);
            Rest();
        }

        /// <summary>Stood still or left the ground: the next step is a bit more than half a stride away.</summary>
        public void Rest() => walked = Stride * 0.55f;

        /// <summary>Walked <paramref name="metres"/> on the ground; true when that completed a step.</summary>
        public bool Advance(float metres)
        {
            walked += Math.Max(0, metres);
            if (walked < Stride)
                return false;
            walked -= Stride;
            return true;
        }
    }

    /// <summary>The low-health heartbeat: silent while health is above <see cref="Threshold"/>, then louder and faster as it falls to nothing.</summary>
    public static class HeartbeatRules
    {
        /// <summary>The share of full health below which the heart is heard.</summary>
        public const float Threshold = 0.4f;

        /// <summary>How hard the heart beats, 0 (silent) to 1, for a health share (0 to 1).</summary>
        public static float Strength(float healthShare)
        {
            var s = (Threshold - healthShare) / Threshold;
            return s < 0 ? 0 : s > 1 ? 1 : s;
        }

        /// <summary>The loop's volume (0 to 1) for a health share: the strength, eased in so a scratch is barely heard.</summary>
        public static float Volume(float healthShare)
        {
            var s = Strength(healthShare);
            return s * (2 - s);
        }

        /// <summary>The loop's pitch: 1 at the threshold, up to 1.3 at death's door.</summary>
        public static float Pitch(float healthShare) => 1 + 0.3f * Strength(healthShare);
    }
}
