using System;
using System.IO;
using SdfHeads;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;

namespace SdfEffects.Editor
{
    /// <summary>
    /// Lists the explosions' debris prefabs (ticket 46) in <c>Assets/Effects/Generated/Resources/MonsterDebrisModels.asset</c>, which
    /// <see cref="MonsterDebris"/> reads at run time. The models are <c>content/models/effects/monster/*.sdf</c> (small SDF parts: ice shard,
    /// snowball, bat, shamrock, weed tangle, eyeball, tentacle, bandage strip, fur tuft, fang, pumpkin seed, bolt, candy-cane piece, red
    /// confetti), meshed at 128 cells and one level of detail:
    /// <c>sdf mesh content/models/effects/monster --into Unity/Assets/Mushrooms/Generated/effects/monster --resolution 128 --lods 1</c>;
    /// <see cref="MushroomBuilder"/> makes <c>Assets/Mushrooms/&lt;name&gt;.prefab</c> from them. Batch:
    /// <c>tools/unity-batch.sh debris46 -executeMethod SdfEffects.Editor.MonsterDebrisBuilder.BuildFromCommandLine</c>.
    /// </summary>
    public static class MonsterDebrisBuilder
    {
        public const string Folder = "Assets/Effects/Generated";
        public const string ResourcesFolder = Folder + "/Resources";
        public const string AssetPath = ResourcesFolder + "/" + MonsterDebrisModels.ResourceName + ".asset";

        [MenuItem("Sdf/Effects/Build Monster Debris Models")]
        public static void BuildFromMenu() => Debug.Log($"MonsterDebrisBuilder: {Build()} of {DebrisKinds.Count} models");

        /// <summary>Batch: builds the registry; exit 1 if a model has no prefab.</summary>
        public static void BuildFromCommandLine()
        {
            var made = 0;
            try
            {
                made = Build();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            EditorApplication.Exit(made == DebrisKinds.Count ? 0 : 1);
        }

        /// <summary>Writes the asset; returns how many models have a prefab.</summary>
        public static int Build()
        {
            var prefabs = new GameObject[DebrisKinds.Count];
            var found = 0;
            for (var k = 0; k < prefabs.Length; k++)
            {
                var name = DebrisKinds.ModelOf((DebrisKind)k);
                prefabs[k] = AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/{name}.prefab");
                if (prefabs[k] != null)
                    found++;
                else
                    Debug.LogError($"MonsterDebrisBuilder: {MushroomBuilder.PrefabFolder}/{name}.prefab is missing: mesh content/models/effects/monster (sdf mesh ... --into Unity/Assets/Mushrooms/Generated/effects/monster --resolution 128 --lods 1) and let Unity import it");
            }
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Application.dataPath), ResourcesFolder));
            var models = AssetDatabase.LoadAssetAtPath<MonsterDebrisModels>(AssetPath);
            if (models == null)
            {
                models = ScriptableObject.CreateInstance<MonsterDebrisModels>();
                AssetDatabase.CreateAsset(models, AssetPath);
            }
            models.prefabs = prefabs;
            EditorUtility.SetDirty(models);
            AssetDatabase.SaveAssets();
            return found;
        }
    }
}
