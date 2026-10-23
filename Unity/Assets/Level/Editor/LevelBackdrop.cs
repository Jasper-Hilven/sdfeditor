using Newtonsoft.Json.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SdfLevel.Editor
{
    /// <summary>
    /// The room as a backdrop (ticket 28.2; level.json "room" with <c>form</c> "backdrop", from the level file's <c>backdrop</c> line): one
    /// model standing beyond the play area, under Level/Backdrop, and nothing else. It has no collider (physics objects and players pass
    /// through it, and the room is out of reach anyway), casts and receives no shadows, and the play area gets no edge walls: a scout who
    /// walks off falls, and <c>Match</c> kills it 3 m below the spawns outside the area. The camera's far plane and clear colour come
    /// from the block (<see cref="FarPlane"/>, <see cref="Clear"/>), its lamp is the room block's <c>light</c> as a closed room's.
    /// Positions are the pipeline's; x is mirrored here, as in the rest of the builder.
    /// </summary>
    public static class LevelBackdrop
    {
        public const string BackdropName = "Backdrop";

        /// <summary>Whether the level's room block is a backdrop rather than a closed room.</summary>
        public static bool IsBackdrop(JObject data) => data["room"] is JObject room && room.Value<string>("form") == "backdrop";

        /// <summary>The camera's far plane in metres.</summary>
        public static float FarPlane(JObject data) => data["room"].Value<float>("farPlane");

        /// <summary>The colour the camera clears to (sRGB).</summary>
        public static Color Clear(JObject data)
        {
            var clear = data["room"]["clear"];
            return new Color(clear[0].Value<float>(), clear[1].Value<float>(), clear[2].Value<float>());
        }

        /// <summary>
        /// Level/Backdrop: an instance of the model's prefab at its place (x mirrored, yaw turned round), unpacked so that its colliders
        /// can go, its renderers casting and receiving no shadows. A missing prefab logs a warning and builds nothing. Returns whether it
        /// was built.
        /// </summary>
        public static bool Add(Transform levelRoot, Scene scene, JObject data)
        {
            var room = data["room"];
            var model = room.Value<string>("model");
            var prefabPath = $"{MushroomBuilder.PrefabFolder}/{model}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"SDF level: there is no prefab {prefabPath} for the backdrop {model}; mesh content/models (sdf unity) and let the prefabs build");
                return false;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = BackdropName;
            instance.transform.SetParent(levelRoot, false);
            var at = room["at"];
            instance.transform.SetLocalPositionAndRotation(new Vector3(-at[0].Value<float>(), at[1].Value<float>(), at[2].Value<float>()),
                Quaternion.Euler(0, -room.Value<float>("yaw"), 0));
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return true;
        }
    }
}
