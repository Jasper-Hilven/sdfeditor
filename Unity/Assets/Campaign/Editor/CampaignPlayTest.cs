using System;
using System.Collections.Generic;
using SdfBattle;
using SdfGui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// Plays Boot.unity without a window through the loader (ticket 32.4): Boot, PLAY into the Meadow with its setup, a win recorded by
    /// two captures, back to the title, then PLAY on to Fungal. Checks the persistent GUI, the setup being spent, the time scale and the
    /// scene names after each step, and fails on any error logged. <c>Unity -batchmode -projectPath Unity -executeMethod
    /// SdfCampaign.Editor.CampaignPlayTest.Run</c> (without -quit: it quits when done, exit code 1 on a failure). It clears the saved
    /// campaign first and again after.
    /// </summary>
    [InitializeOnLoad]
    public static class CampaignPlayTest
    {
        private const string ActiveKey = "SdfCampaign.PlayTest";

        private static readonly List<string> errors = new List<string>();
        private static int step;
        private static float stepStarted;
        private static float started = -1;
        private static CampaignRunner runner;

        static CampaignPlayTest()
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;
            SceneLoader.AutoPlay = true;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if ((type == LogType.Exception || type == LogType.Error) && !stack.Contains("UnityEditor.Search"))
                    errors.Add(message + "\n" + stack);
            };
            EditorApplication.update += Update;
        }

        public static void Run()
        {
            SessionState.SetBool(ActiveKey, true);
            PlayerPrefs.DeleteKey(CampaignProgress.Key);
            PlayerPrefs.DeleteKey(CampaignProgress.BrokenKey);
            EditorSceneManager.OpenScene(BootBuilder.ScenePath);
            EditorApplication.EnterPlaymode();
        }

        private static void Expect(bool ok, string what)
        {
            if (!ok)
                throw new Exception("Expected: " + what);
        }

        private static string Active => SceneManager.GetActiveScene().name;

        private static void Update()
        {
            if (!EditorApplication.isPlaying)
                return;
            var now = Time.realtimeSinceStartup;
            if (started < 0)
                started = stepStarted = now;
            runner = CampaignRunner.Instance;
            if (runner == null)
                return;
            try
            {
                if (!Advance(now - stepStarted))
                    return;
                Debug.Log($"SDF Campaign play test: step {step} done at {now - started:0.0}s (scene {Active})");
                step++;
                stepStarted = now;
            }
            catch (Exception exception)
            {
                errors.Add(exception.ToString());
                step = int.MaxValue;
            }
            if (step < 7 && now - started < 240)
                return;
            if (now - started >= 240)
                errors.Add($"Timed out in step {step}, scene {Active}");
            Finish();
        }

        // One step of the walk; true when it is done, false to look again next frame
        private static bool Advance(float waited)
        {
            var flow = runner.flow;
            switch (step)
            {
                case 0: // Boot shows the title
                    Expect(Active == CampaignRunner.BootScene, "starts in Boot");
                    Expect(flow.screen == GuiScreen.MainMenu, "the title is up");
                    Expect(GuiFlow.Persistent == flow, "the GUI is the persistent one");
                    Expect(!runner.Progress.HasSave, "no save to start");
                    flow.Execute(GuiCommandId.Play, GuiScreen.Hud);
                    return true;
                case 1: // into the Meadow, through the Loading screen, no click needed
                    if (Active != "Meadow" || runner.loader.Busy || Match.Current == null)
                        return Waiting(waited, "the Meadow");
                    var m = Match.Current;
                    Expect(flow.screen == GuiScreen.Hud, "the HUD is up after the load");
                    Expect(MatchSetup.Pending == null, "the setup is spent");
                    Expect(m.Setup != null, "the match took the campaign's setup");
                    Expect(m.Players.Count == 6, $"3 v 3 in the Meadow, got {m.Players.Count}");
                    Expect(m.State.Rules.capturesToWin == 2, "first to 2 in the Meadow");
                    Expect(Mathf.Approximately(Time.timeScale, 1), "time runs");
                    Expect(UnityEngine.Object.FindObjectsByType<GuiFlow>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
                        "only the persistent GUI, the scene's own is gone");
                    Expect(m.Hud != null && m.Hud.gameObject == flow.gameObject, "the HUD is on the persistent GUI");
                    return true;
                case 2: // a win: two captures
                    if (waited < 1)
                        return false;
                    Expect(Match.Current.State.TimeLeft < Match.Current.State.Rules.roundSeconds, "the clock runs");
                    Match.Current.State.ReportCapture(Team.Red);
                    Match.Current.State.ReportCapture(Team.Red);
                    Expect(Match.Current.State.Over, "two captures end the Meadow");
                    Expect(runner.Progress.Won(CampaignDefinition.Meadow), "the win is recorded");
                    Expect(runner.Progress.BestMedal(CampaignDefinition.Meadow) != Medal.None, "a medal is recorded");
                    runner.QuitToTitle();
                    return true;
                case 3: // back on the title
                    if (Active != CampaignRunner.BootScene || runner.loader.Busy)
                        return Waiting(waited, "Boot");
                    Expect(flow.screen == GuiScreen.MainMenu, "the title is up again");
                    Expect(Mathf.Approximately(Time.timeScale, 1), "time runs on the title");
                    Expect(UnityEngine.Object.FindObjectsByType<CampaignRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
                        "one Campaign object after the Boot reload");
                    flow.Execute(GuiCommandId.Play, GuiScreen.Hud);
                    return true;
                case 4: // on to Fungal
                    if (Active != "Fungal" || runner.loader.Busy || Match.Current == null)
                        return Waiting(waited, "Fungal");
                    Expect(runner.Current.id == CampaignDefinition.Fungal, "the current step is Fungal");
                    Expect(flow.screen == GuiScreen.Hud, "the HUD is up");
                    Expect(Match.Current.Players.Count == 8, $"4 v 4 in Fungal, got {Match.Current.Players.Count}");
                    Expect(Match.Current.Setup != null && Match.Current.Setup.SkillOf(Team.Blue) > 0.29f, "Fungal's blue skill");
                    Expect(Match.Current.Hud != null && Match.Current.Hud.gameObject == flow.gameObject, "the HUD is on the persistent GUI");
                    return true;
                case 5: // a retry in place, then out
                    runner.Retry();
                    Expect(Match.Current.State.Round > 0 && Match.Current.State.Score(Team.Red) == 0, "the retry starts the match over");
                    runner.QuitToTitle();
                    return true;
                default:
                    if (Active != CampaignRunner.BootScene || runner.loader.Busy)
                        return Waiting(waited, "Boot");
                    Expect(flow.screen == GuiScreen.MainMenu, "the title again");
                    return true;
            }
        }

        private static bool Waiting(float waited, string what)
        {
            if (waited > 60)
                throw new Exception($"Waited over a minute for {what}; scene {Active}");
            return false;
        }

        private static void Finish()
        {
            SessionState.EraseBool(ActiveKey);
            PlayerPrefs.DeleteKey(CampaignProgress.Key);
            foreach (var error in errors)
                Debug.Log("SDF Campaign play test error: " + error);
            Debug.Log($"SDF Campaign play test: {errors.Count} errors");
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }
    }
}
