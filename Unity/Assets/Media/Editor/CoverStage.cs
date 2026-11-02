using System;
using System.Collections.Generic;
using System.Linq;
using SdfBattle;
using SdfEffects;
using SdfHeads;
using SdfRig;
using SdfRig.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SdfMedia.Editor
{
    /// <summary>
    /// The staged scene of the covers, built in a scene of its own over the level's ground and sky: one moment of the game, "mid-air
    /// minigun". The red amanita scout leaps straight at the camera, minigun blazing (a muzzle flash, tracers and flying shell casings),
    /// his eyes wide on the lens and his hands round the gun; two blue chanterelle scouts dive aside behind him, and behind them a
    /// blue scout bursts in a toon explosion (flash, rays, cream and cap-red puffs, spores and his pieces flying). Every character is
    /// the game's own prefab at its finest level of detail, posed in code: the body leaned and tipped, the floating eyes turned to look
    /// at something, the floating hands put round the gun the way <see cref="WeaponView"/> holds it in a scout's hands. The shots and
    /// the explosion are drawn by the game's own effects shader (SdfEffect.shader, through <see cref="FxBuffer"/>, with the weapon's
    /// own tracer colour and the exploding mushroom's own colours), frozen at one frame; each character stands on a soft blob of
    /// shadow, because the level's sun shadow map holds the level only.
    /// <para>
    /// The stage has its own space, in metres: the origin on the ground at <see cref="Centre"/> of the level, x to the right, y up and z
    /// away from the camera, turned in the level by <see cref="YawDegrees"/> (90: looking along +x, from the red end of the field towards the
    /// castle). Everything in the "Composition" block below is in it, and so are the cameras of <see cref="CoverCapture.Formats"/>
    /// (which say where the hero's middle, <see cref="HeroCentre"/>, lands on the picture). Heights are above the ground under each
    /// character, which follows the hills.
    /// </para>
    /// </summary>
    internal sealed class CoverStage : IDisposable
    {
        public const string RootName = "Cover Stage";

        // ------------------------------------------------------------------------------------------------------------ Composition

        /// <summary>Which way the stage looks in the level: degrees about the up axis, 0 along +z, 90 along +x (the level's long side).</summary>
        public static float YawDegrees = 90;
        /// <summary>The stage's middle on the level's ground (level x, z), the middle of the field.</summary>
        public static readonly Vector2 Centre = new Vector2(0, 0);

        // The red hero: where he stands (the ground under him), how high he leaps, how far his cap leans towards the camera and tips sideways
        // (a negative roll tips the cap to the picture's left)
        private static readonly Vector3 RedPosition = new Vector3(0, 0, 0);
        private const float RedLift = 0.6f, RedLean = 18, RedRoll = -9;
        /// <summary>The way he faces (a direction in the stage's space); his gun aims at <see cref="RedAimAt"/>.</summary>
        private static readonly Vector3 RedFacing = new Vector3(-0.45f, 0, -1);
        /// <summary>Left of the lens and a little low: the minigun points past the camera, so its tracers streak across the foreground.</summary>
        private static readonly Vector3 RedAimAt = new Vector3(-4f, 1.5f, 1.2f);
        /// <summary>The hero's middle: the point of him the formats' cameras place on the picture (<see cref="CoverFormat.Anchor"/>).</summary>
        public static readonly Vector3 HeroCentre = new Vector3(0, 1.4f, 0);

        // The two red soldiers beside him (werewolf left, yeti right), a step behind, leaning at the lens, each with a gun aimed past it
        private static readonly Vector3 WolfPosition = new Vector3(-1.45f, 0, 1.7f);
        private static readonly Vector3 YetiPosition = new Vector3(1.25f, 0, 1.2f);
        private const float WolfLift = 0.25f, WolfLean = 12, WolfRoll = 7;
        private const float YetiLift = 0.2f, YetiLean = 10, YetiRoll = -7;
        private static readonly Vector3 WolfFacing = new Vector3(0.35f, 0, -1), YetiFacing = new Vector3(-0.35f, 0, -1);
        private static readonly Vector3 WolfAimAt = new Vector3(-3f, 0.9f, -4f), YetiAimAt = new Vector3(3.5f, 0.8f, -4f);

        // The burst behind them: its radius in metres, the head pieces' size on top of the game's, how many bats and how many purple puffs
        private const float ExplosionRadius = 2.7f;
        private const float DebrisScale = 1.6f;
        private const int Bats = 8, Puffs = 26;

        /// <summary>The soft shadow under a character: its radius in metres at scale 1, and how dark.</summary>
        private const float ShadowRadius = 0.95f, ShadowDarkness = 0.55f;

        // ------------------------------------------------------------------------------------------------------------ The stage

        // The three heads in the picture: the hero's, the two diving soldiers', and the one that bursts (its colours colour the explosion)
        private const string HeroHead = "pumpkin", LeftHead = "werewolf", RightHead = "yeti", BurstHead = "vampire";
        private const string AimClip = "Assets/Battle/Body/Animations/Locomotion/soldier_loco_aim_idle.anim";
        private const string RunClip = "Assets/Battle/Body/Animations/Locomotion/soldier_loco_run_f.anim";
        // The headless soldier behind them, lifted and tipped back by the burst
        private static readonly Vector3 VictimPosition = new Vector3(1.6f, 0, 6.2f);
        private const float VictimLift = 2.0f, VictimLean = -25, VictimRoll = 15;
        private const string EffectMaterialPath = "Assets/Effects/Materials/Effect.mat";

        private readonly Scene scene;
        private readonly Transform root;
        private readonly Quaternion rotation;
        private readonly Vector3 origin;
        private readonly float fallbackGroundY;
        private readonly WeaponTableAsset weapons;
        private readonly List<Object> owned = new List<Object>();
        private readonly Dictionary<PrimitiveType, Mesh> meshes = new Dictionary<PrimitiveType, Mesh>();
        private Texture2D blob;
        /// <summary>Each soldier's head bone turned relative to its root in the bind pose, before any clip is sampled.</summary>
        private readonly Dictionary<Transform, Quaternion> restHead = new Dictionary<Transform, Quaternion>();
        /// <summary>Roughly where the cameras are (stage space), which the faces look at.</summary>
        private static readonly Vector3 LookAt = new Vector3(-0.3f, 0.6f, -4.3f);

        /// <param name="scene">The empty scene the stage is built in.</param>
        /// <param name="fallbackGroundY">The ground height where the level's ground cannot be hit (the player's feet).</param>
        public CoverStage(Scene scene, float fallbackGroundY)
        {
            this.scene = scene;
            this.fallbackGroundY = fallbackGroundY;
            var rootObject = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(rootObject, scene);
            root = rootObject.transform;
            rotation = Quaternion.Euler(0, YawDegrees, 0);
            origin = new Vector3(Centre.x, Ground(new Vector3(Centre.x, 0, Centre.y), out _), Centre.y);
            weapons = WeaponTableAsset.Load();
            if (weapons == null)
                throw new InvalidOperationException("there is no weapon table (Assets/Battle/Resources/Weapons.asset)");
            Build();
        }

        public void Dispose()
        {
            if (root != null)
                Object.DestroyImmediate(root.gameObject);
            foreach (var item in owned)
                if (item != null)
                    Object.DestroyImmediate(item);
            owned.Clear();
        }

        // ------------------------------------------------------------------------------------------------------------ Space

        /// <summary>A point of the stage's space in the world (the height is above the stage's origin).</summary>
        public Vector3 ToWorld(Vector3 local) => origin + rotation * local;

        /// <summary>A direction of the stage's space in the world.</summary>
        public Vector3 ToWorldDirection(Vector3 local) => rotation * local;

        /// <summary>The ground under a world position (the level's ground colliders), its height and its slope; the player's height where there is none.</summary>
        private float Ground(Vector3 world, out Vector3 normal)
        {
            normal = Vector3.up;
            var from = new Vector3(world.x, 200, world.z);
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 400).OrderBy(h => h.distance))
                if (hit.collider is MeshCollider && hit.collider.transform.parent != null && hit.collider.transform.parent.name == "Ground")
                {
                    normal = hit.normal;
                    return hit.point.y;
                }
            return fallbackGroundY;
        }

        /// <summary>
        /// Where a format's camera stands and looks, in the world: at <see cref="CoverFormat.CameraPosition"/>, turned so that
        /// <see cref="HeroCentre"/> lands at <see cref="CoverFormat.Anchor"/> of the picture (share from its left and from its top), then
        /// rolled about that line of sight by <see cref="CoverFormat.Roll"/>.
        /// </summary>
        public void CameraPose(CoverFormat format, out Vector3 position, out Quaternion look)
        {
            var toHero = ToWorldDirection((HeroCentre - format.CameraPosition).normalized);
            var half = Mathf.Tan(format.FieldOfView * 0.5f * Mathf.Deg2Rad);
            // The hero's direction in the camera's own space if he is to be at the anchor
            var wanted = new Vector3((format.Anchor.x * 2 - 1) * half * format.Aspect, (1 - format.Anchor.y * 2) * half, 1).normalized;
            var aimed = Quaternion.LookRotation(toHero, Vector3.up) * Quaternion.Euler(0, 0, format.Roll);
            position = ToWorld(format.CameraPosition);
            look = aimed * Quaternion.Inverse(Quaternion.LookRotation(wanted, Vector3.up));
        }

        /// <summary>Puts a camera at a format's framing.</summary>
        public void PlaceCamera(Camera camera, CoverFormat format)
        {
            CameraPose(format, out var position, out var look);
            camera.transform.SetPositionAndRotation(position, look);
            camera.fieldOfView = format.FieldOfView;
            camera.aspect = format.Aspect;
        }

        /// <summary>A visible camera object in the stage at a format's framing, to align the Scene view to (the preview).</summary>
        public void PlaceCameraObject(CoverFormat format)
        {
            var cameraObject = new GameObject("Cover Camera " + format.FileName.Replace("cover-", "").Replace(".png", ""), typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.transform.SetParent(root, true);
            var camera = cameraObject.GetComponent<Camera>();
            // Not drawn: it is there to select and align the Scene view to
            camera.enabled = false;
            PlaceCamera(camera, format);
        }

        // ------------------------------------------------------------------------------------------------------------ The cast

        private void Build()
        {
            var fx = CreateEffects();
            fx.Begin();

            // Red: a pumpkin-headed soldier leaping at the camera, the minigun firing past it
            var red = Soldier(SoldierModels.RedSkin, HeroHead, "Red Hero", AimClip, 0.2f);
            Stand(red.transform, RedPosition, RedFacing, RedLift, RedLean, RedRoll, 1, out var redFeet, out var redNormal);
            var redLook = weapons.LookOf(WeaponIds.Minigun);
            PoseArms(red.transform, new Vector3(0.28f, 1.22f, 0.5f), ToWorld(RedAimAt), true);
            FaceCamera(red.transform);
            var redGun = Hold(red.transform, redLook, ToWorld(RedAimAt));
            Shade(redFeet, redNormal, 1);
            Shots(fx, redGun.transform, redLook);

            // Red: the werewolf and the yeti beside him
            Mate(LeftHead, "Red Werewolf", WolfPosition, WolfFacing, WolfLift, WolfLean, WolfRoll, 0.1f, WeaponIds.Pistol, WolfAimAt, fx);
            Mate(RightHead, "Red Yeti", YetiPosition, YetiFacing, YetiLift, YetiLean, YetiRoll, 0.3f, WeaponIds.Pistol, YetiAimAt, fx);

            // Behind them a blue soldier's head bursts
            var victim = Soldier(SoldierModels.BlueSkin, null, "Blue Soldier Headless", RunClip, 0.35f);
            Stand(victim.transform, VictimPosition, new Vector3(-0.2f, 0, -1), VictimLift, VictimLean, VictimRoll, 1, out _, out _);
            var socket = SoldierBody.FindSocket(victim.transform, "head_top");
            Explosion(fx, socket.position + Vector3.up * 0.1f);

            fx.End();
            // A capture shows the finest level of every model, whatever the distance
            foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
                group.ForceLOD(0);
        }

        /// <summary>A red soldier with a monster head and a gun, in the aim pose, leaning at the lens.</summary>
        private void Mate(string head, string name, Vector3 position, Vector3 facing, float lift, float lean, float roll, float phase,
            string weapon, Vector3 aimAt, FxBuffer fx)
        {
            var soldier = Soldier(SoldierModels.RedSkin, head, name, AimClip, phase);
            Stand(soldier.transform, position, facing, lift, lean, roll, 1, out var feet, out var normal);
            PoseArms(soldier.transform, new Vector3(0.36f, 1.0f, 0.75f), ToWorld(aimAt), false);
            FaceCamera(soldier.transform);
            Hold(soldier.transform, weapons.LookOf(weapon), ToWorld(aimAt), 1.7f);
            Shade(feet, normal, 0.9f);
        }

        /// <summary>The team's soldier prefab, with a monster head (none when null), in a pose sampled from a clip.</summary>
        private GameObject Soldier(string skin, string head, string name, string clipPath, float phase)
        {
            var path = SoldierSkins.SkinPrefab(skin);
            var instance = Spawn(path, name);
            var headBone = instance.transform.Find(SoldierBody.Head);
            if (headBone != null)
                restHead[instance.transform] = Quaternion.Inverse(instance.transform.rotation) * headBone.rotation;
            if (head != null && HeadWear.Attach(instance.transform, head) == null)
                throw new InvalidOperationException($"the head {head} did not attach (build the heads first)");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip != null)
                clip.SampleAnimation(instance, clip.length * phase);
            return instance;
        }

        private GameObject Spawn(string path, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException($"{path} is missing (run the builders: Tools > SDF ...)");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            instance.name = name;
            // Only to be seen: a collider would be hit by the ground queries and the like
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            return instance;
        }

        /// <summary>
        /// Stands a character on the ground at a stage position, facing a direction (stage space), its body leaned forward (head towards
        /// the camera, for a positive <paramref name="lean"/>) and rolled sideways, lifted off the ground. Returns the ground's height.
        /// </summary>
        private float Stand(Transform body, Vector3 stagePosition, Vector3 facing, float lift, float lean, float roll, float scale,
            out Vector3 feet, out Vector3 normal)
        {
            var world = ToWorld(stagePosition);
            var ground = Ground(world, out normal);
            feet = new Vector3(world.x, ground, world.z);
            var face = Quaternion.LookRotation(ToWorldDirection(new Vector3(facing.x, 0, facing.z).normalized), Vector3.up);
            body.SetPositionAndRotation(feet + Vector3.up * lift * scale, face * Quaternion.Euler(lean, 0, roll));
            body.localScale = Vector3.one * scale;
            return ground;
        }

        // ------------------------------------------------------------------------------------------------------------ Poses

        /// <summary>
        /// The arms in code, over the sampled pose: the right hand out in front of the body at <paramref name="rightHand"/> (in the soldier's own
        /// space, root at its feet) holding a gun that points at <paramref name="aimAt"/>; the left hand on the gun's front for a two-handed
        /// gun, otherwise let down at the side. Two-bone inverse kinematics on Shoulder and Elbow, the elbows pointing down and out.
        /// </summary>
        private void PoseArms(Transform body, Vector3 rightHand, Vector3 aimAt, bool twoHands)
        {
            var rightPath = SoldierBody.HandR;
            var right = Chain(body, rightPath);
            var target = body.TransformPoint(rightHand);
            Solve(right, target, body.TransformDirection(new Vector3(0.7f, -1f, -0.3f)));
            var aim = (aimAt - target).normalized;
            var left = Chain(body, SoldierBody.HandL);
            if (twoHands)
                Solve(left, target + aim * 0.5f - Vector3.up * 0.04f, body.TransformDirection(new Vector3(-0.7f, -1f, -0.3f)));
            else
                Solve(left, body.TransformPoint(new Vector3(-0.3f, 0.8f, 0.12f)), body.TransformDirection(new Vector3(-0.7f, -1f, -0.5f)));
        }

        private static Transform[] Chain(Transform body, string handPath)
        {
            var hand = body.Find(handPath);
            var elbow = hand.parent;
            return new[] { elbow.parent, elbow, hand };
        }

        /// <summary>Bends a shoulder-elbow-hand chain so the hand is at the target (as far as the arm reaches), the elbow towards the hint.</summary>
        private static void Solve(Transform[] chain, Vector3 target, Vector3 hint)
        {
            var upper = chain[0];
            var elbow = chain[1];
            var hand = chain[2];
            var a = upper.position;
            var l1 = (elbow.position - a).magnitude;
            var l2 = (hand.position - elbow.position).magnitude;
            var toTarget = target - a;
            var d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(l1 - l2) + 0.01f, l1 + l2 - 0.005f);
            var axis = toTarget.normalized;
            var x = (l1 * l1 - l2 * l2 + d * d) / (2 * d);
            var h = Mathf.Sqrt(Mathf.Max(0, l1 * l1 - x * x));
            var pole = Vector3.ProjectOnPlane(hint, axis).normalized;
            var wantedElbow = a + axis * x + pole * h;
            upper.rotation = Quaternion.FromToRotation(elbow.position - a, wantedElbow - a) * upper.rotation;
            elbow.rotation = Quaternion.FromToRotation(hand.position - elbow.position, a + axis * d - elbow.position) * elbow.rotation;
        }

        /// <summary>Turns the head bone so the face looks at the cameras (the head upright, a little down at the lens from the low angle).</summary>
        private void FaceCamera(Transform body)
        {
            var bone = body.Find(SoldierBody.Head);
            if (bone == null || !restHead.TryGetValue(body, out var rest))
                return;
            var direction = ToWorld(LookAt) - bone.position;
            var flat = new Vector3(direction.x, 0, direction.z);
            var pitch = Mathf.Clamp(Mathf.Atan2(direction.y, flat.magnitude) * Mathf.Rad2Deg, -12f, 4f);
            var look = Quaternion.LookRotation(flat.normalized, Vector3.up) * Quaternion.Euler(-pitch, 0, 0);
            bone.rotation = look * rest;
        }

        // ------------------------------------------------------------------------------------------------------------ Guns

        /// <summary>
        /// A weapon in a soldier's right hand as <see cref="WeaponView"/> holds it: the model's grip on the grip socket, pointing along the aim.
        /// Returns the weapon's model.
        /// </summary>
        private GameObject Hold(Transform body, WeaponTableAsset.Look look, Vector3 aimAt, float scale = 1)
        {
            var grip = SoldierBody.FindSocket(body, "grip");
            if (grip == null)
                throw new InvalidOperationException("the soldier has no grip socket");
            var aim = Quaternion.LookRotation((aimAt - grip.position).normalized, Vector3.up);
            var gun = (GameObject)PrefabUtility.InstantiatePrefab(look.model, root);
            gun.name = "Gun " + look.model.name;
            foreach (var collider in gun.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            gun.transform.SetPositionAndRotation(grip.position - aim * look.grip * scale, aim);
            gun.transform.localScale = Vector3.one * scale;
            return gun;
        }

        private static Bounds BoundsOf(GameObject target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (renderers.Length == 0)
                return new Bounds(target.transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        // ------------------------------------------------------------------------------------------------------------ Effects

        /// <summary>The one draw of the game's effects (the quads of <see cref="FxBuffer"/>, SdfEffect.shader), at the world's origin.</summary>
        private FxBuffer CreateEffects()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(EffectMaterialPath)
                           ?? throw new InvalidOperationException($"{EffectMaterialPath} is missing (Tools > SDF Effects builds it)");
            var holder = new GameObject("Cover Effects", typeof(MeshFilter), typeof(MeshRenderer), typeof(FxBuffer));
            SceneManager.MoveGameObjectToScene(holder, scene);
            holder.transform.SetParent(root, false);
            var renderer = holder.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return holder.GetComponent<FxBuffer>();
        }

        /// <summary>The colour scaled past 1, so it reads as light through the tone mapping and catches the bloom (as the effects' own).</summary>
        private static Color Hdr(Color color, float intensity) => new Color(color.r * intensity, color.g * intensity, color.b * intensity, 1);

        /// <summary>
        /// The minigun's burst as <see cref="TracerGun"/> draws it, in the weapon's own tracer colour, but bigger than in the game (the
        /// camera is a few metres away): a muzzle flash (a glowing disc, a tongue along the barrel and four spokes), a fan of tracers
        /// streaking along the barrel's line, and brass casings thrown out of the gun's right side.
        /// </summary>
        private void Shots(FxBuffer fx, Transform gun, WeaponTableAsset.Look look)
        {
            var muzzle = gun.TransformPoint(look.muzzle);
            var forward = gun.forward;
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var up = Vector3.Cross(forward, right).normalized;
            var band = Hdr(look.tracer, 1.2f);
            var core = Hdr(new Color(1f, 0.97f, 0.8f), 2.6f);

            // The flash
            var size = 0.3f;
            var at = muzzle + forward * size * 0.6f;
            var flashBand = Hdr(look.tracer, 1.3f);
            var flashCore = Hdr(new Color(1f, 0.97f, 0.8f), 3.1f);
            fx.Glow(at, size * 0.55f, flashBand, flashCore, 0.55f);
            fx.Streak(at, at + forward * size * 2.2f, size * 0.5f, 0, flashBand, flashCore, 0.45f);
            for (var i = 0; i < 4; i++)
            {
                var spoke = Quaternion.AngleAxis(20 + i * 90, forward) * (up * 0.8f + forward * 0.6f);
                fx.Streak(at, at + spoke * size * 1.4f, size * 0.35f, 0, flashBand, flashCore, 0.4f);
            }

            // The tracers: slender streaks along slightly different lines, starting at different distances, none going past the lens
            var fan = new[] { new Vector2(0, 0), new Vector2(0.9f, 0.35f), new Vector2(-0.8f, 0.5f), new Vector2(0.4f, -0.8f), new Vector2(-0.5f, -0.45f),
                new Vector2(1.3f, -0.2f), new Vector2(-1.2f, -0.15f), new Vector2(0.15f, 1.1f), new Vector2(-0.2f, -1.2f), new Vector2(1.0f, 0.9f),
                new Vector2(-0.9f, 1.0f), new Vector2(0.6f, -1.3f) };
            const float width = 0.075f;
            for (var i = 0; i < fan.Length; i++)
            {
                var line = (forward + (right * fan[i].x + up * fan[i].y) * 0.06f).normalized;
                var start = 0.9f + (i * 0.37f) % 2.6f;
                var length = Mathf.Min(1.3f + (i % 3) * 0.55f, 4.4f - start);
                if (length < 0.4f)
                    continue;
                fx.Streak(muzzle + line * start, muzzle + line * (start + length), width * 0.25f, width, band, core, 0.5f);
            }

            // Brass casings, tumbling out of the right side of the gun and up: toon balls with the game's ink line, each with a glint
            var brass = new Color(0.95f, 0.72f, 0.25f);
            var brassShade = new Color(0.35f, 0.2f, 0.02f);
            var port = gun.position - forward * 0.05f + up * 0.05f;
            for (var i = 0; i < 9; i++)
            {
                var out1 = 0.22f + i * 0.17f + (i % 2) * 0.06f;
                var rise = 0.1f + (i % 4) * 0.13f + i * 0.04f;
                var back = (i % 3) * 0.14f - 0.1f;
                var p = port + right * out1 + up * rise - forward * back;
                fx.Lit(p, 0.034f, brass, brassShade);
                fx.Streak(p, p - (right * 0.9f + up * 0.35f).normalized * 0.12f, 0.03f, 0.012f, Hdr(brass, 1.0f), Hdr(Color.white, 1.5f), 0.4f, 0.8f);
            }
        }

        /// <summary>
        /// A head's burst as the game plays it for the Vampire (ticket 46's profile): deep purple mist puffs, small and scattered (not one
        /// disc), glowing purple glints, tiny bats flying outward, and the head's own pieces (ticket 51) thrown out, around the point of the neck.
        /// </summary>
        private void Explosion(FxBuffer fx, Vector3 centre)
        {
            var random = new FxRandom(11);
            var size = ExplosionRadius;
            var blood = MonsterHeads.ById(BurstHead).Value.Blood;
            var mist = Color.Lerp(blood, Color.black, 0.15f);
            var mistLight = Color.Lerp(blood, Color.white, 0.25f);
            var glint = Hdr(Color.Lerp(blood, Color.white, 0.45f), 1.5f);
            // The mist: round puffs of different sizes thrown out, a few paler, with the game's ink line
            for (var i = 0; i < Puffs; i++)
            {
                var direction = random.OnUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.9f;
                direction.z *= 0.6f;
                var at = centre + ToWorldDirection(direction) * size * random.Range(0.1f, 0.75f);
                var radius = size * random.Range(0.09f, 0.2f);
                fx.Lit(at, radius, i % 4 == 0 ? mistLight : mist, (i % 4 == 0 ? mistLight : mist) * 0.3f);
            }
            // Glints
            for (var i = 0; i < 30; i++)
            {
                var direction = random.OnUnitSphere;
                direction.z *= 0.6f;
                var at = centre + ToWorldDirection(direction) * size * random.Range(0.5f, 1.5f);
                fx.Glow(at, random.Range(0.04f, 0.09f), glint, Hdr(Color.white, 1.6f), 0.45f, random.Range(0.55f, 1f));
            }
            Bats_(centre, random);
            Debris(centre, random);
        }

        /// <summary>Tiny cartoon bats (the game's debris model) flying out of the burst, wings spread, turned along their flight.</summary>
        private void Bats_(Vector3 centre, FxRandom random)
        {
            var models = MonsterDebrisModels.Load();
            var prefab = models != null ? models.Prefab(DebrisKind.Bat) : null;
            if (prefab == null)
                throw new InvalidOperationException("the debris models have no bat (run MonsterDebrisBuilder)");
            for (var i = 0; i < Bats; i++)
            {
                var bat = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                bat.name = "Bat " + i;
                foreach (var collider in bat.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
                var local = random.OnUnitSphere;
                local.y = Mathf.Abs(local.y) * 0.8f + 0.1f;
                local.z *= 0.35f;
                var direction = rotation * local.normalized;
                bat.transform.localScale = Vector3.one * random.Range(3.2f, 4.2f);
                // The bat flies along its direction, its back up and a little rolled
                bat.transform.rotation = Quaternion.LookRotation(Vector3.Cross(direction, Vector3.up).normalized, Vector3.up) * Quaternion.Euler(0, 0, random.Range(-25f, 25f));
                bat.transform.position = centre + direction * ExplosionRadius * random.Range(0.8f, 1.5f);
            }
        }

        /// <summary>The pieces of a blue scout, the game's own exploding-model pieces, each moved out from where it fits in the model and turned.</summary>
        private void Debris(Vector3 centre, FxRandom random)
        {
            var models = MonsterHeadModels.Load();
            if (models == null || !models.TryGet(BurstHead, out var entry) || entry.pieces == null)
                throw new InvalidOperationException($"the head pieces of {BurstHead} are missing (build the heads first)");
            // The pieces are in the head's own frame, whose origin is the head socket: the burst's centre
            foreach (var source in entry.pieces)
            {
                if (source == null)
                    continue;
                var piece = (GameObject)PrefabUtility.InstantiatePrefab(source, root);
                piece.name = source.name;
                foreach (var collider in piece.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
                piece.transform.SetPositionAndRotation(centre, Quaternion.identity);
                piece.transform.localScale = Vector3.one * DebrisScale;
                var middle = BoundsOf(piece).center;
                var fromModel = middle - centre;
                var away = middle - centre;
                var outward = away.sqrMagnitude > 1e-4f ? away.normalized : random.OnUnitSphere;
                var local = Quaternion.Inverse(rotation) * (outward + random.OnUnitSphere * 0.5f).normalized;
                local.z *= 0.5f;
                var target = centre + rotation * local.normalized * ExplosionRadius * random.Range(0.45f, 1.0f);
                var turn = Quaternion.Euler(random.Range(-60f, 60f), random.Range(0f, 360f), random.Range(-60f, 60f));
                piece.transform.rotation = turn;
                piece.transform.position = target - turn * fromModel;
            }
        }

        private Mesh MeshOf(PrimitiveType type)
        {
            if (meshes.TryGetValue(type, out var mesh))
                return mesh;
            var sample = GameObject.CreatePrimitive(type);
            mesh = sample.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(sample);
            meshes[type] = mesh;
            return mesh;
        }

        /// <summary>A primitive shape in the stage that casts and receives no shadow.</summary>
        private GameObject Piece(PrimitiveType type, string name, Material material, Vector3 position, Quaternion rotationOfPiece, Vector3 scale)
        {
            var piece = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            SceneManager.MoveGameObjectToScene(piece, scene);
            piece.transform.SetParent(root, false);
            piece.GetComponent<MeshFilter>().sharedMesh = MeshOf(type);
            var renderer = piece.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            piece.transform.SetPositionAndRotation(position, rotationOfPiece);
            piece.transform.localScale = scale;
            return piece;
        }

        /// <summary>A soft dark blob on the ground under a character, lying on the slope.</summary>
        private void Shade(Vector3 feet, Vector3 normal, float size)
        {
            if (blob == null)
            {
                const int resolution = 64;
                blob = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                for (var y = 0; y < resolution; y++)
                for (var x = 0; x < resolution; x++)
                {
                    var distance = new Vector2((x + 0.5f) / resolution - 0.5f, (y + 0.5f) / resolution - 0.5f).magnitude * 2;
                    var alpha = Mathf.SmoothStep(1, 0, Mathf.Clamp01(distance));
                    blob.SetPixel(x, y, new Color(0, 0, 0, alpha));
                }
                blob.Apply();
                owned.Add(blob);
            }
            var shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, mainTexture = blob };
            material.SetColor("_Color", new Color(0, 0, 0, ShadowDarkness));
            owned.Add(material);
            var shadow = Piece(PrimitiveType.Quad, "Shadow", material, feet + normal * 0.04f, Quaternion.FromToRotation(Vector3.forward, normal),
                new Vector3(ShadowRadius * 2 * size, ShadowRadius * 2 * size, 1));
            shadow.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
