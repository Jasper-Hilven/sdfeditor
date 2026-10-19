using UnityEngine;

namespace SdfEffects
{
    /// <summary>
    /// The blob gun's goop: gobs of magenta goo shot in a ballistic arc that stretch along their flight with speed and wobble, squash
    /// flat where they hit the ground or a target, then splat: a puddle of goo stuck to the surface, along it, that stays a few seconds
    /// and shrinks away into it, with a few drops thrown off. The gobs and puddles are SDF-baked models (content/models/effects/,
    /// goop-blob0..2 and goop-splat0..1) drawn with the toon shader and its ink line like everything else, so the goo glows from inside
    /// as the gun's own reservoir does; the stretch and squash are only their transform's scale, no shader of their own.
    /// <para>
    /// The gobs and puddles are fixed pools of objects the scene builder made (inactive until used, the oldest puddle reused when all
    /// are out), so a shot allocates nothing. Hits are found with a raycast along each step of the flight.
    /// </para>
    /// </summary>
    public sealed class GoopGun : FxEffect
    {
        [Tooltip("The mouth of the gun's muzzle; it shoots along its forward axis")]
        public Transform muzzle;
        [Tooltip("The pool of gobs (goop-blob instances, which fly along +z)")]
        public Transform[] blobs;
        [Tooltip("The pool of puddles (goop-splat instances, lying on y = 0)")]
        public Transform[] splats;

        [Header("Shot")]
        public float speed = 7.5f;
        [Tooltip("Degrees above the barrel's axis the gob leaves at")]
        public float lift = 12;
        [Tooltip("Largest angle off the aim, in degrees")]
        public float spread = 3;
        public int burst = 3;
        public float fireInterval = 0.22f;
        public float gravity = 9.8f;
        public float blobScale = 0.9f;

        [Header("Splat")]
        public float squashTime = 0.07f;
        [Tooltip("Seconds a puddle stays before it shrinks away")]
        public float stick = 3;
        public float shrinkTime = 1.2f;
        public float splatScale = 1;
        public Color goo = new Color(0.85f, 0.18f, 0.65f);
        public Color glow = new Color(0.4f, 0.06f, 0.3f);

        public override string Label => "Goop";

        private struct Blob
        {
            public bool flying;
            public float squash;
            public Vector3 position;
            public Vector3 velocity;
            public Vector3 normal;
            public Quaternion landed;
            public float age;
        }

        private struct Splat
        {
            public float age;
            public float scale;
            public Vector3 position;
            public Vector3 normal;
            public Quaternion rotation;
        }

        private Blob[] blobState;
        private Splat[] splatState;
        private int nextBlob;
        private int nextSplat;
        private int burstLeft;
        private float untilNext;
        private float muzzlePuff = -1;

        private void Ensure()
        {
            if (blobState != null && blobState.Length == blobs.Length && splatState.Length == splats.Length)
                return;
            blobState = new Blob[blobs.Length];
            splatState = new Splat[splats.Length];
        }

        public override void Fire()
        {
            burstLeft = burst;
            untilNext = 0;
        }

        public override void ResetEffect()
        {
            Ensure();
            burstLeft = 0;
            muzzlePuff = -1;
            for (var i = 0; i < blobs.Length; i++)
            {
                blobState[i] = default;
                blobs[i].gameObject.SetActive(false);
            }
            for (var i = 0; i < splats.Length; i++)
            {
                splatState[i] = default;
                splats[i].gameObject.SetActive(false);
            }
        }

        public override void Tick(float dt)
        {
            Ensure();
            untilNext -= dt;
            while (burstLeft > 0 && untilNext <= 0)
            {
                Shoot();
                burstLeft--;
                untilNext += fireInterval;
            }
            if (burstLeft == 0)
                untilNext = Mathf.Max(untilNext, 0);

            if (muzzlePuff >= 0)
            {
                var t = muzzlePuff / 0.08f;
                Fx.Lit(muzzle.position + muzzle.forward * 0.03f, 0.06f * (1 - t * t), goo, glow);
                muzzlePuff += dt;
                if (muzzlePuff > 0.08f)
                    muzzlePuff = -1;
            }

            for (var i = 0; i < blobs.Length; i++)
                TickBlob(i, dt);
            for (var i = 0; i < splats.Length; i++)
                TickSplat(i, dt);
        }

        private void Shoot()
        {
            var i = nextBlob;
            nextBlob = (nextBlob + 1) % blobs.Length;
            var aim = Quaternion.AngleAxis(-lift, muzzle.right) * muzzle.forward;
            aim = Quaternion.AngleAxis(Random.Range(0, 360f), muzzle.forward) * Quaternion.AngleAxis(Random.Range(0, spread), muzzle.up) * aim;
            blobState[i] = new Blob { flying = true, position = muzzle.position, velocity = aim * speed * Random.Range(0.92f, 1.08f) };
            blobs[i].gameObject.SetActive(true);
            muzzlePuff = 0;
        }

