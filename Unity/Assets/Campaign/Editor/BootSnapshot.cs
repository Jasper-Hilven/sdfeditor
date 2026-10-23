using System;
using System.IO;
using System.Linq;
using SdfGui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// Renders Boot's menus (ticket 35) to PNGs the way <see cref="SdfGui.Editor.GuiSnapshot"/> does for the GUI scene: the title (with the sample
    /// save, and with the campaign complete), Level Select, Story, Credits, Settings, the difficulty card and Confirm over the title, each with the
    /// desk diorama behind and the camera on that menu's shot, then the title, Level Select and Story again with the flat wall. Tools > SDF Campaign > Save
    /// Boot Snapshots; in batch <c>-executeMethod SdfCampaign.Editor.BootSnapshot.Capture -snapshotDir &lt;folder&gt;</c>.
    /// </summary>
    public static class BootSnapshot
    {
        [MenuItem("Tools/SDF Campaign/Save Boot Snapshots")]
        public static void CaptureFromMenu() => Shoot(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Temp", "Boot Snapshots"));

        public static void Capture()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-snapshotDir");
            Shoot(index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Path.GetTempPath(), "boot-snapshots"));
        }

        private static void Shoot(string directory)
        {
            Directory.CreateDirectory(directory);
            var scene = EditorSceneManager.OpenScene(BootBuilder.ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var flow = roots.Select(g => g.GetComponentInChildren<GuiFlow>(true)).First(f => f != null);
            var backdrop = roots.Select(g => g.GetComponentInChildren<MenuBackdrop>(true)).First(b => b != null);
            var before = GuiSettings.MenuBackdrop;
            var seconds = 3f;
            void Take(string name, GuiScreen screen, Action<GuiFlow> setup = null)
            {
                flow.Show(screen);
                setup?.Invoke(flow);
                backdrop.Snap(screen, seconds);
                SdfGui.Editor.GuiSnapshot.ShootOpen(scene, Path.Combine(directory, name + ".png"));
            }
            try
            {
                GuiSettings.MenuBackdrop = BackdropChoice.ForceDesk;
                Take("Title", GuiScreen.Title);
                Take("LevelSelect", GuiScreen.LevelSelect);
                Take("Story", GuiScreen.Story);
                Take("Credits", GuiScreen.Credits);
                Take("Ending", GuiScreen.Ending);
                // Cards over the title keep its shot
                Take("Settings", GuiScreen.Title, f => f.Show(GuiScreen.Settings));
                Take("Difficulty", GuiScreen.Title, f => f.Show(GuiScreen.Difficulty));
                Take("Confirm", GuiScreen.Title, f => f.Ask("Start the story again?\nYour medals and unlocked levels stay.", null));
                GuiCampaign.PreviewComplete = true;
                Take("TitleComplete", GuiScreen.Title);
                GuiCampaign.PreviewComplete = false;
                // Every arena won and every comic seen: all eight desk props, the full wall
                GuiCampaign.PreviewFull = true;
                Take("FullTitle", GuiScreen.Title);
                Take("FullLevelSelect", GuiScreen.LevelSelect);
                Take("FullStory", GuiScreen.Story);
                GuiCampaign.PreviewFull = false;
                GuiSettings.MenuBackdrop = BackdropChoice.ForceFlat;
                Take("FlatTitle", GuiScreen.Title);
                Take("FlatLevelSelect", GuiScreen.LevelSelect);
                Take("FlatStory", GuiScreen.Story);
            }
            finally
            {
                GuiCampaign.PreviewFull = false;
                GuiCampaign.PreviewComplete = false;
                GuiSettings.MenuBackdrop = before;
            }
            Debug.Log($"SDF Campaign: boot snapshots saved to {directory}");
        }
    }
}
