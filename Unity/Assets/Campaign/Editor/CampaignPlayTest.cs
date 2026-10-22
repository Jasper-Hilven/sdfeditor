using System;
using System.Collections.Generic;
using System.Linq;
using SdfBattle;
using SdfGui;
using SdfStory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// Plays Boot.unity without a window through the loader (ticket 32.4, menus 32.5): Boot, NEW CAMPAIGN and its difficulty card, the
    /// intro (skipped) into the Meadow with its setup, a win recorded by two captures, the end (slow-down, RED WINS! banner, Victory with
    /// the medal, RETRY in place, a second win and MENU; ticket 32.6), back to the title (CONTINUE and LEVEL SELECT now there, NEW CAMPAIGN
    /// asking first), CONTINUE on to Fungal, two losses (Defeat with Prime's taunt, RETRY in place, then RETRY EASIER), a pause and
    /// QUIT TO MENU, then Level Select, PLAY on the Meadow and a win whose CONTINUE goes back to Level Select. Checks the persistent GUI,
    /// the setup being spent, the time scale and the scene names after each step, and fails on any error logged. <c>Unity -batchmode -projectPath Unity -executeMethod
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
        private static bool sawIntro;
        private static bool sawSlow, sawBanner;
        private static float skillBefore;
        private static bool warmDone, sawCaption, sawBeatCaption;
        private static int beatPhase, replayPhase;
        private static string replayNext;

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
            if (step < 22 && now - started < 300)
                return;
            if (now - started >= 300)
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
                    Expect(flow.screen == GuiScreen.Title, "the title is up");
                    Expect(GuiFlow.Persistent == flow, "the GUI is the persistent one");
                    Expect(!runner.Progress.HasSave, "no save to start");
                    var title = flow.GetComponentInChildren<GuiTitle>(true);
                    Expect(!title.continueButton.activeSelf && !title.levelSelectButton.activeSelf, "no CONTINUE or LEVEL SELECT without a save");
                    Expect(title.progress.text == "", "no progress line without a save");
                    flow.Execute(GuiCommandId.NewCampaign, GuiScreen.Hud);
                    Expect(flow.screen == GuiScreen.Difficulty && !flow.ConfirmOpen, "NEW CAMPAIGN without a save opens the difficulty card");
                    flow.Execute(GuiCommandId.BeginCampaign, GuiScreen.Hud, null, (int)Difficulty.Normal);
                    return true;
                case 1: // the intro (comic panels, when the viewer is built), then the Meadow through the Loading screen, no click needed
                    if (Active == CampaignRunner.BootScene)
                    {
                        var intro = UnityEngine.Object.FindAnyObjectByType<StoryIntro>();
                        if (intro != null && intro.skip != null)
                        {
                            sawIntro = true;
                            Expect(!flow.GetComponent<Canvas>().enabled, "the GUI is hidden behind the intro");
                            intro.skip.onClick.Invoke();
                        }
                    }
                    if (Active != "Meadow" || runner.loader.Busy || Match.Current == null)
                        return Waiting(waited, "the Meadow");
                    var m = Match.Current;
                    Expect(flow.GetComponent<Canvas>().enabled, "the GUI is back after the intro");
                    Expect(runner.Progress.Seen(CampaignDefinition.Intro), "the intro counts as seen");
                    Expect(runner.Progress.Difficulty == Difficulty.Normal, "the card's difficulty is saved");
                    Expect(flow.screen == GuiScreen.Hud, "the HUD is up after the load");
                    Expect(MatchSetup.Pending == null, "the setup is spent");
                    Expect(m.Setup != null, "the match took the campaign's setup");
                    Expect(m.Players.Count == 6, $"3 v 3 in the Meadow, got {m.Players.Count}");
                    Expect(m.State.Rules.capturesToWin == 2, "first to 2 in the Meadow");
                    Expect(Mathf.Approximately(Time.timeScale, 1), "time runs");
                    Expect(m.GetComponent<WarmUpPlayer>() != null && m.Held && m.YouFree, "the Meadow's first play starts with the warm-up: held, and you free");
                    Expect(!m.Setup.sporesOn && Spores.Current != null && !Spores.Current.State.Enabled, "spores are off in the Meadow");
                    Expect(UnityEngine.Object.FindObjectsByType<GuiFlow>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
                        "only the persistent GUI, the scene's own is gone");
                    Expect(m.Hud != null && m.Hud.gameObject == flow.gameObject, "the HUD is on the persistent GUI");
                    return true;
                case 2: // the warm-up is cleared by doing it, then the clock runs; a win: two captures
                    if (!warmDone)
                    {
                        if (!DriveWarmUp(flow))
                            return false;
                        warmDone = true;
                        stepStarted = Time.realtimeSinceStartup;
                        return false;
                    }
                    if (waited < 1)
                        return false;
                    Expect(Match.Current.State.TimeLeft < Match.Current.State.Rules.roundSeconds, "the clock runs");
                    Match.Current.State.ReportCapture(Team.Red);
                    Match.Current.State.ReportCapture(Team.Red);
                    Expect(Match.Current.State.Over, "two captures end the Meadow");
                    Expect(runner.Progress.Won(CampaignDefinition.Meadow), "the win is recorded");
                    Expect(runner.Progress.BestMedal(CampaignDefinition.Meadow) != Medal.None, "a medal is recorded");
                    sawSlow = sawBanner = false;
                    return true;
                case 3: // the end: slow-down, banner, Victory; then RETRY in place
                    if (!WaitForEnd(flow, GuiScreen.Victory, waited, "Victory"))
                        return false;
                    var won = flow.Match.end;
                    Expect(won.won && won.redScore == 2 && won.blueScore == 0, $"the score 2-0 is on the end, got {won.redScore}-{won.blueScore}");
                    Expect(won.medal > 0 && won.newBest, "the first win is a new best with a medal");
                    Expect(won.captures == 0 && won.seconds > 0, "your captures (none by you) and the time are there");
                    Expect(flow.Match.arena == "Meadow", "the arena is named as the campaign does: " + flow.Match.arena);
                    var screen = flow.GetComponentInChildren<GuiEndScreen>(true);
                    Expect(screen.victory && screen.medal.activeSelf && screen.newBest.gameObject.activeSelf, "Victory shows the medal and NEW BEST");
                    var same = Match.Current;
                    flow.Execute(GuiCommandId.Retry, GuiScreen.Hud);
                    Expect(Match.Current == same && same.State.Score(Team.Red) == 0 && !same.State.Over, "RETRY starts the match over in place, no reload");
                    Expect(flow.screen == GuiScreen.Hud && !flow.Match.end.set, "back on the HUD");
                    Expect(Mathf.Approximately(Time.timeScale, 1), "time runs after RETRY");
                    var leftover = same.GetComponent<WarmUpPlayer>();
                    Expect((leftover == null || leftover.Over) && !same.Held, "a retry skips the warm-up and the match runs");
                    return true;
                case 4: // a second win, then out through MENU
                    if (waited < 0.5f)
                        return false;
                    Match.Current.State.ReportCapture(Team.Red);
                    Match.Current.State.ReportCapture(Team.Red);
                    sawSlow = sawBanner = false;
                    return true;
                case 5:
                    if (!WaitForEnd(flow, GuiScreen.Victory, waited, "Victory again"))
                        return false;
                    flow.Execute(GuiCommandId.QuitToMenu, GuiScreen.Hud);
                    return true;
                case 6: // back on the title
                    if (Active != CampaignRunner.BootScene || runner.loader.Busy)
                        return Waiting(waited, "Boot");
                    Expect(flow.screen == GuiScreen.Title, "the title is up again");
                    Expect(Mathf.Approximately(Time.timeScale, 1), "time runs on the title");
                    Expect(UnityEngine.Object.FindObjectsByType<CampaignRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
                        "one Campaign object after the Boot reload");
                    var saved = flow.GetComponentInChildren<GuiTitle>(true);
                    saved.Refresh();
                    Expect(saved.continueButton.activeSelf && saved.levelSelectButton.activeSelf, "CONTINUE and LEVEL SELECT after a win");
                    Expect(saved.progress.text.StartsWith("Arena 2 of 8"), "the progress line: " + saved.progress.text);
                    flow.Execute(GuiCommandId.NewCampaign, GuiScreen.Hud);
                    Expect(flow.ConfirmOpen && flow.screen == GuiScreen.Title, "NEW CAMPAIGN with a save asks first");
                    flow.Execute(GuiCommandId.Cancel, GuiScreen.Hud);
                    Expect(!flow.ConfirmOpen && flow.screen == GuiScreen.Title && runner.Progress.NextId == "beat-fungal", "NO leaves everything as it was");
                    flow.Execute(GuiCommandId.Continue, GuiScreen.Hud);
                    return true;
                case 7: // on to Fungal through its beat B1, then a first loss: blue takes three captures
                    if (Active != "Fungal" || runner.loader.Busy || Match.Current == null)
                        return Waiting(waited, "Fungal");
                    if (!DriveBeat(flow, waited, "beat-fungal", CampaignDefinition.Fungal))
                        return false;
                    Expect(runner.Current.id == CampaignDefinition.Fungal, "the current step is Fungal");
                    Expect(flow.screen == GuiScreen.Hud, "the HUD is up");
                    Expect(Match.Current.Players.Count == 8, $"4 v 4 in Fungal, got {Match.Current.Players.Count}");
                    Expect(Match.Current.Setup != null && Match.Current.Setup.SkillOf(Team.Blue) > 0.29f, "Fungal's blue skill");
                    Expect(Match.Current.Hud != null && Match.Current.Hud.gameObject == flow.gameObject, "the HUD is on the persistent GUI");
                    for (var i = 0; i < 3; i++)
                        Match.Current.State.ReportCapture(Team.Blue);
                    Expect(runner.Progress.LossStreak(CampaignDefinition.Fungal) == 1, "the loss is counted");
                    sawSlow = sawBanner = false;
                    return true;
                case 8: // Defeat with the first taunt and no RETRY EASIER; RETRY in place; a second loss
                    if (!WaitForEnd(flow, GuiScreen.Defeat, waited, "Defeat"))
                        return false;
                    var lost = flow.Match.end;
                    Expect(!lost.won && lost.blueScore == 3 && lost.medal == 0, "a loss 0-3 without a medal");
                    Expect(lost.taunt == Taunts.Pick(CampaignDefinition.Fungal, 1) && lost.taunt != "", "Prime's first taunt: " + lost.taunt);
                    Expect(!lost.canRetryEasier, "no RETRY EASIER after one loss");
                    var fungal = Match.Current;
                    flow.Execute(GuiCommandId.Retry, GuiScreen.Hud);
                    Expect(Match.Current == fungal && flow.screen == GuiScreen.Hud && !fungal.State.Over && fungal.State.Score(Team.Blue) == 0, "RETRY restarts Fungal in place");
                    skillBefore = fungal.Setup.SkillOf(Team.Blue);
                    return true;
                case 9: // a second loss (a moment after the restart: the GUI follows the phase from frame to frame)
                    if (waited < 0.5f)
                        return false;
                    for (var i = 0; i < 3; i++)
                        Match.Current.State.ReportCapture(Team.Blue);
                    Expect(runner.Progress.LossStreak(CampaignDefinition.Fungal) == 2, "two losses in a row");
                    sawSlow = sawBanner = false;
                    return true;
                case 10: // two losses: RETRY EASIER, blue a step clumsier
                    if (!WaitForEnd(flow, GuiScreen.Defeat, waited, "Defeat again"))
                        return false;
                    Expect(flow.Match.end.canRetryEasier, "RETRY EASIER after two losses in a row");
                    Expect(flow.Match.end.taunt == Taunts.Pick(CampaignDefinition.Fungal, 2), "the second taunt differs from the first");
                    var easier = flow.GetComponentsInChildren<GuiEndScreen>(true);
                    Expect(easier.Any(e => !e.victory && e.retryEasier.activeInHierarchy), "the button shows");
                    var again = Match.Current;
                    flow.Execute(GuiCommandId.RetryEasier, GuiScreen.Hud);
                    Expect(Match.Current == again && flow.screen == GuiScreen.Hud, "RETRY EASIER restarts in place");
                    Expect(runner.EasierSteps == 1, "one step easier");
                    Expect(Mathf.Abs(again.Setup.SkillOf(Team.Blue) - (skillBefore - DifficultyRules.EasierStep)) < 1e-4f, "blue is 0.1 clumsier");
                    return true;
                case 11: // a pause, then out
                    flow.Show(GuiScreen.Pause);
                    flow.Execute(GuiCommandId.QuitToMenu, GuiScreen.Hud);
                    return true;
                case 12: // back on the title, then Level Select
                    if (Active != CampaignRunner.BootScene || runner.loader.Busy)
                        return Waiting(waited, "Boot");
                    Expect(flow.screen == GuiScreen.Title, "the title again");
                    flow.Execute(GuiCommandId.Show, GuiScreen.LevelSelect);
                    var levels = flow.GetComponentInChildren<GuiLevelSelect>(true);
                    Expect(levels.cards[0].play.gameObject.activeSelf, "the Meadow can be played");
                    Expect(levels.cards[0].info.text.ToUpperInvariant().Contains("BRONZE") || levels.cards[0].info.text.ToUpperInvariant().Contains("SILVER") ||
                           levels.cards[0].info.text.ToUpperInvariant().Contains("GOLD"), "the Meadow shows its medal: " + levels.cards[0].info.text);
                    Expect(levels.cards[1].play.gameObject.activeSelf, "Fungal is unlocked by the Meadow");
                    Expect(!levels.cards[2].play.gameObject.activeSelf && levels.cards[2].cover.activeSelf, "the Chessboard is locked");
                    Expect(!levels.cards[3].play.gameObject.activeSelf && levels.cards[3].coverText.text == "Coming soon", "the Toy box has no scene yet");
                    levels.difficulty.Set((int)Difficulty.Hard, true);
                    Expect(runner.Progress.Difficulty == Difficulty.Hard, "the selector sets the difficulty");
                    flow.Execute(GuiCommandId.PlayLevel, GuiScreen.Hud, CampaignDefinition.Meadow);
                    return true;
                case 13: // a replay straight from Level Select: no warm-up, Hard difficulty on blue's skill; a win
                    if (Active != "Meadow" || runner.loader.Busy || Match.Current == null)
                        return Waiting(waited, "the Meadow again");
                    Expect(runner.Current.id == CampaignDefinition.Meadow, "PLAY loads the card's arena");
                    Expect(Match.Current.Setup != null && Match.Current.Setup.SkillOf(Team.Blue) > 0.29f, "Hard raises blue's skill");
                    Expect(flow.screen == GuiScreen.Hud, "the HUD is up");
                    Match.Current.State.ReportCapture(Team.Red);
                    Match.Current.State.ReportCapture(Team.Red);
                    sawSlow = sawBanner = false;
                    return true;
                case 14: // CONTINUE after a replay goes back to Level Select
                    if (!WaitForEnd(flow, GuiScreen.Victory, waited, "Victory in the replay"))
                        return false;
                    flow.Execute(GuiCommandId.Next, GuiScreen.Hud);
                    return true;
                case 15: // CONTINUE after a replay is back in Level Select; then B1 again from the Story gallery
                    if (Active != CampaignRunner.BootScene || runner.loader.Busy)
                        return Waiting(waited, "Boot");
                    Expect(flow.screen == GuiScreen.LevelSelect, "CONTINUE after a Level Select replay is back in Level Select, got " + flow.screen);
                    Expect(CampaignRunner.CanWatch(runner.Definition.Find("beat-fungal")), "a seen beat can be watched");
                    flow.Execute(GuiCommandId.WatchCutscene, GuiScreen.Hud, "beat-fungal");
                    return true;
                case 16: // the replay: held scene, the beat plays, a skip ends it, back in the Story gallery, nothing recorded
                    if (replayPhase == 0)
                    {
                        if (Active != "Fungal" || runner.loader.Busy || Match.Current == null || UnityEngine.Object.FindAnyObjectByType<BeatPlayer>() == null)
                            return Waiting(waited, "the replay of B1");
                        Expect(Match.Current.Held && flow.screen == GuiScreen.Cutscene, "the replay holds the match and shows the beat");
                        Expect(MatchSetup.Pending == null, "the replay's setup is spent");
                        replayNext = runner.Progress.NextId;
                        UnityEngine.Object.FindAnyObjectByType<BeatPlayer>().Sequencer.Skip();
                        replayPhase = 1;
                        return false;
                    }
                    if (Active != CampaignRunner.BootScene || runner.loader.Busy)
                        return Waiting(waited, "Boot after the replay");
                    Expect(flow.screen == GuiScreen.Story, "a replay ends in the Story gallery, got " + flow.screen);
                    Expect(runner.Progress.NextId == replayNext, "a replay records nothing");
                    flow.Execute(GuiCommandId.Continue, GuiScreen.Hud);
                    return true;
                case 17: // CONTINUE: B1 is seen, so Fungal loads straight to its Loading screen; a win
                    if (Active != "Fungal" || runner.loader.Busy || Match.Current == null)
                        return Waiting(waited, "Fungal to win");
                    Expect(!Match.Current.Held && flow.screen == GuiScreen.Hud && UnityEngine.Object.FindAnyObjectByType<BeatPlayer>() == null, "a seen beat does not play again");
                    for (var i = 0; i < 3; i++)
                        Match.Current.State.ReportCapture(Team.Red);
                    sawSlow = sawBanner = false;
                    return true;
                case 18: // Victory in Fungal; CONTINUE goes to B2
                    if (!WaitForEnd(flow, GuiScreen.Victory, waited, "Victory in Fungal"))
                        return false;
                    Expect(runner.Progress.Won(CampaignDefinition.Fungal) && runner.Progress.NextId == "beat-chessboard", "Fungal is won, B2 is next");
                    flow.Execute(GuiCommandId.Next, GuiScreen.Hud);
                    beatPhase = 0;
                    sawBeatCaption = false;
                    return true;
                case 19: // B2 in the Chessboard, then its match with the Chessboard's numbers
                    if (Active != "Chessboard" || runner.loader.Busy || Match.Current == null)
                        return Waiting(waited, "the Chessboard");
                    if (!DriveBeat(flow, waited, "beat-chessboard", CampaignDefinition.Chessboard))
                        return false;
                    Expect(Match.Current.Setup != null && Mathf.Abs(Match.Current.Setup.SkillOf(Team.Blue) - 0.55f) < 0.01f, "the Chessboard's blue skill (0.40 plus Hard's 0.15, chosen in Level Select)");
                    Expect(Match.Current.Players.Count == 8 && Match.Current.State.Rules.capturesToWin == 3, "4 v 4, first to 3");
                    for (var i = 0; i < 3; i++)
                        Match.Current.State.ReportCapture(Team.Red);
                    sawSlow = sawBanner = false;
                    return true;
                case 20: // Victory in the Chessboard: the third arena is won and saved
                    if (!WaitForEnd(flow, GuiScreen.Victory, waited, "Victory in the Chessboard"))
                        return false;
                    Expect(runner.Progress.Won(CampaignDefinition.Chessboard) && runner.Progress.BestMedal(CampaignDefinition.Chessboard) != Medal.None, "the Chessboard is won with a medal");
                    var reloaded = CampaignProgress.Load(runner.Definition, new PlayerPrefsCampaignStorage(), null);
                    Expect(reloaded.Won(CampaignDefinition.Chessboard) && reloaded.Seen("beat-chessboard"), "the save read back after a reload has the win and the beat");
                    return true;
                default:
                    return true;
            }
        }

        // The warm-up: each prompt is answered as the player would, the match is let go at the end and Mushroom 7 speaks; true when all that is seen
        private static bool DriveWarmUp(GuiFlow flow)
        {
            var m = Match.Current;
            var warm = m.GetComponent<WarmUpPlayer>();
            if (warm == null)
            {
                Expect(!m.Held, "the warm-up is over and the match let go");
                Expect(sawCaption, "the prompts showed");
                return true;
            }
            if (warm.Over)
            {
                Expect(!m.Held && !m.YouFree, "cleared prompts let the match go");
                Expect(flow.Match.caption.line == WarmUp.Closer && flow.Match.caption.speaker == WarmUp.CloserSpeaker, "Mushroom 7 says what to do: " + flow.Match.caption.line);
                return false;
            }
            Expect(m.Held && m.YouFree && flow.screen == GuiScreen.Hud, "held, you free, the HUD up");
            Expect(m.State.TimeLeft >= m.State.Rules.roundSeconds - 0.01f, "the clock stands still during the warm-up");
            var prompt = warm.Rules.Prompt;
            if (prompt == null)
                return false;
            if (flow.Match.caption.line == prompt.text)
            {
                sawCaption = true;
                Expect(flow.Shows(GuiScreen.Caption) && flow.Shows(GuiScreen.Hud), "the caption box shows over the HUD");
            }
            else
                return false;
            if ((prompt.needs & WarmUpAction.TakeMinigun) != 0)
                Expect(GameObject.Find("Minigun Arrow") != null, "an arrow marks the minigun pod");
            warm.Rules.Report(prompt.needs);
            return false;
        }

        // A beat in its battle scene (B1 in Fungal, B2 in the Chessboard): the scene is held under the Cutscene screen, lines show, a click moves on, a held skip goes to the hand-over, the click gives
        // the scene to the match; true when the beat has been handed over and checked
        private static bool DriveBeat(GuiFlow flow, float waited, string beatId, string battleId)
        {
            var player = UnityEngine.Object.FindAnyObjectByType<BeatPlayer>();
            switch (beatPhase)
            {
                case 0:
                    Expect(player != null, beatId + " plays after the load");
                    var m = Match.Current;
                    Expect(m.Held && !m.YouFree, "the match is held under the beat");
                    Expect(flow.screen == GuiScreen.Cutscene && !flow.Shows(GuiScreen.Hud), "the Cutscene screen is up without the HUD");
                    Expect(runner.Current.id == battleId && MatchSetup.Pending == null, "the battle is the current step, its setup spent");
                    Expect(!runner.Progress.Seen(beatId), "not seen until it is over");
                    Expect(m.Players.Count == 8, $"the real players are there, held: {m.Players.Count}");
                    beatPhase = 1;
                    return false;
                case 1: // lines show; a click moves on
                    Expect(player != null, "the beat is still playing");
                    if (flow.Match.caption.Visible)
                        sawBeatCaption = true;
                    Expect(Match.Current.Held, "the match stays held through the shots");
                    if (player.Sequencer.ShotIndex == 0 && player.Sequencer.ShotTime > 0.5f)
                    {
                        player.Sequencer.Advance();
                        Expect(player.Sequencer.ShotIndex == 1, "a click moves on to the next shot");
                        beatPhase = 2;
                    }
                    if (waited > 40)
                        throw new Exception("B1 did not get past its first shot");
                    return false;
                case 2: // a held skip: to the hand-over, then the click (automatic here) lets the match go
                    if (player != null && player.Sequencer.Stage == BeatStage.Shots)
                    {
                        player.Sequencer.Tick(SkipHold.Seconds + 0.05f, true);
                        Expect(player.Sequencer.Skipped && player.Sequencer.Stage == BeatStage.Arrival, "holding skip jumps to the hand-over");
                    }
                    if (player != null)
                        return false;
                    Expect(sawBeatCaption, "a caption line was shown");
                    var live = Match.Current;
                    Expect(!live.Held && flow.screen == GuiScreen.Hud, "the hand-over let the match go onto the HUD");
                    Expect(runner.Progress.Seen(beatId), "the beat is seen once it is over");
                    Expect(flow.Match.caption.line == "", "the caption is cleared");
                    Expect(live.You == null || live.You.GetComponentInChildren<Camera>().transform.localPosition.sqrMagnitude < 4, "your camera is back in your eyes");
                    beatPhase = 3;
                    return true;
                default:
                    return true;
            }
        }

        // The match's end as the GUI plays it: the time slows (seen on the way), the banner shows at full speed, then the screen; true on it
        private static bool WaitForEnd(GuiFlow flow, GuiScreen end, float waited, string what)
        {
            if (flow.Ending && flow.screen == GuiScreen.Hud && Time.timeScale < 0.5f)
                sawSlow = true;
            if (flow.screen == GuiScreen.MatchEnd)
            {
                sawBanner = true;
                Expect(Mathf.Approximately(Time.timeScale, 1), "the banner runs at full speed");
                var text = flow.GetComponentInChildren<GuiEndBanner>(true).label.text;
                Expect(text == (flow.Match.end.winnerBlue ? "BLUE WINS!" : "RED WINS!"), "the banner names the winner: " + text);
            }
            if (flow.screen != end)
                return Waiting(waited, what);
            Expect(sawSlow, "the time slowed down before the banner");
            Expect(sawBanner, "the banner showed before " + end);
            Expect(Mathf.Approximately(Time.timeScale, 1), "time runs on the end screen");
            Expect(Cursor.lockState != CursorLockMode.Locked && Cursor.visible, "the pointer is free for the buttons");
            return true;
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
            Debug.Log($"SDF Campaign play test: {errors.Count} errors (the intro viewer {(sawIntro ? "played and was skipped" : "is not built here, so the intro was passed over")})");
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }
    }
}
