using UnityEngine;
using UnityEngine.Rendering;

namespace SdfMushrooms
{
    /// <summary>
    /// The sky of a scene, as the toon shaders see it (SdfSky.hlsl): the tone on the toon bands, the banded gradient and the abyss's
    /// glow, the fade toward the horizon with distance. Shader globals are not saved with a scene, so this sets them whenever it is
    /// enabled or edited, in the editor and in builds; every scene has one. Its defaults are the identity: the toon look as it is, which
    /// the model showcase keeps.
    /// <para>
    /// In a level it also moves the sky's far layer (SdfToonFar.shader): the eye moon's iris wanders by smooth noise and its lids close
    /// now and then, at seeded random intervals; the floaters drift round and bob. Only while playing, so the scene is not changed in the
    /// editor. The far layer is laid out round the origin and drawn round the camera, so before each camera renders, each far renderer's
    /// bounds are moved to that camera for culling.
    /// </para>
    /// Directions and colours are Unity's (x mirrored from the pipeline), colours linear. LevelBuilder fills it from level.json.
    /// </summary>
    [ExecuteAlways]
    public sealed class SkySettings : MonoBehaviour
    {
        /// <summary>The bands the shaders take (SDF_SKY_MAX_BANDS).</summary>
        public const int MaxBands = 8;

        [Header("Tone")]
        [Tooltip("The shaded colour is multiplied by this")]
        [Range(0, 2)] public float brightness = 1;
        [Tooltip("The shadow band's tone is multiplied by this (linear)")]
        public Vector3 shadowTint = Vector3.one;
        [Tooltip("How much glowing edges keep their strength against the brightness")]
        [Range(0, 1)] public float glowKeep;

        [Header("Gradient")]
        [Tooltip("The gradient and abyss together, from the zenith down: each band's colour (linear) from its elevation up to the band above")]
        public Vector4[] bandColors = new Vector4[0];
        [Tooltip("Each band's lowest elevation, in degrees")]
        public float[] bandFrom = new float[0];
        [Tooltip("The width of the band edges, in degrees")]
        public float softness = 1;
        public float abyssGlowElevation = -55;
        public float abyssGlowWidth = 12;
        public Vector4 abyssGlowColor;

        [Header("Fade")]
        [Tooltip("Beyond start metres a surface fades toward the sky behind it, up to amount at end")]
        public float fadeStart = 25;
        public float fadeEnd = 180;
        [Range(0, 1)] public float fadeAmount;

        [Header("Moon")]
        public Transform moonEye;
        public Transform moonUpperLid;
        public Transform moonLowerLid;
        [Tooltip("How far the iris wanders, in degrees, and about how long a swing takes, in seconds")]
        public float turnAmount = 12;
        public float turnPeriod = 40;
        [Tooltip("Seconds between blinks, a random time in this range, and how long a blink takes")]
        public Vector2 blinkEvery = new Vector2(9, 25);
        public float blinkDuration = 0.35f;
        [Tooltip("How far each lid turns to close, in degrees (content/parts/moon_lid_*.sdf are open by 0.8 rad)")]
        public float lidCloseAngle = 0.8f * Mathf.Rad2Deg;
        public int seed;

        [Header("Floaters")]
        public Transform[] floaters = new Transform[0];
        [Tooltip("Each floater's azimuth and elevation (degrees, Unity's), distance (metres) and bob phase (0 to 1)")]
        public Vector4[] floaterPlaces = new Vector4[0];
        [Tooltip("Metres a second round the sky at its distance, and metres up and down")]
        public float drift;
        public float bob;

        [Header("Far layer")]
        [Tooltip("How far from the camera the far layer is laid out, in metres (inside the camera's far plane, for culling)")]
        public float farRadius = 100;
        [Tooltip("The renderers drawn round the camera (SdfToonFar.shader, the near motes)")]
        public Renderer[] farRenderers = new Renderer[0];

        /// <summary>The seconds between floaters' bobs.</summary>
        private const float BobPeriod = 9;

        private static readonly Vector4[] colors = new Vector4[MaxBands];
        private static readonly Vector4[] froms = new Vector4[MaxBands];
        private System.Random blinks;
        private float nextBlink = -1;

        private void OnEnable()
        {
            Apply();
            RenderPipelineManager.beginCameraRendering += FollowCamera;
        }

        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= FollowCamera;

        private void OnValidate() => Apply();

