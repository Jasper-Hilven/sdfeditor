using System.Collections.Generic;
using SdfRig;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The body of a killed soldier, lying down (ticket 51): a copy of the team's soldier (headless: the monster head bursts on its own,
    /// <see cref="Corpse"/>) taken over by physics in the pose it died in, pushed by the hit. The human body never bursts or bleeds, it
    /// ragdolls; there are no death clips. Built here (BattleRoyale has no ragdoll to port) on the Synty skeleton: 10 rigid bodies, one per
    /// <see cref="Parts"/> row (pelvis, chest, two upper arms, two forearms, two thighs, two calves), each a capsule along its bone, joined
    /// to its parent by a <see cref="CharacterJoint"/> (cone and twist limits around the pose it died in: the joints are made then, so a
    /// running pose is not out of its limits). The neck, head, hands and feet ride on their parent's body; the neck's collar is closed.
    /// <para>
    /// Cheap on the web: the bodies are pooled (<see cref="Gore"/> keeps them per skin, at most <see cref="GoreRules.MaxCorpses"/> times
    /// the quality's gore amount at once, the oldest goes first), the colliders hit the level only (on the Ignore Raycast layer, which no
    /// shot, sight line or blood meets, excluding that layer so no other corpse, piece or ragdoll; <see cref="IgnorePlayer"/> for the
    /// players' capsules), and each ragdoll simulates for <see cref="GoreRules.RagdollSeconds"/> at most, or until every body has
    /// settled, then its joints go and its bodies turn kinematic with no collider: a lying corpse costs nothing but its skinned mesh.
    /// It sinks away after <see cref="lifetime"/> or when its player respawns.
    /// </para>
    /// </summary>
    public sealed class SoldierRagdoll : MonoBehaviour
    {
        /// <summary>One rigid body of the ragdoll: the bone it is on, the bone its capsule ends at, and its numbers.</summary>
        public readonly struct Part
        {
            public Part(string bone, string end, string parent, float radius, float extra, float mass, float swing, float twist)
            {
                Bone = bone; End = end; Parent = parent; Radius = radius; Extra = extra; Mass = mass; Swing = swing; Twist = twist;
            }

            public string Bone { get; }
            /// <summary>The bone the capsule runs to from <see cref="Bone"/>'s origin.</summary>
            public string End { get; }
            /// <summary>The bone of the body this one is joined to; null for the root (the pelvis).</summary>
            public string Parent { get; }
            public float Radius { get; }
            /// <summary>Metres the capsule runs on past its end bone (the sole under the ankle, the palm past the wrist).</summary>
            public float Extra { get; }
            public float Mass { get; }
            /// <summary>Degrees of swing either way (about both axes) and of twist either way, from the pose it died in.</summary>
            public float Swing { get; }
            public float Twist { get; }
        }

        /// <summary>The ten bodies, parents first. About 70 kg together.</summary>
        public static readonly Part[] Parts =
        {
            new Part("Hips", "Spine_01", null, 0.15f, 0f, 12f, 0, 0),
            new Part("Spine_02", "Neck", "Hips", 0.17f, 0.05f, 18f, 35, 25),
            new Part("Shoulder_L", "Elbow_L", "Spine_02", 0.055f, 0f, 2.5f, 70, 30),
            new Part("Elbow_L", "Hand_L", "Shoulder_L", 0.05f, 0.07f, 1.5f, 60, 15),
            new Part("Shoulder_R", "Elbow_R", "Spine_02", 0.055f, 0f, 2.5f, 70, 30),
            new Part("Elbow_R", "Hand_R", "Shoulder_R", 0.05f, 0.07f, 1.5f, 60, 15),
            new Part("UpperLeg_L", "LowerLeg_L", "Hips", 0.09f, 0f, 8f, 50, 20),
            new Part("LowerLeg_L", "Ankle_L", "UpperLeg_L", 0.07f, 0.09f, 4f, 55, 10),
            new Part("UpperLeg_R", "LowerLeg_R", "Hips", 0.09f, 0f, 8f, 50, 20),
            new Part("LowerLeg_R", "Ankle_R", "UpperLeg_R", 0.07f, 0.09f, 4f, 55, 10),
        };

        [Tooltip("Seconds the corpse lies before it sinks away of itself (sooner when its player respawns)")]
        public float lifetime = 10;
        public float sinkTime = 0.7f;

        private sealed class Body
        {
            public Part part;
            public Transform bone;
            public Rigidbody rigidbody;
            public CapsuleCollider capsule;
            public CharacterJoint joint;
            public Body parent;
        }

        private readonly List<Body> bodies = new List<Body>();
        private Gore owner;
        private float age;
        private float calm;
        private float sinking = -1;
        private bool simulating;
        private Vector3 scale = Vector3.one;

        /// <summary>The skin this body wears (<see cref="SoldierModels.SkinKeys"/>), for the pool.</summary>
        public int Skin { get; private set; }

        /// <summary>Whose corpse it is, while it lies.</summary>
        public PlayerState Victim { get; private set; }

        public bool Simulating => simulating;

        public bool Sinking => sinking >= 0;

        /// <summary>Seconds since it fell.</summary>
        public float Age => age;

        public int BodyCount => bodies.Count;

        /// <summary>How many of its bodies are simulating right now (not kinematic).</summary>
        public int ActiveBodies
        {
            get
            {
                var count = 0;
                foreach (var body in bodies)
                    if (body.rigidbody != null && !body.rigidbody.isKinematic)
                        count++;
                return count;
            }
        }

        /// <summary>The rigid bodies, for tests and captures.</summary>
        public IEnumerable<Rigidbody> Rigidbodies
        {
            get
            {
                foreach (var body in bodies)
                    yield return body.rigidbody;
            }
        }

        public IEnumerable<Collider> Colliders
        {
            get
            {
                foreach (var body in bodies)
                    yield return body.capsule;
            }
        }

        // ---------------------------------------------------------------- Making

        /// <summary>
        /// A ragdoll made from the soldier prefab <paramref name="prefab"/>: its Animator, hit capsules and head sockets' contents gone,
        /// its ten bodies and capsules on, all kinematic and inactive. Null when the prefab lacks a bone of <see cref="Parts"/>.
        /// </summary>
        public static SoldierRagdoll Create(GameObject prefab, int skin, Transform parent, Gore owner = null)
        {
            if (prefab == null)
                return null;
            var instance = Instantiate(prefab, parent, false);
            instance.name = "Ragdoll";
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(collider);
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
                DestroyImmediate(animator);
            var ragdoll = instance.AddComponent<SoldierRagdoll>();
            ragdoll.owner = owner;
            ragdoll.Skin = skin;
            if (!ragdoll.Build())
            {
                DestroyImmediate(instance);
                return null;
            }
            instance.SetActive(false);
            return ragdoll;
        }

        private bool Build()
        {
            var bones = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name))
                    bones[t.name] = t;
            var byBone = new Dictionary<string, Body>();
            foreach (var part in Parts)
            {
                if (!bones.TryGetValue(part.Bone, out var bone) || !bones.TryGetValue(part.End, out var end))
                    return false;
                var rigidbody = bone.gameObject.AddComponent<Rigidbody>();
                rigidbody.mass = part.Mass;
                rigidbody.linearDamping = 0.1f;
                rigidbody.angularDamping = 0.6f;
                rigidbody.maxAngularVelocity = 20;
                rigidbody.maxDepenetrationVelocity = 2;
                rigidbody.solverIterations = 6;
                rigidbody.solverVelocityIterations = 2;
                rigidbody.sleepThreshold = 0.3f;
                rigidbody.interpolation = RigidbodyInterpolation.None;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rigidbody.isKinematic = true;
                var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
                Fit(capsule, bone, end.position, part.Radius, part.Extra);
                capsule.sharedMaterial = Lying;
                capsule.excludeLayers = 1 << Corpse.IgnoreRaycastLayer;
                capsule.enabled = false;
                var body = new Body { part = part, bone = bone, rigidbody = rigidbody, capsule = capsule };
                if (part.Parent != null)
                    body.parent = byBone[part.Parent];
                byBone[part.Bone] = body;
                bodies.Add(body);
            }
            // The skin's bounds follow the pelvis (the bodies fall away from the root), generous, so it is never culled while it lies
            var hips = byBone[Parts[0].Bone].bone;
            foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.rootBone = hips;
                skin.localBounds = new Bounds(Vector3.zero, Vector3.one * 3f);
                skin.updateWhenOffscreen = false;
            }
            SetLayer(transform, Corpse.IgnoreRaycastLayer);
            return true;
        }

        /// <summary>A capsule on <paramref name="bone"/> from its origin to <paramref name="endWorld"/> and on by <paramref name="extra"/>, along the bone's nearest axis.</summary>
        public static void Fit(CapsuleCollider capsule, Transform bone, Vector3 endWorld, float radius, float extra)
        {
            var end = bone.InverseTransformPoint(endWorld);
            var axis = 0;
            if (Mathf.Abs(end.y) > Mathf.Abs(end[axis]))
                axis = 1;
            if (Mathf.Abs(end.z) > Mathf.Abs(end[axis]))
                axis = 2;
            var length = Mathf.Abs(end[axis]) + extra;
            var sign = end[axis] >= 0 ? 1 : -1;
            var centre = Vector3.zero;
            capsule.direction = axis;
            capsule.radius = radius;
            capsule.height = Mathf.Max(length + radius, radius * 2.01f);
            // Its length is the segment between the bone and its end, the rounded caps beyond: the middle sits half-way along
            centre[axis] = sign * length * 0.5f;
            capsule.center = centre;
        }

        // ---------------------------------------------------------------- Lying down

        /// <summary>
        /// The ragdoll takes the pose of <paramref name="source"/> (the dead soldier's body: same skeleton, any extra sockets and heads
        /// ignored) where it stands and starts to fall, pushed along <paramref name="push"/> (a velocity at the chest, metres per second,
        /// flat is expected) on top of <paramref name="inherited"/> (what the player was moving at).
        /// </summary>
        public void Launch(Transform source, PlayerState victim, Vector3 push, Vector3 inherited, bool headshot)
        {
            Victim = victim;
            age = 0;
            calm = 0;
            sinking = -1;
            transform.SetPositionAndRotation(source.position, source.rotation);
            transform.localScale = scale = source.localScale;
            CopyPose(source, transform);
            gameObject.SetActive(true);
            Physics.SyncTransforms();
            foreach (var body in bodies)
            {
                body.capsule.enabled = true;
                body.rigidbody.isKinematic = false;
                if (body.parent == null)
                    continue;
                // The joint is made now, in this pose: its limits are round it, so a bent elbow starts inside them
                var joint = body.bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = body.parent.rigidbody;
                joint.autoConfigureConnectedAnchor = true;
                joint.anchor = Vector3.zero;
                joint.axis = Vector3.right;
                joint.swingAxis = Vector3.up;
                joint.enablePreprocessing = false;
                joint.enableProjection = true;
                joint.enableCollision = false;
                var swing = body.part.Swing;
                var twist = body.part.Twist;
                joint.lowTwistLimit = new SoftJointLimit { limit = -twist };
                joint.highTwistLimit = new SoftJointLimit { limit = twist };
                joint.swing1Limit = new SoftJointLimit { limit = swing };
                joint.swing2Limit = new SoftJointLimit { limit = swing };
                body.joint = joint;
            }
            var chest = ChestSpeed(push, headshot);
            foreach (var body in bodies)
            {
                var weight = body.part.Bone == "Spine_02" ? 1f : body.part.Bone == "Hips" ? 0.7f : 0.85f;
                body.rigidbody.linearVelocity = inherited + chest * weight;
                body.rigidbody.angularVelocity = Random.insideUnitSphere * 0.8f;
            }
            simulating = true;
        }

        /// <summary>The velocity the hit gives the chest: the push, and a hop up (more for a headshot, which throws the body back).</summary>
        public static Vector3 ChestSpeed(Vector3 push, bool headshot)
        {
            push.y = 0;
            return push + Vector3.up * (headshot ? 2.2f : 1.4f);
        }

        /// <summary>Every bone of <paramref name="from"/> found by name under <paramref name="to"/> takes its local pose, parents first.</summary>
        public static void CopyPose(Transform from, Transform to)
        {
            to.localPosition = from.localPosition;
            to.localRotation = from.localRotation;
            to.localScale = from.localScale;
            foreach (Transform child in from)
            {
                // Sockets and the head hang under the bones too: only what the copy has is posed
                var twin = to.Find(child.name);
                if (twin != null)
                    CopyPose(child, twin);
            }
        }

        /// <summary>Keeps the ragdoll's colliders from pushing a player's capsule (and the other way): they lie where they fell.</summary>
        public void IgnorePlayer(Player player)
        {
            if (player == null || player.Capsule == null || !player.Capsule.enabled || !gameObject.activeSelf)
                return;
            foreach (var body in bodies)
                if (body.capsule.enabled)
                    Physics.IgnoreCollision(body.capsule, player.Capsule);
        }

        /// <summary>The corpse sinks and shrinks away, then goes back to the pool.</summary>
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
                if (simulating)
                    Freeze();
                sinking += dt;
                var shrink = 1 - Mathf.SmoothStep(0, 1, sinking / sinkTime);
                transform.localScale = scale * Mathf.Max(shrink, 0.001f);
                transform.position += Vector3.down * (0.3f * dt);
                if (sinking >= sinkTime)
                    Release();
                return;
            }
            if (simulating)
                Settle(dt);
        }

        private void Settle(float dt)
        {
            var still = true;
            foreach (var body in bodies)
                if (body.rigidbody.linearVelocity.sqrMagnitude > GoreRules.RagdollSettleSpeed * GoreRules.RagdollSettleSpeed
                    || body.rigidbody.angularVelocity.sqrMagnitude > 1f)
                {
                    still = false;
                    break;
                }
            calm = still ? calm + dt : 0;
            if (age >= GoreRules.RagdollSeconds || (age > 0.5f && calm >= GoreRules.RagdollSettleSeconds))
                Freeze();
        }

        /// <summary>The ragdoll lies still for good: the joints go, the bodies turn kinematic and the capsules off.</summary>
        public void Freeze()
        {
            simulating = false;
            foreach (var body in bodies)
            {
                if (body.joint != null)
                    Destroy(body.joint);
                body.joint = null;
                body.rigidbody.isKinematic = true;
                body.capsule.enabled = false;
            }
        }

        private void Release()
        {
            Freeze();
            gameObject.SetActive(false);
            Victim = null;
            sinking = -1;
            if (owner != null)
                owner.Release(this);
        }

        // ---------------------------------------------------------------- Helpers

        private static PhysicsMaterial lying;

        /// <summary>Cloth on the ground: friction, no bounce.</summary>
        private static PhysicsMaterial Lying => lying != null ? lying : lying = new PhysicsMaterial("Gore Ragdoll")
        {
            dynamicFriction = 0.7f, staticFriction = 0.8f, bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Minimum,
        };

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root)
                SetLayer(child, layer);
        }
    }
}