        private void TickBlob(int i, float dt)
        {
            ref var b = ref blobState[i];
            var blob = blobs[i];
            if (!blob.gameObject.activeSelf)
                return;
            b.age += dt;

            if (b.flying)
            {
                var velocity = b.velocity + Vector3.down * (gravity * dt);
                var step = (b.velocity + velocity) * (0.5f * dt);
                if (Physics.Raycast(b.position, step, out var hit, step.magnitude + 0.04f, ~0, QueryTriggerInteraction.Ignore))
                {
                    // Landed: squash flat against the surface, along it, turned the way it flew
                    b.flying = false;
                    b.squash = 0;
                    b.position = hit.point;
                    b.normal = hit.normal;
                    var along = Vector3.ProjectOnPlane(velocity, hit.normal);
                    b.landed = Quaternion.LookRotation(along.sqrMagnitude > 1e-6f ? along : Vector3.Cross(hit.normal, Vector3.right), hit.normal);
                    b.velocity = velocity;
                }
                else
                {
                    b.position += step;
                    b.velocity = velocity;
                    // Stretched along its flight with speed, thinner across to keep its volume, and a wobble on top
                    var stretch = 1 + Mathf.Clamp(b.velocity.magnitude * 0.045f, 0, 0.5f);
                    var wobble = Mathf.Sin(b.age * 38 + i) * 0.08f;
                    var across = 1 / Mathf.Sqrt(stretch);
                    blob.SetPositionAndRotation(b.position, Quaternion.LookRotation(b.velocity));
                    blob.localScale = new Vector3(across * (1 + wobble), across * (1 - wobble), stretch) * blobScale;
                    if (b.age > 4 || b.position.y < -5)
                        blob.gameObject.SetActive(false);
                    return;
                }
            }

            // Squashing: flattening onto the surface, spreading out along it; then the puddle takes its place
            b.squash += dt / squashTime;
            var s = Mathf.Clamp01(b.squash);
            blob.SetPositionAndRotation(b.position + b.normal * (0.045f * blobScale * (1 - s)), b.landed);
            blob.localScale = new Vector3(1 + 1.1f * s, 1 - 0.7f * s, 1 + 1.4f * s) * blobScale;
            if (b.squash >= 1)
            {
                blob.gameObject.SetActive(false);
                PlaceSplat(b.position, b.normal, b.landed, b.velocity);
            }
        }

        private void PlaceSplat(Vector3 at, Vector3 normal, Quaternion landed, Vector3 velocity)
        {
            var i = nextSplat;
            nextSplat = (nextSplat + 1) % splats.Length;
            splatState[i] = new Splat
            {
                position = at + normal * 0.004f,
                normal = normal,
                rotation = Quaternion.AngleAxis(Random.Range(-40f, 40f), normal) * landed,
                scale = splatScale * Random.Range(0.85f, 1.15f),
            };
            splats[i].gameObject.SetActive(true);
            TickSplat(i, 0);

            // Drops thrown off along the way it was going and up off the surface
            var along = Vector3.ProjectOnPlane(velocity, normal).normalized;
            for (var d = 0; d < 6; d++)
                Particles.Emit(FxParticles.Kind.Drop, at + normal * 0.03f, (Random.Around(normal + along * 0.8f, 0.8f) * Random.Range(1.2f, 2.6f)),
                    Random.Range(0.35f, 0.6f), Random.Range(0.012f, 0.022f), goo, glow, gravity: 9.8f);
        }

        private void TickSplat(int i, float dt)
        {
            ref var s = ref splatState[i];
            var splat = splats[i];
            if (!splat.gameObject.activeSelf)
                return;
            s.age += dt;
            // Pops out to a little past its size, settles, stays, then shrinks back into the surface
            var pop = s.age < 0.12f ? Mathf.Lerp(0.55f, 1.12f, s.age / 0.12f) : Mathf.Lerp(1.12f, 1, Mathf.Clamp01((s.age - 0.12f) / 0.1f));
            var shrink = 1 - Mathf.SmoothStep(0, 1, (s.age - stick) / shrinkTime);
            if (shrink <= 0.02f)
            {
                splat.gameObject.SetActive(false);
                return;
            }
            var size = s.scale * pop * shrink;
            splat.SetPositionAndRotation(s.position - s.normal * (0.01f * (1 - shrink)), s.rotation);
            splat.localScale = new Vector3(size, s.scale * pop * Mathf.Sqrt(shrink), size);
        }
    }
}