        /// <summary>Sets the shader globals of SdfSky.hlsl from these settings.</summary>
        public void Apply()
        {
            Shader.SetGlobalVector("_SkyTone", new Vector4(brightness, glowKeep, 1, 0));
            Shader.SetGlobalVector("_SkyShadowTint", shadowTint);
            var count = Mathf.Min(MaxBands, Mathf.Min(bandColors.Length, bandFrom.Length));
            for (var i = 0; i < MaxBands; i++)
            {
                var from = i < count ? bandFrom[i] * Mathf.Deg2Rad : -Mathf.PI / 2;
                // The edge's half width, from degrees along the sky to the sine of the elevation the shader compares
                colors[i] = i < count ? bandColors[i] : Vector4.zero;
                froms[i] = new Vector4(Mathf.Sin(from), Mathf.Cos(from) * softness * 0.5f * Mathf.Deg2Rad, 0, 0);
            }
            Shader.SetGlobalVectorArray("_SkyBandColors", colors);
            Shader.SetGlobalVectorArray("_SkyBandFrom", froms);
            Shader.SetGlobalVector("_SkyBands", new Vector4(count, 0, 0, 0));
            var glow = abyssGlowElevation * Mathf.Deg2Rad;
            Shader.SetGlobalVector("_SkyAbyssGlow", new Vector4(Mathf.Sin(glow), Mathf.Cos(glow) * abyssGlowWidth * 0.5f * Mathf.Deg2Rad, 0, 0));
            Shader.SetGlobalVector("_SkyAbyssGlowColor", abyssGlowColor);
            Shader.SetGlobalVector("_SkyFade", new Vector4(fadeStart, Mathf.Max(fadeEnd, fadeStart + 0.01f), fadeAmount, 0));
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;
            var time = Time.time;
            if (moonEye != null)
            {
                // The iris wanders by smooth noise, about a turn of the period; it never looks at anyone
                var x = (Mathf.PerlinNoise(time / turnPeriod, seed % 1000 * 0.137f) * 2 - 1) * turnAmount;
                var y = (Mathf.PerlinNoise(seed % 1000 * 0.291f, time / turnPeriod) * 2 - 1) * turnAmount;
                moonEye.localRotation = Quaternion.Euler(x, y, 0);
            }
            if (moonUpperLid != null && moonLowerLid != null)
            {
                blinks ??= new System.Random(seed);
                if (nextBlink < 0)
                    nextBlink = time + Mathf.Lerp(blinkEvery.x, blinkEvery.y, (float)blinks.NextDouble());
                var into = (time - nextBlink) / Mathf.Max(blinkDuration, 0.01f);
                var closed = into >= 0 && into <= 1 ? Mathf.Sin(into * Mathf.PI) : 0;
                if (into > 1)
                    nextBlink = time + Mathf.Lerp(blinkEvery.x, blinkEvery.y, (float)blinks.NextDouble());
                moonUpperLid.localRotation = Quaternion.Euler(closed * lidCloseAngle, 0, 0);
                moonLowerLid.localRotation = Quaternion.Euler(-closed * lidCloseAngle, 0, 0);
            }
            for (var i = 0; i < floaters.Length && i < floaterPlaces.Length; i++)
            {
                if (floaters[i] == null)
                    continue;
                var place = floaterPlaces[i];
                var distance = Mathf.Max(place.z, 1);
                var azimuth = place.x + drift * time / distance * Mathf.Rad2Deg;
                var elevation = place.y + bob * Mathf.Sin((time / BobPeriod + place.w) * 2 * Mathf.PI) / distance * Mathf.Rad2Deg;
                floaters[i].localPosition = Direction(azimuth, elevation) * farRadius;
            }
        }

        /// <summary>
        /// Turns the sky's objects (the moon, floaters, spores and motes) on (1) or off (0), leaving the skybox and the tone. For comparing
        /// frame times with and without them, e.g. from the web build's page: <c>unityInstance.SendMessage("Sky", "SetObjectsActive", 0)</c>.
        /// </summary>
        public void SetObjectsActive(int active)
        {
            foreach (Transform child in transform)
                child.gameObject.SetActive(active != 0);
        }

        /// <summary>Turns the main camera towards the moon, for the same comparison where the sky costs the most: <c>SendMessage("Sky", "FaceMoon")</c>.</summary>
        public void FaceMoon()
        {
            if (Camera.main != null && moonEye != null)
                Camera.main.transform.rotation = Quaternion.LookRotation(moonEye.parent.localPosition.normalized);
        }

        /// <summary>The unit direction at an azimuth (degrees from +z towards +x) and elevation (degrees up).</summary>
        public static Vector3 Direction(float azimuth, float elevation)
        {
            var (a, e) = (azimuth * Mathf.Deg2Rad, elevation * Mathf.Deg2Rad);
            return new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
        }

        /// <summary>The far layer is drawn round the camera: its bounds are moved there before the camera culls.</summary>
        private void FollowCamera(ScriptableRenderContext context, Camera camera)
        {
            var offset = camera.transform.position;
            foreach (var renderer in farRenderers)
            {
                if (renderer == null)
                    continue;
                renderer.ResetBounds();
                var bounds = renderer.bounds;
                bounds.center += offset;
                renderer.bounds = bounds;
            }
        }
    }
}
