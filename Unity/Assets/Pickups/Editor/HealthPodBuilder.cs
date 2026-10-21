using System.IO;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;

namespace SdfPickups.Editor
{
    /// <summary>
    /// Builds the health pod, Assets/Pickups/Pods/health-pod.prefab: the weapon pod with the floating health cross
    /// (content/models/pickups/health-cross.sdf, <see cref="CrossModel"/>) instead of a weapon. <see cref="WeaponPodBuilder"/> calls it
    /// along with the weapon pods, under the same rules: rebuilt when missing, older than the cross's or the pod's prefab, or built by
    /// an older <see cref="WeaponPodBuilder.BuilderVersion"/>. The prefab reads, from the root down:
    /// <code>
    /// health-pod          HealthPickup (amount, respawn, event) and the trigger, an upright capsule over the pod
    ///   Pod               the weapon-pod prefab: the 30 cm pedestal with its convex collider, to walk onto
    ///   Cross             SpinHover: 1 m above the pod's top, 6 rpm about the vertical axis, a small bob
    ///     Model           the cross's prefab, moved so the middle of its bounds is on the Cross's origin and it turns on the spot
    /// </code>
    /// </summary>
    public static class HealthPodBuilder
    {
        public const string CrossModel = "health-cross";

        public static string PodPrefab => $"{WeaponPodBuilder.PodFolder}/health-pod.prefab";

        private static string CrossPrefab => $"{MushroomBuilder.PrefabFolder}/{CrossModel}.prefab";

        public static bool IsStale()
        {
            var path = PodPrefab;
            if (!File.Exists(path) || AssetImporter.GetAtPath(path)?.userData != WeaponPodBuilder.Stamp)
                return true;
            var built = File.GetLastWriteTimeUtc(path);
            return File.Exists(CrossPrefab) && File.GetLastWriteTimeUtc(CrossPrefab) > built
                   || File.Exists(WeaponPodBuilder.PodModelPrefab) && File.GetLastWriteTimeUtc(WeaponPodBuilder.PodModelPrefab) > built;
        }

        public static void Build()
        {
            var pod = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPodBuilder.PodModelPrefab);
            var cross = AssetDatabase.LoadAssetAtPath<GameObject>(CrossPrefab);
            if (pod == null || cross == null)
            {
                Debug.LogWarning($"SDF pickups: no prefab at {(pod == null ? WeaponPodBuilder.PodModelPrefab : CrossPrefab)} yet (run sdf unity), " +
                                 "no health pod built");
                return;
            }
            Directory.CreateDirectory(WeaponPodBuilder.PodFolder);
            try
            {
                BuildOne(pod, cross);
                AssetDatabase.SaveAssets();
                Debug.Log($"SDF pickups: built {PodPrefab}");
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"SDF pickups: could not build the health pod: {exception.Message}\n{exception.StackTrace}");
            }
        }

        private static void BuildOne(GameObject podPrefab, GameObject crossPrefab)
        {
            var root = new GameObject("health-pod");
            try
            {
                var pod = (GameObject)PrefabUtility.InstantiatePrefab(podPrefab, root.transform);
                pod.name = "Pod";
                var podTop = WeaponPodBuilder.Measure(pod) is { } podBounds ? podBounds.max.y : WeaponPodBuilder.DefaultPodHeight;

                var hover = new GameObject("Cross");
                hover.transform.SetParent(root.transform, false);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(crossPrefab, hover.transform);
                model.name = "Model";
                // The middle of the cross's bounds on the hover's origin, so it turns on the spot
                if (WeaponPodBuilder.Measure(model) is { } crossBounds)
                    model.transform.localPosition = -crossBounds.center;

                var spin = hover.AddComponent<SpinHover>();
                spin.baseHeight = podTop;
                spin.hoverHeight = WeaponPodBuilder.HoverHeight;
                hover.transform.localPosition = new Vector3(0, podTop + WeaponPodBuilder.HoverHeight, 0);

                // The trigger, as on the weapon pods: an upright capsule over the pod, from just under its top to above the cross
                var trigger = root.AddComponent<CapsuleCollider>();
                trigger.isTrigger = true;
                trigger.direction = 1;
                trigger.radius = WeaponPodBuilder.TriggerRadius;
                trigger.height = 2.2f;
                trigger.center = new Vector3(0, podTop + 1, 0);

                var pickup = root.AddComponent<HealthPickup>();
                pickup.cross = hover;

                var path = PodPrefab;
                PrefabUtility.SaveAsPrefabAsset(root, path, out var saved);
                if (!saved)
                    throw new System.InvalidOperationException($"Could not save {path}");
                var importer = AssetImporter.GetAtPath(path);
                if (importer != null && importer.userData != WeaponPodBuilder.Stamp)
                {
                    importer.userData = WeaponPodBuilder.Stamp;
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
