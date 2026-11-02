using SdfHeads;
using SdfLevel;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfEffects
{
    /// <summary>
    /// The debris of the monster explosions (ticket 46): ice shards, bats, shamrocks, an eyeball, tentacles, bandages, fur, fangs, seeds, bolts,
    /// candy canes, confetti. One fixed pool of <see cref="capacity"/> pieces (the oldest slot reused when it is full), each moved by scripted
    /// motion (<see cref="DebrisMotion"/>: ballistic, fluttering, flying, bouncing, deflating, with a ground that is one height per burst
    /// refined by <see cref="Probe"/>; no physics, no colliders, no rigidbodies, which the web cannot spare), and drawn instanced: one
    /// <see cref="Graphics.RenderMeshInstanced"/> per model part and, with its ink line, one more, per frame, whatever the number of pieces.
    /// Nothing is allocated after <see cref="Prepare"/>, neither per piece nor per death.
    /// <para>
    /// In a match it runs itself (<see cref="autoRun"/>: Update moves, LateUpdate draws); on the bench the bench's clock calls <see cref="Step"/>
    /// and <see cref="Draw"/>, so the editor's snapshots can step it without Play mode.
    /// </para>
    /// </summary>
    public sealed class MonsterDebris : MonoBehaviour
    {
        /// <summary>The ground under a point (a downward ray from <paramref name="from"/>): its height, or false when there is none.</summary>
        public delegate bool GroundProbe(Vector3 from, out float y);

        public int capacity = 640;
        public bool autoRun = true;
        [Tooltip("Seconds a piece shrinks away over, at the end of its life")]
        public float shrinkTime = 0.45f;

        private struct Piece
        {
            public bool alive;
            public int kind;
            public DebrisMotion motion;
            public Vector3 position, velocity;
            public Quaternion rotation;
            public Vector3 spin;
            public float scale;
            public Vector3 stretch;
            public float age, life, groundY, radius, seed, probeIn, landedAt, turn;
            public bool grounded;
        }

        private sealed class Model
        {
            public ScatterPart[] parts;
            public float radius;
            public Matrix4x4[] matrices;
            public int count;
        }

        private Piece[] pieces;
        private Model[] models;
        private Matrix4x4[] batch;
        private Material surface, outline;
        private int next;
        private bool prepared;
        private Vector3 lowest, highest;

        /// <summary>The ground a probe finds a piece's way down; none: each burst's own ground height only.</summary>
        public GroundProbe Probe;

        /// <summary>Pieces alive now.</summary>
        public int Live { get; private set; }

        /// <summary>The most alive at once since <see cref="Clear"/>.</summary>
        public int Peak { get; private set; }

        /// <summary>Pieces spawned since <see cref="Clear"/>.</summary>
        public int Spawned { get; private set; }

        /// <summary>Instanced draw calls last frame (surface and ink line).</summary>
        public int Draws { get; private set; }

        /// <summary>Whether the debris models were found (the registry and the prefabs); without them nothing is thrown.</summary>
        public bool Ready => prepared && models != null;

        /// <summary>Makes the pool, the models' parts and the instancing materials; does nothing the second time.</summary>
        public void Prepare()
        {
            if (prepared)
                return;
            prepared = true;
            var size = Mathf.CeilToInt(capacity * SdfMushrooms.QualityExtras.GoreAmount);
            pieces = new Piece[size];
            batch = new Matrix4x4[size];
            var registry = MonsterDebrisModels.Load();
            if (registry == null)
                return;
            models = new Model[DebrisKinds.Count];
            Material surfaceSource = null, outlineSource = null;
            for (var k = 0; k < models.Length; k++)
            {
                var prefab = registry.Prefab((DebrisKind)k);
                var levels = prefab != null ? InstancedScatterRenderer.LevelsOf(prefab) : null;
                if (levels == null || levels.Length == 0 || levels[0].parts.Length == 0)
                    continue;
                var model = new Model { parts = levels[0].parts, matrices = new Matrix4x4[size] };
                var extent = 0f;
                foreach (var part in model.parts)
                    extent = Mathf.Max(extent, Vector3.Scale(part.mesh.bounds.extents, part.transform.lossyScale).magnitude);
                model.radius = Mathf.Clamp(extent * 0.45f, 0.012f, 0.1f);
                models[k] = model;
                foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var material = renderer.sharedMaterial;
                    if (material == null || material.shader == null)
                        continue;
                    if (material.shader.name.EndsWith("Outline", System.StringComparison.Ordinal))
                        outlineSource ??= material;
                    else
                        surfaceSource ??= material;
                }
            }
            if (surfaceSource == null)
            {
                models = null;
                return;
            }
            surface = new Material(surfaceSource) { name = surfaceSource.name + " (monster debris)", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            if (outlineSource != null)
                outline = new Material(outlineSource) { name = outlineSource.name + " (monster debris)", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
        }

        private void OnDestroy()
        {
            if (surface != null)
                Destroy(surface);
            if (outline != null)
                Destroy(outline);
        }

        /// <summary>Every piece gone.</summary>
        public void Clear()
        {
            Prepare();
            for (var i = 0; i < pieces.Length; i++)
                pieces[i].alive = false;
            Live = Peak = Spawned = 0;
            next = 0;
        }

        /// <summary>
        /// Throws a piece: <paramref name="kind"/> at <paramref name="position"/> with <paramref name="velocity"/> and a random spin of up to
        /// <paramref name="spinDegrees"/> a second, at <paramref name="scale"/>, shrinking away at <paramref name="life"/> seconds; the ground
        /// is at <paramref name="groundY"/> unless <see cref="Probe"/> finds another. False when there is no such model.
        /// </summary>
        public bool Spawn(DebrisKind kind, DebrisMotion motion, Vector3 position, Vector3 velocity, float spinDegrees, float scale, float life, float groundY)
        {
            Prepare();
            var model = models != null ? models[(int)kind] : null;
            if (model == null)
                return false;
            var index = next;
            next = (next + 1) % pieces.Length;
            ref var p = ref pieces[index];
            if (!p.alive)
                Live++;
            p = new Piece
            {
                alive = true, kind = (int)kind, motion = motion, position = position, velocity = velocity, scale = scale, stretch = Vector3.one, life = life,
                groundY = groundY, radius = model.radius * scale, seed = Random.value * 100, probeIn = Random.value * 0.05f, landedAt = -1,
                turn = (Random.value < 0.5f ? -1 : 1) * Random.Range(1f, 2.4f),
                rotation = motion == DebrisMotion.Fly ? Quaternion.LookRotation(velocity.sqrMagnitude > 1e-4f ? velocity : Vector3.forward) : Random.rotationUniform,
                spin = Random.onUnitSphere * (spinDegrees * Random.Range(0.4f, 1f)),
            };
            if (motion == DebrisMotion.Bounce)
                p.spin = Vector3.zero;
            Peak = Mathf.Max(Peak, Live);
            Spawned++;
            return true;
        }

        private void Update()
        {
            if (autoRun && prepared)
                Step(Mathf.Min(Time.deltaTime, 0.05f));
        }

        private void LateUpdate()
        {
            if (autoRun && prepared)
                Draw();
        }

        /// <summary>Moves every piece on by <paramref name="dt"/>.</summary>
        public void Step(float dt)
        {
            if (!prepared || models == null)
                return;
            lowest = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            highest = -lowest;
            var live = 0;
            for (var i = 0; i < pieces.Length; i++)
            {
                ref var p = ref pieces[i];
                if (!p.alive)
                    continue;
                p.age += dt;
                if (p.age >= p.life || p.position.y < p.groundY - 40)
                {
                    p.alive = false;
                    continue;
                }
                live++;
                Move(ref p, dt);
                p.rotation = Quaternion.Normalize(p.rotation);
                lowest = Vector3.Min(lowest, p.position);
                highest = Vector3.Max(highest, p.position);
            }
            Live = live;
        }

        private void Move(ref Piece p, float dt)
        {
            switch (p.motion)
            {
                case DebrisMotion.Fly:
                    Fly(ref p, dt);
                    return;
                case DebrisMotion.Flutter:
                    // Light and flat: it hangs in the air, swaying
                    p.velocity += Vector3.down * (2.6f * dt);
                    p.velocity += new Vector3(Mathf.Sin(p.age * 6.5f + p.seed), 0, Mathf.Cos(p.age * 5.2f + p.seed)) * (2.4f * dt);
                    p.velocity *= 1 / (1 + 2.2f * dt);
                    Land(ref p, dt, 0.08f, 6);
                    break;
                case DebrisMotion.Bounce:
                    p.velocity += Vector3.down * (9.8f * dt);
                    RollBounce(ref p, dt);
                    return;
                case DebrisMotion.Deflate:
                    p.velocity += Vector3.down * (9.8f * dt);
                    Land(ref p, dt, 0.15f, 5);
                    if (p.landedAt < 0)
                        p.rotation = Quaternion.Euler(p.spin * dt) * p.rotation;
                    Deflate(ref p, dt);
                    return;
                default:
                    p.velocity += Vector3.down * (9.8f * dt);
                    p.velocity *= 1 / (1 + 0.15f * dt);
                    Land(ref p, dt, 0.3f, 4);
                    break;
            }
            p.rotation = Quaternion.Euler(p.spin * dt) * p.rotation;
        }

        /// <summary>The ground under a piece: its burst's height, refined by the probe while it is falling close to it.</summary>
        private void Ground(ref Piece p, float dt)
        {
            if (Probe == null || p.velocity.y > 0 || p.position.y > p.groundY + 1.2f)
                return;
            p.probeIn -= dt;
            if (p.probeIn > 0)
                return;
            p.probeIn = 0.08f;
            if (Probe(p.position + Vector3.up * 0.4f, out var y) && Mathf.Abs(y - p.groundY) < 6)
                p.groundY = y;
        }

        /// <summary>Moves a piece; where it meets the ground it bounces (losing speed and spin) or, slow enough, lies still.</summary>
        private void Land(ref Piece p, float dt, float bounce, float friction)
        {
            Ground(ref p, dt);
            if (p.grounded)
            {
                p.velocity.x *= Mathf.Max(0, 1 - friction * dt);
                p.velocity.z *= Mathf.Max(0, 1 - friction * dt);
                p.spin *= Mathf.Max(0, 1 - 4 * dt);
                if (p.velocity.y > 0.5f)
                    p.grounded = false;
            }
            p.position += p.velocity * dt;
            if (p.position.y < p.groundY + p.radius && p.velocity.y < 0)
            {
                p.position.y = p.groundY + p.radius;
                if (-p.velocity.y > 1.2f)
                {
                    p.velocity = new Vector3(p.velocity.x * 0.7f, -p.velocity.y * bounce, p.velocity.z * 0.7f);
                    p.spin *= 0.55f;
                    p.grounded = false;
                }
                else
                {
                    p.velocity.y = 0;
                    p.grounded = true;
                    if (p.landedAt < 0)
                        p.landedAt = p.age;
                }
            }
        }

        /// <summary>A bat: out and up along a curving path, bobbing, flapping, never falling.</summary>
        private static void Fly(ref Piece p, float dt)
        {
            var turn = Quaternion.AngleAxis(p.turn * 57.3f * dt, Vector3.up);
            p.velocity = turn * p.velocity;
            p.velocity.y += Mathf.Sin(p.age * 4.5f + p.seed) * 2.2f * dt;
            p.velocity *= 1 / (1 + 0.5f * dt);
            // Slowly climbing to a flight height, never below the burst
            p.velocity.y += (p.position.y < p.groundY + 2.2f ? 0.9f : -0.2f) * dt;
            p.position += p.velocity * dt;
            if (p.velocity.sqrMagnitude > 0.04f)
                p.rotation = Quaternion.Slerp(p.rotation, Quaternion.LookRotation(p.velocity) * Quaternion.Euler(0, 0, Mathf.Sin(p.age * 3 + p.seed) * 18), 8 * dt);
            // Wings beat: the bat's span (x) closes and opens
            var flap = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(p.age * 24 + p.seed));
            p.stretch = new Vector3(flap, 1 + (1 - flap) * 0.3f, 1);
        }

        /// <summary>The eyeball: heavy, bounces high, rolls on the way, and settles with its gaze wandering.</summary>
        private void RollBounce(ref Piece p, float dt)
        {
            Ground(ref p, dt);
            p.position += p.velocity * dt;
            if (p.position.y < p.groundY + p.radius && p.velocity.y < 0)
            {
                p.position.y = p.groundY + p.radius;
                if (-p.velocity.y > 0.9f)
                {
                    p.velocity = new Vector3(p.velocity.x * 0.8f, -p.velocity.y * 0.62f, p.velocity.z * 0.8f);
                    // A knock from the bounce turns it
                    p.spin = Random.onUnitSphere * Random.Range(120f, 300f);
                }
                else
                {
                    p.velocity.y = 0;
                    p.grounded = true;
                }
            }
            var flat = new Vector3(p.velocity.x, 0, p.velocity.z);
            if (p.grounded)
            {
                var speed = flat.magnitude;
                // Rolling without slipping: about the horizontal axis across its way, at speed over radius
                if (speed > 0.05f)
                {
                    var axis = Vector3.Cross(Vector3.up, flat / speed);
                    p.rotation = Quaternion.AngleAxis(speed / p.radius * 57.3f * dt, axis) * p.rotation;
                }
                p.velocity.x *= Mathf.Max(0, 1 - 1.4f * dt);
                p.velocity.z *= Mathf.Max(0, 1 - 1.4f * dt);
            }
            else
            {
                p.rotation = Quaternion.Euler(p.spin * dt) * p.rotation;
            }
        }

        /// <summary>A tentacle: after it lands it wriggles and its length drains away, thinning too, until it is gone.</summary>
        private static void Deflate(ref Piece p, float dt)
        {
            if (p.landedAt < 0)
            {
                p.stretch = Vector3.one;
                return;
            }
            var t = Mathf.Clamp01((p.age - p.landedAt) / Mathf.Max(0.5f, p.life - p.landedAt - 0.4f));
            var wriggle = 1 + 0.12f * Mathf.Sin(p.age * 17 + p.seed) * (1 - t);
            var along = Mathf.Lerp(1, 0.12f, t * t) * wriggle;
            var across = Mathf.Lerp(1, 0.45f, t) / Mathf.Sqrt(Mathf.Max(0.2f, wriggle));
            p.stretch = new Vector3(across, across, along);
            // It writhes in place
            p.rotation = Quaternion.AngleAxis(Mathf.Sin(p.age * 11 + p.seed) * 60 * (1 - t) * dt * 4, Vector3.up) * p.rotation;
        }

        /// <summary>Draws every live piece: one instanced call per model part, and one more for its ink line.</summary>
        public void Draw()
        {
            Draws = 0;
            if (!prepared || models == null || Live == 0)
                return;
            for (var k = 0; k < models.Length; k++)
                if (models[k] != null)
                    models[k].count = 0;
            for (var i = 0; i < pieces.Length; i++)
            {
                ref var p = ref pieces[i];
                if (!p.alive)
                    continue;
                var model = models[p.kind];
                var shrink = 1 - Mathf.SmoothStep(0, 1, (p.age - (p.life - shrinkTime)) / shrinkTime);
                var rotation = Quaternion.Normalize(p.rotation);
                if (p.motion == DebrisMotion.Bounce && p.grounded && Vector3.SqrMagnitude(new Vector3(p.velocity.x, 0, p.velocity.z)) < 0.01f)
                    rotation *= Quaternion.Euler(0, Mathf.Sin(p.age * 1.7f + p.seed) * 25, 0);
                model.matrices[model.count++] = Matrix4x4.TRS(p.position, rotation, p.stretch * (p.scale * shrink));
            }
            var bounds = new Bounds((lowest + highest) * 0.5f, highest - lowest + Vector3.one * 1.5f);
            var parameters = new RenderParams(surface)
            {
                layer = gameObject.layer,
                worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };
            for (var k = 0; k < models.Length; k++)
            {
                var model = models[k];
                if (model == null || model.count == 0)
                    continue;
                foreach (var part in model.parts)
                {
                    for (var i = 0; i < model.count; i++)
                        batch[i] = model.matrices[i] * part.transform;
                    parameters.material = surface;
                    parameters.shadowCastingMode = ShadowCastingMode.Off;
                    Graphics.RenderMeshInstanced(parameters, part.mesh, 0, batch, model.count);
                    Draws++;
                    if (outline == null || !part.outline)
                        continue;
                    parameters.material = outline;
                    Graphics.RenderMeshInstanced(parameters, part.mesh, 0, batch, model.count);
                    Draws++;
                }
            }
        }
    }
}
