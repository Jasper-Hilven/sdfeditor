using System.IO;
using Newtonsoft.Json.Linq;
using SdfBattle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfLevel.Editor
{
    /// <summary>
    /// Puts the level's zone mask (ticket 29.7; level.json "zones" and the bake's zones.bin) into the scene: a <see cref="LevelZones"/> object
    /// at the root holding the bytes, which the controller (soap) reads by position. On Battle, a <see cref="ZoneEffects"/> for the wet
    /// scouts. A level.json without "zones" adds nothing.
    /// </summary>
    internal static class LevelZonesBuilder
    {
        public static void Add(Scene scene, string levelFolder, JObject data, BattleArena battle)
        {
            if (!(data["zones"] is JObject zones))
                return;
            var path = $"{levelFolder}/{zones.Value<string>("file")}";
            if (!File.Exists(path))
            {
                Debug.LogWarning($"SDF level: level.json names the zone mask {path}, which is missing; bake the level again");
                return;
            }
            var root = new GameObject(LevelZones.ObjectName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var component = root.AddComponent<LevelZones>();
            component.min = new Vector2(zones["min"].Value<float>(0), zones["min"].Value<float>(1));
            component.width = zones["cells"].Value<int>(0);
            component.height = zones["cells"].Value<int>(1);
            component.cellSize = zones.Value<float?>("cellSize") ?? 1;
            component.cells = File.ReadAllBytes(path);
            if (battle != null)
                battle.gameObject.AddComponent<ZoneEffects>();
        }
    }
}
