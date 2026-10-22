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
    /// Builds Assets/Scenes/Boot.unity, the first scene of the game (ticket 32.4): a camera over a plain backdrop and the persistent
    /// Campaign object with the <see cref="CampaignRunner"/>, the <see cref="SceneLoader"/>, an event system and the GUI prefab, on the
    /// title. Nothing is placed by hand; run again after the GUI prefab is rebuilt. Then it writes the build's scene list.
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

            // The backdrop is the GUI's own shapes over the night colour; the camera only has to clear to it
            var camera = MushroomScene.CreateCamera(scene, "Main Camera", 60);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = GuiPalette.Night;
            camera.cullingMask = 0;
            camera.gameObject.AddComponent<AudioListener>();

            var campaign = new GameObject("Campaign");
            SceneManager.MoveGameObjectToScene(campaign, scene);
            var runner = campaign.AddComponent<CampaignRunner>();
            var loader = campaign.AddComponent<SceneLoader>();

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(gui, scene);
            instance.transform.SetParent(campaign.transform, false);
            var flow = instance.GetComponent<GuiFlow>();
            flow.testPanel = false;
            flow.Show(GuiScreen.Title);
            // The title's view is not a match in play: no showcase bots going behind the menu
            instance.GetComponent<MatchView>().autoEvents = false;

            var events = new GameObject("Event System", typeof(EventSystem), typeof(StandaloneInputModule));
            events.transform.SetParent(campaign.transform, false);

            runner.flow = flow;
            runner.loader = loader;
            loader.flow = flow;

            EditorSceneManager.SaveScene(scene, ScenePath);
            CampaignBuildScenes.Update();
            AssetDatabase.SaveAssets();
            Debug.Log($"SDF Campaign: built {ScenePath}");
        }
    }
}
