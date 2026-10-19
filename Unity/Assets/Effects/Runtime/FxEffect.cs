using UnityEngine;

namespace SdfEffects
{
    /// <summary>
    /// One effect on the bench: it is fired, moved on by the bench's clock in <see cref="Tick"/> (never by its own Update, so the
    /// snapshots can step it in the editor), and draws into the bench's <see cref="FxBuffer"/> and <see cref="FxParticles"/>.
    /// </summary>
    public abstract class FxEffect : MonoBehaviour
    {
        protected FxBuffer Fx { get; private set; }
        protected FxParticles Particles { get; private set; }
        protected FxRandom Random { get; private set; }

        /// <summary>A short name for the bench's buttons.</summary>
        public abstract string Label { get; }

        /// <summary>How often the bench's loop fires it, in seconds.</summary>
        public float loopInterval = 2;

        /// <summary>Seconds into the loop of the first shot, so the effects take turns.</summary>
        public float loopOffset;

        public void Initialize(FxBuffer fx, FxParticles particles, FxRandom random)
        {
            Fx = fx;
            Particles = particles;
            Random = random;
        }

        public abstract void Fire();

        public abstract void Tick(float dt);

        /// <summary>Back to how it was before it first fired.</summary>
        public abstract void ResetEffect();

        /// <summary>The colour scaled past 1, so it reads as light through the tone mapping and catches the bloom.</summary>
        protected static Color Hdr(Color color, float intensity) => new Color(color.r * intensity, color.g * intensity, color.b * intensity, 1);
    }
}
