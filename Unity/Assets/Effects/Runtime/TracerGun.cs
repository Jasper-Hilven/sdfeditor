using UnityEngine;

namespace SdfEffects
{
    /// <summary>
    /// A bullet gun's shots as tracer lines: a bright streak <see cref="tracerLength"/> long flying at <see cref="speed"/> from the
    /// muzzle to whatever the shot hits, tapering to its tail, in the weapon's own colour (set per weapon on its component). Each shot
    /// has a muzzle flash (a cel-painted star for a couple of frames) and, where it lands, a puff of dust and a few sparks. A burst
    /// fires <see cref="burst"/> shots <see cref="fireInterval"/> apart, spread by up to <see cref="spread"/> degrees; a sniper's shot
    /// leaves a thin trail along its line that thins away. Tracers are a fixed pool, drawn into the <see cref="FxBuffer"/>.
    /// </summary>
    public sealed class TracerGun : FxEffect
    {
        [Tooltip("The end of the barrel; the gun shoots along its forward axis")]
        public Transform muzzle;
        public string label = "Pistol";

        [Header("Tracer")]
        [Tooltip("Metres per second; real bullets are faster, tracers are slowed so they read")]
        public float speed = 120;
        public float tracerLength = 1.5f;
        public float tracerWidth = 0.025f;
        public Color band = new Color(1f, 0.7f, 0.2f);
        public Color core = new Color(1f, 0.97f, 0.8f);
        [Tooltip("How bright the band is: about 1, so the tone mapping keeps the weapon's colour")]
        public float bandIntensity = 1.2f;
        [Tooltip("How bright the core is, well above 1 so it reads as hot")]
        public float coreIntensity = 2.6f;
        [Tooltip("Seconds a trail along the shot's line lasts; 0 for none")]
        public float trail;

        [Header("Firing")]
        public int burst = 1;
        public float fireInterval = 0.06f;
        [Tooltip("Largest angle off the barrel's axis, in degrees")]
        public float spread = 0.4f;
        public float range = 60;

        [Header("Flash and impact")]
        public float flashSize = 0.09f;
        public Color dust = new Color(0.62f, 0.55f, 0.46f);

        public override string Label => label;

        private struct Tracer
        {
            public Vector3 start;
            public Vector3 direction;
            public float distance;
            public float travelled;
            public bool landed;
            public Vector3 normal;
            public float age;
        }

        private const int Pool = 48;
        private readonly Tracer[] tracers = new Tracer[Pool];
        private int tracerCount;
        private int burstLeft;
        private float untilNext;
        private float flash = -1;

        public override void Fire()
        {
            burstLeft = burst;
            untilNext = 0;
        }

        public override void ResetEffect()
        {
            tracerCount = 0;
            burstLeft = 0;
            flash = -1;
        }

        public override void Tick(float dt)
        {
            untilNext -= dt;
            while (burstLeft > 0 && untilNext <= 0)
            {
                Shoot();
                burstLeft--;
                untilNext += fireInterval;
            }
            if (burstLeft == 0)
                untilNext = Mathf.Max(untilNext, 0);

            var bandColor = Hdr(band, bandIntensity);
            var coreColor = Hdr(core, coreIntensity);
            for (var i = 0; i < tracerCount; i++)
            {
                ref var t = ref tracers[i];
                t.age += dt;
                var wasShort = t.travelled < t.distance;
                t.travelled += speed * dt;
                if (wasShort && t.travelled >= t.distance)
                    Impact(t.start + t.direction * t.distance, t.normal, t.landed);
                var head = Mathf.Min(t.travelled, t.distance);
                var tail = Mathf.Max(0, t.travelled - tracerLength);
                var trailLeft = trail > 0 ? 1 - t.age / trail : 0;
                if (tail >= t.distance && trailLeft <= 0)
                {
                    tracers[i--] = tracers[--tracerCount];
                    continue;
                }
                if (trailLeft > 0)
                    Fx.Streak(t.start, t.start + t.direction * head, tracerWidth * 0.3f * trailLeft, tracerWidth * 0.3f * trailLeft,
                        Hdr(band, bandIntensity * 0.8f), Hdr(core, coreIntensity * 0.5f), 0.3f, Mathf.Clamp01(trailLeft * 1.5f));
                if (tail < head)
                    Fx.Streak(t.start + t.direction * tail, t.start + t.direction * head, tracerWidth * 0.25f, tracerWidth, bandColor, coreColor, 0.5f);
            }

            if (flash >= 0)
            {
                DrawFlash();
                flash += dt;
                if (flash > 0.05f)
                    flash = -1;
            }
        }

