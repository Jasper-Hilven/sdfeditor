using System;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SdfLevel.Editor
{
    /// <summary>
    /// Everything Unity builds from the pipeline's output, in the order the builders need each other: after `sdf unity` (the
    /// `Update to Unity (256)` run config), this one command replaces the per-tool rebuild menus. Model prefabs first, then the
    /// prefabs built over them (pods, flag stands, machines, Prime, the Prime-copter), then the scenes (levels, home, effects, GUI,
    /// the intro viewer, Boot), the build scene list, and last the navmesh checks, whose reports go to the console.
    /// </summary>
    public static class UpdateAll
    {
        [MenuItem("Tools/SDF/Update All", priority = 0)]
        public static void UpdateFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Run();
        }

        /// <summary>For batch mode (<c>-executeMethod SdfLevel.Editor.UpdateAll.UpdateInBatch</c>): exits with 1 if a step failed.</summary>
        public static void UpdateInBatch()
        {
            if (Run() > 0)
                EditorApplication.Exit(1);
        }

        /// <summary>Runs every step, going on after one fails; returns how many failed.</summary>
        public static int Run()
        {
            var failed = 0;
            void Step(string name, Func<bool> step)
            {
                EditorUtility.DisplayProgressBar("SDF Update All", name, 0);
                try
                {
                    if (!step())
                    {
                        failed++;
                        Debug.LogError($"SDF Update All: {name} failed");
                    }
                }
                catch (Exception exception)
                {
                    failed++;
                    Debug.LogError($"SDF Update All: {name} failed: {exception.Message}\n{exception.StackTrace}");
                }
            }
            void Do(string name, Action step) => Step(name, () => { step(); return true; });

            try
            {
                AssetDatabase.Refresh();
                Do("render pipeline", RenderPipelineSetup.Ensure);
                Do("model prefabs", MushroomBuilder.BuildStale);
                Do("weapon pods", SdfPickups.Editor.WeaponPodBuilder.BuildAll);
                Do("flag stands", SdfPickups.Editor.FlagStandBuilder.BuildAll);
                Do("machines", SdfMachines.Editor.MachineBuilder.BuildAll);
                Do("Prime", SdfPrime.Editor.PrimeBuilder.BuildAll);
                Do("Prime-copter", SdfMachines.Editor.PrimeCopterBuilder.BuildAll);
                foreach (var level in LevelBuilder.Levels())
                    Step($"level {level}", () => LevelBuilder.Build(level));
                Step("home", SdfStory.Editor.HomeBuilder.Build);
                Do("special effects", SdfEffects.Editor.SpecialEffectsScene.Build);
                // The GUI renders the weapon and scout icons: without a graphics device (-nographics) they come out blank
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    Debug.LogWarning("SDF Update All: no graphics device, so the GUI and its icons are left as they are (run without -nographics)");
                else
                    Do("GUI", SdfGui.Editor.GuiScene.BuildInBatch);
                Do("intro viewer", SdfStory.Editor.StoryIntroBuilder.Build);
                Do("Boot", SdfCampaign.Editor.BootBuilder.Build);
                Do("build scene list", SdfCampaign.Editor.CampaignBuildScenes.Update);
                Do("navmesh check", SdfBattle.Editor.NavmeshCheck.CheckAll);
                // The hob check builds its own floor in the open scene, so it gets an empty one
                Do("hob navmesh check", () =>
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    SdfBattle.Editor.HeatNavmeshCheck.Check();
                });
                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Debug.Log(failed == 0 ? "SDF Update All: done" : $"SDF Update All: done, {failed} step(s) failed (see above)");
            return failed;
        }
    }
}
