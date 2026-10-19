using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// The default scene, Assets/Scenes/Mushrooms.unity: an instance of every mushroom prefab side by side on a forest floor, under a
    /// physically based sky and sun with fixed exposure, framed by the camera. Created the first time; after that only touched when a
    /// prefab appears or disappears, because the instances follow their prefabs by themselves.
    /// </summary>
    public static class MushroomScene
    {
        public const string MushroomsRootName = "Mushrooms";
        private const string VolumeProfilePath = "Assets/Scenes/Mushrooms/Sky and Exposure.asset";
        private const string GroundMaterialPath = "Assets/Mushrooms/Materials/Forest Floor.mat";
        private const float Gap = 0.35f;

        /// <summary>
        /// Where the sun shines, from the front, the side and about 32° up: low enough to reach under the caps, so the stem below a
        /// cap and the faces are lit like the rest of the stem rather than lost in the cap's shadow.
        /// </summary>
        private static readonly Vector3 SunDirection = new Vector3(4, -4, -5).normalized;

        /// <summary>The first scenes had a sun 50° up, which left the stems under the caps dark; it is moved to the current one.</summary>
        private static readonly Vector3 OldSunDirection = new Vector3(4, -6, -3).normalized;

        public static void Ensure(IReadOnlyList<string> prefabPaths)
        {
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

            var changed = UpdateMushrooms(scene, prefabPaths) | UpdateSun(scene);
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

        /// <summary>Whether the saved scene lacks an instance of a prefab, or has one of a prefab that is gone, without opening it.</summary>
        private static bool NeedsChange(IReadOnlyList<string> prefabPaths)
        {
            var dependencies = AssetDatabase.GetDependencies(MushroomBuilder.ScenePath, false);
            var used = dependencies.Where(d => d.EndsWith(".prefab")).ToHashSet();
            return prefabPaths.Any(p => !used.Contains(p)) || used.Any(u => u.StartsWith(MushroomBuilder.PrefabFolder + "/") && !File.Exists(u));
        }

        private static bool UpdateMushrooms(Scene scene, IReadOnlyList<string> prefabPaths)
        {
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MushroomsRootName);
            if (root == null)
            {
                root = new GameObject(MushroomsRootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            var changed = false;
            var instances = new Dictionary<string, GameObject>();
            foreach (Transform child in root.transform.Cast<Transform>().ToArray())
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                var path = source != null ? AssetDatabase.GetAssetPath(source) : null;
                if (path == null || !prefabPaths.Contains(path))
                {
                    Object.DestroyImmediate(child.gameObject);
                    changed = true;
                }
                else
                {
                    instances[path] = child.gameObject;
                }
            }
            foreach (var path in prefabPaths.Where(p => !instances.ContainsKey(p)))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetParent(root.transform, false);
                instances[path] = instance;
                changed = true;
            }
            if (!changed)
                return false;

            // Side by side in name order, left to right as the camera sees them, centred on the origin
            var ordered = prefabPaths.Select(p => instances[p]).ToArray();
            var widths = ordered.Select(o => Bounds(o).size.x).ToArray();
            var total = widths.Sum() + Gap * (ordered.Length - 1);
            var x = total / 2;
            for (var i = 0; i < ordered.Length; i++)
            {
                // The camera looks down -z, so its left is +x
                ordered[i].transform.localPosition = new Vector3(x - widths[i] / 2 - Bounds(ordered[i]).center.x + ordered[i].transform.position.x, 0, 0);
                ordered[i].transform.SetSiblingIndex(i);
                x -= widths[i] + Gap;
            }
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

        /// <summary>Points the camera at all the mushrooms from the front and a little above, close enough that they fill the view.</summary>
        private static void FrameCamera(Scene scene)
        {
            var camera = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Camera>()).FirstOrDefault(c => c != null);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MushroomsRootName);
            if (camera == null || root == null || root.transform.childCount == 0)
                return;
            var bounds = Bounds(root);
            var target = bounds.center;
            var aspect = 16f / 9f;
            var halfHeight = Mathf.Max(bounds.extents.y, bounds.extents.x / aspect) * 1.12f;
            var distance = halfHeight / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) + bounds.extents.z;
            var direction = new Vector3(0, 0.22f, 1).normalized;
            camera.transform.position = target + direction * distance;
            camera.transform.LookAt(target);
        }

        private static void BuildEnvironment(Scene scene)
        {
            GameObject Create(string name)
            {
                var gameObject = new GameObject(name);
                SceneManager.MoveGameObjectToScene(gameObject, scene);
                return gameObject;
            }

            // Camera: a moderate telephoto like a product shot; temporal anti-aliasing also settles the grain of subsurface scattering
            var cameraObject = Create("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 28;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500;
            var cameraData = cameraObject.AddComponent<HDAdditionalCameraData>();
            cameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            cameraData.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;

            // Sun from the front, above and to the side
            var sunObject = Create("Sun");
            var sun = sunObject.AddHDLight(LightType.Directional);
            var light = sunObject.GetComponent<Light>();
            light.lightUnit = LightUnit.Lux;
            light.intensity = 100000;
            light.useColorTemperature = true;
            light.colorTemperature = 5600;
            light.shadows = LightShadows.Soft;
            sun.SetShadowResolution(4096);
            sunObject.transform.rotation = Quaternion.LookRotation(SunDirection);

            // Sky, exposure for a sunny day, tone mapping like the preview's ACES, sharp sun shadows nearby
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
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
                var environment = Add<VisualEnvironment>();
                environment.skyType.value = (int)SkyType.PhysicallyBased;
                environment.skyAmbientMode.value = SkyAmbientMode.Dynamic;
                Add<PhysicallyBasedSky>();
                var exposure = Add<Exposure>();
                exposure.mode.value = ExposureMode.Fixed;
                exposure.fixedExposure.value = 14f;
                var tonemapping = Add<Tonemapping>();
                tonemapping.mode.value = TonemappingMode.ACES;
                var fog = Add<Fog>();
                fog.enabled.value = false;
                var shadows = Add<HDShadowSettings>();
                shadows.maxShadowDistance.value = 25;
                var contactShadows = Add<ContactShadows>();
                contactShadows.enable.value = true;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            var volumeObject = Create("Sky and Exposure");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            // Forest floor: a dark, rough, earthy ground that takes the shadows
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            SceneManager.MoveGameObjectToScene(ground, scene);
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.localScale = new Vector3(6, 1, 6);
            ground.GetComponent<MeshRenderer>().sharedMaterial = GroundMaterial();
        }

        private static Material GroundMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (material != null)
                return material;
            material = new Material(Shader.Find("HDRP/Lit")) { name = "Forest Floor" };
            material.SetColor("_BaseColor", new Color(0.2f, 0.16f, 0.11f));
            material.SetFloat("_Smoothness", 0.15f);
            material.SetFloat("_Metallic", 0);
            HDMaterial.ValidateMaterial(material);
            MushroomBuilder.EnsureFolder(Path.GetDirectoryName(GroundMaterialPath));
            AssetDatabase.CreateAsset(material, GroundMaterialPath);
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
