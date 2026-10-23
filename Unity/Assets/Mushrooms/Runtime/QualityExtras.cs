using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SdfMushrooms
{
    /// <summary>
    /// What the two levels above Ultra High change outside their URP assets: the foliage's draw distances (<see cref="FoliageDistance"/>,
    /// read by the instanced renderer) and the bloom's high-quality filtering, put on by a hidden global volume of its own over the
    /// level's (only that one setting, so the level's bloom intensity and scatter stay). Both cost frame time only, no download.
    /// </summary>
    public static class QualityExtras
    {
        public const string Epic = "Epic";
        public const string UltraEpic = "Ultra Epic";

        private static Volume volume;

        private static string Current
        {
            get
            {
                var names = QualitySettings.names;
                var level = QualitySettings.GetQualityLevel();
                return level >= 0 && level < names.Length ? names[level] : null;
            }
        }

        /// <summary>The factor on every foliage ring's distance: 1.5 on Ultra Epic, 1.25 on Epic, 1 below.</summary>
        public static float FoliageDistance => Current switch { UltraEpic => 1.5f, Epic => 1.25f, _ => 1 };

        /// <summary>Whether the bloom is high-quality filtered (bicubic upsampling, no blocky halo round bright pixels): Epic and up.</summary>
        public static bool BloomHighQuality => Current is Epic or UltraEpic;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            QualitySettings.activeQualityLevelChanged += (_, _) => ApplyBloom();
            ApplyBloom();
        }

        private static void ApplyBloom()
        {
            if (!BloomHighQuality)
            {
                if (volume != null)
                    volume.enabled = false;
                return;
            }
            if (volume == null)
            {
                var host = new GameObject("Quality Extras") { hideFlags = HideFlags.HideInHierarchy };
                Object.DontDestroyOnLoad(host);
                volume = host.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 100;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.Add<Bloom>().highQualityFiltering.Override(true);
                volume.sharedProfile = profile;
            }
            volume.enabled = true;
        }
    }
}
