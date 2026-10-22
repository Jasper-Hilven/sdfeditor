using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// What is left of a killed scout (the gore, <see cref="Gore"/>): a fresh copy of the team's scout where the body stood, in its pose
    /// (the eyes and hands where the live scout held them), burst at once. The floating eyes and hands pop off hardest and roll a good
    /// way (balls, little friction, little angular drag), dripping blood and leaving small splats where they land and along their roll.
    /// The body breaks into its pieces where the scout has them (each team's own, content/models/characters/exploding, picked by the
    /// scout prefab's name: amanita-scout-* for red, chanterelle-scout-* for blue; the cap breaks off in one, or shatters for a
    /// cannon; the stem pieces slump and topple), or topples whole (a scout without pieces); these are boxes with more friction. How hard and which way each flies is <see cref="GoreRules.Launch"/>: away from the hit, up,
    /// the cap and eyes highest for a headshot. The flamethrower's corpse is charred dark. At ultra gore (<see cref="GoreRules.Flings"/>)
    /// every piece, the stem's too, flies far and leaves a long trail: drops and gobs of blood all the way through the air, and splats
    /// close together along the ground.
    /// <para>
    /// The pieces are real rigidbodies on the Ignore Raycast layer (no shot, sight line or blood raycast meets them) and let the
    /// players' capsules through. They settle and freeze after <see cref="settleAfter"/> seconds, and sink and shrink away after
    /// <see cref="lifetime"/> or when the player respawns (<see cref="Sink"/>), whichever is first.
    /// </para>
    /// </summary>
    public sealed class Corpse : MonoBehaviour
    {
        private const string OutlineName = "Outline";
        private const int IgnoreRaycastLayer = 2;
        private static readonly string[] LooseParts = { "eye.0", "eye.1", "hand.0", "hand.1" };

        /// <summary>Where the burst comes from, in the scout's own coordinates: its middle, about where the eyes float.</summary>
        private static readonly Vector3 Origin = new Vector3(0, 0.8f, 0);

        [Tooltip("Seconds the pieces move freely before they slow and freeze")]
        public float settleAfter = 4;
        public float settleTime = 0.8f;
        [Tooltip("Seconds before the corpse sinks away of itself (it sinks sooner when the player respawns)")]
        public float lifetime = 10;
        public float sinkTime = 0.7f;
        [Tooltip("Metres a rolling eye or hand goes between the small splats it leaves")]
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

        private static PhysicsMaterial rolling;
        private static PhysicsMaterial resting;

        private readonly List<Gib> gibs = new List<Gib>();
        private Gore gore;
        private Team team;
        private float age;
        private float sinking = -1;
        private bool settled;
        private int trailSplats;
        private bool fling;
        private Vector3 stump;

        /// <summary>Whose corpse it is.</summary>
        public PlayerState Victim { get; private set; }

        public bool Sinking => sinking >= 0;

        /// <summary>
        /// The corpse of <paramref name="player"/>, killed by <paramref name="death"/>: <paramref name="scout"/> (the team's prefab) where
        /// it stood, with the body pieces of <paramref name="pieces"/> named after it, burst away from <paramref name="away"/> (the hit's
        /// push, flat; zero when unknown); with <paramref name="fling"/> (ultra gore) every piece flies far, trailing blood. Null without
        /// a scout prefab.
        /// </summary>
        public static Corpse Make(Gore gore, Player player, Death death, GameObject scout, GameObject[] pieces, Vector3 away, bool fling = false)
        {
            if (scout == null)
                return null;
            var root = new GameObject($"Corpse of {player.name}");
            root.transform.SetPositionAndRotation(player.transform.position, Quaternion.Euler(0, player.Controller.Yaw, 0));
            var corpse = root.AddComponent<Corpse>();
            corpse.gore = gore;
            corpse.team = death.Victim.Team;
            corpse.Victim = death.Victim;
            corpse.fling = fling;
            corpse.stump = root.transform.position + Vector3.up * 0.55f;

            var body = Instantiate(scout, root.transform, false);
            body.name = "Body";
            body.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            Strip(body);
            // The pose: the eyes and hands where the live scout held them (you have no scout drawn: at rest)
            var live = player.body != null ? player.body.transform : null;
            var loose = new List<Transform>();
            foreach (var name in LooseParts)
            {
                var part = Find(body.transform, name);
                if (part == null)
                    continue;
                var held = live != null ? Find(live, name) : null;
                if (held != null)
                    part.SetLocalPositionAndRotation(held.localPosition, held.localRotation);
                loose.Add(part);
            }

            var strength = GoreRules.Strength(death.Weapon);
            var headshot = death.Headshot;
            foreach (var part in loose)
            {
                part.SetParent(root.transform, true);
                corpse.Add(part, GibKind.Loose, away, headshot, strength);
            }

            // The body: in its pieces where the scout has them, else whole
            var prefix = scout.name + "-";
            var own = pieces != null ? System.Array.FindAll(pieces, p => p != null && p.name.StartsWith(prefix)) : new GameObject[0];
            if (own.Length > 0)
            {
                Destroy(body);
                // A cannon shatters the cap; otherwise it breaks off in one
                var shatter = death.Weapon == "cannon";
                Transform cap = null;
                foreach (var prefab in own)
                {
                    var piece = Instantiate(prefab, root.transform, false);
                    piece.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Strip(piece);
                    var isCap = prefab.name.Substring(prefix.Length).StartsWith("cap");
                    if (isCap && !shatter)
                    {
                        if (cap == null)
                        {
                            cap = new GameObject("Cap").transform;
                            cap.SetParent(root.transform, false);
                        }
                        piece.transform.SetParent(cap, false);
                        AddBox(piece.transform);
                        continue;
                    }
                    corpse.Add(piece.transform, isCap ? GibKind.Cap : GibKind.Stem, away, headshot, strength);
                }
                if (cap != null)
                    corpse.Add(cap, GibKind.Cap, away, headshot, strength);
            }
            else
            {
                corpse.Add(body.transform, GibKind.Stem, away, headshot, strength);
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
        private void Add(Transform part, GibKind kind, Vector3 away, bool headshot, float strength)
        {
            var bounds = LocalBounds(part, part);
            var radius = Mathf.Max(0.03f, Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)) * 0.85f);
            if (kind == GibKind.Loose)
            {
                var sphere = part.gameObject.AddComponent<SphereCollider>();
                sphere.center = bounds.center;
                sphere.radius = radius;
            }
            else if (part.GetComponentInChildren<Collider>() == null)
            {
                AddBox(part);
            }
            var colliders = part.GetComponentsInChildren<Collider>();
            var material = kind == GibKind.Loose ? Rolling : Resting;
            foreach (var collider in colliders)
                collider.sharedMaterial = material;

            var body = part.gameObject.AddComponent<Rigidbody>();
            body.mass = kind == GibKind.Loose ? 0.3f : kind == GibKind.Cap ? 1.5f : 2.5f;
            body.linearDamping = kind == GibKind.Loose ? 0.05f : 0.3f;
            body.angularDamping = kind == GibKind.Loose ? 0.1f : 1.5f;
            body.maxAngularVelocity = 40;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = kind == GibKind.Loose ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Discrete;

            var outward = part.TransformPoint(bounds.center) - transform.TransformPoint(Origin);
            body.linearVelocity = GoreRules.Launch(kind, outward, away, headshot, strength, Random.insideUnitSphere, Random.value, fling);
            var spin = Random.onUnitSphere * (GoreRules.Spin(kind) * Random.Range(0.4f, 1f) * Mathf.Deg2Rad);
            // The stem topples over, away from the hit
            if (kind == GibKind.Stem && !fling)
            {
                var fall = away.sqrMagnitude > 1e-4f ? away : new Vector3(outward.x, 0, outward.z).normalized;
                if (fall.sqrMagnitude > 1e-4f)
                    spin += Vector3.Cross(Vector3.up, fall).normalized * Random.Range(1.5f, 3f);
            }
            body.angularVelocity = spin;

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
            if (age < 1.5f)
                Gush(dt);
            if (age < settleAfter)
                foreach (var gib in gibs)
                    if (gib.kind == GibKind.Loose || fling)
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

        /// <summary>For a moment blood spurts up out of the stump.</summary>
        private void Gush(float dt)
        {
            if (Random.value > dt * 30)
                return;
            gore.Drip(stump, (Vector3.up * 3 + Random.insideUnitSphere) * Random.Range(0.8f, 1.4f), team);
        }

        /// <summary>
        /// A rolling eye or hand drips, and leaves a splat where it lands and every so often along its roll. At ultra gore every piece
        /// does, more thickly, and sheds gobs as it flies that splat on the ground under its path.
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
                    gore.Drip(position, velocity, team, fling ? 3 : 1);
                    gib.dripIn = 0.06f;
                }
                if (fling && speed > 2)
                {
                    gib.gobIn -= dt;
                    if (gib.gobIn <= 0)
                    {
                        gore.Throw(position, velocity * 0.25f + Random.insideUnitSphere * 0.5f, Random.Range(0.035f, 0.06f), team);
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
                gore.Splat(point, normal, Random.Range(0.15f, 0.22f), team);
            }
            else if (speed > 0.4f && (point - gib.lastSplat).sqrMagnitude > spacing * spacing)
            {
                gore.Splat(point, normal, Random.Range(0.06f, 0.11f), team);
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

        /// <summary>Balls that roll a long way: little friction, a little bounce.</summary>
        private static PhysicsMaterial Rolling => rolling != null ? rolling : rolling = new PhysicsMaterial("Gore Rolling")
        {
            dynamicFriction = 0.3f, staticFriction = 0.35f, bounciness = 0.35f,
            frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Maximum,
        };

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
