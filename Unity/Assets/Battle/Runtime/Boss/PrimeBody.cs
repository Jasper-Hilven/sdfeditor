using SdfPrime;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>What Chanterelle Prime is doing with his body: the pose each of his attacks and windows has (ticket 33.6).</summary>
    public enum PrimeAct
    {
        Idle,
        /// <summary>The spore mortar's wind-up: the hands scoop.</summary>
        Mortar,
        /// <summary>The royal glare: head up, eyes wide, the hands on the hips.</summary>
        Glare,
        /// <summary>The cap slam's wind-up: hands and cap rise.</summary>
        SlamWind,
        /// <summary>The slam itself: down hard.</summary>
        SlamHit,
        /// <summary>The cap stuck in the floor after a slam: the gills show.</summary>
        Stuck,
        /// <summary>A hand flies over the target (<see cref="PrimeBody.ReachHand"/>) and grabs.</summary>
        Grab,
        /// <summary>Phase 1's window: kneeling, the cap tilted forward, the gills to the field.</summary>
        Kneel,
        /// <summary>Phase 3's burst hand: stunned on his feet, swaying.</summary>
        Dizzy,
        /// <summary>The last bar is gone.</summary>
        Beaten,
    }

    /// <summary>
    /// The acting with Chanterelle Prime's pieces (ticket 33.6): pose tweens for each wind-up and window, the blink and the look of his eyes,
    /// the springs of the cape and the medal, the kneel, the slam and the walk's bob. A component on the <see cref="PrimeRig"/>'s object,
    /// whose scale is 4.6 in the boss scene (31's beats use the same prefab at 1:1, so every length here is in the prefab's own metres,
    /// scout scale). Data and a few eases: no skeleton, no animator; the pose table is <see cref="PoseFor"/>. The boss tells it what to do
    /// (<see cref="Act"/>, <see cref="LookAt"/>, <see cref="Glow"/>, <see cref="ReachHand"/>); the Prime-copter, while it holds him, owns his
    /// cap and hands (<see cref="HeldByCopter"/>). Runs in <c>Update</c>, before the copter's late update places him.
    /// </summary>
    public sealed class PrimeBody : MonoBehaviour
    {
        /// <summary>Prime's size in the boss scene against the prefab's scout scale.</summary>
        public const float BossScale = 4.6f;

        /// <summary>One pose: how far the cap tilts and lifts, the stem leans, the hands sit from their rest, the eyes close.</summary>
        public struct Pose
        {
            /// <summary>Degrees the cap tilts forward (negative: back).</summary>
            public float capTilt;
            /// <summary>Metres the cap lifts, in the prefab's own metres.</summary>
            public float capLift;
            /// <summary>Degrees the stem leans forward.</summary>
            public float lean;
            /// <summary>Where each hand floats from where it rests, in the stem's model space (x is mirrored for the second hand).</summary>
            public Vector3 hand;
            /// <summary>0 eyes open, 1 shut.</summary>
            public float shut;

            public static Pose Lerp(Pose a, Pose b, float t) => new Pose
            {
                capTilt = Mathf.Lerp(a.capTilt, b.capTilt, t),
                capLift = Mathf.Lerp(a.capLift, b.capLift, t),
                lean = Mathf.Lerp(a.lean, b.lean, t),
                hand = Vector3.Lerp(a.hand, b.hand, t),
                shut = Mathf.Lerp(a.shut, b.shut, t),
            };
        }

        [Header("Tuning (prefab metres)")]
        [Tooltip("How fast the pose follows its target: the share of the way it goes in a second, as a rate")]
        public float poseRate = 9;
        [Tooltip("Metres the walk's bob lifts the stem")]
        public float bobHeight = 0.025f;
        [Tooltip("The cape's and the medal's spring: stiffness and damping")]
        public float springStiffness = 40;
        public float springDamping = 5;
        [Tooltip("Seconds between blinks: least and most")]
        public Vector2 blinkEvery = new Vector2(2.5f, 5.5f);

        private PrimeRig rig;
        private Quaternion capRest, stemRest;
        private Vector3 capRestPosition, stemRestPosition;
        private Vector3[] handRest = new Vector3[0];
        private Quaternion[] eyeRest = new Quaternion[0];
        private Vector3[] eyeScale = new Vector3[0];
        private Pose pose, target;
        private PrimeAct act;
        private float actProgress;
        private Vector3? look;
        private readonly Vector3?[] handWorld = new Vector3?[2];
        private Color glowColour = Color.white;
        private float glow;
        private bool walking;
        private bool eyesShut;
        private float clock;
        private float blinkIn = 2;
        private float blink;
        private Vector3 lastPosition;
        private Quaternion lastRotation;
        // The cape's and the medal's springs: angle about x and z (degrees) and their speeds
        private Vector2 capeAngle, capeSpeed, medalAngle, medalSpeed;
        // The cape left on the floor in the victory (DropCape): where it hung
        private bool capeDropped;
        private Transform capeParent;
        private Vector3 capeLocalPosition, capeLocalScale;
        private Quaternion capeLocalRotation;

        public PrimeRig Rig => rig;

        /// <summary>What he is doing.</summary>
        public PrimeAct Act => act;

        /// <summary>The Prime-copter has him (phase 2): his cap and hands are the copter's to place, the rest (eyes, cape, medal) is still his.</summary>
        public bool HeldByCopter { get; set; }

        /// <summary>The eyes are shut (a wind-up in phase 3, the beaten Prime): hits on them do nothing.</summary>
        public bool EyesShut => pose.shut > 0.6f;

        /// <summary>The colour and strength (0 to 1) of the glow of his eyes, for the telegraph: white-blue for the glare, gold for the slam, green for spores.</summary>
        public Color GlowColour => glowColour;

        public float GlowAmount => glow;

        /// <summary>Makes his body from the rig, remembering the pose every piece rests in.</summary>
        public static PrimeBody Create(PrimeRig rig)
        {
            var body = rig.GetComponent<PrimeBody>();
            if (body == null)
                body = rig.gameObject.AddComponent<PrimeBody>();
            body.Bind(rig);
            return body;
        }

        private void Bind(PrimeRig prime)
        {
            rig = prime;
            capRest = rig.cap != null ? rig.cap.localRotation : Quaternion.identity;
            capRestPosition = rig.cap != null ? rig.cap.localPosition : Vector3.zero;
            stemRest = rig.stem != null ? rig.stem.localRotation : Quaternion.identity;
            stemRestPosition = rig.stem != null ? rig.stem.localPosition : Vector3.zero;
            handRest = new Vector3[rig.hands != null ? rig.hands.Length : 0];
            for (var i = 0; i < handRest.Length; i++)
                handRest[i] = rig.hands[i].localPosition;
            eyeRest = new Quaternion[rig.eyes != null ? rig.eyes.Length : 0];
            eyeScale = new Vector3[eyeRest.Length];
            for (var i = 0; i < eyeRest.Length; i++)
            {
                eyeRest[i] = rig.eyes[i].localRotation;
                eyeScale[i] = rig.eyes[i].localScale;
            }
            lastPosition = transform.position;
            lastRotation = transform.rotation;
            target = pose = PoseFor(PrimeAct.Idle, 0);
        }

        // ---------------------------------------------------------------- What the boss asks

        /// <summary>
        /// Takes a pose. <paramref name="progress"/> is how far through the wind-up or the stage it is (0 to 1), which some poses follow
        /// (the hands rising for the slam).
        /// </summary>
        public void SetAct(PrimeAct next, float progress = 0)
        {
            act = next;
            actProgress = Mathf.Clamp01(progress);
            target = PoseFor(act, actProgress);
        }

        /// <summary>Looks at a point in the world with both eyes (at most 70 degrees off straight ahead); null for straight ahead.</summary>
        public void LookAt(Vector3? point) => look = point;

        /// <summary>The eyes glow in this colour, 0 to 1 (0 for none): the telegraph's cue beside the pose and the ground marker.</summary>
        public void Glow(Color colour, float amount)
        {
            glowColour = colour;
            glow = Mathf.Clamp01(amount);
        }

        /// <summary>
        /// Floats a hand (0 or 1) to a point in the world, or back to rest with null: the hand that hovers over the grab's target. Blends
        /// in over a moment.
        /// </summary>
        public void ReachHand(int index, Vector3? world)
        {
            if (index >= 0 && index < handWorld.Length)
                handWorld[index] = world;
        }

        /// <summary>Shuts his eyes (phase 3's wind-ups: the weak point is closed) or opens them.</summary>
        public void ShutEyes(bool shut) => eyesShut = shut;

        /// <summary>He walks: the bob.</summary>
        public void SetWalking(bool value) => walking = value;

        /// <summary>The world position of an eye (0 or 1), for the beams and the glow.</summary>
        public Vector3 EyePosition(int index) => rig.eyes != null && index < rig.eyes.Length ? rig.eyes[index].position : transform.position;

        /// <summary>The middle of his eyes.</summary>
        public Vector3 EyesCentre => rig.eyes != null && rig.eyes.Length >= 2 ? (rig.eyes[0].position + rig.eyes[1].position) * 0.5f : transform.position + Vector3.up * 5;

        /// <summary>Where the gills are, under the cap's front rim.</summary>
        public Vector3 GillsPosition => rig.cap != null ? rig.cap.TransformPoint(GillsLocal) : transform.position;

        /// <summary>The gills' sphere in the cap's space (prefab metres).</summary>
        public static readonly Vector3 GillsLocal = new Vector3(0, -0.02f, 0.22f);

        /// <summary>The world position of a hand (0 or 1).</summary>
        public Vector3 HandPosition(int index) => rig.hands != null && index < rig.hands.Length ? rig.hands[index].position : transform.position;

        /// <summary>The top of the cap, where the crown sits, in the world.</summary>
        public Vector3 CapTop => rig.crown != null ? rig.crown.position : transform.position + Vector3.up * 6;

        /// <summary>
        /// Leaves the cape behind on the floor at <paramref name="ground"/>, a flat puddle of blue cloth (the victory's shrink, ticket 33.8). It
        /// stops swinging with him; <see cref="TakeCapeBack"/> puts it on again.
        /// </summary>
        public void DropCape(Vector3 ground, Transform parent)
        {
            if (rig.cape == null || capeDropped)
                return;
            capeDropped = true;
            capeParent = rig.cape.parent;
            capeLocalPosition = rig.cape.localPosition;
            capeLocalRotation = rig.cape.localRotation;
            capeLocalScale = rig.cape.localScale;
            var puddleScale = rig.cape.lossyScale;
            rig.cape.SetParent(parent, true);
            // The cape hangs from its top edge: laid back, it covers the floor behind that edge, and thin
            rig.cape.SetPositionAndRotation(ground + Vector3.up * 0.06f, Quaternion.Euler(90, transform.eulerAngles.y, 0));
            rig.cape.localScale = new Vector3(puddleScale.x * 1.4f, puddleScale.y * 1.2f, puddleScale.z * 0.12f);
        }

        /// <summary>The cape back on his cap, as it was (a retry after the victory).</summary>
        public void TakeCapeBack()
        {
            if (!capeDropped || rig.cape == null)
                return;
            capeDropped = false;
            rig.cape.SetParent(capeParent, false);
            rig.cape.localPosition = capeLocalPosition;
            rig.cape.localRotation = capeLocalRotation;
            rig.cape.localScale = capeLocalScale;
        }

        /// <summary>Puts his springs at rest where he stands now (after he was moved by hand: a phase change, a retry).</summary>
        public void Settle()
        {
            lastPosition = transform.position;
            lastRotation = transform.rotation;
            capeAngle = capeSpeed = medalAngle = medalSpeed = Vector2.zero;
            pose = target;
        }

        // ---------------------------------------------------------------- The pose table

        /// <summary>The pose each act has, in the prefab's own metres and degrees (progress 0 to 1 through the act's stage).</summary>
        public static Pose PoseFor(PrimeAct act, float t)
        {
            switch (act)
            {
                case PrimeAct.Mortar:
                    // Scoop: the hands go low and forward, then come up with the ball
                    return new Pose { capTilt = 8 * t, lean = 6 * t, hand = new Vector3(0, -0.2f + 0.5f * Mathf.SmoothStep(0, 1, t), 0.3f) };
                case PrimeAct.Glare:
                    return new Pose { capTilt = -8, lean = -4, hand = new Vector3(0.08f, 0.02f, 0.04f), shut = 0 };
                case PrimeAct.SlamWind:
                    return new Pose { capTilt = -10 * t, capLift = 0.11f * t, lean = -7 * t, hand = new Vector3(0, 0.5f * t, 0.08f) };
                case PrimeAct.SlamHit:
                    return new Pose { capTilt = 32, capLift = -0.01f, lean = 14, hand = new Vector3(0, -0.42f, 0.32f) };
                case PrimeAct.Stuck:
                    return new Pose { capTilt = 44, capLift = -0.04f, lean = 16, hand = new Vector3(0, -0.45f, 0.3f), shut = 0 };
                case PrimeAct.Grab:
                    return new Pose { lean = 8, hand = new Vector3(0, 0.1f, 0.12f) };
                case PrimeAct.Kneel:
                    return new Pose { capTilt = 35, lean = 12, hand = new Vector3(0, -0.45f, 0.35f) };
                case PrimeAct.Dizzy:
                    return new Pose { capTilt = 14, lean = 6, hand = new Vector3(0, -0.1f, 0.05f) };
                case PrimeAct.Beaten:
                    return new Pose { capTilt = 52, lean = 20, hand = new Vector3(0, -0.5f, 0.2f), shut = 1 };
                default:
                    return default;
            }
        }

        // ---------------------------------------------------------------- Every frame

        private void Update()
        {
            if (rig == null)
                return;
            var dt = Time.deltaTime;
            if (dt <= 0)
                return;
            clock += dt;
            var wanted = target;
            if (eyesShut)
                wanted.shut = 1;
            pose = Pose.Lerp(pose, wanted, 1 - Mathf.Exp(-poseRate * dt));
            if (!HeldByCopter)
                ApplyBody(dt);
            Eyes(dt);
            Springs(dt);
        }

        private void ApplyBody(float dt)
        {
            if (rig.stem != null)
            {
                var bob = walking ? Mathf.Abs(Mathf.Sin(clock * 5.2f)) * bobHeight : Mathf.Sin(clock * 1.3f) * bobHeight * 0.3f;
                rig.stem.localPosition = stemRestPosition + Vector3.up * bob;
                rig.stem.localRotation = Quaternion.Euler(pose.lean, 0, walking ? Mathf.Sin(clock * 2.6f) * 2.5f : 0) * stemRest;
            }
            if (rig.cap != null)
            {
                rig.cap.localRotation = capRest * Quaternion.Euler(pose.capTilt, 0, 0);
                rig.cap.localPosition = capRestPosition + Vector3.up * pose.capLift;
            }
            if (rig.hands == null)
                return;
            for (var i = 0; i < rig.hands.Length; i++)
            {
                var side = i % 2 == 0 ? 1f : -1f;
                var sway = walking ? Mathf.Sin(clock * 5.2f + i * Mathf.PI) * 0.06f : Mathf.Sin(clock * 1.7f + i) * 0.015f;
                var hand = rig.hands[i];
                if (i < handWorld.Length && handWorld[i].HasValue)
                {
                    // Floating over its target in the world, eased there from wherever it is
                    hand.position = Vector3.Lerp(hand.position, handWorld[i].Value, Mathf.Clamp01(dt * 6));
                    continue;
                }
                var rest = handRest[i] + new Vector3(pose.hand.x * side, pose.hand.y + sway, pose.hand.z);
                hand.localPosition = Vector3.Lerp(hand.localPosition, rest, Mathf.Clamp01(dt * 10));
            }
        }

        private void Eyes(float dt)
        {
            if (rig.eyes == null)
                return;
            blinkIn -= dt;
            if (blinkIn <= 0)
            {
                blink = 0.14f;
                blinkIn = Random.Range(blinkEvery.x, blinkEvery.y);
            }
            blink = Mathf.Max(0, blink - dt);
            var closed = Mathf.Max(pose.shut, blink > 0 ? 1 : 0);
            var ease = Mathf.Clamp01(dt * 10);
            for (var i = 0; i < rig.eyes.Length; i++)
            {
                var eye = rig.eyes[i];
                var scale = eyeScale[i];
                scale.y *= Mathf.Lerp(1, 0.08f, closed);
                eye.localScale = Vector3.Lerp(eye.localScale, scale, Mathf.Clamp01(dt * 25));
                var wanted = eyeRest[i];
                if (look.HasValue)
                {
                    var local = eye.parent.InverseTransformDirection(look.Value - eye.position);
                    if (local.sqrMagnitude > 1e-6f)
                    {
                        var direction = Vector3.RotateTowards(eyeRest[i] * Vector3.forward, local.normalized, 70 * Mathf.Deg2Rad, 0);
                        wanted = Quaternion.LookRotation(direction, Vector3.up);
                    }
                }
                eye.localRotation = Quaternion.Slerp(eye.localRotation, wanted, ease);
            }
        }

        // The cape and the medal swing against his motion: damped springs on two angles each, pushed by his speed along and across and his turn
        private void Springs(float dt)
        {
            var velocity = (transform.position - lastPosition) / dt / BossScale;
            var local = Quaternion.Inverse(transform.rotation) * velocity;
            var turn = Mathf.DeltaAngle(lastRotation.eulerAngles.y, transform.rotation.eulerAngles.y) / dt;
            lastPosition = transform.position;
            lastRotation = transform.rotation;
            local = Vector3.ClampMagnitude(local, 12);

            var flap = Mathf.Sin(clock * 1.9f) * 3;
            var capeTarget = new Vector2(Mathf.Clamp(local.z * 6, -12, 40) + 4 + flap + pose.lean * 0.4f, Mathf.Clamp(-local.x * 6 + turn * 0.08f, -35, 35));
            Spring(ref capeAngle, ref capeSpeed, capeTarget, dt);
            var medalTarget = new Vector2(Mathf.Clamp(local.z * 5, -10, 30) + flap * 0.6f, Mathf.Clamp(-local.x * 5, -25, 25));
            Spring(ref medalAngle, ref medalSpeed, medalTarget, dt);
            if (rig.cape != null && !capeDropped)
                rig.cape.localRotation = Quaternion.Euler(capeAngle.x, 0, capeAngle.y);
            if (rig.medal != null)
                rig.medal.localRotation = Quaternion.Euler(medalAngle.x, 0, medalAngle.y);
        }

        private void Spring(ref Vector2 angle, ref Vector2 speed, Vector2 target, float dt)
        {
            // Semi-implicit steps, split so a long frame does not blow it up
            var steps = Mathf.CeilToInt(dt / 0.02f);
            var h = dt / steps;
            for (var i = 0; i < steps; i++)
            {
                speed += ((target - angle) * springStiffness - speed * springDamping) * h;
                angle += speed * h;
            }
        }
    }
}
