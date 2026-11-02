using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SdfEffects.Editor
{
    /// <summary>
    /// Builds the special-effects test bench, Assets/Scenes/SpecialEffects.unity, from code, anew each time; it is never edited by hand.
    /// On the forest floor under the mushroom scene's sun and sky (<see cref="MushroomScene.BuildLighting"/>):
    /// <list type="bullet">
    /// <item>the guns side by side at a firing height, each with a muzzle point: the electro gun (lightning), the pistol, minigun and
    /// sniper (tracers) and the blob gun (goop);</item>
    /// <item>their targets down range: the chanterelle scout for the lightning, a rock as the tracers' backstop, a rock for the goop,
    /// with colliders, and the amanita scout that bursts, with its pieces (content/models/characters/exploding/) waiting under it;</item>
    /// <item>a row of the twelve monster heads (ticket 46), each on a stand with its seven cut pieces waiting, which burst in their own
    /// way (<see cref="ExplodingMonster"/>, the profiles of <see cref="SdfHeads.MonsterExplosions"/>, the debris pool, four lightning guns for
    /// Frankenstein's sparks);</item>
    /// <item>the <see cref="SpecialEffectsBench"/> with the effects, the <see cref="FxBuffer"/> they draw into (SdfEffect.shader, one
    /// draw), the particles, and the pools of goop the blob gun uses, all made here so a shot instantiates nothing.</item>
    /// </list>
    /// Tools > SDF Effects > Rebuild Scene, or without a window: <c>Unity -batchmode -quit -projectPath Unity -executeMethod
    /// SdfEffects.Editor.SpecialEffectsScene.BuildInBatch</c>. The models come from the prefabs <see cref="MushroomBuilder"/> builds;
    /// stale ones are rebuilt first. The scene is added to the build settings after the others.
    /// </summary>
    public static class SpecialEffectsScene
    {
        public const string ScenePath = "Assets/Scenes/SpecialEffects.unity";
        public const string EffectShaderPath = "Assets/Effects/Shaders/SdfEffect.shader";
        public const string EffectMaterialPath = "Assets/Effects/Materials/Effect.mat";
        private const string GroundMaterialPath = "Assets/Mushrooms/Materials/Forest Floor.mat";

        public const string BenchName = "Bench";
        public const string ExplodingName = "Exploding Amanita";
        public const string MonstersName = "Monsters";

        /// <summary>Where the row of heads stands: its z, the spacing and the height of the heads' sockets.</summary>
        private const float RowZ = 16, RowSpacing = 1.5f, HeadHeight = 1.35f;

        /// <summary>Height of the guns' grips above the ground: held at about chest height.</summary>
        private const float GunHeight = 1.05f;

        /// <summary>The pieces the amanita scout bursts into, as MushroomBuilder names their prefabs.</summary>
        private const string PiecePrefix = "amanita-scout-";

        /// <summary>Gobs in flight and puddles on the ground at once, at most.</summary>
        private const int BlobPool = 9;
        private const int SplatPool = 14;

        [MenuItem("Tools/SDF Effects/Rebuild Scene")]
        private static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Build();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        public static void BuildInBatch()
        {
            try
            {
                Build();
            }
            catch (Exception exception)
            {
                Debug.LogError($"SDF effects: could not build {ScenePath}: {exception.Message}\n{exception.StackTrace}");
                EditorApplication.Exit(1);
            }
        }

        public static void Build()
        {
            RenderPipelineSetup.Ensure();
            MushroomBuilder.BuildStale();
            MonsterDebrisBuilder.Build();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = MushroomScene.CreateCamera(scene, "Main Camera", 52);
            // Behind the guns and to the left, above them, looking down range over all the lanes
            camera.transform.position = new Vector3(-4.8f, 3.1f, -3.6f);
            camera.transform.LookAt(new Vector3(0.3f, 0.8f, 4.6f));
            MushroomScene.BuildLighting(scene);
            Ground(scene);

            var root = Create(scene, BenchName);
            var targets = Child(root.transform, "Targets");
            var guns = Child(root.transform, "Guns");

            // Down range: the chanterelle for the lightning, a rock behind the tracers' lane, a rock for the goop to land on
            var chanterelle = Place(targets, "chanterelle-scout", new Vector3(-4.6f, 0, 5.6f), 200);
            AddCapsule(chanterelle);
            Place(targets, "rock1", new Vector3(-2.1f, 0, 13), 30);
            Place(targets, "rock3", new Vector3(1.3f, 0, 5.6f), 10);

            // The guns, each with its muzzle where its shots leave it (the models' coordinates, x mirrored as MushroomBuilder does)
            var electro = Gun(guns, "electro-gun", -4.6f, new Vector3(0, 0.13f, 0.34f));
            var pistol = Gun(guns, "pistol", -3.2f, new Vector3(0, 0.128f, 0.105f));
            var minigun = Gun(guns, "minigun", -2.1f, new Vector3(0, 0.135f, 1.0f));
            var sniper = Gun(guns, "sniper", -0.9f, new Vector3(0, 0.172f, 0.69f));
            var blob = Gun(guns, "blob-gun", 0.6f, new Vector3(0, 0.135f, 0.25f));
            // The bullet guns aim a little in or down, onto the backstop
            AimAt(pistol, new Vector3(-2.4f, 0.6f, 13));
            AimAt(minigun, new Vector3(-2.1f, 0.8f, 13));
            AimAt(sniper, new Vector3(-1.8f, 1.1f, 13));

            var effects = Child(root.transform, "Effects");
            var lightning = effects.gameObject.AddComponent<LightningGun>();
            lightning.muzzle = electro;
            lightning.target = chanterelle.GetComponent<Collider>();
            lightning.loopInterval = 2.4f;

            var pistolGun = Tracers(effects, "Pistol", pistol, speed: 110, length: 1.4f, width: 0.05f, new Color(1f, 0.55f, 0.1f), burst: 3, interval: 0.16f, spread: 0.6f);
            pistolGun.loopOffset = 0.5f;
            var minigunGun = Tracers(effects, "Minigun", minigun, speed: 140, length: 2.2f, width: 0.045f, new Color(1f, 0.3f, 0.05f), burst: 16, interval: 0.055f, spread: 1.6f);
            minigunGun.loopOffset = 0.9f;
            minigunGun.loopInterval = 3;
            minigunGun.flashSize = 0.13f;
            var sniperGun = Tracers(effects, "Sniper", sniper, speed: 320, length: 6, width: 0.065f, new Color(0.3f, 0.7f, 1f), burst: 1, interval: 0.1f, spread: 0);
            sniperGun.trail = 0.7f;
            sniperGun.loopOffset = 1.5f;
            sniperGun.loopInterval = 3;
            sniperGun.flashSize = 0.12f;

            var goop = effects.gameObject.AddComponent<GoopGun>();
            goop.muzzle = blob;
            goop.loopInterval = 2.6f;
            goop.loopOffset = 0.2f;
            Pools(root.transform, goop);

            var exploding = Exploding(root.transform, effects, camera.transform.position);
            var monsters = MonsterRow(root.transform, camera.transform.position);
            var debris = Child(root.transform, "Monster Debris").gameObject.AddComponent<MonsterDebris>();
            debris.autoRun = false;
            var bolts = new LightningGun[4];
            for (var i = 0; i < bolts.Length; i++)
            {
                var bolt = Child(root.transform, $"Monster Bolt {i + 1}");
                var gun = bolt.gameObject.AddComponent<LightningGun>();
                gun.muzzle = Child(bolt, "Muzzle");
                gun.width = 0.04f * Mathf.Pow(0.85f, i);
                bolts[i] = gun;
            }

            // The effects' one draw, and the bench that runs them all
            var fxObject = Child(root.transform, "Effects Draw").gameObject;
            fxObject.AddComponent<MeshFilter>();
            var renderer = fxObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = EffectMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var fx = fxObject.AddComponent<FxBuffer>();
            var particles = root.AddComponent<FxParticles>();
            var bench = root.AddComponent<SpecialEffectsBench>();
            bench.fx = fx;
            bench.particles = particles;
            bench.effects = new FxEffect[] { lightning, pistolGun, minigunGun, sniperGun, goop, exploding };
            bench.monsters = monsters;
            bench.debris = debris;
            bench.bolts = bolts;
            bench.monsterView = Child(root.transform, "Monster View");
            bench.monsterView.position = new Vector3(0, 2.1f, RowZ - 13);
            bench.monsterView.rotation = Quaternion.LookRotation(new Vector3(0, HeadHeight - 0.2f, RowZ) - bench.monsterView.position);

            MushroomBuilder.EnsureFolder(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            Debug.Log($"SDF effects: built {ScenePath}, {exploding.pieces.Length} pieces, {monsters.Length} monster heads, {goop.blobs.Length} gobs and {goop.splats.Length} puddles pooled");
        }

        private static GameObject Create(Scene scene, string name)
        {
            var gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject;
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        /// <summary>The forest floor of the mushroom scene, 40 m across, with a collider for the shots and the goop to land on.</summary>
        private static void Ground(Scene scene)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.localScale = new Vector3(4, 1, 4);
            ground.transform.position = new Vector3(0, 0, 6);
            ground.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
        }

        private static GameObject Prefab(string model) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/{model}.prefab")
            ?? throw new InvalidOperationException($"No prefab for {model}; mesh content/models with sdf unity first");

        private static GameObject Place(Transform parent, string model, Vector3 position, float yaw)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(model), parent);
            instance.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            return instance;
        }

        /// <summary>An upright capsule round a model, for the shots to hit.</summary>
        private static void AddCapsule(GameObject model)
        {
            var bounds = Bounds(model);
            var capsule = model.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.8f;
            capsule.height = bounds.size.y;
            capsule.center = model.transform.InverseTransformPoint(bounds.center);
        }

        /// <summary>A gun pointing down range (+z) at the firing height, and a child at its muzzle; returns the muzzle.</summary>
        private static Transform Gun(Transform parent, string model, float x, Vector3 muzzle)
        {
            var gun = Place(parent, model, new Vector3(x, GunHeight, 0), 0);
            var point = Child(gun.transform, "Muzzle");
            point.localPosition = new Vector3(-muzzle.x, muzzle.y, muzzle.z);
            return point;
        }

        /// <summary>Turns a gun so the line from its muzzle along its barrel passes through <paramref name="target"/>.</summary>
        private static void AimAt(Transform muzzle, Vector3 target)
        {
            var gun = muzzle.parent;
            gun.rotation = Quaternion.LookRotation(target - muzzle.position);
        }

        private static TracerGun Tracers(Transform effects, string label, Transform muzzle, float speed, float length, float width, Color band,
            int burst, float interval, float spread)
        {
            var gun = effects.gameObject.AddComponent<TracerGun>();
            gun.label = label;
            gun.muzzle = muzzle;
            gun.speed = speed;
            gun.tracerLength = length;
            gun.tracerWidth = width;
            gun.band = band;
            gun.burst = burst;
            gun.fireInterval = interval;
            gun.spread = spread;
            gun.loopInterval = 2.4f;
            return gun;
        }

        /// <summary>The gobs and puddles the blob gun reuses, inactive, round the three gob shapes and the two puddle shapes.</summary>
        private static void Pools(Transform root, GoopGun goop)
        {
            var pool = Child(root, "Goop");
            var blobs = new List<Transform>();
            for (var i = 0; i < BlobPool; i++)
                blobs.Add(Pooled(pool, $"goop-blob{i % 3}"));
            var splats = new List<Transform>();
            for (var i = 0; i < SplatPool; i++)
                splats.Add(Pooled(pool, $"goop-splat{i % 2}"));
            goop.blobs = blobs.ToArray();
            goop.splats = splats.ToArray();
        }

        private static Transform Pooled(Transform parent, string model)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(model), parent);
            instance.SetActive(false);
            return instance.transform;
        }

        /// <summary>
        /// The amanita scout and, under an object with its transform, an instance of each of its pieces at the same place, inactive;
        /// each piece's middle and size for the burst come from its bounds.
        /// </summary>
        private static ExplodingMonster Exploding(Transform root, Transform effects, Vector3 camera)
        {
            // On its own to the right of the lanes, room round it for the pieces, turned to face the camera
            var holder = Child(root, ExplodingName);
            var position = new Vector3(4, 0, 4.4f);
            var facing = camera - position;
            facing.y = 0;
            holder.SetLocalPositionAndRotation(position, Quaternion.LookRotation(facing) * Quaternion.Euler(0, -25, 0));
            var intact = Place(holder, "amanita-scout", Vector3.zero, 0);
            var piecesRoot = Child(holder, "Pieces");

            var prefabs = Directory.GetFiles(MushroomBuilder.PrefabFolder, PiecePrefix + "*.prefab")
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !Path.GetFileNameWithoutExtension(p).EndsWith("_LOD"))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
            if (prefabs.Length == 0)
                throw new InvalidOperationException("No exploding pieces; mesh content/models/characters/exploding with sdf unity first");
            // Each piece's levels of detail change where the intact model's do (its size and middle), not by the piece's own smaller size,
            // so at the swap every piece shows the level the model showed
            var intactLevels = intact.GetComponent<LODGroup>();
            var pieces = new List<Transform>();
            var centers = new List<Vector3>();
            var radii = new List<float>();
            var loose = new List<bool>();
            foreach (var path in prefabs)
            {
                var piece = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), piecesRoot);
                var bounds = LocalBounds(piece, piecesRoot);
                if (intactLevels != null && piece.TryGetComponent<LODGroup>(out var levels))
                {
                    levels.size = intactLevels.size;
                    levels.localReferencePoint = intactLevels.localReferencePoint;
                    // and at the model's heights, which the scout's eyes and hands (uses with LOD groups of their own) also keep to
                    var own = levels.GetLODs();
                    var model = intactLevels.GetLODs();
                    for (var level = 0; level < own.Length && level < model.Length; level++)
                        own[level].screenRelativeTransitionHeight = model[level].screenRelativeTransitionHeight;
                    levels.SetLODs(own);
                }
                pieces.Add(piece.transform);
                centers.Add(bounds.center);
                radii.Add(Mathf.Min(bounds.extents.x, Mathf.Min(bounds.extents.y, bounds.extents.z)));
                var name = Path.GetFileNameWithoutExtension(path);
                loose.Add(name.Contains("-eye") || name.Contains("-hand"));
                piece.SetActive(false);
            }
            piecesRoot.gameObject.SetActive(false);

            var exploding = effects.gameObject.AddComponent<ExplodingMonster>();
            exploding.intact = intact;
            exploding.piecesRoot = piecesRoot;
            exploding.pieces = pieces.ToArray();
            exploding.centers = centers.ToArray();
            exploding.radii = radii.ToArray();
            exploding.loose = loose.ToArray();
            exploding.intactSurfaces = intact.GetComponentsInChildren<Renderer>(true).Where(r => r.name != MushroomBuilder.OutlineObjectName).ToArray();
            exploding.outlines = pieces.SelectMany(p => p.GetComponentsInChildren<Renderer>(true)).Where(r => r.name == MushroomBuilder.OutlineObjectName).ToArray();
            exploding.loopInterval = 5;
            exploding.loopOffset = 1.2f;
            return exploding;
        }

        /// <summary>
        /// The twelve heads in a row down range: each on a stand, facing back toward the bench, with an instance of each of its seven cut pieces
        /// (content/models/characters/head-pieces/, ticket 51) in the same place under it, inactive, as for the amanita. Heads that are not
        /// built or have no pieces are left out.
        /// </summary>
        private static ExplodingMonster[] MonsterRow(Transform root, Vector3 camera)
        {
            var row = Child(root, MonstersName);
            var heads = SdfHeads.MonsterHeads.All.Where(h => h.Built).ToArray();
            var made = new List<ExplodingMonster>();
            for (var n = 0; n < heads.Length; n++)
            {
                var head = heads[n];
                var x = (n - (heads.Length - 1) / 2f) * RowSpacing;
                var holder = Child(row, head.Id);
                holder.SetLocalPositionAndRotation(new Vector3(x, HeadHeight, RowZ), Quaternion.LookRotation(Vector3.back));
                var intact = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(head.Id + "_fast"), holder);
                intact.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                var piecesRoot = Child(holder, "Pieces");
                var pieces = new List<Transform>();
                var centers = new List<Vector3>();
                var radii = new List<float>();
                for (var i = 0; ; i++)
                {
                    var path = $"{MushroomBuilder.PrefabFolder}/{head.Id}-head-piece{i}.prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                        break;
                    var piece = (GameObject)PrefabUtility.InstantiatePrefab(prefab, piecesRoot);
                    var bounds = LocalBounds(piece, piecesRoot);
                    pieces.Add(piece.transform);
                    centers.Add(bounds.center);
                    radii.Add(Mathf.Min(bounds.extents.x, Mathf.Min(bounds.extents.y, bounds.extents.z)));
                    piece.SetActive(false);
                }
                piecesRoot.gameObject.SetActive(false);
                if (pieces.Count == 0)
                    continue;

                // The stand under it, no collider: the neck, the soldier's collar
                var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stand.name = $"Stand {head.Id}";
                stand.transform.SetParent(row, false);
                stand.transform.localPosition = new Vector3(x, (HeadHeight - 0.3f) / 2, RowZ);
                stand.transform.localScale = new Vector3(0.2f, (HeadHeight - 0.3f) / 2, 0.2f);
                UnityEngine.Object.DestroyImmediate(stand.GetComponent<Collider>());
                stand.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

                var exploding = holder.gameObject.AddComponent<ExplodingMonster>();
                exploding.headId = head.Id;
                exploding.intact = intact;
                exploding.piecesRoot = piecesRoot;
                exploding.pieces = pieces.ToArray();
                exploding.centers = centers.ToArray();
                exploding.radii = radii.ToArray();
                exploding.loose = new bool[pieces.Count];
                exploding.origin = Vector3.zero;
                exploding.groundLocalY = -HeadHeight;
                exploding.intactSurfaces = intact.GetComponentsInChildren<Renderer>(true).Where(r => r.name != MushroomBuilder.OutlineObjectName).ToArray();
                exploding.outlines = pieces.SelectMany(p => p.GetComponentsInChildren<Renderer>(true)).Where(r => r.name == MushroomBuilder.OutlineObjectName).ToArray();
                exploding.linger = 2.6f;
                exploding.resetAfter = 5;
                exploding.loopInterval = 5;
                made.Add(exploding);
            }
            return made.ToArray();
        }

        /// <summary>The bounds of a model's finest level in another transform's coordinates, from its meshes.</summary>
        private static Bounds LocalBounds(GameObject model, Transform space)
        {
            var filters = model.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.name != MushroomBuilder.OutlineObjectName && !f.name.Contains("_LOD"))
                .ToArray();
            Bounds? total = null;
            foreach (var filter in filters)
            {
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
            return total ?? new Bounds();
        }

        private static Bounds Bounds(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1))
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        /// <summary>The effects' material on SdfEffect.shader, created on first use.</summary>
        private static Material EffectMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(EffectMaterialPath);
            if (material != null)
                return material;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(EffectShaderPath)
                         ?? throw new InvalidOperationException($"The shader {EffectShaderPath} is missing");
            material = new Material(shader) { name = "Effect" };
            MushroomBuilder.EnsureFolder(Path.GetDirectoryName(EffectMaterialPath));
            AssetDatabase.CreateAsset(material, EffectMaterialPath);
            return material;
        }

        private static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Any(s => s.path == ScenePath))
                return;
            EditorBuildSettings.scenes = scenes.Append(new EditorBuildSettingsScene(ScenePath, true)).ToArray();
        }
    }
}
