using System.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;

namespace SdfBattle.Editor
{
    /// <summary>
    /// Keeps the weapon table asset (ticket 22), <see cref="AssetPath"/>, in place: made with the first balance
    /// (<see cref="WeaponTable.Default"/>) and the default looks when it is missing, and its empty references filled in (each look's model
    /// from Assets/Mushrooms/&lt;id&gt;.prefab, the effects' material, the goop models, the scouts' gore pieces) when the editor loads or
    /// the models are built.
    /// The numbers already in it are never touched, so tuning in the inspector stays; Tools > SDF Battle > Reset Weapon Table puts them
    /// back to the first balance.
    /// </summary>
    public static class WeaponTableSetup
    {
        public const string Folder = "Assets/Battle/Resources";
        public const string AssetPath = Folder + "/" + WeaponTableAsset.ResourceName + ".asset";
        public const string EffectMaterialPath = "Assets/Effects/Materials/Effect.mat";

        private static readonly string[] BlobModels = { "goop-blob0", "goop-blob1", "goop-blob2" };
        private static readonly string[] SplatModels = { "goop-splat0", "goop-splat1" };

        /// <summary>The scouts' body pieces (eyes and hands are the scout's own), not their variants (_256, _512, _fast).</summary>
        private static string[] ScoutPieces() =>
            System.Array.ConvertAll(System.IO.Directory.GetFiles(MushroomBuilder.PrefabFolder, "*-scout-*.prefab"), System.IO.Path.GetFileNameWithoutExtension)
                .Where(n => !n.Contains('_') && !n.Contains("-eye") && !n.Contains("-hand"))
                .OrderBy(n => n, System.StringComparer.Ordinal)
                .ToArray();

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            MushroomBuilder.Built += _ => EditorApplication.delayCall += EnsureQuietly;
            EditorApplication.delayCall += EnsureQuietly;
        }

        private static void EnsureQuietly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            Ensure();
        }

        /// <summary>The asset, made if missing, its empty references filled.</summary>
        public static WeaponTableAsset Ensure()
        {
            var asset = AssetDatabase.LoadAssetAtPath<WeaponTableAsset>(AssetPath);
            var created = asset == null;
            if (created)
            {
                if (!AssetDatabase.IsValidFolder(Folder))
                    AssetDatabase.CreateFolder("Assets/Battle", "Resources");
                asset = ScriptableObject.CreateInstance<WeaponTableAsset>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            if (Fill(asset) || created)
            {
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                Debug.Log($"SDF battle: {(created ? "made" : "filled in")} {AssetPath}");
            }
            return asset;
        }

        [MenuItem("Tools/SDF Battle/Reset Weapon Table")]
        public static void Reset()
        {
            var asset = Ensure();
            Undo.RecordObject(asset, "Reset Weapon Table");
            asset.table = WeaponTable.Default();
            asset.looks = WeaponTableAsset.DefaultLooks();
            Fill(asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Selection.activeObject = asset;
        }

        [MenuItem("Tools/SDF Battle/Select Weapon Table")]
        public static void Select() => Selection.activeObject = Ensure();

        /// <summary>Fills what is empty; returns whether anything changed.</summary>
        private static bool Fill(WeaponTableAsset asset)
        {
            var changed = false;
            if (asset.table == null || asset.table.weapons == null || asset.table.weapons.Length == 0)
            {
                asset.table = WeaponTable.Default();
                changed = true;
            }
            if (asset.looks == null || asset.looks.Length == 0)
            {
                asset.looks = WeaponTableAsset.DefaultLooks();
                changed = true;
            }
            foreach (var look in asset.looks)
            {
                if (look == null || look.model != null || string.IsNullOrEmpty(look.id))
                    continue;
                look.model = Prefab(look.id);
                changed |= look.model != null;
            }
            if (asset.effectMaterial == null)
            {
                asset.effectMaterial = AssetDatabase.LoadAssetAtPath<Material>(EffectMaterialPath);
                changed |= asset.effectMaterial != null;
            }
            changed |= FillModels(ref asset.blobModels, BlobModels);
            changed |= FillModels(ref asset.splatModels, SplatModels);
            changed |= FillModels(ref asset.scoutPieces, ScoutPieces());
            return changed;
        }

        /// <summary>
        /// Fills a list of models with the prefabs of <paramref name="names"/> when it has gaps (a missing reference) or lacks one of
        /// them that exists (a scout's pieces meshed after the list was filled, e.g. the chanterelle's beside the amanita's); a full
        /// list is left alone, so models added by hand in the inspector stay.
        /// </summary>
        private static bool FillModels(ref GameObject[] models, string[] names)
        {
            var found = System.Array.FindAll(System.Array.ConvertAll(names, Prefab), m => m != null);
            var current = models;
            var complete = current != null && current.Length > 0 && System.Array.TrueForAll(current, m => m != null);
            if (complete && System.Array.TrueForAll(found, m => System.Array.IndexOf(current, m) >= 0))
                return false;
            if (found.Length == 0)
                return false;
            models = complete ? current.Concat(found.Where(m => System.Array.IndexOf(current, m) < 0)).ToArray() : found;
            return true;
        }

        private static GameObject Prefab(string model) => AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/{model}.prefab");
    }
}
