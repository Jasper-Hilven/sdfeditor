using UnityEngine;

namespace SdfEffects
{
    /// <summary>
    /// A mushroom that bursts: the intact model is swapped for its pieces (content/models/characters/exploding/, the model cut by
    /// cells that split space, so they fit together exactly), which sit where the model was at the moment of the swap and so look
    /// the same, then fly apart with a spin, bounce on the ground, and shrink away after <see cref="linger"/> seconds. A toon puff
    /// (cream and cap-red balls that swell and shrink), a flash, and a cloud of glowing spores that drifts up go with it; the floating
    /// eyes and hands are pieces of their own and pop off faster. After <see cref="resetAfter"/> seconds the mushroom pops back.
    /// <para>
    /// The pieces are instances of their prefabs under <see cref="piecesRoot"/>, which has the intact model's transform, built by the
    /// scene builder and kept inactive until the burst. The motion is scripted rather than physics: ballistic, with a bounce on the
    /// ground plane through the mushroom's foot, each piece taken as a ball of <see cref="radii"/> round its <see cref="centers"/>.
    /// </para>
    /// </summary>
    public sealed class ExplodingMushroom : FxEffect
    {
        public GameObject intact;
        public Transform piecesRoot;
        public Transform[] pieces;
        [Tooltip("Each piece's middle in the mushroom's own coordinates (its bounds' centre), which it spins round")]
        public Vector3[] centers;
        [Tooltip("Each piece's size for the bounce off the ground: about half its smallest side")]
        public float[] radii;
        [Tooltip("Pieces that pop off rather than burst out (the eyes and hands)")]
        public bool[] loose;
        [Tooltip("The pieces' ink-line renderers, left off while the pieces are still together")]
        public Renderer[] outlines;
        [Tooltip("The intact model's renderers other than its ink lines, hidden while its ink line still goes round the pieces")]
        public Renderer[] intactSurfaces;

        [Header("Burst")]
        [Tooltip("Where the burst comes from, in the mushroom's own coordinates")]
        public Vector3 origin = new Vector3(0, 0.8f, 0);
        public Vector2 speed = new Vector2(2.5f, 4.5f);
        public Vector2 looseSpeed = new Vector2(4.5f, 6.5f);
        [Tooltip("Degrees per second of spin, at most")]
        public float spin = 540;
        public float gravity = 9.8f;
        public float linger = 2.4f;
        public float shrinkTime = 0.5f;
        public float resetAfter = 3.6f;
        [Tooltip("Seconds into the burst the pieces' ink lines take over from the intact model's: together, each piece's own line would " +
                 "draw its cuts along the surface, so until they part the intact model's line goes round them")]
        public float outlinesAfter = 0.05f;

        [Header("Puff and spores")]
        public Color cream = new Color(0.95f, 0.9f, 0.78f);
        public Color capRed = new Color(0.85f, 0.18f, 0.08f);
        public Color spore = new Color(0.85f, 1f, 0.5f);
        public int sporeCount = 48;

        public override string Label => "Explode";

        private Vector3[] position;
        private Vector3[] velocity;
        private Quaternion[] rotation;
        private Vector3[] angular;
        private float time = -1;
        private float popIn = -1;

        public bool Bursting => time >= 0;

        private void Ensure()
        {
            if (position != null && position.Length == pieces.Length)
                return;
            position = new Vector3[pieces.Length];
            velocity = new Vector3[pieces.Length];
            rotation = new Quaternion[pieces.Length];
            angular = new Vector3[pieces.Length];
        }

        public override void Fire()
        {
            if (Bursting)
                return;
            Ensure();
            ShowPieces();
            time = 0;
            for (var i = 0; i < pieces.Length; i++)
            {
                var outward = centers[i] - origin;
                outward.y = Mathf.Max(outward.y, 0) * 0.6f;
                var range = loose[i] ? looseSpeed : speed;
                velocity[i] = (outward.normalized + Vector3.up * (loose[i] ? 0.9f : 0.6f) + Random.InsideUnitSphere * 0.3f).normalized
                              * Random.Range(range.x, range.y);
                angular[i] = Random.OnUnitSphere * Random.Range(spin * 0.4f, spin);
            }
            Burst();
        }

        /// <summary>The pieces in place of the intact model, put together as it was: the burst's first frame, before anything moves.</summary>
        public void ShowPieces()
        {
            Ensure();
            foreach (var surface in intactSurfaces)
                surface.enabled = false;
            piecesRoot.gameObject.SetActive(true);
            for (var i = 0; i < pieces.Length; i++)
            {
                position[i] = centers[i];
                rotation[i] = Quaternion.identity;
                pieces[i].gameObject.SetActive(true);
                Place(i, 1);
            }
            ShowOutlines(false);
        }

