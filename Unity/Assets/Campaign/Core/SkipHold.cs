using System;

namespace SdfCampaign
{
    /// <summary>Holding a key for a set time (the skip rule of ticket 32: Space, Escape or Start for 0.7 s), as plain timing.</summary>
    public sealed class SkipHold
    {
        public const float Seconds = 0.7f;

        private float held;

        /// <summary>How far the hold has got, 0 to 1.</summary>
        public float Progress => Math.Min(1, held / Seconds);

        /// <summary>Lets <paramref name="dt"/> seconds pass; true on the step the hold completes. Letting go starts it over.</summary>
        public bool Tick(float dt, bool down)
        {
            if (!down)
            {
                held = 0;
                return false;
            }
            var before = held;
            held += dt;
            return before < Seconds && held >= Seconds;
        }

        public void Reset() => held = 0;
    }
}
