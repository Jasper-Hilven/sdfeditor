using System;
using System.Collections.Generic;
using System.Linq;
using SdfBattle;
using SdfEffects;
using SdfMushrooms.Editor;
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
        public const float YawDegrees = 90;
        /// <summary>The stage's middle on the level's ground (level x, z), the middle of the field.</summary>
        public static readonly Vector2 Centre = new Vector2(0, 0);

        // The red hero: where he stands (the ground under him), how high he leaps, how far his cap leans towards the camera and tips sideways
        // (a negative roll tips the cap to the picture's left)
        private static readonly Vector3 RedPosition = new Vector3(0, 0, 0);
        private const float RedLift = 0.6f, RedLean = 18, RedRoll = -9;
        /// <summary>The way he faces (a direction in the stage's space); his gun aims at <see cref="RedAimAt"/>.</summary>
        private static readonly Vector3 RedFacing = new Vector3(-0.1f, 0, -1);
        /// <summary>Left of the lens and a little low: the minigun points past the camera, so its tracers streak across the foreground.</summary>
        private static readonly Vector3 RedAimAt = new Vector3(-2.2f, 0.7f, -6.5f);
        /// <summary>The hero's middle: the point of him the formats' cameras place on the picture (<see cref="CoverFormat.Anchor"/>).</summary>
        public static readonly Vector3 HeroCentre = new Vector3(0, 1.4f, 0);

        // The two blue scouts dive aside, one each way, looking back at the camera, hands flung out
        private static readonly Vector3 BlueLeftPosition = new Vector3(-2.0f, 0, 3.8f);
        private static readonly Vector3 BlueRightPosition = new Vector3(1.9f, 0, 3.4f);
        private const float BlueLeftLift = 0.55f, BlueLeftLean = 10, BlueLeftRoll = -38;
        private const float BlueRightLift = 0.35f, BlueRightLean = 8, BlueRightRoll = 34;

        // The explosion behind them: where its middle is (in the stage's space), its radius in metres, and the pieces of a blue scout
        // blown out of it (their size on top of the game's)
        private static readonly Vector3 ExplosionCentre = new Vector3(1.9f, 4.8f, 9.0f);
        private const float ExplosionRadius = 2.8f;
        private const float DebrisScale = 1.3f;

        /// <summary>The soft shadow under a character: its radius in metres at scale 1, and how dark.</summary>
        private const float ShadowRadius = 0.95f, ShadowDarkness = 0.55f;

        // ------------------------------------------------------------------------------------------------------------ The stage

        private const string RedScoutPrefab = MushroomBuilder.PrefabFolder + "/amanita-scout.prefab";
        private const string BlueScoutPrefab = MushroomBuilder.PrefabFolder + "/chanterelle-scout.prefab";
        private const string BluePiecePrefix = "chanterelle-scout-";
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
            var cameraPoint = ToWorld(CoverCapture.Formats[0].CameraPosition);
            var fx = CreateEffects();
            fx.Begin();

            // Red: leaping at the camera, eyes on it, the minigun firing past it
            var red = Spawn(RedScoutPrefab, "Red Hero");
            Stand(red.transform, RedPosition, RedFacing, RedLift, RedLean, RedRoll, 1, out var redFeet, out var redNormal);
            LookAtPoint(Eyes(red.transform), cameraPoint);
            var redLook = weapons.LookOf(WeaponIds.Minigun);
            var redGun = Hold(red.transform, Hands(red.transform), redLook, redLook.model, ToWorld(RedAimAt), 1, true);
            Shade(redFeet, redNormal, 1);
            Shots(fx, redGun.transform, redLook);

            // Blue: two scouts diving aside, looking back, hands flung out
            Dive(BlueScoutPrefab, "Blue Scout Left", BlueLeftPosition, new Vector3(0.3f, 0, -1), BlueLeftLift, BlueLeftLean, BlueLeftRoll, cameraPoint);
            Dive(BlueScoutPrefab, "Blue Scout Right", BlueRightPosition, new Vector3(-0.3f, 0, -1), BlueRightLift, BlueRightLean, BlueRightRoll, cameraPoint);

            // The explosion behind them
            Explosion(fx, ToWorld(ExplosionCentre));

            fx.End();
            // A capture shows the finest level of every model, whatever the distance
            foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
                group.ForceLOD(0);
        }

        /// <summary>A scout in the air, leaning and rolled to one side, looking at a point, his hands thrown out to his sides and up.</summary>
        private void Dive(string prefab, string name, Vector3 position, Vector3 facing, float lift, float lean, float roll, Vector3 lookAt)
        {
            var scout = Spawn(prefab, name);
            Stand(scout.transform, position, facing, lift, lean, roll, 1, out var feet, out var normal);
            LookAtPoint(Eyes(scout.transform), lookAt);
            foreach (var hand in Hands(scout.transform))
            {
                var rest = scout.transform.InverseTransformPoint(hand.position);
                var side = rest.x >= 0 ? 1 : -1;
                hand.position = scout.transform.TransformPoint(rest + new Vector3(side * 0.4f, 0.35f, 0.1f));
            }
            Shade(feet, normal, 0.85f);
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
        /// Stands a character on the ground at a stage position, facing a direction (stage space), its body leaned forward (cap towards
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

        // ------------------------------------------------------------------------------------------------------------ Eyes, hands, guns

        /// <summary>A part of a character by its name in the prefab (eye.0, hand.1 ...), whichever level of detail it is under.</summary>
        private static Transform Part(Transform body, string name) =>
            body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        private static Transform[] Eyes(Transform body) => new[] { Part(body, "eye.0"), Part(body, "eye.1") }.Where(t => t != null).ToArray();

        private static Transform[] Hands(Transform body) => new[] { Part(body, "hand.0"), Part(body, "hand.1") }.Where(t => t != null).ToArray();

        /// <summary>The floating eyes turned to a world point; they look along their +z at rest.</summary>
        private static void LookAtPoint(Transform[] eyes, Vector3 point)
        {
            foreach (var eye in eyes)
                eye.rotation = Quaternion.LookRotation((point - eye.position).normalized, Vector3.up);
        }

        /// <summary>
        /// A weapon in a character's hands as <see cref="WeaponView"/> holds it in a scout's: the grip a little in front of the hand,
        /// the weapon pointing along the aim, at the size the weapon table gives times <paramref name="scale"/> (Prime's). A two-handed
        /// weapon has the other hand brought up to its fore-end. The hands are first moved forward of where they rest, as when aiming.
        /// Returns the weapon's model.
        /// </summary>
        private GameObject Hold(Transform body, Transform[] hands, WeaponTableAsset.Look look, GameObject model, Vector3 aimAt, float scale, bool twoHanded)
        {
            // The hand on the body's +x side holds the grip
            var ordered = hands.OrderByDescending(h => body.InverseTransformPoint(h.position).x).ToArray();
            var grip = ordered[0];
            var rest = body.InverseTransformPoint(grip.position);
            grip.position = body.TransformPoint(rest + new Vector3(0, 0.12f, 0.4f));

            var aim = Quaternion.LookRotation((aimAt - grip.position).normalized, Vector3.up);
            var reach = Reach(grip);
            var gun = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
            gun.name = "Gun " + model.name;
            foreach (var collider in gun.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            var size = look.handScale * scale;
            gun.transform.SetPositionAndRotation(grip.position + aim * Vector3.forward * reach - aim * (look.grip * size), aim);
            gun.transform.localScale = Vector3.one * size;
            grip.rotation = aim;
            if (twoHanded && ordered.Length > 1)
            {
                ordered[1].position = gun.transform.TransformPoint(look.grip + new Vector3(0, -0.02f, 0.3f));
                ordered[1].rotation = aim;
            }
            return gun;
        }

        /// <summary>How far in front of a hand's middle a grip goes, as in the game: most of the way to the front of the hand.</summary>
        private static float Reach(Transform hand)
        {
            var renderers = hand.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 0.12f * hand.lossyScale.x;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return (bounds.extents.x + bounds.extents.y + bounds.extents.z) / 3 * 0.8f;
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
            var size = 0.42f;
            var at = muzzle + forward * size * 0.6f;
            var flashBand = Hdr(look.tracer, 1.3f);
            var flashCore = Hdr(new Color(1f, 0.97f, 0.8f), 3.1f);
            fx.Glow(at, size * 1.05f, Hdr(look.tracer, 0.7f), flashBand, 0.2f, 0.55f);
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
        /// A toon burst as <see cref="ExplodingMushroom"/> makes one, frozen at its first instants and scaled to
        /// <see cref="ExplosionRadius"/>: a cel-painted "pow" (a glowing disc and a ring of rays), balls of cream and cap-red smoke, a
        /// cloud of glowing spores, and the pieces of a blue scout (the game's own exploding-model pieces) thrown out of it.
        /// </summary>
        private void Explosion(FxBuffer fx, Vector3 centre)
        {
            var random = new FxRandom(11);
            var size = ExplosionRadius;
            var cream = new Color(0.95f, 0.9f, 0.78f);
            var capRed = new Color(0.85f, 0.18f, 0.08f);
            var spore = new Color(0.85f, 1f, 0.5f);
            var flashBand = Hdr(new Color(1, 0.8f, 0.45f), 1.3f);
            var flashCore = Hdr(new Color(1, 0.97f, 0.85f), 2.6f);

            // The disc first, so the smoke is drawn over it
            fx.Glow(centre, size * 1.0f, flashBand, flashCore, 0.5f);
            // The smoke: bigger toon balls on the outside, cream mostly, every third cap-red
            for (var i = 0; i < 16; i++)
            {
                var direction = random.OnUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.8f;
                direction.z *= 0.5f;
                var at = centre + ToWorldDirection(Vector3.Scale(direction, new Vector3(0.62f, 0.62f, 0.62f))) * size;
                var radius = size * random.Range(0.17f, 0.3f);
                fx.Lit(at, radius, i % 3 == 0 ? capRed : cream, i % 3 == 0 ? new Color(0.25f, 0.03f, 0) : new Color(0.2f, 0.16f, 0.08f));
            }
            // The rays out of the disc, mostly across the picture
            for (var i = 0; i < 18; i++)
            {
                var ray = random.OnUnitSphere;
                ray.z *= 0.45f;
                ray.Normalize();
                var world = ToWorldDirection(ray);
                fx.Streak(centre + world * size * 0.55f, centre + world * size * random.Range(1.05f, 1.4f), size * 0.045f, 0, flashBand, flashCore, 0.45f);
            }
            // Glowing spores drifting out
            var sporeCore = Hdr(Color.white, 1.6f);
            var sporeBand = Hdr(spore, 1.4f);
            for (var i = 0; i < 44; i++)
            {
                var direction = random.OnUnitSphere;
                direction.z *= 0.6f;
                var at = centre + ToWorldDirection(direction) * size * random.Range(0.7f, 1.6f) + Vector3.up * size * 0.1f;
                fx.Glow(at, random.Range(0.05f, 0.11f), sporeBand, sporeCore, 0.45f, random.Range(0.55f, 1f));
            }

            Debris(centre, random);
        }

        /// <summary>The pieces of a blue scout, the game's own exploding-model pieces, each moved out from where it fits in the model and turned.</summary>
        private void Debris(Vector3 centre, FxRandom random)
        {
            var paths = AssetDatabase.FindAssets(BluePiecePrefix + " t:Prefab", new[] { MushroomBuilder.PrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p =>
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(p);
                    return name.StartsWith(BluePiecePrefix) && name.IndexOf('_') < 0;
                })
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
            // A scout's origin is at his feet, his middle about 0.8 m up
            var modelOrigin = centre - Vector3.up * 0.8f * DebrisScale;
            foreach (var path in paths)
            {
                var piece = Spawn(path, System.IO.Path.GetFileNameWithoutExtension(path));
                piece.transform.SetPositionAndRotation(modelOrigin, Quaternion.identity);
                piece.transform.localScale = Vector3.one * DebrisScale;
                var middle = BoundsOf(piece).center;
                var fromModel = middle - modelOrigin;
                var away = middle - centre;
                var outward = away.sqrMagnitude > 1e-4f ? away.normalized : random.OnUnitSphere;
                // Mostly across the picture, less towards or away from the camera
                var local = Quaternion.Inverse(rotation) * (outward + random.OnUnitSphere * 0.4f).normalized;
                local.z *= 0.5f;
                var target = centre + rotation * local.normalized * ExplosionRadius * random.Range(0.55f, 1.15f);
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
