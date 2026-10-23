using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SdfLevel.Editor
{
    /// <summary>
    /// The haze, steam and drips of a level (ticket 29.1; level.json "fog", "steam" and "drips", from the level file's lines of the same
    /// names). The fog is Unity's built-in one, exponential squared, which the toon shaders (<c>multi_compile_fog</c>) mix in per vertex; a
    /// level without the "fog" block has none, as ever. The steam and drips are one <see cref="LevelSteam"/> at the scene's root, which
    /// emits into the match's effect pool. Positions in level.json are the pipeline's; x is mirrored here, as in the rest of the builder.
    /// </summary>
    public static class LevelAtmosphere
    {
        public const string SteamName = "Steam";

        /// <summary>The fog colour (sRGB) of the level, or null for a level without fog.</summary>
        public static Color? FogColor(JObject data)
        {
            if (!(data["fog"] is JObject fog))
                return null;
            var c = fog["color"];
            return new Color(c[0].Value<float>(), c[1].Value<float>(), c[2].Value<float>());
        }

        /// <summary>
        /// Sets the scene's fog from the level (or leaves it off, as MushroomScene.BuildLighting made it). Call after
        /// BuildLighting, which turns fog off. Lighting settings belong to the active scene.
        /// </summary>
        public static void ApplyFog(Scene scene, JObject data)
        {
            if (!(data["fog"] is JObject fog))
                return;
            var active = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = fog.Value<float>("density");
            RenderSettings.fogColor = FogColor(data).Value;
            if (active.IsValid() && active != scene)
                SceneManager.SetActiveScene(active);
        }

        /// <summary>
        /// Steam, at the scene's root (outside the static Level): a <see cref="LevelSteam"/> with the level's plumes and drips; nothing for a
        /// level with neither. Returns how many emitters were made.
        /// </summary>
        public static int AddSteam(Scene scene, JObject data)
        {
            var plumes = (data["steam"] as JArray)?.Select(s => new LevelSteam.Plume
            {
                at = Mirrored(s["at"]),
                size = s.Value<float>("size"),
                rise = s.Value<float>("rise"),
                count = s.Value<int>("count"),
            }).ToArray() ?? new LevelSteam.Plume[0];
            var drips = (data["drips"] as JArray)?.Select(d => new LevelSteam.Drip
            {
                at = Mirrored(d["at"]),
                water = d.Value<float>("water"),
                every = d.Value<float>("every"),
                size = d.Value<float>("size"),
            }).ToArray() ?? new LevelSteam.Drip[0];
            if (plumes.Length + drips.Length == 0)
                return 0;
            var steam = new GameObject(SteamName);
            SceneManager.MoveGameObjectToScene(steam, scene);
            var component = steam.AddComponent<LevelSteam>();
            component.plumes = plumes;
            component.drips = drips;
            if (FogColor(data) is { } fog)
                component.band = fog;
            return plumes.Length + drips.Length;
        }

        private static Vector3 Mirrored(JToken t) => new Vector3(-t[0].Value<float>(), t[1].Value<float>(), t[2].Value<float>());
    }
}
