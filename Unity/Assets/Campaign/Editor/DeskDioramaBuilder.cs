using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfMushrooms.Editor;
using SdfStory.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// Builds 42's desk diorama into the Boot scene (ticket 35) from the room of the intro (<c>content/scenes/home.txt</c>, read as
    /// <see cref="HomeBuilder"/> does, so the desk follows the intro's room): the furniture of the desk corner and the trophy shelf, 42 on their
    /// chair, one small prop per arena on the desk (switched off until the arena is won), the monitor's screen and an empty frame for the panel
    /// wall; plus the light (one directional, no shadows on the low presets) and the camera's start. Only the models of the room's props are
    /// used; the window, its backdrop, Mushroom 7 and the hands are left in the editor-only Home scene. Returns null, and builds nothing, when the
    /// room's meshes are not there (a fresh clone before <c>sdf unity</c>): the menus then use the flat wall.
    /// </summary>
    public static class DeskDioramaBuilder
    {
        public const string MaterialPath = "Assets/Campaign/Materials/DeskScreen.mat";
        public const string ShaderPath = "Assets/Campaign/Shaders/WallCard.shader";
        public const string RootName = "Desk Diorama";

        // What of the intro's room comes along (the rest of home.txt: the window and its backdrop, the poster, Mushroom 7, the hands, the lights)
        private static readonly string[] Furniture =
        {
            "room-shell", "rug", "desk", "monitor", "keyboard", "mousepad", "mouse", "lamp", "snack-bowl", "chair", "soda-can", "crisp-bag", "shelf", "shelf-stuff", "trophy", "poster",
        };

        /// <summary>The desk is this wide against the intro's 1.2 m, to have room for the arenas' props.</summary>
        public const float DeskStretch = 1.5f;

        /// <summary>How far the chair and 42 move along the desk (Unity's x), metres.</summary>
        public const float SeatShift = 0.45f;

        public static MenuBackdrop Build(Scene scene, Camera camera)
        {
            HomePlacement placement;
            try
            {
                placement = HomePlacement.Load(HomeBuilder.PlacementPath);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"SDF Campaign: no desk diorama, home.txt could not be read ({exception.Message}); the menus use the flat wall");
                return null;
            }
            var wanted = placement.Items.Where(i => i.Kind == "model" && Furniture.Contains(i.Model) || i.Kind == "actor" && i.Name == "Mushroom 42").ToList();
            var missing = wanted.Where(i => Prefab(i.Model) == null).Select(i => i.Model).Distinct().ToList();
            if (missing.Count > 0)
            {
                Debug.LogWarning($"SDF Campaign: no desk diorama, these prefabs are missing: {string.Join(", ", missing)}; mesh content/models first (sdf unity); the menus use the flat wall");
                return null;
            }

            var key = MushroomScene.BuildLighting(scene, null, new MushroomScene.IndoorLighting
            {
                // Toward the light: from the front, above and to the side the desk is lit from (the toon look takes the direction, the hue and the shadows)
                lightDirection = new Vector3(0.45f, 0.75f, 0.85f).normalized,
                lightColor = new Color(1f, 0.88f, 0.72f),
                ambient = new Color(0.3f, 0.26f, 0.4f),
            });
            key.shadows = LightShadows.Soft;

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var room = Child(root, "Room");
            var actors = Child(root, "Actors");
            Renderer[] screenRenderers = null;
            var trophy = new List<Renderer>();
            foreach (var item in wanted)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(item.Model), scene);
                instance.name = item.Name;
                instance.transform.SetParent((item.Kind == "actor" ? actors : room).transform, false);
                var at = HomeBuilder.ToUnity(item.At);
                // The chair and 42 sit to the lamp's side of the monitor, so that the screen and the desk's props are in sight
                if (item.Model == "chair" || item.Kind == "actor")
                    at.x += SeatShift;
                instance.transform.SetLocalPositionAndRotation(at, Quaternion.Euler(0, -item.Yaw, 0));
                instance.transform.localScale = Vector3.one * item.Scale;
                if (item.Model == "desk")
                    instance.transform.localScale = new Vector3(item.Scale * DeskStretch, item.Scale, item.Scale);
                // 42 looks over their shoulder at whoever is in the room: a third of a turn off the monitor
                if (item.Kind == "actor")
                    instance.transform.rotation = Quaternion.Euler(0, -item.Yaw + 40, 0);
                if (item.Model == "monitor")
                    screenRenderers = Screen(instance);
                if (item.Model == "trophy")
                    trophy.AddRange(instance.GetComponentsInChildren<Renderer>(true));
            }

            var props = Child(root, "Props");
            foreach (var prop in DeskProgress.Props)
            {
                var prefab = Prefab(prop.model);
                if (prefab == null)
                {
                    Debug.LogWarning($"SDF Campaign: the desk has no {prop.model} prefab for {prop.arena}; that arena leaves no prop");
                    continue;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = prop.arena;
                instance.transform.SetParent(props.transform, false);
                instance.transform.SetLocalPositionAndRotation(prop.at, Quaternion.Euler(0, prop.yaw, 0));
                var size = Measure(instance).size;
                if (size.y > 1e-4f)
                    instance.transform.localScale = Vector3.one * (prop.height / size.y);
                // Stands on the desk's top whatever the model's own origin is
                var bounds = Measure(instance);
                instance.transform.position += new Vector3(0, prop.at.y - bounds.min.y, 0);
                instance.SetActive(false);
            }

            var board = Child(root, "Wall Board");
            board.transform.SetPositionAndRotation(new Vector3(0, 1.4f, -1.49f), Quaternion.Euler(0, 180, 0));

            var backdropObject = new GameObject("Menu Backdrop");
            SceneManager.MoveGameObjectToScene(backdropObject, scene);
            var backdrop = backdropObject.AddComponent<MenuBackdrop>();
            backdrop.cam = camera;
            backdrop.key = key;
            backdrop.world = root;
            backdrop.board = board.transform;
            backdrop.props = props.transform;
            backdrop.trophy = trophy.ToArray();
            backdrop.screens = screenRenderers ?? new Renderer[0];
            backdrop.screenRect = screenRect;
            backdrop.screenCover = Picture("screen-cover");
            backdrop.screenGame = Picture("screen-game");
            backdrop.screenComplete = Picture("screen-ending-title");
            backdrop.cardShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            // The camera starts where the title wants it
            var pose = MenuShots.Pose(MenuShot.Wide);
            camera.transform.position = pose.position;
            camera.transform.rotation = Quaternion.LookRotation(pose.target - pose.position);
            camera.fieldOfView = pose.fov;
            return backdrop;
        }

        /// <summary>The scene with no desk (its meshes missing): only the component, which then draws the flat wall.</summary>
        public static MenuBackdrop BuildFlatOnly(Scene scene, Camera camera)
        {
            var backdropObject = new GameObject("Menu Backdrop");
            SceneManager.MoveGameObjectToScene(backdropObject, scene);
            var backdrop = backdropObject.AddComponent<MenuBackdrop>();
            backdrop.cam = camera;
            return backdrop;
        }

        private static GameObject Prefab(string model) => AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/{model}.prefab");

        private static Texture2D Picture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Story/Screens/{name}.png");

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static Bounds Measure(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (renderers.Length == 0)
                return new Bounds(instance.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        // The monitor's "screen" part gets the screen material: the picture comes from the backdrop's property block, the rect from the part's own mesh
        private static Vector4 screenRect;

        private static Renderer[] Screen(GameObject monitor)
        {
            // The screen part and its lower levels of detail (screen_LOD1, screen_LOD2), which the monitor swaps to with distance
            var parts = monitor.transform.Cast<Transform>().Where(c => c.name == "screen" || c.name.StartsWith("screen_LOD")).ToList();
            var first = parts.FirstOrDefault(c => c.name == "screen");
            if (first == null || !first.TryGetComponent<MeshFilter>(out var filter))
            {
                Debug.LogWarning("SDF Campaign: the monitor has no screen part; its picture is not shown");
                return null;
            }
            var material = ScreenMaterial();
            var bounds = filter.sharedMesh.bounds;
            screenRect = new Vector4(bounds.center.x, bounds.center.y, bounds.extents.x, bounds.extents.y);
            var renderers = new List<Renderer>();
            foreach (var part in parts)
                if (part.TryGetComponent<MeshRenderer>(out var renderer))
                {
                    renderer.sharedMaterial = material;
                    renderers.Add(renderer);
                }
            return renderers.ToArray();
        }

        private static Material ScreenMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath)!);
                material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            material.SetFloat("_Screen", 1);
            material.SetFloat("_Glow", 1.2f);
            material.SetColor("_Tint", Color.white);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
