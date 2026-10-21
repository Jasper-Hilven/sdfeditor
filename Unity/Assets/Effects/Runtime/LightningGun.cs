using UnityEngine;

namespace SdfEffects
{
    /// <summary>
    /// The electro gun's lightning: a jagged bolt from the orb at the muzzle to the target, with a few forks off it, redrawn in a new
    /// shape every <see cref="flickerInterval"/> (and now and then left out for a frame, so it strobes), for <see cref="duration"/>
    /// per shot. Cel-painted rather than glowing: a white core in a hard-edged electric-blue band (the gun's glass core), drawn as
    /// streaks in the <see cref="FxBuffer"/>, so the whole bolt is part of the one effects draw and its shape is rebuilt on the CPU at
    /// no cost worth counting. Where it strikes: a flash and a spray of sparks with every new shape.
    /// <para>
    /// The bolt's shape is midpoint displacement: each segment's middle pushed sideways by a share of its length, halving down to
    /// <see cref="MainPoints"/> points. The points live in arrays made once.
    /// </para>
    /// </summary>
    public sealed class LightningGun : FxEffect
    {
        [Tooltip("The orb at the gun's muzzle, where the bolt starts")]
        public Transform muzzle;
        [Tooltip("What it strikes: the bolt ends on its surface, at a point near its middle")]
        public Collider target;

        public float duration = 0.5f;
        public float flickerInterval = 0.05f;
        [Tooltip("Width of the bolt in metres")]
        public float width = 0.075f;
        [Tooltip("How far the bolt strays from a straight line, as a share of each segment's length")]
        public float jaggedness = 0.22f;
        public Color band = new Color(0.25f, 0.75f, 1f);
        public Color core = new Color(0.92f, 0.98f, 1f);
        [Tooltip("How bright the band is: about 1, so the tone mapping keeps its colour")]
        public float bandIntensity = 1.15f;
        [Tooltip("How bright the core is, well above 1 so it reads as white-hot light")]
        public float coreIntensity = 2.8f;

        public override string Label => "Lightning";

        private const int MainDepth = 5;
        private const int MainPoints = (1 << MainDepth) + 1;
        private const int ForkDepth = 3;
        private const int ForkPoints = (1 << ForkDepth) + 1;
        private const int Forks = 4;

        private readonly Vector3[] bolt = new Vector3[MainPoints];
        private readonly Vector3[][] forks = { new Vector3[ForkPoints], new Vector3[ForkPoints], new Vector3[ForkPoints], new Vector3[ForkPoints] };
        private readonly float[] forkWidths = new float[Forks];
        private int forkCount;
        private float time = -1;
        private float sinceShape;
        private bool shown;
        private Vector3 hit;
        private Vector3 hitNormal;

        public bool Active => time >= 0 && time < duration;

        private Vector3 strikePoint;
        private Vector3 strikeNormal;

        /// <summary>
        /// Fires at a point the game found itself (ticket 22's electro-gun) rather than at <see cref="target"/>: the bolt ends there,
        /// on a surface facing <paramref name="normal"/>. Clears the target.
        /// </summary>
        public void Strike(Vector3 point, Vector3 normal)
        {
            target = null;
            strikePoint = point;
            strikeNormal = normal;
            Fire();
        }

        public override void Fire()
        {
            // Firing again while it lasts keeps it going
            time = Active ? Mathf.Min(time, flickerInterval) : 0;
            sinceShape = flickerInterval;
        }

        public override void ResetEffect() => time = -1;

