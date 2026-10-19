using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// The default scene, Assets/Scenes/Mushrooms.unity: one instance of each model (its best variant, see
    /// <see cref="MushroomBuilder.SceneModels"/>) on a forest floor, a row per group (characters, rocks, trees, weapons) with the lower
    /// rows in front, under a procedural sky and a sun, with ACES tone mapping, framed by the camera. Created the first time; after
    /// that only touched when what it should show or where changes, because the instances follow their prefabs by themselves.
    /// </summary>
    public static class MushroomScene
    {
        public const string MushroomsRootName = "Mushrooms";
        public const string SkySettingsName = "Sky";
        private const string VolumeProfilePath = "Assets/Scenes/Mushrooms/Post Processing.asset";
        private const string GroundMaterialPath = "Assets/Mushrooms/Materials/Forest Floor.mat";
        private const string SkyMaterialPath = "Assets/Mushrooms/Materials/Sky.mat";
        private const float Gap = 0.35f;
        private const float RowGap = 0.8f;

        /// <summary>
        /// Where the sun shines, from the front, the side and about 32° up: low enough to reach under the caps, so the stem below a
        /// cap and the faces are lit like the rest of the stem rather than lost in the cap's shadow.
        /// </summary>
        private static readonly Vector3 SunDirection = new Vector3(4, -4, -5).normalized;

        /// <summary>The first scenes had a sun 50° up, which left the stems under the caps dark; it is moved to the current one.</summary>
        private static readonly Vector3 OldSunDirection = new Vector3(4, -6, -3).normalized;

        /// <summary>
        /// Groups of models that are not shown: the pieces a model bursts into and the effects' goop, which the special-effects scene
        /// (Assets/Scenes/SpecialEffects.unity) uses instead.
        /// </summary>
        private static readonly string[] HiddenGroups = { "characters/exploding", "effects" };

        public static void Ensure(IReadOnlyList<(string group, string prefab)> models)
        {
            models = models.Where(m => !HiddenGroups.Contains(m.group)).ToArray();
            var prefabPaths = models.Select(m => m.prefab).ToArray();
            var existing = SceneManager.GetSceneByPath(MushroomBuilder.ScenePath);
            var openedHere = false;
            Scene scene;
            if (existing.IsValid() && existing.isLoaded)
            {
                scene = existing;
            }
            else if (File.Exists(MushroomBuilder.ScenePath))
            {
                if (!NeedsChange(prefabPaths))
                    return;
                scene = EditorSceneManager.OpenScene(MushroomBuilder.ScenePath, NothingWorthKeepingOpen() ? OpenSceneMode.Single : OpenSceneMode.Additive);
                openedHere = EditorSceneManager.sceneCount > 1;
            }
            else
            {
                MushroomBuilder.EnsureFolder(Path.GetDirectoryName(MushroomBuilder.ScenePath));
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NothingWorthKeepingOpen() ? NewSceneMode.Single : NewSceneMode.Additive);
                openedHere = EditorSceneManager.sceneCount > 1;
                BuildEnvironment(scene);
            }

            var changed = UpdateMushrooms(scene, models) | UpdateSun(scene) | EnsureSkySettings(scene);
            if (changed || string.IsNullOrEmpty(scene.path))
            {
                FrameCamera(scene);
                EditorSceneManager.SaveScene(scene, MushroomBuilder.ScenePath);
            }
            AddToBuildSettings();
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }

        private static bool NothingWorthKeepingOpen()
        {
            if (EditorSceneManager.sceneCount != 1)
                return false;
            var active = SceneManager.GetActiveScene();
            return string.IsNullOrEmpty(active.path) && !active.isDirty;
        }

        /// <summary>
        /// Whether the saved scene lacks an instance of a prefab, or has one of a prefab it should not show (gone, or a worse variant),
        /// without opening it. A model that only moved to another group is placed the next time the scene is open or changes.
        /// </summary>
        private static bool NeedsChange(IReadOnlyList<string> prefabPaths)
        {
            var dependencies = AssetDatabase.GetDependencies(MushroomBuilder.ScenePath, false);
            var used = dependencies.Where(d => d.EndsWith(".prefab")).ToHashSet();
            return prefabPaths.Any(p => !used.Contains(p)) || used.Any(u => u.StartsWith(MushroomBuilder.PrefabFolder + "/") && !prefabPaths.Contains(u));
        }

        /// <summary>
        /// Gives a saved scene from before the sky its <see cref="SkySettings"/> (when its script is not among the scene's dependencies),
        /// with the identity tone, leaving everything else as it is: the rows are not laid out again, nor the camera framed.
        /// </summary>
        public static void EnsureSkySettings()
        {
            if (!File.Exists(MushroomBuilder.ScenePath)
                || AssetDatabase.GetDependencies(MushroomBuilder.ScenePath, false).Any(d => Path.GetFileName(d) == nameof(SkySettings) + ".cs"))
                return;
            var existing = SceneManager.GetSceneByPath(MushroomBuilder.ScenePath);
            var openedHere = !existing.IsValid() || !existing.isLoaded;
            var scene = openedHere
                ? EditorSceneManager.OpenScene(MushroomBuilder.ScenePath, NothingWorthKeepingOpen() ? OpenSceneMode.Single : OpenSceneMode.Additive)
                : existing;
            if (EnsureSkySettings(scene))
                EditorSceneManager.SaveScene(scene, MushroomBuilder.ScenePath);
            if (openedHere && EditorSceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(scene, true);
        }

        /// <summary>
        /// Makes the root hold an object per group with an instance of each of its prefabs, and lays them out: each group a row from
        /// left to right in name order, centred on x = 0, the rows one behind the other from the lowest at the front to the tallest at
        /// the back, so nothing hides behind something taller. Returns whether anything changed.
        /// </summary>
        private static bool UpdateMushrooms(Scene scene, IReadOnlyList<(string group, string prefab)> models)
        {
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MushroomsRootName);
            if (root == null)
            {
                root = new GameObject(MushroomsRootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            var changed = false;
            var wanted = models.Select(m => m.prefab).ToHashSet();
            var instances = new Dictionary<string, GameObject>();
            foreach (var child in root.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject).Where(PrefabUtility.IsOutermostPrefabInstanceRoot).ToArray())
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(child);
                var path = source != null ? AssetDatabase.GetAssetPath(source) : null;
                if (path == null || !wanted.Contains(path) || instances.ContainsKey(path))
                {
                    Object.DestroyImmediate(child);
                    changed = true;
                }
                else
                {
                    instances[path] = child;
                }
            }
            foreach (var path in wanted.Where(p => !instances.ContainsKey(p)))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                instances[path] = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                changed = true;
            }

            // An object per group, at the origin, holding its instances in name order
            var groups = models.GroupBy(m => m.group).ToArray();
            var rows = new List<GameObject[]>();
            for (var g = 0; g < groups.Length; g++)
            {
                var name = GroupObjectName(groups[g].Key);
                var groupObject = root.transform.Cast<Transform>().FirstOrDefault(t => t.name == name && !PrefabUtility.IsPartOfPrefabInstance(t))?.gameObject;
                if (groupObject == null)
                {
                    groupObject = new GameObject(name);
                    groupObject.transform.SetParent(root.transform, false);
                    changed = true;
                }
                changed |= Place(groupObject.transform, Vector3.zero);
                if (groupObject.transform.GetSiblingIndex() != g)
                {
                    groupObject.transform.SetSiblingIndex(g);
                    changed = true;
                }
                var row = groups[g].Select(m => instances[m.prefab]).ToArray();
                for (var i = 0; i < row.Length; i++)
                {
                    if (row[i].transform.parent != groupObject.transform)
                    {
                        row[i].transform.SetParent(groupObject.transform, false);
                        changed = true;
                    }
                    if (row[i].transform.GetSiblingIndex() != i)
                    {
                        row[i].transform.SetSiblingIndex(i);
                        changed = true;
                    }
                }
                rows.Add(row);
            }
            // Group objects left without instances, e.g. after a group was renamed
            foreach (var empty in root.transform.Cast<Transform>().Where(t => t.childCount == 0 && !PrefabUtility.IsPartOfPrefabInstance(t)).ToArray())
            {
                Object.DestroyImmediate(empty.gameObject);
                changed = true;
            }

            // Rows from the front (+z, towards the camera) to the back, lowest first
            var z = 0f;
            var first = true;
            foreach (var row in rows.OrderBy(r => r.Max(o => Bounds(o).size.y)))
            {
                var depth = row.Max(o => Bounds(o).size.z);
                z -= first ? 0 : depth / 2;
                first = false;
                var widths = row.Select(o => Bounds(o).size.x).ToArray();
                var x = (widths.Sum() + Gap * (row.Length - 1)) / 2;
                for (var i = 0; i < row.Length; i++)
                {
                    // The camera looks down -z, so its left is +x; each model's own centre goes on the row's line
                    var bounds = Bounds(row[i]);
                    var position = row[i].transform.position;
                    changed |= Place(row[i].transform, new Vector3(x - widths[i] / 2 - bounds.center.x + position.x, 0, z - bounds.center.z + position.z));
                    x -= widths[i] + Gap;
                }
                z -= depth / 2 + RowGap;
            }
            return changed;
        }

        /// <summary>The model instances under the root, row by row: the children of its group objects.</summary>
        public static IEnumerable<Transform> Instances(GameObject root) =>
            root.transform.Cast<Transform>().SelectMany(group => group.Cast<Transform>());

        /// <summary>"weapons" is shown as Weapons; models straight in the Generated folder go under Other.</summary>
        private static string GroupObjectName(string group) =>
            group.Length == 0 ? "Other" : char.ToUpperInvariant(group[0]) + group.Substring(1).Replace('/', ' ');

        /// <summary>Moves the object to a local position unless it is already there, so an unchanged scene is not saved again.</summary>
        private static bool Place(Transform transform, Vector3 localPosition)
        {
            if ((transform.localPosition - localPosition).sqrMagnitude < 1e-6f)
                return false;
            transform.localPosition = localPosition;
            return true;
        }

        private static bool UpdateSun(Scene scene)
        {
            var sun = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Sun");
            if (sun == null || Vector3.Angle(sun.transform.forward, OldSunDirection) > 0.5f)
                return false;
            sun.transform.rotation = Quaternion.LookRotation(SunDirection);
            return true;
        }

        /// <summary>Gives a scene from before the sky its <see cref="SkySettings"/>, with the identity (day) tone; returns whether it did.</summary>
        private static bool EnsureSkySettings(Scene scene)
        {
            if (scene.GetRootGameObjects().Any(g => g.GetComponent<SkySettings>() != null))
                return false;
            var sky = new GameObject(SkySettingsName);
            SceneManager.MoveGameObjectToScene(sky, scene);
            sky.AddComponent<SkySettings>();
            return true;
        }

        private static Bounds Bounds(GameObject gameObject)
        {
            var renderers = gameObject.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(gameObject.transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1))
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        /// <summary>Points the camera at all the rows from the front and above, close enough that they fill the view.</summary>
        private static void FrameCamera(Scene scene)
        {
            var camera = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Camera>()).FirstOrDefault(c => c != null);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MushroomsRootName);
            if (camera == null || root == null || root.transform.childCount == 0)
                return;
            var bounds = Bounds(root);
            var target = bounds.center;
            var aspect = 16f / 9f;
            // From higher up than for a single row, so the rows behind show over the ones in front
            var direction = new Vector3(0, 0.45f, 1).normalized;
            var halfHeight = Mathf.Max(bounds.extents.y * direction.z + bounds.extents.z * direction.y, bounds.extents.x / aspect) * 1.12f;
            var distance = halfHeight / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) + bounds.extents.z;
            camera.transform.position = target + direction * distance;
            camera.transform.LookAt(target);
        }

        private static void BuildEnvironment(Scene scene)
        {
            // Camera: a moderate telephoto like a product shot
            CreateCamera(scene, "Main Camera", 28);
            BuildLighting(scene);

            // Forest floor: a dark, rough, earthy ground that takes the shadows
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            SceneManager.MoveGameObjectToScene(ground, scene);
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.localScale = new Vector3(6, 1, 6);
            ground.GetComponent<MeshRenderer>().sharedMaterial = GroundMaterial();
        }

        /// <summary>
        /// A camera tagged as the main one, with post-processing (tone mapping) and the quality level's anti-aliasing
        /// (<see cref="QualityPresetCamera"/>); at the scene's root, or under <paramref name="parent"/>.
        /// </summary>
        public static Camera CreateCamera(Scene scene, string name, float fieldOfView, Transform parent = null)
        {
            var cameraObject = new GameObject(name);
            if (parent != null)
                cameraObject.transform.SetParent(parent, false);
            else
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraObject.AddComponent<QualityPresetCamera>();
            return camera;
        }

        /// <summary>
        /// A level's sky as the lighting takes it (LevelBuilder resolves it from level.json): the main light, towards it in Unity's
        /// coordinates and its colour, the skybox material, the ambient, and the post-processing, kept in a volume profile of the level's.
        /// </summary>
        public sealed class SkyLighting
        {
            public Vector3 lightDirection;
            public Color lightColor;
            public Material skybox;
            public Color ambient;
            public float exposure;
            public float bloom;
            public string volumeProfilePath;
        }

        /// <summary>
        /// The light of the toon world, shared by the mushroom scene and the level scenes: the sun, a procedural sky with a flat dim
        /// ambient, and the post-processing volume (tone mapping, a faint bloom); with a level's <paramref name="sky"/>, its light, skybox,
        /// ambient and post-processing instead. Without one the scene also gets a <see cref="SkySettings"/> with the identity (day) tone,
        /// so the toon look is as it was; a level adds its own.
        /// </summary>
        public static Light BuildLighting(Scene scene, SkyLighting sky = null)
        {
            GameObject Create(string name)
            {
                var gameObject = new GameObject(name);
                SceneManager.MoveGameObjectToScene(gameObject, scene);
                return gameObject;
            }

            // Sun from the front, above and to the side. The toon look takes only its direction, hue and shadows; its intensity lights
            // the ground. The filter stands in for the atmosphere HDRP's physically based sky put between the sun and the scene, which
            // warmed its 5600 K (fitted to the HDRP snapshots)
            var sunObject = Create("Sun");
            var light = sunObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.useColorTemperature = true;
            light.colorTemperature = 5600;
            light.color = new Color(1, 0.94f, 0.83f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 1;
            sunObject.transform.rotation = Quaternion.LookRotation(SunDirection);
            if (sky != null)
            {
                // The sky's light: the toon shaders take its direction and hue only, so the intensity stays the sun's
                light.useColorTemperature = false;
                light.color = sky.lightColor;
                sunObject.transform.rotation = Quaternion.LookRotation(-sky.lightDirection);
            }

            // A procedural sky, whose ground colour fills the background below the horizon, and a flat dim ambient for the ground's
            // shadows (no lighting to bake); no fog. Lighting settings belong to the active scene
            var active = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            RenderSettings.sun = light;
            RenderSettings.skybox = sky != null ? sky.skybox : SkyMaterial();
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = sky != null ? sky.ambient : new Color(0.17f, 0.14f, 0.1f);
            RenderSettings.fog = false;
            if (active.IsValid() && active != scene)
                SceneManager.SetActiveScene(active);

            // Tone mapping like the preview's ACES, and the faint glow HDRP's default bloom gave. HDRP blended 15% of the blurred image
            // in (2^0.2 - 1), which also dimmed bright models over the dark ground; URP adds it, so the sum is scaled back by the rest
            // (-0.23 EV, a little more for the brighter tone curve; fitted to the HDRP snapshots)
            var profile = sky != null ? LevelVolumeProfile(sky) : AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                MushroomBuilder.EnsureFolder(Path.GetDirectoryName(VolumeProfilePath));
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
                T Add<T>() where T : VolumeComponent
                {
                    var component = profile.Add<T>(true);
                    component.name = typeof(T).Name;
                    AssetDatabase.AddObjectToAsset(component, profile);
                    return component;
                }
                var tonemapping = Add<Tonemapping>();
                tonemapping.mode.value = TonemappingMode.ACES;
                var bloom = Add<Bloom>();
                bloom.threshold.value = 0;
                bloom.intensity.value = 0.175f;
                bloom.scatter.value = 0.7f;
                var adjustments = Add<ColorAdjustments>();
                adjustments.postExposure.value = -0.26f;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            var volumeObject = Create("Post Processing");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            if (sky == null)
                EnsureSkySettings(scene);
            return light;
        }

        /// <summary>
        /// A level's post-processing, Assets/Scenes/&lt;Level&gt;/Post Processing.asset: the showcase's ACES tone mapping and bloom
        /// threshold, with the sky's exposure and bloom; updated in place when they change.
        /// </summary>
        private static VolumeProfile LevelVolumeProfile(SkyLighting sky)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(sky.volumeProfilePath);
            if (profile == null)
            {
                MushroomBuilder.EnsureFolder(Path.GetDirectoryName(sky.volumeProfilePath));
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, sky.volumeProfilePath);
            }
            T Get<T>() where T : VolumeComponent
            {
                if (profile.TryGet<T>(out var component))
                    return component;
                component = profile.Add<T>(true);
                component.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(component, profile);
                return component;
            }
            Get<Tonemapping>().mode.value = TonemappingMode.ACES;
            var bloom = Get<Bloom>();
            bloom.threshold.value = 0;
            bloom.intensity.value = sky.bloom;
            bloom.scatter.value = 0.7f;
            Get<ColorAdjustments>().postExposure.value = sky.exposure;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static Material GroundMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Forest Floor" };
            material.SetColor("_BaseColor", new Color(0.22f, 0.17f, 0.1f));
            material.SetFloat("_Smoothness", 0.15f);
            material.SetFloat("_Metallic", 0);
            MushroomBuilder.EnsureFolder(Path.GetDirectoryName(GroundMaterialPath));
            AssetDatabase.CreateAsset(material, GroundMaterialPath);
            return material;
        }

        private static Material SkyMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Skybox/Procedural")) { name = "Sky" };
            material.SetFloat("_SunSize", 0.03f);
            material.SetFloat("_AtmosphereThickness", 1);
            material.SetColor("_SkyTint", new Color(0.5f, 0.5f, 0.5f));
            material.SetColor("_GroundColor", new Color(0.32f, 0.26f, 0.21f));
            material.SetFloat("_Exposure", 1);
            MushroomBuilder.EnsureFolder(Path.GetDirectoryName(SkyMaterialPath));
            AssetDatabase.CreateAsset(material, SkyMaterialPath);
            return material;
        }

        private static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Any(s => s.path == MushroomBuilder.ScenePath))
                return;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MushroomBuilder.ScenePath, true) }.Concat(scenes).ToArray();
        }
    }
}
