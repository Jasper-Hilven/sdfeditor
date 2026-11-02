using System;
using UnityEngine;

namespace SdfRig
{
    /// <summary>
    /// The soldier's prefabs, one per team skin (ticket 43): the same rigged headless mesh on the Synty skeleton, recoloured per skin.
    /// Made by the editor (SdfRig.Editor.SoldierSkins) in Assets/Rig/Generated/Resources/SoldierModels.asset: generated like the prefabs
    /// it lists, so not in git.
    /// <code>var prefab = SoldierModels.Prefab(SoldierModels.RedSkin);   // an unknown key gives the default (blue)</code>
    /// </summary>
    public sealed class SoldierModels : ScriptableObject
    {
        /// <summary>Where it lives under a Resources folder, without the extension.</summary>
        public const string ResourceName = "SoldierModels";

        public const string RedSkin = "red", BlueSkin = "blue";

        /// <summary>The skin keys: red is the west side's team, blue the east's.</summary>
        public static readonly string[] SkinKeys = { RedSkin, BlueSkin };

        public const string DefaultSkin = BlueSkin;

        /// <summary>Prefabs at the index of their key in <see cref="SkinKeys"/>.</summary>
        public GameObject[] prefabs = new GameObject[2];

        static SoldierModels loaded;

        /// <summary>The index of a skin key, or -1.</summary>
        public static int IndexOf(string skin) => Array.IndexOf(SkinKeys, skin);

        /// <summary>The index of a skin key, the default's for an unknown or null one.</summary>
        public static int IndexOrDefault(string skin)
        {
            var i = IndexOf(skin);
            return i >= 0 ? i : IndexOf(DefaultSkin);
        }

        /// <summary>The skin of a team by name: "red" gives red, anything else blue.</summary>
        public static string SkinOfTeam(string teamName) =>
            string.Equals(teamName, "red", StringComparison.OrdinalIgnoreCase) ? RedSkin : BlueSkin;

        /// <summary>The asset, or null before the models were imported.</summary>
        public static SoldierModels Load()
        {
            if (loaded == null)
                loaded = Resources.Load<SoldierModels>(ResourceName);
            return loaded;
        }

        /// <summary>The soldier in a skin (an unknown or null key gives the default); null when the models were not imported.</summary>
        public GameObject PrefabOf(string skin)
        {
            var i = IndexOrDefault(skin);
            return prefabs != null && i < prefabs.Length ? prefabs[i] : null;
        }

        /// <summary>The soldier in a skin; null when the models were not imported.</summary>
        public static GameObject Prefab(string skin)
        {
            var models = Load();
            return models == null ? null : models.PrefabOf(skin);
        }
    }
}
