using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfMushrooms;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static SdfGui.Editor.GuiFactory;

namespace SdfGui.Editor
{
    /// <summary>
    /// Builds the GUI and its test scene, Assets/Scenes/GUI.unity: the icons, the component and screen prefabs, the GUI prefab (the
    /// canvas with every screen, the test panel, the <see cref="MatchView"/> and the <see cref="GuiFlow"/>), the first-person gun with
    /// its diegetic HUD, and the scene: a few mushrooms in a clearing seen from eye height, the gun in hand, the GUI over it.
    /// <para>
    /// The views draw from the match in the editor too, so opening the scene shows exactly the first frame of play: pick a screen on
    /// the GUI object's <see cref="GuiFlow"/> (or press the test panel's buttons in play mode) to see the others. The match starts
    /// mid-round with a little of everything going on; its fields are the scene's sample state.
    /// </para>
    /// Tools > SDF GUI > Rebuild Prefabs and Scene replaces all of it; without a window, <c>Unity -batchmode -quit -projectPath Unity
    /// -executeMethod SdfGui.Editor.GuiScene.BuildInBatch</c> (without -nographics: the icons are rendered).
    /// </summary>
    public static class GuiScene
    {
        public const string ScenePath = "Assets/Scenes/GUI.unity";
        public const string GuiPrefabPath = GuiPrefabs.Folder + "/GUI.prefab";
        public const string GunPrefabPath = GuiPrefabs.Folder + "/First Person Gun.prefab";
        private const float EyeHeight = 1.5f;
        private const float GunLength = 0.7f;

        [MenuItem("Tools/SDF GUI/Rebuild Prefabs and Scene")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (!GuiAssets.EnsureTmpEssentials())
            {
                // The font assets need TextMesh Pro's shaders, which arrive with the import
                EditorApplication.delayCall += Build;
                return;
            }
            Build();
        }

        public static void BuildInBatch()
        {
            GuiAssets.EnsureTmpEssentials();
            Build();
        }

        public static void Build()
        {
            GuiIcons.Render();
            var factory = new GuiFactory(GuiAssets.Load());
            var prefabs = new GuiPrefabs(factory);
            prefabs.Build();
            var screens = new GuiScreens(prefabs);
            screens.Build();
            var gui = BuildGuiPrefab(screens);
            var gun = BuildGunPrefab(factory);
            BuildScene(gui, gun);
            AssetDatabase.SaveAssets();
            Debug.Log($"SDF GUI: built {ScenePath} with its prefabs in {GuiPrefabs.Folder}");
        }

        // ---------------------------------------------------------------- The GUI prefab

        private static GameObject BuildGuiPrefab(GuiScreens screens)
        {
            var root = Object("GUI", null);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            SampleRound(root.AddComponent<MatchView>());
            var flow = root.AddComponent<GuiFlow>();
            flow.screen = GuiScreen.Hud;

            // Lowest first: the HUD under the screens that show over it
            var order = new[] { GuiScreen.Hud, GuiScreen.Scoreboard, GuiScreen.Pause, GuiScreen.Death, GuiScreen.MatchEnd, GuiScreen.Victory, GuiScreen.Defeat, GuiScreen.Title, GuiScreen.Settings, GuiScreen.Gallery, GuiScreen.Loading,
                GuiScreen.LevelSelect, GuiScreen.Difficulty, GuiScreen.Story, GuiScreen.Confirm };
            foreach (var screen in order)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(screens.Screens[screen], root.transform);
                Stretch(instance.transform);
                instance.SetActive(flow.Shows(screen));
            }
            var test = (GameObject)PrefabUtility.InstantiatePrefab(screens.TestPanel, root.transform);
            Stretch(test.transform);

            // Last, over everything: the black scene changes fade through (ticket 32.4)
            var fade = Object("Fade", root.transform);
            Stretch(fade.transform);
            var fadeImage = fade.AddComponent<Image>();
            fadeImage.color = Color.clear;
            fadeImage.enabled = false;
            fade.AddComponent<GuiFade>();
            return Save(root, GuiPrefabPath);
        }