        public override void Tick(float dt)
        {
            if (!Active)
                return;
            time += dt;
            sinceShape += dt;
            if (sinceShape >= flickerInterval)
            {
                sinceShape = 0;
                Reshape();
            }
            if (!Active)
                return;

            // The charged orb, flickering with the bolt
            var flicker = Random.Range(0.75f, 1.15f);
            Fx.Glow(muzzle.position, 0.045f * flicker, Hdr(band, bandIntensity), Hdr(core, coreIntensity), 0.55f);
            if (!shown)
                return;

            // The bolt thins towards the end of the shot, then is gone
            var fade = 1 - Mathf.SmoothStep(0.6f, 1, time / duration);
            var w = width * flicker * Mathf.Lerp(0.35f, 1, fade);
            var bandColor = Hdr(band, bandIntensity);
            var coreColor = Hdr(core, coreIntensity);
            for (var i = 0; i < MainPoints - 1; i++)
                Fx.Streak(bolt[i], bolt[i + 1], w, w, bandColor, coreColor, 0.4f);
            for (var f = 0; f < forkCount; f++)
            {
                var points = forks[f];
                for (var i = 0; i < ForkPoints - 1; i++)
                {
                    var t0 = (float)i / (ForkPoints - 1);
                    var t1 = (float)(i + 1) / (ForkPoints - 1);
                    Fx.Streak(points[i], points[i + 1], w * forkWidths[f] * (1 - t0), w * forkWidths[f] * (1 - t1), bandColor, coreColor, 0.35f);
                }
            }
            // The flash where it strikes
            Fx.Glow(hit + hitNormal * 0.03f, Random.Range(0.12f, 0.2f) * Mathf.Lerp(0.5f, 1, fade), bandColor, coreColor, 0.6f);
        }

        /// <summary>A new shape for the bolt and its forks, and sparks where it strikes; one shape in ten is left out (a strobe).</summary>
        private void Reshape()
        {
            shown = Random.Value > 0.1f || time < flickerInterval;
            var start = muzzle.position;
            if (target == null)
            {
                // A point given by Strike
                hit = strikePoint;
                hitNormal = strikeNormal;
            }
            else
            {
                // A point on the target's surface near its middle, found along the line to it
                var bounds = target.bounds;
                var aim = bounds.center + Vector3.Scale(bounds.extents, Random.InsideUnitSphere) * 0.35f;
                if (target.Raycast(new Ray(start, aim - start), out var hitInfo, 1000))
                {
                    hit = hitInfo.point;
                    hitNormal = hitInfo.normal;
                }
                else
                {
                    hit = aim;
                    hitNormal = (start - aim).normalized;
                }
            }

            bolt[0] = start;
            bolt[MainPoints - 1] = hit;
            Subdivide(bolt, 0, MainPoints - 1, jaggedness);
            // Keep the first stretch out of the gun: the bolt leaves the orb straight ahead
            bolt[1] = Vector3.Lerp(bolt[0], bolt[2], 0.5f);

            forkCount = Random.Range(2, Forks + 1);
            var length = Vector3.Distance(start, hit);
            for (var f = 0; f < forkCount; f++)
            {
                var from = Random.Range(MainPoints / 5, MainPoints - 3);
                var direction = Random.Around(bolt[from + 1] - bolt[from], 0.9f);
                var points = forks[f];
                points[0] = bolt[from];
                points[ForkPoints - 1] = bolt[from] + direction * length * Random.Range(0.08f, 0.22f);
                Subdivide(points, 0, ForkPoints - 1, jaggedness * 1.3f);
                forkWidths[f] = Random.Range(0.45f, 0.7f);
            }

            if (!shown)
                return;
            // Sparks thrown off the surface it strikes
            var sparkBand = Hdr(band, bandIntensity);
            var sparkCore = Hdr(core, coreIntensity);
            for (var i = 0; i < 4; i++)
                Particles.Emit(FxParticles.Kind.Spark, hit + hitNormal * 0.02f, Random.Around(hitNormal, 0.8f) * Random.Range(2.5f, 5.5f),
                    Random.Range(0.2f, 0.4f), Random.Range(0.02f, 0.03f), sparkBand, sparkCore, gravity: 9.8f);
        }

        /// <summary>Fills the points between <paramref name="a"/> and <paramref name="b"/> by midpoint displacement.</summary>
        private void Subdivide(Vector3[] points, int a, int b, float amount)
        {
            if (b - a < 2)
                return;
            var m = (a + b) / 2;
            var along = points[b] - points[a];
            var sideways = Random.OnUnitSphere;
            sideways -= along.normalized * Vector3.Dot(sideways, along.normalized);
            points[m] = (points[a] + points[b]) * 0.5f + sideways * (along.magnitude * amount);
            Subdivide(points, a, m, amount);
            Subdivide(points, m, b, amount);
        }
    }
}