        /// <summary>
        /// A shot whose line the game found itself (ticket 22's weapons): a tracer from the muzzle to <paramref name="end"/>, landing
        /// there on a surface facing <paramref name="normal"/> (dust and sparks) or flying on into the distance, and the muzzle flash.
        /// </summary>
        public void ShootAt(Vector3 end, bool landed, Vector3 normal)
        {
            var start = muzzle.position;
            var along = end - start;
            var distance = along.magnitude;
            if (distance < 1e-3f)
                return;
            var direction = along / distance;
            if (tracerCount < Pool)
                tracers[tracerCount++] = new Tracer { start = start, direction = direction, distance = distance, normal = landed ? normal : -direction, landed = landed };
            flash = 0;
        }

        private void Shoot()
        {
            var direction = Quaternion.AngleAxis(Random.Range(0, 360f), muzzle.forward)
                            * Quaternion.AngleAxis(Random.Range(0, spread), muzzle.up) * muzzle.forward;
            var start = muzzle.position;
            var tracer = new Tracer { start = start, direction = direction, distance = range, normal = -direction };
            if (Physics.Raycast(start, direction, out var hitInfo, range, ~0, QueryTriggerInteraction.Ignore))
            {
                tracer.distance = hitInfo.distance;
                tracer.normal = hitInfo.normal;
                tracer.landed = true;
            }
            if (tracerCount < Pool)
                tracers[tracerCount++] = tracer;
            flash = 0;
        }

        /// <summary>The muzzle flash: a star of short streaks round a glowing disc, a new turn each frame.</summary>
        private void DrawFlash()
        {
            var at = muzzle.position + muzzle.forward * flashSize * 0.6f;
            var bandColor = Hdr(band, bandIntensity * 1.1f);
            var coreColor = Hdr(core, coreIntensity * 1.2f);
            var size = flashSize * Random.Range(0.8f, 1.2f);
            Fx.Glow(at, size * 0.55f, bandColor, coreColor, 0.55f);
            Fx.Streak(at, at + muzzle.forward * size * 2.2f, size * 0.5f, 0, bandColor, coreColor, 0.45f);
            var turn = Random.Range(0, 360f);
            for (var i = 0; i < 4; i++)
            {
                var out1 = Quaternion.AngleAxis(turn + i * 90, muzzle.forward) * (muzzle.up * 0.8f + muzzle.forward * 0.6f);
                Fx.Streak(at, at + out1 * size * 1.1f, size * 0.35f, 0, bandColor, coreColor, 0.4f);
            }
        }

        /// <summary>Dust and sparks where a shot lands; nothing where it flies off into the distance.</summary>
        private void Impact(Vector3 at, Vector3 normal, bool landed)
        {
            if (!landed)
                return;
            for (var i = 0; i < 4; i++)
                Particles.Emit(FxParticles.Kind.Puff, at + normal * 0.03f, Random.Around(normal, 0.7f) * Random.Range(0.4f, 1.3f),
                    Random.Range(0.35f, 0.6f), Random.Range(0.05f, 0.09f), dust, Color.black, gravity: -0.4f, drag: 3);
            for (var i = 0; i < 5; i++)
                Particles.Emit(FxParticles.Kind.Spark, at + normal * 0.02f, Random.Around(normal, 0.9f) * Random.Range(2, 5),
                    Random.Range(0.12f, 0.3f), tracerWidth * 0.8f, Hdr(band, bandIntensity), Hdr(core, coreIntensity), gravity: 9.8f);
        }
    }
}