        /// <summary>A round two minutes in: four players out, you with two kills, hurt and halfway through your spores.</summary>
        private static void SampleRound(MatchView match)
        {
            match.players = MatchView.NewRoster(SdfBattle.Cast.You);
            var p = match.players;
            p[0].kills = 2;
            p[3].kills = 1;
            p[4].kills = 1;
            foreach (var (victim, place) in new[] { (5, 8), (6, 7), (2, 6) })
            {
                p[victim].alive = false;
                p[victim].placement = place;
            }
            match.feed = new List<ViewKill>
            {
                new ViewKill { killer = 0, victim = 5, weapon = 0, headshot = false, age = 4.2f },
                new ViewKill { killer = 3, victim = 6, weapon = 2, headshot = true, age = 3.1f },
                new ViewKill { killer = 4, victim = 2, weapon = 3, headshot = false, age = 1.9f },
                new ViewKill { killer = 0, victim = 7, weapon = 0, headshot = true, age = 0.6f },
            };
            p[7].alive = false;
            p[7].placement = 5;
            match.phase = MatchPhase.Playing;
            match.timeLeft = 134;
            match.health = 72;
            match.ammo = 164;
            // What the death screen shows when picked in the editor
            match.killedBy = 3;
            match.killedByWeapon = 2;
            match.respawnIn = 5;
        }

        // ---------------------------------------------------------------- The first-person gun

