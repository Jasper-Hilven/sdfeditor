using UnityEngine;

namespace SdfEffects
{
    /// <summary>
    /// The small things the effects throw about, drawn into the <see cref="FxBuffer"/>: sparks (streaks along their flight), puffs of
    /// smoke or dust (lit balls that swell and shrink away), drops (lit balls that fall) and spores (glowing motes that drift up and
    /// dissolve). A fixed pool, the oldest reused when it is full; no allocation after the first frame. Simple motion: gravity, drag,
    /// and a bounce off the ground plane at <see cref="groundHeight"/>, no colliders.
    /// <para>
    /// Epic and up (<see cref="SdfMushrooms.QualityExtras.ParticleCopies"/>, <see cref="SdfMushrooms.QualityExtras.ParticleLife"/>): each
    /// small particle comes with one or two scattered copies, puffs, drops and spores live longer, and the pool grows to match; but not
    /// within <see cref="ClearAround"/> of the camera, so your own weapon (the flamethrower's flame and smoke, a muzzle, a hit right in
    /// front) hides no more of the view than on the other levels.
    /// </para>
    /// </summary>
    public sealed class FxParticles : MonoBehaviour
    {
        public enum Kind : byte
        {
            /// <summary>A hot streak along its velocity, shortening as it dies.</summary>
            Spark,
            /// <summary>A lit ball that swells to its size quickly, then shrinks to nothing.</summary>
            Puff,
            /// <summary>A lit ball that keeps its size and shrinks only at the end; drops of goop.</summary>
            Drop,
            /// <summary>A glowing mote that dissolves (dithered) over its life.</summary>
            Spore,
        }

        private struct Particle
        {
            public Kind kind;
            public Vector3 position;
            public Vector3 velocity;
            public float age;
            public float life;
            public float size;
            public float gravity;
            public float drag;
            public float growIn;
            public float growFrom;
            public Color band;
            public Color core;
        }

        public int capacity = 768;
        [Tooltip("Height of the ground the particles bounce on and settle")]
        public float groundHeight;

        /// <summary>Particles at least this big (a flash, a steam plume's ball) get no copies on Epic and up: two would read as two.</summary>
        private const float CopiedBelow = 0.4f;
        /// <summary>Metres round the camera where particles get none of Epic's extras: the flamethrower's smoke rises up to about 5.5 m out.</summary>
        private const float ClearAround = 6;

        private Particle[] particles;
        private int count;
        private int next;
        private readonly System.Random scatter = new System.Random(1);

        public int Count => count;

        private void Ensure()
        {
            // Room for the copies and the longer lives Epic and up add, so they push out no other effect's particles
            var size = Mathf.CeilToInt(capacity * (1 + SdfMushrooms.QualityExtras.ParticleCopies) * SdfMushrooms.QualityExtras.ParticleLife);
            if (particles == null || particles.Length != size)
            {
                particles = new Particle[size];
                count = 0;
                next = 0;
            }
        }

        public void Clear()
        {
            Ensure();
            count = 0;
            next = 0;
        }

        /// <summary>
        /// Adds one; <paramref name="size"/> is a spark's width, a ball's radius. <paramref name="core"/> is a spark's or spore's core
        /// colour, or the glow in a lit ball's shade. <paramref name="growIn"/> above 0 starts it at <paramref name="growFrom"/> of its
        /// size and grows it to full size over that many seconds of its own age (the flame, small at the muzzle by the camera).
        /// </summary>
        public void Emit(Kind kind, Vector3 position, Vector3 velocity, float life, float size, Color band, Color core, float gravity = 0, float drag = 0,
            float growIn = 0, float growFrom = 1)
        {
            Ensure();
            var view = Camera.main;
            if (view != null && (position - view.transform.position).sqrMagnitude < ClearAround * ClearAround)
            {
                Add(kind, position, velocity, life, size, band, core, gravity, drag, growIn, growFrom);
                return;
            }
            if (kind != Kind.Spark)
                life *= SdfMushrooms.QualityExtras.ParticleLife;
            Add(kind, position, velocity, life, size, band, core, gravity, drag, growIn, growFrom);
            if (size >= CopiedBelow)
                return;
            // Epic and up: copies scattered round it, a little smaller, flying a little differently
            for (var c = SdfMushrooms.QualityExtras.ParticleCopies; c > 0; c--)
            {
                var offset = new Vector3(Next(-1, 1), Next(-1, 1), Next(-1, 1)) * Mathf.Max(size * 1.5f, 0.05f);
                var turn = new Vector3(Next(-1, 1), Next(-1, 1), Next(-1, 1)) * (velocity.magnitude * 0.2f);
                Add(kind, position + offset, (velocity + turn) * Next(0.85f, 1.15f), life * Next(0.8f, 1.1f), size * Next(0.7f, 1), band, core,
                    gravity, drag, growIn, growFrom);
            }
        }

