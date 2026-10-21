using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;

namespace SdfPickups.Editor
{
    /// <summary>
    /// Builds one weapon pickup pod prefab per weapon model, Assets/Pickups/Pods/&lt;weapon&gt;-pod.prefab, so a new weapon in
    /// content/models/weapons gets its pod when its model is meshed and its prefab built, with nothing authored by hand.
    /// <para>
    /// The weapons are the .glb files <see cref="MushroomBuilder"/> found in the Generated folder's <c>weapons</c> group (a variant like
    /// minigun_512 counts as minigun); the pod is the prefab of content/models/pickups/weapon-pod.sdf, <see cref="PodModel"/>. A pod
    /// prefab reads, from the root down:
    /// <code>
    /// minigun-pod         WeaponPickup (weapon id, respawn, event) and the trigger, an upright capsule over the pod
    ///   Pod               the weapon-pod prefab: the 30 cm pedestal with its convex collider, to walk onto
    ///   Weapon            SpinHover: 1 m above the pod's top, 6 rpm about the vertical axis, a small bob
    ///     Model           the weapon's prefab, <see cref="ScaleOf"/> times its size, moved so the middle of its bounds is on the
    ///                     Weapon's origin and it turns on the spot
    /// </code>
    /// A pod is rebuilt when it is missing, older than the weapon's or the pod's prefab, or built by an older version of this code; that
    /// is checked when the editor loads and whenever <see cref="MushroomBuilder"/> built prefabs. By hand: Tools > SDF Pickups > Rebuild
    /// Pods. In a batch: <c>Unity -batchmode -quit -projectPath Unity -executeMethod SdfPickups.Editor.WeaponPodBuilder.BuildAllInBatch</c>
    /// (the level builder's batch method does it too).
    /// </para>
    /// </summary>
    public static class WeaponPodBuilder
    {
        public const string PodFolder = "Assets/Pickups/Pods";
        public const string PodModel = "weapon-pod";
        public const string WeaponGroup = "weapons";

        /// <summary>Raise when the way pods are built changes, so every pod is rebuilt when the editor loads. Version 2: the health pod
        /// (<see cref="HealthPodBuilder"/>). Version 3: the weapon <see cref="WeaponScale"/> times its size. Version 4:
        /// <see cref="ScaleOf"/>.</summary>
        public const int BuilderVersion = 4;

        /// <summary>How much bigger the floating weapon is than the one in the hand, so it reads from across the field.</summary>
        public const float WeaponScale = 2.25f;

        /// <summary>The minigun, about a metre long in the hand, would crowd the pod at <see cref="WeaponScale"/>.</summary>
        public const float MinigunScale = 1.5f;

        /// <summary>How much bigger the weapon floats over its pod than it is in the hand.</summary>
        public static float ScaleOf(string weapon) => weapon == "minigun" ? MinigunScale : WeaponScale;

        /// <summary>Metres from the pod's top to the middle of the weapon.</summary>
        public const float HoverHeight = 1;

        /// <summary>The pod's height if its mesh is not there to measure.</summary>
        public const float DefaultPodHeight = 0.3f;

        /// <summary>The trigger's radius, a little inside the top's, 0.95 m: the player is on the pod when it takes the weapon.</summary>
        internal const float TriggerRadius = 0.9f;

        private static readonly Regex VariantSuffix = new Regex(@"_(\d+|fast|superfast)$");

        internal static string Stamp => $"sdf-pod {BuilderVersion}";

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            MushroomBuilder.Built += _ => EditorApplication.delayCall += BuildStale;
            EditorApplication.delayCall += BuildStale;
        }

        /// <summary>The weapons that have a model built, by name.</summary>
        public static IReadOnlyList<string> Weapons() =>
            MushroomBuilder.GeneratedModels()
                .Where(glb => MushroomBuilder.GroupOf(glb) == WeaponGroup)
                .Select(glb => VariantSuffix.Replace(Path.GetFileNameWithoutExtension(glb), ""))
                .Distinct()
                .OrderBy(name => name, System.StringComparer.Ordinal)
                .ToArray();

        public static string PrefabOf(string weapon) => $"{MushroomBuilder.PrefabFolder}/{weapon}.prefab";

        public static string PodPrefabOf(string weapon) => $"{PodFolder}/{weapon}-pod.prefab";

        internal static string PodModelPrefab => $"{MushroomBuilder.PrefabFolder}/{PodModel}.prefab";

        [MenuItem("Tools/SDF Pickups/Rebuild Pods")]
        public static void BuildAll()
        {
            Build(Weapons());
            HealthPodBuilder.Build();
        }

        /// <summary>For batch mode: every pod.</summary>
        public static void BuildAllInBatch() => BuildAll();

