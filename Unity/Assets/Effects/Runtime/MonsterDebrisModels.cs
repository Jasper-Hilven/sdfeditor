using SdfHeads;
using UnityEngine;

namespace SdfEffects
{
    /// <summary>
    /// The prefabs of the explosions' debris (ticket 46: content/models/effects/monster/), one per <see cref="DebrisKind"/>, which
    /// <see cref="MonsterDebris"/> reads the meshes and materials from. Made by the editor (SdfEffects.Editor.MonsterDebrisBuilder) in
    /// Assets/Effects/Generated/Resources/MonsterDebrisModels.asset from the prefabs the toon importer builds out of the meshed models, so
    /// generated like them and not in git. Without it the explosions throw no debris (the mist and sparks still play).
    /// </summary>
    public sealed class MonsterDebrisModels : ScriptableObject
    {
        public const string ResourceName = "MonsterDebrisModels";

        [Tooltip("By DebrisKind: the prefab of each model")]
        public GameObject[] prefabs = new GameObject[0];

        private static MonsterDebrisModels loaded;

        public static MonsterDebrisModels Load()
        {
            if (loaded == null)
                loaded = Resources.Load<MonsterDebrisModels>(ResourceName);
            return loaded;
        }

        public GameObject Prefab(DebrisKind kind) => (int)kind < (prefabs?.Length ?? 0) ? prefabs[(int)kind] : null;
    }
}