        private float Next(float from, float to) => from + (float)scatter.NextDouble() * (to - from);

        private void Add(Kind kind, Vector3 position, Vector3 velocity, float life, float size, Color band, Color core, float gravity, float drag,
            float growIn, float growFrom)
        {
            int index;
            if (count < particles.Length)
            {
                index = count++;
            }
            else
            {
                // Full: the oldest slot goes round
                index = next;
                next = (next + 1) % particles.Length;
            }
            particles[index] = new Particle
            {
                kind = kind, position = position, velocity = velocity, life = Mathf.Max(life, 1e-3f), size = size, gravity = gravity, drag = drag,
                growIn = growIn, growFrom = growFrom, band = band, core = core,
            };
        }

        /// <summary>Moves every particle on by <paramref name="dt"/>, drops the dead ones and draws the rest.</summary>
        public void Tick(float dt, FxBuffer fx)
        {
            Ensure();
            for (var i = 0; i < count; i++)
            {
                ref var p = ref particles[i];
                p.age += dt;
                if (p.age >= p.life)
                {
                    // Swap the last one in, and look at this slot again
                    particles[i] = particles[--count];
                    if (next > count)
                        next = 0;
                    i--;
                    continue;
                }
                p.velocity += Vector3.down * (p.gravity * dt);
                p.velocity *= 1 / (1 + p.drag * dt);
                p.position += p.velocity * dt;
                if (p.position.y < groundHeight && p.velocity.y < 0)
                {
                    p.position.y = groundHeight;
                    p.velocity = new Vector3(p.velocity.x * 0.4f, -p.velocity.y * 0.25f, p.velocity.z * 0.4f);
                }
                Draw(ref p, fx);
            }
        }

        private static void Draw(ref Particle p, FxBuffer fx)
        {
            var t = p.age / p.life;
            var size = p.size;
            if (p.growIn > 0)
                size *= Mathf.Lerp(p.growFrom, 1, Mathf.Clamp01(p.age / p.growIn));
            switch (p.kind)
            {
                case Kind.Spark:
                    // A streak behind it as long as 30 ms of its flight, thinning to nothing
                    var width = size * (1 - t);
                    fx.Streak(p.position - p.velocity * 0.03f, p.position, width * 0.4f, width, p.band, p.core, 0.45f);
                    break;
                case Kind.Puff:
                    // Swells over the first fifth of its life, then shrinks away
                    var grow = t < 0.2f ? Mathf.SmoothStep(0.25f, 1, t / 0.2f) : 1 - Mathf.SmoothStep(0, 1, (t - 0.2f) / 0.8f);
                    fx.Lit(p.position, size * grow, p.band, p.core);
                    break;
                case Kind.Drop:
                    fx.Lit(p.position, size * (t < 0.7f ? 1 : 1 - (t - 0.7f) / 0.3f), p.band, p.core);
                    break;
                case Kind.Spore:
                    fx.Glow(p.position, size * (1 - 0.4f * t), p.band, p.core, 0.45f, 1 - t * t);
                    break;
            }
        }
    }
}