        public static void BuildStale()
        {
            Build(Weapons().Where(IsStale).ToArray());
            if (HealthPodBuilder.IsStale())
                HealthPodBuilder.Build();
        }

        private static bool IsStale(string weapon)
        {
            var path = PodPrefabOf(weapon);
            if (!File.Exists(path) || AssetImporter.GetAtPath(path)?.userData != Stamp)
                return true;
            var built = File.GetLastWriteTimeUtc(path);
            return File.Exists(PrefabOf(weapon)) && File.GetLastWriteTimeUtc(PrefabOf(weapon)) > built
                   || File.Exists(PodModelPrefab) && File.GetLastWriteTimeUtc(PodModelPrefab) > built;
        }

        public static void Build(IReadOnlyList<string> weapons)
        {
            if (weapons.Count == 0)
                return;
            var pod = AssetDatabase.LoadAssetAtPath<GameObject>(PodModelPrefab);
            if (pod == null)
            {
                Debug.LogWarning($"SDF pickups: no pod prefab at {PodModelPrefab} yet (run sdf unity), no pods built");
                return;
            }
            Directory.CreateDirectory(PodFolder);
            var built = new List<string>();
            foreach (var weapon in weapons)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabOf(weapon));
                if (prefab == null)
                    continue;
                try
                {
                    built.Add(BuildOne(weapon, pod, prefab));
                }
                catch (System.Exception exception)
                {
                    Debug.LogError($"SDF pickups: could not build the pod of {weapon}: {exception.Message}\n{exception.StackTrace}");
                }
            }
            AssetDatabase.SaveAssets();
            if (built.Count > 0)
                Debug.Log($"SDF pickups: built {string.Join(", ", built)}");
        }

        private static string BuildOne(string weapon, GameObject podPrefab, GameObject weaponPrefab)
        {
            var root = new GameObject($"{weapon}-pod");
            try
            {
                var pod = (GameObject)PrefabUtility.InstantiatePrefab(podPrefab, root.transform);
                pod.name = "Pod";
                var podTop = Measure(pod) is { } podBounds ? podBounds.max.y : DefaultPodHeight;

                var hover = new GameObject("Weapon");
                hover.transform.SetParent(root.transform, false);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(weaponPrefab, hover.transform);
                model.name = "Model";
                // The middle of the weapon's bounds (all of its levels, measured at its own size) on the hover's origin, so it turns on
                // the spot
                if (Measure(model) is { } weaponBounds)
                    model.transform.localPosition = -weaponBounds.center * ScaleOf(weapon);
                model.transform.localScale = Vector3.one * ScaleOf(weapon);

                var spin = hover.AddComponent<SpinHover>();
                spin.baseHeight = podTop;
                spin.hoverHeight = HoverHeight;
                hover.transform.localPosition = new Vector3(0, podTop + HoverHeight, 0);

                // The trigger: an upright capsule over the pod, from just under its top to above the weapon
                var trigger = root.AddComponent<CapsuleCollider>();
                trigger.isTrigger = true;
                trigger.direction = 1;
                trigger.radius = TriggerRadius;
                trigger.height = 2.2f;
                trigger.center = new Vector3(0, podTop + 1, 0);

                var pickup = root.AddComponent<WeaponPickup>();
                pickup.weaponId = weapon;
                pickup.weapon = hover;

                var path = PodPrefabOf(weapon);
                PrefabUtility.SaveAsPrefabAsset(root, path, out var saved);
                if (!saved)
                    throw new System.InvalidOperationException($"Could not save {path}");
                var importer = AssetImporter.GetAtPath(path);
                if (importer != null && importer.userData != Stamp)
                {
                    importer.userData = Stamp;
                    importer.SaveAndReimport();
                }
                return path;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The bounds of the object's meshes (not the ink outline's copies) in its parent's space, or null if it has none.</summary>
        internal static Bounds? Measure(GameObject owner)
        {
            Bounds? bounds = null;
            var toParent = owner.transform.parent != null ? owner.transform.parent.worldToLocalMatrix : Matrix4x4.identity;
            foreach (var filter in owner.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || filter.name == MushroomBuilder.OutlineObjectName)
                    continue;
                var local = filter.sharedMesh.bounds;
                var matrix = toParent * filter.transform.localToWorldMatrix;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = matrix.MultiplyPoint3x4(new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x, (corner & 2) == 0 ? local.min.y : local.max.y, (corner & 4) == 0 ? local.min.z : local.max.z));
                    if (bounds is { } b)
                    {
                        b.Encapsulate(point);
                        bounds = b;
                    }
                    else
                        bounds = new Bounds(point, Vector3.zero);
                }
            }
            return bounds;
        }
    }
}
