using SdfCampaign;
using SdfGui;
using SdfGui.Editor;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// Builds Assets/Scenes/Boot.unity, the first scene of the game: a plain camera, the persistent Campaign object with the
    /// <see cref="GameLoop"/> and the <see cref="SceneLoader"/>, an event system and the GUI prefab. Boot goes straight into a match on the
    /// Meadow (ticket 42). Nothing is placed by hand; run again after the GUI prefab is rebuilt. Then it writes the build's scene list.
    /// <c>Unity -batchmode -quit -projectPath Unity -executeMethod SdfCampaign.Editor.BootBuilder.BuildInBatch</c>.
    /// </summary>
    public static class BootBuilder
    {
        public const string ScenePath = "Assets/Scenes/Boot.unity";

        [MenuItem("Tools/SDF Campaign/Rebuild Boot Scene")]
        public static void BuildFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                Build();
        }

        public static void BuildInBatch() => Build();

        public static void Build()
        {
            var gui = AssetDatabase.LoadAssetAtPath<GameObject>(GuiScene.GuiPrefabPath);
            if (gui == null)
            {
                Debug.LogError($"SDF Campaign: no GUI prefab at {GuiScene.GuiPrefabPath}; run Tools > SDF GUI > Rebuild Prefabs and Scene first");
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = MushroomScene.CreateCamera(scene, "Main Camera", 48);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = GuiPalette.Night;
            camera.gameObject.AddComponent<AudioListener>();

            var campaign = new GameObject("Campaign");
            SceneManager.MoveGameObjectToScene(campaign, scene);
            var loop = campaign.AddComponent<GameLoop>();
            var loader = campaign.AddComponent<SceneLoader>();

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(gui, scene);
            instance.transform.SetParent(campaign.transform, false);
            var flow = instance.GetComponent<GuiFlow>();
            flow.testPanel = false;
            flow.Show(GuiScreen.Hud);
            // The boot's view is not a match in play: no showcase bots going behind the scene
            instance.GetComponent<MatchView>().autoEvents = false;

            var events = new GameObject("Event System", typeof(EventSystem), typeof(StandaloneInputModule));
            events.transform.SetParent(campaign.transform, false);

            loop.flow = flow;
            loop.loader = loader;
            loader.flow = flow;

            EditorSceneManager.SaveScene(scene, ScenePath);
            CampaignBuildScenes.Update();
            AssetDatabase.SaveAssets();
            Debug.Log($"SDF Campaign: built {ScenePath}");
        }
    }
}
