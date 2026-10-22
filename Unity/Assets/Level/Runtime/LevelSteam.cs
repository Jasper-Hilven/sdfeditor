using System;
using SdfBattle;
using SdfEffects;
using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// The level's steam and drips (ticket 29.1, the level file's <c>steam</c> and <c>drip</c> lines): emitters that put soft puffs and
    /// falling drops into the match's <see cref="FxParticles"/> pool (<see cref="CombatFx.Particles"/>), so they draw with the other effects
    /// in the one buffer and cost no objects of their own. Without a match (the pool is not there) nothing is emitted.
    /// <para>
    /// A plume keeps <see cref="Plume.count"/> puffs alive (half as many on the low preset): each is born at the plume's point, rises its
    /// <see cref="Plume.rise"/> metres at a slow steady pace while it swells and dissolves, so the plume is a column of haze. A drip lets
    /// a drop fall every <see cref="Drip.every"/> seconds from its point to the water's height, where it makes a ring of motes that spread
    /// over the surface. The points are in Unity's coordinates (the level builder mirrors x).
    /// </para>
    /// </summary>
    public sealed class LevelSteam : MonoBehaviour
    {
        [Serializable]
        public struct Plume
        {
            public Vector3 at;
            [Tooltip("The puffs' width in metres")]
            public float size;
            [Tooltip("How far a puff rises before it is gone, in metres")]
            public float rise;
            [Tooltip("How many puffs are alive at a time")]
            public int count;
        }

        [Serializable]
        public struct Drip
        {
            public Vector3 at;
            [Tooltip("The height the drop lands at")]
            public float water;
            public float every;
            [Tooltip("The drop's width in metres")]
            public float size;
        }

        public Plume[] plumes = new Plume[0];
        public Drip[] drips = new Drip[0];
        [Tooltip("The quality level (by name) from which, and below, only half the puffs are made")]
        public string lowQualityLevel = "Integrated GPU / Web";
        [Tooltip("The steam's shade and its glow (sRGB)")]
        public Color band = new Color(0.9f, 0.93f, 0.92f);
        public Color core = new Color(1, 1, 0.98f);

        /// <summary>How fast a puff climbs: slow and steady.</summary>
        private const float RiseSpeed = 2.5f;
        private const float LeastLife = 4;
        private const float MostLife = 16;
        /// <summary>The longest step the emitters make up for; a pause or a hitch does not make a burst.</summary>
        private const float LongestStep = 0.25f;

        private float[] plumeClock;
        private float[] dripClock;
        private bool low;
        private bool primed;

        private void OnEnable()
        {
            plumeClock = new float[plumes.Length];
            dripClock = new float[drips.Length];
            primed = false;
            for (var i = 0; i < dripClock.Length; i++)
                dripClock[i] = drips[i].every * 0.5f;
            low = IsLowPreset();
        }

        private bool IsLowPreset()
        {
            var level = QualitySettings.GetQualityLevel();
            var lowest = Array.IndexOf(QualitySettings.names, lowQualityLevel);
            return lowest >= 0 && level >= lowest;
        }

        /// <summary>The seconds a puff of this plume lives: long enough to climb its rise at <see cref="RiseSpeed"/>.</summary>
        public static float LifeOf(Plume plume) => Mathf.Clamp(plume.rise / RiseSpeed, LeastLife, MostLife);

        /// <summary>How many puffs of a plume are alive at a time on the preset.</summary>
        public static int AliveOf(Plume plume, bool lowPreset) => lowPreset ? Mathf.Max(1, plume.count / 2) : plume.count;

        private void Update()
        {
            var particles = Combat.Current != null && Combat.Current.Fx != null ? Combat.Current.Fx.Particles : null;
            if (particles == null || plumeClock == null)
                return;
            var dt = Mathf.Min(Time.deltaTime, LongestStep);
            if (!primed)
            {
                primed = true;
                Prime(particles);
            }
            for (var i = 0; i < plumes.Length; i++)
            {
                var plume = plumes[i];
                var life = LifeOf(plume);
                // One puff every life / alive seconds keeps that many alive
                var gap = life / AliveOf(plume, low);
                plumeClock[i] -= dt;
                while (plumeClock[i] <= 0)
                {
                    plumeClock[i] += gap;
                    Puff(particles, plume, life);
                }
            }
            for (var i = 0; i < drips.Length; i++)
            {
                dripClock[i] -= dt;
                if (dripClock[i] <= 0)
                {
                    dripClock[i] += Mathf.Max(0.2f, drips[i].every);
                    Fall(particles, drips[i]);
                }
            }
            RingsDue(particles, dt);
        }

        /// <summary>The first puffs of every plume, already spread up its column, so the haze is there from the first frame.</summary>
        private void Prime(FxParticles particles)
        {
            foreach (var plume in plumes)
            {
                var life = LifeOf(plume);
                var alive = AliveOf(plume, low);
                for (var k = 0; k < alive; k++)
                    Puff(particles, plume, life, (k + 0.5f) / alive);
            }
        }

        /// <summary>A puff born <paramref name="aged"/> (0 to 1) of the way through its life.</summary>
        private void Puff(FxParticles particles, Plume plume, float life, float aged = 0)
        {
            var around = UnityEngine.Random.insideUnitCircle * plume.size * 0.3f;
            var start = plume.at + new Vector3(around.x, plume.rise * aged, around.y);
            var drift = UnityEngine.Random.insideUnitCircle * 0.25f;
            var velocity = new Vector3(drift.x, plume.rise / life, drift.y);
            // A puff's size is a radius: half the plume's width, a little different each time
            particles.Emit(FxParticles.Kind.Spore, start, velocity, life * (1 - aged), plume.size * 0.5f * UnityEngine.Random.Range(0.8f, 1.2f), band, core);
        }

        // ---------------------------------------------------------------- Drips

        private struct Landing
        {
            public float time;
            public Vector3 at;
            public float size;
        }

        private readonly Landing[] landings = new Landing[8];
        private int landingCount;

        /// <summary>A drop leaves the tap; the ring it makes is due when it has fallen.</summary>
        private void Fall(FxParticles particles, Drip drip)
        {
            var height = Mathf.Max(0.01f, drip.at.y - drip.water);
            // Falling from rest under 9.8 m/s^2
            const float gravity = 9.8f;
            var time = Mathf.Sqrt(2 * height / gravity);
            particles.Emit(FxParticles.Kind.Drop, drip.at, Vector3.zero, time, drip.size * 0.5f, band, core, gravity: gravity);
            if (landingCount < landings.Length)
                landings[landingCount++] = new Landing { time = time, at = new Vector3(drip.at.x, drip.water, drip.at.z), size = drip.size };
        }

        /// <summary>The rings of the drops that have landed: motes that spread over the water and dissolve.</summary>
        private void RingsDue(FxParticles particles, float dt)
        {
            for (var i = 0; i < landingCount; i++)
            {
                landings[i].time -= dt;
                if (landings[i].time > 0)
                    continue;
                var landing = landings[i];
                landings[i] = landings[--landingCount];
                i--;
                const int motes = 8;
                for (var k = 0; k < motes; k++)
                {
                    var angle = k * Mathf.PI * 2 / motes;
                    var outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    particles.Emit(FxParticles.Kind.Spore, landing.at + outward * landing.size * 0.5f, outward * landing.size * 4f, 0.8f,
                        landing.size * 0.35f, band, core, drag: 3);
                }
            }
        }
    }
}
