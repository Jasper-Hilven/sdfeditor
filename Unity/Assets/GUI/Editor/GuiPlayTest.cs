using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SdfGui.Editor
{
    /// <summary>
    /// Plays the GUI scene without a window and drives it through a round with the test panel's commands (the bots running on their
    /// own meanwhile), saving a snapshot after each step and failing on any exception logged: <c>Unity -batchmode -projectPath Unity
    /// -executeMethod SdfGui.Editor.GuiPlayTest.Run -snapshotDir &lt;folder&gt;</c> (without -quit: it quits when done, with exit code 1
    /// if anything threw).
    /// </summary>
    [InitializeOnLoad]
    public static class GuiPlayTest
    {
        private const string DirectoryKey = "SdfGui.PlayTest.Directory";

        private static readonly (float at, string name, Action<GuiFlow> step)[] Steps =
        {
            (0.05f, "01 start", _ => { }),
            (1.2f, "02 firing", f => { f.Match.Fire(0.4f); f.Execute(GuiCommandId.YouKill, GuiScreen.Hud); }),
            (1.4f, "03 hit", f => { f.Execute(GuiCommandId.Damage, GuiScreen.Hud); f.Execute(GuiCommandId.TogglePickup, GuiScreen.Hud); }),
            (1.6f, "04 reload", f => f.Execute(GuiCommandId.Reload, GuiScreen.Hud)),
            (2.2f, "05 scoreboard", f => f.Show(GuiScreen.Scoreboard)),
            (2.4f, "06 pause", f => f.Show(GuiScreen.Pause)),
            (2.6f, "07 play from the menu", f => f.Execute(GuiCommandId.Play, GuiScreen.Hud)),
            (3.0f, "08 new round", f => f.Execute(GuiCommandId.StartNow, GuiScreen.Hud)),
            (3.2f, "09 died", f => f.Execute(GuiCommandId.Die, GuiScreen.Hud)),
            (3.4f, "10 spectating", f => f.Execute(GuiCommandId.Spectate, GuiScreen.Hud)),
            (3.6f, "11 won", f => { f.Execute(GuiCommandId.StartNow, GuiScreen.Hud); f.Execute(GuiCommandId.Win, GuiScreen.Hud); }),
            (3.8f, "12 settings", f => f.Show(GuiScreen.Settings)),
            (4.0f, "13 main menu", f => f.Show(GuiScreen.MainMenu)),
            (4.2f, "14 pause", f => { f.Show(GuiScreen.Hud); f.Show(GuiScreen.Pause); }),
            (4.4f, "15 resume", f => { Expect(Time.timeScale == 0, "Pause stops the time"); f.Execute(GuiCommandId.Resume, GuiScreen.Hud); }),
            (4.6f, "16 playing again", _ => Expect(Time.timeScale == 1, "Resume sets the time going again")),
        };

        private static void Expect(bool ok, string what)
        {
            if (!ok)
                throw new Exception("Expected: " + what);
        }

        private static float started = -1;
        private static int next;
        private static readonly List<string> errors = new List<string>();

        static GuiPlayTest()
        {
            if (string.IsNullOrEmpty(SessionState.GetString(DirectoryKey, "")))
                return;
            Application.logMessageReceived += (message, stack, type) =>
            {
                // Unity's own search indexer throws at start-up without a window; only what the GUI does counts
                if ((type == LogType.Exception || type == LogType.Error) && !stack.Contains("UnityEditor.Search"))
                    errors.Add(message + "\n" + stack);
            };
            EditorApplication.update += Update;
        }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-snapshotDir");
            var directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Path.GetTempPath(), "gui-play");
            Directory.CreateDirectory(directory);
            SessionState.SetString(DirectoryKey, directory);
            EditorSceneManager.OpenScene(GuiScene.ScenePath);
            EditorApplication.EnterPlaymode();
        }

        private static void Update()
        {
            if (!EditorApplication.isPlaying)
                return;
            if (started < 0)
                started = Time.realtimeSinceStartup;
            var elapsed = Time.realtimeSinceStartup - started;
            var flow = UnityEngine.Object.FindAnyObjectByType<GuiFlow>();
            if (flow == null || next >= Steps.Length || elapsed < Steps[next].at)
                return;
            var directory = SessionState.GetString(DirectoryKey, "");
            try
            {
                Steps[next].step(flow);
                GuiSnapshot.ShootOpen(flow.gameObject.scene, Path.Combine(directory, Steps[next].name + ".png"));
            }
            catch (Exception exception)
            {
                errors.Add(exception.ToString());
            }
            next++;
            if (next < Steps.Length)
                return;
            SessionState.EraseString(DirectoryKey);
            foreach (var error in errors)
                Debug.Log("SDF GUI play test error: " + error);
            Debug.Log($"SDF GUI play test: {Steps.Length} steps, {errors.Count} errors, snapshots in {directory}");
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }
    }
}
