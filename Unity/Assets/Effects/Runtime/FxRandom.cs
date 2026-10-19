using UnityEngine;

namespace SdfEffects
{
    /// <summary>
    /// A small seeded random source (xorshift) for the effects, so a run from the same seed plays the same way (the snapshots rely on
    /// it) and the effects do not share UnityEngine.Random's state with anything else.
    /// </summary>
    public sealed class FxRandom
    {
        private uint state;

        public FxRandom(uint seed) => Reset(seed);

        public void Reset(uint seed) => state = seed == 0 ? 0x9E3779B9u : seed;

        private uint Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        /// <summary>0 to 1.</summary>
        public float Value => (Next() >> 8) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * Value;

        public int Range(int min, int maxExclusive) => min + (int)(Next() % (uint)Mathf.Max(1, maxExclusive - min));

        public Vector3 InsideUnitSphere
        {
            get
            {
                while (true)
                {
                    var p = new Vector3(Value * 2 - 1, Value * 2 - 1, Value * 2 - 1);
                    if (p.sqrMagnitude <= 1)
                        return p;
                }
            }
        }

        public Vector3 OnUnitSphere
        {
            get
            {
                while (true)
                {
                    var p = InsideUnitSphere;
                    var length = p.magnitude;
                    if (length > 1e-3f)
                        return p / length;
                }
            }
        }

        /// <summary>A direction within <paramref name="spread"/> (0 to 1, 1 being a hemisphere) of <paramref name="axis"/>.</summary>
        public Vector3 Around(Vector3 axis, float spread) => (axis.normalized + OnUnitSphere * spread).normalized;
    }
}