        private void ShowOutlines(bool shown)
        {
            foreach (var outline in outlines)
                outline.enabled = shown;
        }

        public override void ResetEffect()
        {
            time = -1;
            popIn = -1;
            piecesRoot.gameObject.SetActive(false);
            intact.SetActive(true);
            foreach (var surface in intactSurfaces)
                surface.enabled = true;
            intact.transform.localScale = Vector3.one;
        }

        public override void Tick(float dt)
        {
            if (popIn >= 0)
            {
                // Back again: a quick pop up to a little over its size and down
                popIn += dt;
                var t = Mathf.Clamp01(popIn / 0.3f);
                intact.transform.localScale = Vector3.one * (t < 0.6f ? Mathf.Lerp(0.2f, 1.08f, t / 0.6f) : Mathf.Lerp(1.08f, 1, (t - 0.6f) / 0.4f));
                if (t >= 1)
                    popIn = -1;
            }
            if (!Bursting)
                return;
            if (time < outlinesAfter && time + dt >= outlinesAfter)
            {
                ShowOutlines(true);
                intact.SetActive(false);
            }
            time += dt;
            if (time >= resetAfter)
            {
                ResetEffect();
                popIn = 0;
                intact.transform.localScale = Vector3.one * 0.2f;
                return;
            }

            var shrink = 1 - Mathf.SmoothStep(0, 1, (time - linger) / shrinkTime);
            for (var i = 0; i < pieces.Length; i++)
            {
                velocity[i] += Vector3.down * (gravity * dt);
                position[i] += velocity[i] * dt;
                rotation[i] = Quaternion.Euler(angular[i] * dt) * rotation[i];
                // A bounce off the ground, losing most of its speed and spin; at rest it slides to a stop
                var radius = radii[i] * shrink;
                if (position[i].y < radius && velocity[i].y < 0)
                {
                    position[i].y = radius;
                    velocity[i] = new Vector3(velocity[i].x * 0.55f, -velocity[i].y * 0.3f, velocity[i].z * 0.55f);
                    angular[i] *= 0.5f;
                }
                if (shrink <= 0.01f)
                    pieces[i].gameObject.SetActive(false);
                else
                    Place(i, shrink);
            }
        }

        /// <summary>Puts a piece where its middle is, turned round its middle, at a scale.</summary>
        private void Place(int i, float scale)
        {
            var piece = pieces[i];
            piece.localRotation = rotation[i];
            piece.localScale = Vector3.one * scale;
            piece.localPosition = position[i] - rotation[i] * (centers[i] * scale);
        }

        /// <summary>The flash, the puff and the spores, all at the moment of the burst.</summary>
        private void Burst()
        {
            var root = piecesRoot;
            var center = root.TransformPoint(origin);
            // A cel-painted "pow": a glowing disc and a ring of rays out of it, gone in a tenth of a second
            var flashBand = Hdr(new Color(1, 0.8f, 0.45f), 1.3f);
            var flashCore = Hdr(new Color(1, 0.97f, 0.85f), 2.6f);
            Particles.Emit(FxParticles.Kind.Spore, center, Vector3.zero, 0.1f, 0.5f, flashBand, flashCore);
            for (var i = 0; i < 16; i++)
            {
                var ray = Random.OnUnitSphere;
                Particles.Emit(FxParticles.Kind.Spark, center + ray * 0.35f, ray * Random.Range(14f, 22f), Random.Range(0.06f, 0.1f), Random.Range(0.08f, 0.13f),
                    flashBand, flashCore);
            }
            for (var i = 0; i < 14; i++)
            {
                var direction = Random.OnUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.8f;
                var at = center + Vector3.Scale(direction, new Vector3(0.35f, 0.5f, 0.35f));
                Particles.Emit(FxParticles.Kind.Puff, at, direction * Random.Range(0.8f, 2.2f), Random.Range(0.5f, 0.85f), Random.Range(0.18f, 0.32f),
                    i % 3 == 0 ? capRed : cream, i % 3 == 0 ? new Color(0.25f, 0.03f, 0) : new Color(0.2f, 0.16f, 0.08f), gravity: -0.6f, drag: 4);
            }
            var sporeCore = Hdr(Color.white, 1.6f);
            var sporeBand = Hdr(spore, 1.4f);
            for (var i = 0; i < sporeCount; i++)
            {
                var at = root.TransformPoint(new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0.3f, 1.5f), Random.Range(-0.6f, 0.6f)));
                var away = (at - center).normalized;
                Particles.Emit(FxParticles.Kind.Spore, at, away * Random.Range(0.6f, 1.8f) + Vector3.up * Random.Range(0.2f, 0.7f),
                    Random.Range(2.2f, 3.6f), Random.Range(0.015f, 0.032f), sporeBand, sporeCore, gravity: -0.25f, drag: 1.4f);
            }
        }
    }
}