        private static GameObject BuildGunPrefab(GuiFactory f)
        {
            var root = new GameObject("First Person Gun");
            var view = root.AddComponent<GuiViewModel>();
            var holder = new GameObject("Gun");
            holder.transform.SetParent(root.transform, false);
            view.gun = holder.transform;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/minigun.prefab");
            var gun = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            // Along the view: the barrel's long side forward, GunLength long, centred on the holder
            var bounds = LocalBounds(gun);
            var alongX = bounds.size.x >= bounds.size.z;
            gun.transform.localRotation = alongX ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;
            var length = Mathf.Max(bounds.size.x, bounds.size.z);
            var scale = GunLength / Mathf.Max(length, 1e-3f);
            gun.transform.localScale = Vector3.one * scale;
            gun.transform.localPosition = -(gun.transform.localRotation * bounds.center) * scale;
            var gunBounds = LocalBounds(holder);

            // The player's own floating hands, the amanita scout's, holding it from below
            var scout = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/amanita-scout.prefab"), holder.transform);
            PrefabUtility.UnpackPrefabInstance(scout, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            scout.name = "Hands";
            foreach (var group in scout.GetComponentsInChildren<LODGroup>())
                UnityEngine.Object.DestroyImmediate(group);
            foreach (var child in scout.transform.Cast<Transform>().Where(c => c.name != "hand").ToArray())
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            var handBounds = LocalBounds(scout);
            var handScale = gunBounds.size.x * 1.8f / Mathf.Max(handBounds.size.x, 1e-3f);
            scout.transform.localScale = Vector3.one * handScale;
            scout.transform.localPosition = new Vector3(gunBounds.center.x, gunBounds.min.y + gunBounds.size.y * 0.15f, gunBounds.center.z - gunBounds.size.z * 0.12f) - handBounds.center * handScale;
            handBounds = LocalBounds(scout);
            foreach (var renderer in holder.GetComponentsInChildren<Renderer>())
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // The diegetic HUD: a world-space card on top of the gun's near side, facing the eye
            var card = new GameObject("Gun HUD", typeof(RectTransform));
            card.transform.SetParent(holder.transform, false);
            var canvas = card.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
            var rect = (RectTransform)card.transform;
            rect.sizeDelta = new Vector2(400, 400);
            rect.localScale = Vector3.one * 0.00036f;
            rect.localPosition = new Vector3(gunBounds.min.x + 0.01f, gunBounds.max.y + 0.06f, gunBounds.center.z - gunBounds.size.z * 0.1f);
            rect.localRotation = Quaternion.Euler(0, -20, 0);

            view.tank = f.Shape(card.transform, "Spore Tank", ToonShapeKind.Pill, GuiPalette.Spore, Centre, new Vector2(0, 30), new Vector2(110, 260), s =>
            {
                s.FillMode = ToonFillMode.Vertical;
                s.FillAmount = 0.656f;
                s.Outline = 9;
                s.Shadow = 0;
                s.Glow = 30;
            });
            view.tankLabel = f.Text(card.transform, "Count", "164", TextStyle.Title, 70, GuiPalette.Cream, Centre, new Vector2(0, -150), new Vector2(300, 90));

            // A glow behind each hand: its health
            var glows = new List<ToonShape>();
            var handInset = handBounds.size.x * 0.2f;
            foreach (var (name, x) in new[] { ("Left Hand Glow", handBounds.min.x + handInset), ("Right Hand Glow", handBounds.max.x - handInset) })
            {
                var glowCard = new GameObject(name, typeof(RectTransform));
                glowCard.transform.SetParent(holder.transform, false);
                var glowCanvas = glowCard.AddComponent<Canvas>();
                glowCanvas.renderMode = RenderMode.WorldSpace;
                glowCanvas.additionalShaderChannels = canvas.additionalShaderChannels;
                var glowRect = (RectTransform)glowCard.transform;
                glowRect.sizeDelta = new Vector2(400, 400);
                glowRect.localScale = Vector3.one * handBounds.size.y * 1.8f / 400;
                glowRect.localPosition = new Vector3(x, handBounds.center.y, handBounds.max.z);
                glows.Add(f.Shape(glowCard.transform, "Glow", ToonShapeKind.Glow, GuiPalette.WithAlpha(GuiPalette.Spore, 0.75f), Centre, Vector2.zero, new Vector2(400, 400)));
            }
            view.handGlows = glows.ToArray();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = 0; // drawn by the world camera, not the UI's
            return Save(root, GunPrefabPath);
        }

        /// <summary>Bounds of an object's renderers (outlines left out) in its parent's space.</summary>
        private static Bounds LocalBounds(GameObject gameObject)
        {
            var parent = gameObject.transform.parent;
            var renderers = gameObject.GetComponentsInChildren<MeshRenderer>().Where(r => r.gameObject.name != MushroomBuilder.OutlineObjectName && r.gameObject.name.IndexOf("LOD", System.StringComparison.Ordinal) < 0).ToArray();
            var bounds = new Bounds();
            var first = true;
            foreach (var renderer in renderers)
            {
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null)
                    continue;
                var b = mesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3(i & 1, i >> 1 & 1, i >> 2 & 1) * 2 - Vector3.one);
                    var world = renderer.transform.TransformPoint(corner);
                    var local = parent != null ? parent.InverseTransformPoint(world) : world;
                    if (first)
                        bounds = new Bounds(local, Vector3.zero);
                    else
                        bounds.Encapsulate(local);
                    first = false;
                }
            }
            return bounds;
        }

        // ---------------------------------------------------------------- The scene

        private static void BuildScene(GameObject gui, GameObject gun)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MushroomScene.BuildLighting(scene);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(8, 1, 8);
            ground.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Mushrooms/Materials/Forest Floor.mat");

            // A clearing in front of the eye: the other players, and trees and rocks around them
            var world = new GameObject("Clearing");
            Place(world, "amanita-scout", new Vector3(-1.2f, 0, -4.5f), 20);
            Place(world, "chanterelle-scout", new Vector3(1.6f, 0, -6.5f), -15);
            Place(world, "amanita-scout", new Vector3(3.8f, 0, -9), -30);
            Place(world, "chanterelle-scout", new Vector3(-3.5f, 0, -8.5f), 30);
            Place(world, "tree0", new Vector3(-6, 0, -13), 20);
            Place(world, "pine1", new Vector3(5, 0, -16), 0);
            Place(world, "old-tree1", new Vector3(-12, 0, -20), 60);
            Place(world, "birch0", new Vector3(10, 0, -11), 0);
            Place(world, "willow1", new Vector3(14, 0, -22), 0);
            Place(world, "fruit-tree1", new Vector3(-15, 0, -9), 0);
            Place(world, "rock2", new Vector3(2.8f, 0, -3.2f), 30);
            Place(world, "rock0", new Vector3(-4.5f, 0, -5.5f), 0);
            Place(world, "flower1", new Vector3(0.6f, 0, -2.6f), 0);
            Place(world, "flower3", new Vector3(-2.2f, 0, -3.1f), 0);
            Place(world, "grass-turf1", new Vector3(1.4f, 0, -2.2f), 0);
            Place(world, "grass-turf3", new Vector3(-0.8f, 0, -3.6f), 0);

            // The eye, looking into the clearing, the gun in hand
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0, 0, 0);
            player.transform.rotation = Quaternion.Euler(0, 180, 0);
            var camera = MushroomScene.CreateCamera(scene, "Main Camera", 62, player.transform);
            camera.transform.localPosition = new Vector3(0, EyeHeight, 0);
            camera.transform.localRotation = Quaternion.Euler(4, 0, 0);
            camera.nearClipPlane = 0.02f;
            var hand = (GameObject)PrefabUtility.InstantiatePrefab(gun, scene);
            hand.transform.SetParent(camera.transform, false);
            hand.transform.localPosition = new Vector3(0.24f, -0.25f, 0.68f);
            hand.transform.localRotation = Quaternion.Euler(-2, -4, 0);

            PrefabUtility.InstantiatePrefab(gui, scene);
            var events = new GameObject("Event System", typeof(EventSystem), typeof(StandaloneInputModule));
            SceneManager.MoveGameObjectToScene(events, scene);

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (EditorBuildSettings.scenes.All(s => s.path != ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Append(new EditorBuildSettingsScene(ScenePath, true)).ToArray();
        }

        private static void Place(GameObject parent, string model, Vector3 position, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/{model}.prefab");
            if (prefab == null)
            {
                Debug.LogWarning($"SDF GUI: no prefab {model} for the GUI scene");
                return;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        }
    }
}
