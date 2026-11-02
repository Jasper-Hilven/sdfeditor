using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SdfHeads.Editor
{
    /// <summary>
    /// Makes the monster heads the game wears (ticket 45) out of the prefabs the toon importer builds: <c>sdf mesh</c> turns
    /// content/models/characters/monsters/&lt;id&gt;.sdf into Assets/Mushrooms/Generated/characters/monsters/&lt;id&gt;.glb (128 cells, 3 levels
    /// of detail) and <c>&lt;id&gt;_fast.glb</c> (one level), and <see cref="SdfMushrooms.Editor.MushroomBuilder"/> makes
    /// Assets/Mushrooms/&lt;id&gt;.prefab and &lt;id&gt;_fast.prefab from them, the same route as the scouts. This is the head side: for every
    /// head of the <see cref="MonsterHeads"/> table that is modelled, it measures the finest level's bounds and triangles in the head's own
    /// frame (the origin is the soldier's head socket) and lists the prefabs in <c>MonsterHeadModels.asset</c>, which <see cref="HeadWear"/>
    /// reads. The soldier's own prefabs are not touched: the head is put on the socket when a player is made (SoldierPuppet).
    /// <para>
    /// Run <c>sdf mesh content/models/characters/monsters --into Unity/Assets/Mushrooms/Generated/characters/monsters --resolution 128 --lods 3</c>
    /// and the same with <c>--lods 1 --suffix _fast</c> first. Batch:
    /// <c>tools/unity-batch.sh heads -executeMethod SdfHeads.Editor.MonsterHeadBuilder.BuildFromCommandLine</c>.
    /// </para>
    /// </summary>
    public static class MonsterHeadBuilder
    {
        public const string Folder = "Assets/Heads/Generated";
        public const string ResourcesFolder = Folder + "/Resources";
        public const string AssetPath = ResourcesFolder + "/" + MonsterHeadModels.ResourceName + ".asset";

        [MenuItem("Sdf/Heads/Build Head Models")]
        public static void BuildFromMenu() => Debug.Log($"MonsterHeadBuilder: {Build()} heads");

        /// <summary>Batch: builds the registry; exit 1 if a modelled head has no prefab.</summary>
        public static void BuildFromCommandLine()
        {
            var expected = MonsterHeads.All.Count(h => h.Built);
            var made = 0;
            try
            {
                made = Build();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            EditorApplication.Exit(made == expected ? 0 : 1);
        }

        /// <summary>Writes the asset; returns how many heads it lists.</summary>
        public static int Build()
        {
            var entries = new List<MonsterHeadModels.Entry>();
            foreach (var head in MonsterHeads.All.Where(h => h.Built))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(head.PrefabPath);
                if (prefab == null)
                {
                    Debug.LogError($"MonsterHeadBuilder: {head.PrefabPath} is missing: mesh {head.ModelPath} (sdf mesh ... --into Unity/Assets/Mushrooms/Generated/characters/monsters) and let Unity import it");
                    continue;
                }
                var (centre, size, triangles) = Measure(prefab);
                entries.Add(new MonsterHeadModels.Entry
                {
                    id = head.Id,
                    prefab = prefab,
                    fastPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(head.FastPrefabPath),
                    boundsCentre = centre,
                    boundsSize = size,
                    triangles = triangles,
                });
                Debug.Log($"MonsterHeadBuilder: {head.Id}: bounds centre {centre:F3} size {size:F3}, {triangles} triangles at LOD0, hit shape {HeadHit.Fit(centre, size)}");
            }

            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Application.dataPath), ResourcesFolder));
            var models = AssetDatabase.LoadAssetAtPath<MonsterHeadModels>(AssetPath);
            if (models == null)
            {
                models = ScriptableObject.CreateInstance<MonsterHeadModels>();
                AssetDatabase.CreateAsset(models, AssetPath);
            }
            models.entries = entries.ToArray();
            EditorUtility.SetDirty(models);
            AssetDatabase.SaveAssets();
            return entries.Count;
        }

        /// <summary>
        /// A head prefab's bounds and triangles at its finest level, in the prefab's own frame: the renderers that are not a coarser level
        /// (<c>_LOD1</c> ...) or the ink outline's second draw of a mesh.
        /// </summary>
        public static (Vector3 centre, Vector3 size, int triangles) Measure(GameObject prefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var bounds = new Bounds();
                var any = false;
                var triangles = 0;
                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.gameObject.name.Contains("_LOD") || renderer.gameObject.name == SdfMushrooms.Editor.MushroomBuilder.OutlineObjectName)
                        continue;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                        continue;
                    var box = renderer.bounds;
                    if (any)
                        bounds.Encapsulate(box);
                    else
                        bounds = box;
                    any = true;
                    triangles += (int)(filter.sharedMesh.GetIndexCount(0) / 3);
                }
                return (bounds.center, bounds.size, triangles);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
