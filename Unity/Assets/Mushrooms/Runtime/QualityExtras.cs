using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SdfMushrooms
{
    /// <summary>
    /// What the two levels above Ultra High change outside their URP assets: the foliage's draw distances and density
    /// (<see cref="FoliageDistance"/>, <see cref="FoliageCopies"/>, read by the instanced renderer), the level's fixed sun shadow map
    /// (<see cref="SunShadowResolution"/>; a level's sun casts no URP shadow, so the URP assets' shadow settings reach only the menus), the
    /// effects' particles (<see cref="ParticleCopies"/>, <see cref="ParticleLife"/>), the physics step (<see cref="PhysicsRate"/>), the
    /// gore (<see cref="GoreAmount"/>, <see cref="GoreStay"/>), the sky's spores and motes (<see cref="SporeCopies"/>: each set drawn again
    /// with its material shifted, made here when a scene loads) and the bloom's high-quality filtering, put on by a hidden global volume of its own over the
    /// level's (only that one setting, so the level's bloom intensity and scatter stay). Both cost frame time only, no download.
    /// </summary>
    public static class QualityExtras
    {
        public const string Epic = "Epic";
        public const string UltraEpic = "Ultra Epic";

        /// <summary>The name ending of a spore set's copy, made by <see cref="ApplySpores"/>.</summary>
        private const string SporeCopyName = " (Epic copy";
        private const string SporesShader = "SdfMushrooms/Spores";
        private static readonly int SporeShiftId = Shader.PropertyToID("_SporeShift");

        /// <summary>The most frames a second the desktop game draws.</summary>
        public const int MaxFrameRate = 240;

        private static Volume volume;
        private static float projectStep;

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

        /// <summary>How many copies of each foliage instance the instanced renderer adds round it: 2 on Ultra Epic, 1 on Epic, none below.</summary>
        public static int FoliageCopies => Current switch { UltraEpic => 2, Epic => 1, _ => 0 };

        /// <summary>The factor on the level's fixed sun shadow map's resolution: 2 on Ultra Epic (8192 texels a side), 1.5 on Epic, 1 below.</summary>
        public static float SunShadowResolution => Current switch { UltraEpic => 2, Epic => 1.5f, _ => 1 };

        /// <summary>How many scattered copies each small effects particle gets: 2 on Ultra Epic, 1 on Epic, none below.</summary>
        public static int ParticleCopies => Current switch { UltraEpic => 2, Epic => 1, _ => 0 };

        /// <summary>The factor on the life of effects puffs, drops and spores (not sparks, whose life is their streak's length): 1.5 on Ultra Epic, 1.25 on Epic, 1 below.</summary>
        public static float ParticleLife => Current switch { UltraEpic => 1.5f, Epic => 1.25f, _ => 1 };

        /// <summary>Physics steps a second: 120 on Ultra Epic, 100 on Epic, 0 below (the project's own, 50).</summary>
        public static int PhysicsRate => Current switch { UltraEpic => 120, Epic => 100, _ => 0 };

        /// <summary>The factor on how much blood and how many corpses lie about at once: 3 on Ultra Epic, 2 on Epic, 1 below.</summary>
        public static int GoreAmount => Current switch { UltraEpic => 3, Epic => 2, _ => 1 };

        /// <summary>The factor on how long splats and corpses stay: 3 on Ultra Epic, 2 on Epic, 1 below.</summary>
        public static float GoreStay => Current switch { UltraEpic => 3, Epic => 2, _ => 1 };

        /// <summary>How many more times each set of the sky's spores and motes is drawn: 2 on Ultra Epic, 1 on Epic, none below.</summary>
        public static int SporeCopies => Current switch { UltraEpic => 2, Epic => 1, _ => 0 };

        /// <summary>Whether the bloom is high-quality filtered (bicubic upsampling, no blocky halo round bright pixels): Epic and up.</summary>
        public static bool BloomHighQuality => Current is Epic or UltraEpic;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            projectStep = Time.fixedDeltaTime;
#if !UNITY_WEBGL
            // Frames past this only make heat (vsync is off on every level); the browser paces the web build to the display itself, and a
            // target there would swap its requestAnimationFrame for a timer
            Application.targetFrameRate = MaxFrameRate;
#endif
            QualitySettings.activeQualityLevelChanged += (_, _) => Apply();
            SceneManager.sceneLoaded += (_, _) => ApplySpores();
            Apply();
        }

        private static void Apply()
        {
            ApplyBloom();
            Time.fixedDeltaTime = PhysicsRate > 0 ? 1f / PhysicsRate : projectStep;
            ApplySpores();
        }

        /// <summary>
        /// Each set of the sky's spores and motes (a renderer on SdfSpores.shader, LevelSky's) with <see cref="SporeCopies"/> copies beside
        /// it, made once and turned on or off with the level: the same mesh on a copy of its material whose _SporeShift moves every spore
        /// elsewhere and on in its cycle, so the copies fill the gaps between the originals instead of drawing over them.
        /// </summary>
        private static void ApplySpores()
        {
            var copies = SporeCopies;
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var material = renderer.sharedMaterial;
                if (material == null || material.shader == null || material.shader.name != SporesShader || renderer.name.Contains(SporeCopyName))
                    continue;
                for (var c = 1; c <= 2; c++)
                {
                    var copy = renderer.transform.Find(CopyName(renderer, c));
                    if (copy == null && c <= copies)
                        copy = SporeCopy(renderer, c);
                    if (copy != null)
                        copy.gameObject.SetActive(c <= copies);
                }
            }
        }

        private static string CopyName(Renderer original, int index) => $"{original.name}{SporeCopyName} {index})";

        private static Transform SporeCopy(MeshRenderer original, int index)
        {
            var copy = new GameObject(CopyName(original, index)) { layer = original.gameObject.layer };
            copy.transform.SetParent(original.transform, false);
            copy.AddComponent<MeshFilter>().sharedMesh = original.GetComponent<MeshFilter>().sharedMesh;
            var renderer = copy.AddComponent<MeshRenderer>();
            var material = new Material(original.sharedMaterial) { name = $"{original.sharedMaterial.name}{SporeCopyName} {index})" };
            // Abyss spores: metres in x and z; near motes: a share of their box in x, y and z. w moves each on in its cycle
            var abyss = material.GetFloat("_SporeMode") < 0.5f;
            material.SetVector(SporeShiftId, abyss
                ? (index == 1 ? new Vector4(3.7f, 0, -2.9f, 0.37f) : new Vector4(-2.3f, 0, 4.1f, 0.71f))
                : (index == 1 ? new Vector4(0.37f, 0.23f, 0.51f, 0.37f) : new Vector4(0.71f, 0.59f, 0.13f, 0.71f)));
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = original.shadowCastingMode;
            renderer.receiveShadows = original.receiveShadows;
            renderer.lightProbeUsage = original.lightProbeUsage;
            renderer.reflectionProbeUsage = original.reflectionProbeUsage;
            return copy.transform;
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
