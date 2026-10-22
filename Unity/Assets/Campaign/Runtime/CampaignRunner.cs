using System;
using SdfBattle;
using SdfGui;
using SdfStory;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfCampaign
{
    /// <summary>
    /// The campaign while the game runs (ticket 32.4), on the persistent Campaign object of Boot.unity next to the <see cref="SceneLoader"/>
    /// and the GUI. It holds the definition, the save, the current step and the attempt's skill offset (RETRY EASIER); starts the steps in
    /// order (<see cref="Continue"/>, <see cref="PlayLevel"/>, <see cref="Next"/>); hands each battle's setup to the match through
    /// <see cref="MatchSetup.Pending"/> and clears it once the match has read it; and records the match's end. Steps whose scene is not
    /// in the build are skipped (the save's <c>NextStep</c> does that, with a log here). Cutscenes and the credits wait for the tickets
    /// that make them (32.7, 32.8): <see cref="CutsceneHandler"/> is their hook, and without one such a step is seen at once.
    /// </summary>
    public sealed class CampaignRunner : MonoBehaviour
    {
        public const string BootScene = "Boot";

        public GuiFlow flow;
        public SceneLoader loader;

        /// <summary>
        /// Plays a cutscene step (or the credits) in its scene, once loaded: given the step and what to call when it is done, returns
        /// true when it took the step over.
        /// </summary>
        public static Func<CampaignStep, Action, bool> CutsceneHandler;

        public static CampaignRunner Instance { get; private set; }

        public CampaignDefinition Definition { get; private set; }

        public CampaignProgress Progress { get; private set; }

        /// <summary>The step being played, or the last one played.</summary>
        public CampaignStep Current { get; private set; }

        /// <summary>The setup of the battle being played, difficulty and RETRY EASIER included.</summary>
        public BattleSetup Setup { get; private set; }

        /// <summary>RETRY EASIER presses on this battle's attempt (0 to 2).</summary>
        public int EasierSteps { get; private set; }

        /// <summary>The battle just ended, with its result and, for a win, the medal; the Victory and Defeat screens (32.6) read it.</summary>
        public event Action<CampaignStep, MatchResult, WinResult?> BattleEnded;

        private Match match;
        private bool fromLevelSelect;

        private void Awake()
        {
            // Boot is loaded again to get back to the title; the first Campaign object stays
            if (Instance != null)
            {
                // Off at once, so its event system does not meet the first one's before it is gone
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (flow == null)
                flow = GetComponentInChildren<GuiFlow>(true);
            if (loader == null)
                loader = GetComponent<SceneLoader>();
            loader.flow = flow;
            Definition = CampaignDefinition.Default();
            Progress = CampaignProgress.Load(Definition, new PlayerPrefsCampaignStorage(), Available);
            if (Progress.LoadProblem != null)
                Debug.LogWarning($"Campaign: the save was not readable ({Progress.LoadProblem}); kept under {CampaignProgress.BrokenKey}, starting fresh");
            GuiFlow.Persistent = flow;
            GuiFlow.Campaign = OnCommand;
        }

        private void Start()
        {
            if (Instance == this && SceneManager.GetActiveScene().name == BootScene)
                ShowTitle();
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;
            Instance = null;
            GuiFlow.Persistent = null;
            GuiFlow.Campaign = null;
            MatchSetup.Pending = null;
        }

        private static bool Available(string scene) => Application.CanStreamedLevelBeLoaded(scene);

        // The menus' buttons that change what is played (the GUI holds no scene references; it hands them over here)
        private bool OnCommand(GuiCommandId command, string level, int value)
        {
            switch (command)
            {
                case GuiCommandId.Continue:
                    Continue();
                    return true;
                case GuiCommandId.BeginCampaign:
                    NewCampaign((Difficulty)Mathf.Clamp(value, 0, 2));
                    return true;
                case GuiCommandId.PlayLevel:
                    PlayLevel(level);
                    return true;
                case GuiCommandId.WatchCutscene:
                    WatchCutscene(level);
                    return true;
                case GuiCommandId.Restart:
                    Retry();
                    return true;
                case GuiCommandId.QuitToMenu:
                    QuitToTitle();
                    return true;
            }
            return false;
        }

        private void ShowTitle(GuiScreen screen = GuiScreen.Title)
        {
            Unbind();
            Time.timeScale = 1;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            MatchHud.Release(flow.gameObject);
            flow.Show(screen);
        }

        // ---------------------------------------------------------------- Going through the campaign

        /// <summary>Starts the story again at the given difficulty (wins and medals stay), then plays its first step.</summary>
        public void NewCampaign(Difficulty difficulty)
        {
            Progress.NewCampaign(difficulty);
            Continue();
        }

        /// <summary>Plays the first step not done whose scene is in the build.</summary>
        public void Continue()
        {
            var step = Progress.NextStep();
            if (step == null)
            {
                Debug.Log("Campaign: no step left to play with the scenes in the build");
                if (SceneManager.GetActiveScene().name == BootScene)
                    ShowTitle();
                else
                    QuitToTitle();
                return;
            }
            Skipped(step);
            fromLevelSelect = false;
            Run(step);
        }

        /// <summary>Plays one unlocked battle straight away (Level Select): no beat, no warm-up; the way on afterwards is back to the title.</summary>
        public void PlayLevel(string id)
        {
            var step = Definition.Find(id);
            if (step == null || !step.IsBattle || !Progress.IsUnlocked(id))
            {
                Debug.LogWarning($"Campaign: {id} is not a battle that can be played now");
                return;
            }
            fromLevelSelect = true;
            Run(step);
        }

        /// <summary>After a win: on to the next step (its beat), or, from Level Select, back to the title.</summary>
        public void Next()
        {
            if (fromLevelSelect)
                QuitToTitle();
            else
                Continue();
        }

        /// <summary>Starts the battle over: in place when the match can be (same team sizes), else the scene is loaded again.</summary>
        public void Retry(bool easier = false)
        {
            if (Current == null || !Current.IsBattle)
                return;
            if (easier)
                EasierSteps = Mathf.Min(EasierSteps + 1, DifficultyRules.MaxEasierSteps);
            var setup = MakeSetup(Current, retry: true);
            if (match != null && match.State != null && Setup != null && setup.blueSize == Setup.blueSize && setup.redSize == Setup.redSize)
            {
                Setup = setup;
                match.Retry(setup.ToMatchSetup());
                flow.Show(GuiScreen.Hud);
                Time.timeScale = 1;
                return;
            }
            LoadBattle(Current, setup);
        }

        /// <summary>Whether a step can be watched again from the Story gallery: the intro (comic panels) or a step with a cutscene player.</summary>
        public static bool CanWatch(CampaignStep step) => step.id == CampaignDefinition.Intro || CutsceneHandler != null && step.kind == StepKind.Cutscene;

        /// <summary>A replay from the Story gallery: nothing is recorded, and the way back is the gallery.</summary>
        public void WatchCutscene(string id)
        {
            var step = Definition.Find(id);
            if (step == null || step.IsBattle || !Progress.Seen(id) || !CanWatch(step))
            {
                Debug.LogWarning($"Campaign: {id} cannot be watched now");
                return;
            }
            if (step.id == CampaignDefinition.Intro)
            {
                PlayIntro(() => flow.Show(GuiScreen.Story));
                return;
            }
            loader.Load(step.scene, null, null, () =>
            {
                if (!CutsceneHandler(step, () => QuitToTitle(GuiScreen.Story)))
                    QuitToTitle(GuiScreen.Story);
            });
        }

        /// <summary>Leaves for the title screen (or, after a replay, the Story gallery); the campaign stays as saved (never mid-match).</summary>
        public void QuitToTitle(GuiScreen screen = GuiScreen.Title)
        {
            Unbind();
            MatchSetup.Pending = null;
            MatchHud.Release(flow.gameObject);
            // The pause screen holds the time and sound; let go before the fade, which runs on unscaled time
            Time.timeScale = 1;
            AudioListener.pause = false;
            if (SceneManager.GetActiveScene().name == BootScene)
            {
                ShowTitle(screen);
                return;
            }
            loader.Load(BootScene, null, () => ShowTitle(screen));
        }

        // ---------------------------------------------------------------- Steps

        private void Skipped(CampaignStep step)
        {
            var from = Definition.IndexOf(Progress.NextId);
            for (var i = Mathf.Max(0, from); i < Definition.Steps.Count && Definition.Steps[i] != step; i++)
                Debug.Log($"Campaign: skipping {Definition.Steps[i].id}, scene {Definition.Steps[i].scene} is not in the build");
        }

        private void Run(CampaignStep step)
        {
            Current = step;
            EasierSteps = 0;
            switch (step.kind)
            {
                case StepKind.Battle:
                    LoadBattle(step, MakeSetup(step, retry: false));
                    break;
                default:
                    PlayCutscene(step);
                    break;
            }
        }

        // The intro is 31's comic panels over Boot, no scene of its own: the GUI is hidden while they play
        private void PlayIntro(Action then)
        {
            var canvas = flow.GetComponent<Canvas>();
            canvas.enabled = false;
            StoryIntro.Begin(() =>
            {
                canvas.enabled = true;
                then();
            });
        }

        private void PlayCutscene(CampaignStep step)
        {
            if (step.id == CampaignDefinition.Intro)
            {
                PlayIntro(() => FinishCutscene(step));
                return;
            }
            if (CutsceneHandler == null && step.kind == StepKind.Credits)
            {
                // Not played, so not seen: the campaign must not count as complete before its credits exist (32.8)
                Debug.Log($"Campaign: {step.id} has no credits yet; back to the title");
                QuitToTitle();
                return;
            }
            if (CutsceneHandler == null)
            {
                Debug.Log($"Campaign: {step.id} has no cutscene player yet; counted as seen");
                Progress.CompleteCutscene(step.id);
                Continue();
                return;
            }
            loader.Load(step.scene, null, null, () =>
            {
                if (!CutsceneHandler(step, () => FinishCutscene(step)))
                    FinishCutscene(step);
            });
        }

        private void FinishCutscene(CampaignStep step)
        {
            Progress.CompleteCutscene(step.id);
            Continue();
        }

        private BattleSetup MakeSetup(CampaignStep step, bool retry)
        {
            var setup = DifficultyRules.Apply(step.setup, Progress.Difficulty, EasierSteps);
            // The warm-up is for a first play only: never on a retry, a replay or once the level is won
            setup.warmUp = step.setup.warmUp && !retry && !fromLevelSelect && !Progress.Won(step.id);
            return setup;
        }

        private void LoadBattle(CampaignStep step, BattleSetup setup)
        {
            Unbind();
            MatchHud.Release(flow.gameObject);
            Setup = setup;
            MatchSetup.Pending = setup.ToMatchSetup();
            var briefing = new LoadingBriefing
            {
                arena = step.title,
                goal = $"CAPTURE THE FLAG - FIRST TO {setup.capturesToWin}",
                red = setup.redNames,
                blue = setup.blueNames,
            };
            loader.Load(step.scene, briefing, Bind, () => flow.Show(GuiScreen.Hud));
        }

        // The scene is live: its match has read the setup in its Start, so the setup is spent
        private void Bind()
        {
            MatchSetup.Pending = null;
            match = Match.Current;
            if (match == null)
            {
                Debug.LogWarning($"Campaign: {Current?.scene} has no match; nothing to record");
                return;
            }
            if (match.Setup == null)
                Debug.LogWarning("Campaign: the match did not take the campaign's setup");
            match.Ended += OnMatchEnded;
        }

        private void Unbind()
        {
            if (match != null)
                match.Ended -= OnMatchEnded;
            match = null;
        }

        private void OnMatchEnded(MatchResult result)
        {
            var step = Current;
            if (step == null || !step.IsBattle)
                return;
            WinResult? win = null;
            if (result.Winner == Team.Red && result.RedScore > result.BlueScore)
            {
                win = Progress.RecordWin(step.id, result.RedScore, result.BlueScore, result.Seconds, Progress.Difficulty, EasierSteps > 0);
                Debug.Log($"Campaign: won {step.id} {result.RedScore}-{result.BlueScore} in {result.Seconds:0}s, {win.Value.medal}{(win.Value.newBest ? " (new best)" : "")}");
            }
            else
            {
                var streak = Progress.RecordLoss(step.id);
                Debug.Log($"Campaign: lost {step.id} {result.RedScore}-{result.BlueScore}, {streak} in a row");
            }
            BattleEnded?.Invoke(step, result, win);
        }
    }
}
