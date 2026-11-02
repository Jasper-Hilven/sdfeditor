using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// What is left of a killed player's head (the gore, <see cref="Gore"/>, ticket 51): the monster head it wore, burst where it was into
    /// the pieces cut from its own mesh (SDF intersections of the head with Voronoi cells, content/models/characters/head-pieces, listed
    /// in <see cref="SdfHeads.MonsterHeadModels"/>: any head that has its pieces bursts, no code per head). How hard and which way each
    /// flies is <see cref="GoreRules.Launch"/>: away from the hit, up, higher for a headshot. The pieces are boxes with friction that
    /// tumble and lie; at ultra gore (<see cref="GoreRules.Flings"/>) they fly far and trail drops and gobs of the head's blood, with
    /// splats close together along the ground. The flamethrower's pieces are charred dark. The body is never part of it (it ragdolls:
    /// <see cref="SoldierRagdoll"/>) and nothing bleeds at the neck.
    /// <para>
    /// The pieces are real rigidbodies on the Ignore Raycast layer (no shot, sight line or blood raycast meets them) and let the
    /// players' capsules through. They settle and freeze after <see cref="settleAfter"/> seconds, and sink and shrink away after
    /// <see cref="lifetime"/> or when the player respawns (<see cref="Sink"/>), whichever is first.
    /// </para>
    /// </summary>
    public sealed class Corpse : MonoBehaviour
    {
        private const string OutlineName = "Outline";
        public const int IgnoreRaycastLayer = 2;

        [Tooltip("Seconds the pieces move freely before they slow and freeze")]
        public float settleAfter = 4;
        public float settleTime = 0.8f;
        [Tooltip("Seconds before the corpse sinks away of itself (it sinks sooner when the player respawns)")]
        public float lifetime = 10;
        public float sinkTime = 0.7f;
        [Tooltip("Metres a flying piece goes between the small splats it leaves")]
        public float trailSpacing = 0.45f;
        [Tooltip("Metres between the trail's splats at ultra gore")]
        public float flingTrailSpacing = 0.2f;
        [Tooltip("Seconds between the gobs a flying piece sheds at ultra gore")]
        public float flingGobEvery = 0.07f;

        private sealed class Gib
        {
            public Transform transform;
            public Rigidbody body;
            public GibKind kind;
            public Collider[] colliders;
            public Vector3 scale;
            public float radius;
            public float damping;
            public bool landed;
            public Vector3 lastSplat;
            public int splats;
            public float dripIn;
            public float gobIn;
        }

        private static PhysicsMaterial resting;

        private readonly List<Gib> gibs = new List<Gib>();
        private Gore gore;
        private Color blood;
        private float age;
        private float sinking = -1;
        private bool settled;
        private int trailSplats;
        private bool fling;

        /// <summary>Whose corpse it is.</summary>
        public PlayerState Victim { get; private set; }

        public bool Sinking => sinking >= 0;

        /// <summary>
        /// The burst of the head <paramref name="head"/> worn by <paramref name="victim"/>, killed by <paramref name="death"/>: its
        /// <paramref name="pieces"/> (prefabs, in the head's own frame, origin at the soldier's head socket) where the head was
        /// (<paramref name="at"/>, in the head's pose), burst from <paramref name="middle"/> (world) away from <paramref name="away"/> (the
        /// hit's push, flat; zero when unknown), in the head's <paramref name="blood"/>; with <paramref name="fling"/> (ultra gore) every
        /// piece flies far, trailing blood. Null without pieces.
        /// </summary>
        public static Corpse Make(Gore gore, PlayerState victim, Death death, GameObject[] pieces, Transform at, Vector3 middle, Color blood, Vector3 away,
            bool fling = false)
        {
            if (pieces == null || pieces.Length == 0 || at == null)
                return null;
            var root = new GameObject($"Head of {victim?.Name}");
            root.transform.SetPositionAndRotation(at.position, at.rotation);
            root.transform.localScale = at.lossyScale;
            var corpse = root.AddComponent<Corpse>();
            corpse.gore = gore;
            corpse.blood = blood;
            corpse.Victim = victim;
            corpse.fling = fling;
            var strength = GoreRules.Strength(death.Weapon);
            foreach (var prefab in pieces)
            {
                if (prefab == null)
                    continue;
                var piece = Instantiate(prefab, root.transform, false);
                piece.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                Strip(piece);
                corpse.Add(piece.transform, GibKind.Piece, middle, away, death.Headshot, strength);
            }
            if (death.Weapon == WeaponIds.Flamethrower)
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.name != OutlineName)
                        filter.sharedMesh = Gore.Charred(filter.sharedMesh);
            corpse.trailSplats = (death.Weapon == WeaponIds.Flamethrower ? 2 : 6) * (fling ? 6 : 1);
            SetLayer(root.transform, IgnoreRaycastLayer);
            return corpse;
        }

        /// <summary>A piece gets its collider, its physics and its push.</summary>
        private void Add(Transform part, GibKind kind, Vector3 middle, Vector3 away, bool headshot, float strength)
        {
            var bounds = LocalBounds(part, part);
            var radius = Mathf.Max(0.03f, Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)) * 0.85f);
            AddBox(part);
            var colliders = part.GetComponentsInChildren<Collider>();
            foreach (var collider in colliders)
                collider.sharedMaterial = Resting;

            var body = part.gameObject.AddComponent<Rigidbody>();
            body.mass = 0.4f;
            body.linearDamping = 0.15f;
            body.angularDamping = 0.6f;
            body.maxAngularVelocity = 40;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var outward = part.TransformPoint(bounds.center) - middle;
            body.linearVelocity = GoreRules.Launch(kind, outward, away, headshot, strength, Random.insideUnitSphere, Random.value, fling);
            body.angularVelocity = Random.onUnitSphere * (GoreRules.Spin(kind) * Random.Range(0.4f, 1f) * Mathf.Deg2Rad);

            gibs.Add(new Gib
            {
                transform = part,
                body = body,
                kind = kind,
                colliders = colliders,
                scale = part.localScale,
                radius = radius,
                damping = body.linearDamping,
                dripIn = Random.Range(0, 0.05f),
                gobIn = Random.Range(0, flingGobEvery),
            });
        }

        /// <summary>A box round a piece's mesh, in its own coordinates.</summary>
        private static void AddBox(Transform part)
        {
            var bounds = LocalBounds(part, part);
            var box = part.gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.05f);
        }

        /// <summary>Keeps the pieces from pushing (or being pushed by) a player's capsule: they lie where they fell, and nobody trips on them.</summary>
        public void IgnorePlayer(Player player)
        {
            // Only between enabled colliders: a dead player's capsule is off, and is told again when it respawns
            if (player == null || player.Capsule == null || !player.Capsule.enabled)
                return;
            foreach (var gib in gibs)
                foreach (var collider in gib.colliders)
                    if (collider != null && collider.enabled)
                        Physics.IgnoreCollision(collider, player.Capsule);
        }

        /// <summary>The corpse sinks and shrinks away, then is gone.</summary>
        public void Sink()
        {
            if (sinking < 0)
                sinking = 0;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            age += dt;
            if (sinking < 0 && age >= lifetime)
                Sink();
            if (sinking >= 0)
            {
                SinkAway(dt);
                return;
            }
            if (!settled)
                Settle();
            if (age < settleAfter)
                foreach (var gib in gibs)
                    Trail(gib, dt);
        }

        /// <summary>After a few seconds the pieces slow, then freeze: nothing keeps rolling about for ever.</summary>
        private void Settle()
        {
            if (age < settleAfter)
                return;
            var t = (age - settleAfter) / settleTime;
            foreach (var gib in gibs)
            {
                if (gib.body == null)
                    continue;
                if (t >= 1)
                {
                    gib.body.isKinematic = true;
                    continue;
                }
                gib.body.linearDamping = Mathf.Lerp(gib.damping, 10, t);
                gib.body.angularDamping = Mathf.Lerp(gib.body.angularDamping, 10, t);
            }
            settled = t >= 1;
        }

        /// <summary>
        /// A flying piece drips, and leaves a splat where it lands and every so often along its way. At ultra gore it does so more
        /// thickly, and sheds gobs as it flies that splat on the ground under its path.
        /// </summary>
        private void Trail(Gib gib, float dt)
        {
            var position = gib.body.worldCenterOfMass;
            var velocity = gib.body.linearVelocity;
            var speed = velocity.magnitude;
            if (speed > 1.2f)
            {
                gib.dripIn -= dt;
                if (gib.dripIn <= 0)
                {
                    gore.Drip(position, velocity, blood, fling ? 3 : 1);
                    gib.dripIn = 0.06f;
                }
                if (fling && speed > 2)
                {
                    gib.gobIn -= dt;
                    if (gib.gobIn <= 0)
                    {
                        gore.Throw(position, velocity * 0.25f + Random.insideUnitSphere * 0.5f, Random.Range(0.035f, 0.06f), blood);
                        gib.gobIn = flingGobEvery;
                    }
                }
            }
            var spacing = fling ? flingTrailSpacing : trailSpacing;
            if (gib.splats >= trailSplats || !Gore.Ground(position + Vector3.up * 0.05f, out var point, out var normal, gib.radius + 0.2f))
                return;
            if (!gib.landed)
            {
                gib.landed = true;
                gore.Splat(point, normal, Random.Range(0.15f, 0.22f), blood);
            }
            else if (speed > 0.4f && (point - gib.lastSplat).sqrMagnitude > spacing * spacing)
            {
                gore.Splat(point, normal, Random.Range(0.06f, 0.11f), blood);
            }
            else
            {
                return;
            }
            gib.lastSplat = point;
            gib.splats++;
        }

        private void SinkAway(float dt)
        {
            sinking += dt;
            var shrink = 1 - Mathf.SmoothStep(0, 1, sinking / sinkTime);
            foreach (var gib in gibs)
            {
                if (gib.transform == null)
                    continue;
                if (gib.body != null)
                    gib.body.isKinematic = true;
                gib.transform.localScale = gib.scale * Mathf.Max(shrink, 0.001f);
                gib.transform.position += Vector3.down * (0.3f * dt);
            }
            if (sinking >= sinkTime)
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (gore != null)
                gore.Gone(this);
        }

        // ---------------------------------------------------------------- Helpers

        /// <summary>Heavy pieces that stay near where they fall.</summary>
        private static PhysicsMaterial Resting => resting != null ? resting : resting = new PhysicsMaterial("Gore Resting")
        {
            dynamicFriction = 0.8f, staticFriction = 0.9f, bounciness = 0.1f,
            frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Average,
        };

        /// <summary>A copy of a model takes no collider of its own (the corpse adds its own).</summary>
        private static void Strip(GameObject model)
        {
            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(collider);
        }

        private static Transform Find(Transform root, string part)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == part)
                    return t;
            return null;
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root)
                SetLayer(child, layer);
        }

        /// <summary>The bounds of a model's finest level (not its ink line) in another transform's coordinates, from its meshes.</summary>
        public static Bounds LocalBounds(Transform model, Transform space)
        {
            Bounds? total = null;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.name == OutlineName || filter.name.Contains("_LOD"))
                    continue;
                var mesh = filter.sharedMesh.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var local = mesh.center + Vector3.Scale(mesh.extents, new Vector3((corner & 1) * 2 - 1, (corner & 2) - 1, (corner & 4) / 2f - 1));
                    var point = space.InverseTransformPoint(filter.transform.TransformPoint(local));
                    if (total is { } b)
                    {
                        b.Encapsulate(point);
                        total = b;
                    }
                    else
                    {
                        total = new Bounds(point, Vector3.zero);
                    }
                }
            }
            return total ?? new Bounds(Vector3.zero, Vector3.one * 0.1f);
        }
    }
}
