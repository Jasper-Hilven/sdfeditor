using System;
using System.Collections.Generic;

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

    /// <summary>
    /// A wait between the sounds of a rapid-fire weapon, kept for each shooter: the minigun fires 14 shots a second, and a sound for each
    /// is more than the web player's audio can start and stop. <see cref="Allow"/> says whether a shooter's sound may play now; asking
    /// about a shooter with a sound it has just had is a no.
    /// </summary>
    public sealed class ShotSoundLimiter
    {
        /// <summary>The wait, in seconds, between two sounds of one shooter.</summary>
        public float Interval { get; }

        private readonly Dictionary<int, float> last = new Dictionary<int, float>();

        public ShotSoundLimiter(float interval) => Interval = Math.Max(0, interval);

        /// <summary>True (and the sound counted) when <paramref name="shooter"/> has had none for <see cref="Interval"/> seconds before <paramref name="now"/>.</summary>
        public bool Allow(int shooter, float now)
        {
            if (last.TryGetValue(shooter, out var before) && now - before < Interval)
                return false;
            last[shooter] = now;
            return true;
        }

        /// <summary>Forgets every shooter (a match is left).</summary>
        public void Clear() => last.Clear();
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
