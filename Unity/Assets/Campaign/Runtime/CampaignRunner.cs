using System;
using SdfBattle;
using SdfPlatform;
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
    /// in the build are skipped (the save's <c>NextStep</c> does that, with a log here). The story beats (32.7) play in their battle's
    /// scene, loaded once for the beat and its match: as comic panels through the story viewer when the beat's panels are rendered
    /// (<see cref="ComicBeatPlayer"/>, ticket 34), else as shots (<see cref="BeatPlayer"/>); see <see cref="Beats.FormOf"/>. The meadow's first play starts with its warm-up
    /// (<see cref="WarmUpPlayer"/>). The ending (its comic when rendered, else a beat of the same sequencer, the Ending screen) and the credits (<see cref="CreditsPlayer"/>,
    /// the Credits screen) play over Boot's backdrop (ticket 32.8), since Home.unity is editor-only; completing the credits saves the campaign
    /// as complete and goes to the title.
    /// </summary>
    public sealed class CampaignRunner : MonoBehaviour
    {
        public const string BootScene = "Boot";

        public GuiFlow flow;
        public SceneLoader loader;

        /// <summary>
        /// A hook for a cutscene step that is neither a beat of <see cref="Beats"/>, the intro, the ending nor the credits (none today): played
        /// in its scene, once loaded; given the step and what to call when it is done, returns true when it took the step over.
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

        /// <summary>The battle just ended, with its result and, for a win, the medal (already written into the GUI's <c>MatchView.end</c> for Victory and Defeat).</summary>
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
            ContentStreamer.Install();
            Definition = CampaignDefinition.Default();
            Progress = CampaignProgress.Load(Definition, new PlayerPrefsCampaignStorage(), Available);
            if (Progress.LoadProblem != null)
                Debug.LogWarning($"Campaign: the save was not readable ({Progress.LoadProblem}); kept under {CampaignProgress.BrokenKey}, starting fresh");
            GuiFlow.Persistent = flow;
            GuiFlow.Campaign = OnCommand;
            // The boss fight's own beats (ticket 33.8) play through this sequencer: its transitions, and the victory before the Victory screen
            Boss.PlayBeat = PlayBossBeat;
            GuiFlow.BeforeVictory = PlayBossVictory;
            // The ad break (when ads are allowed, WebPortal.AdsAllowed): just after a finished level, before its Victory or Defeat screen
            GuiFlow.AfterLevel = then => WebPortal.MidgameBreak(then);
            // The web portal (ticket 36): its SDK starts here, and on the portal the save moves to the portal's data store once it is up
            WebPortal.Init(OnPortalReady);
        }

        // On the portal the save is read from its data store (an older local save is found behind it); only before a step has started, so nothing in play changes
        private void OnPortalReady()
        {
            if (this == null || !WebPortal.CloudSave || Current != null)
                return;
            Progress = CampaignProgress.Load(Definition, new LayeredCampaignStorage(new PortalCampaignStorage(), new PlayerPrefsCampaignStorage()), Available);
            if (Progress.LoadProblem != null)
                Debug.LogWarning($"Campaign: the portal's save was not readable ({Progress.LoadProblem}); kept under {CampaignProgress.BrokenKey}, starting fresh");
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
            Boss.PlayBeat = null;
            GuiFlow.BeforeVictory = null;
            GuiFlow.AfterLevel = null;
            MatchSetup.Pending = null;
        }

        // A streamed arena (the web build, ticket 38) counts as available: it is fetched when the campaign goes there
        private static bool Available(string scene) => ContentStreamer.CanLoad(scene);

        // The player is in a battle: fetch the next arena and its comic in the background, so the Loading screen rarely waits
        private void PrefetchAhead()
        {
            if (!ContentStreamer.Enabled || Current == null)
                return;
            ContentStreamer.Prefetch(StreamPlan.Upcoming(Definition, Current.id, ContentStreamer.Manifest));
        }

        // The menus' buttons that change what is played (the GUI holds no scene references; it hands them over here)
        private bool OnCommand(GuiCommandId command, string level, int value)
        {
            // An ad is up: the buttons wait for it
            if (WebPortal.AdPlaying)
                return true;
            return Command(command, level, value);
        }

        private bool Command(GuiCommandId command, string level, int value)
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
                case GuiCommandId.Retry:
                    Retry();
                    return true;
                case GuiCommandId.RestartFight:
                    Retry(wholeFight: true);
                    return true;
                case GuiCommandId.RetryEasier:
                    Retry(easier: true);
                    return true;
                case GuiCommandId.Next:
                    Next();
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

        /// <summary>
        /// For checking the streamed web build (ticket 38) in a browser: <c>unityInstance.SendMessage("Campaign", "StreamCheck", "beat-fungal")</c>
        /// runs that step (a battle or a beat) as if the campaign had reached it. Only on a page whose URL has <c>streamcheck</c> in it.
        /// </summary>
        public void StreamCheck(string stepId)
        {
            if (!Application.absoluteURL.Contains("streamcheck"))
                return;
            var step = Definition.Find(stepId);
            if (step == null)
                return;
            Debug.Log($"Campaign: stream check runs {stepId}");
            Current = step;
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

        /// <summary>After a win: on to the next step (its beat), or, from a replay in Level Select, back to Level Select.</summary>
        public void Next()
        {
            if (fromLevelSelect)
                QuitToTitle(GuiScreen.LevelSelect);
            else
                Continue();
        }

        /// <summary>
        /// Starts the battle over: in place when the match can be (same team sizes), else the scene is loaded again. With
        /// <paramref name="easier"/> (RETRY EASIER) blue's skill drops by one more step, two at most. The boss fight (ticket 33.7) always
        /// goes on in place: RETRY PHASE starts the phase it got to again, <paramref name="wholeFight"/> (RESTART FIGHT, and RETRY after
        /// a win) the fight from phase 1; RETRY EASIER gives Prime Easy's damage and wind-ups on top of the chosen difficulty.
        /// </summary>
        public void Retry(bool easier = false, bool wholeFight = false)
        {
            if (Current == null || !Current.IsBattle)
                return;
            if (easier)
                EasierSteps = Mathf.Min(EasierSteps + 1, DifficultyRules.MaxEasierSteps);
            var setup = MakeSetup(Current, retry: true);
            StopWarmUp();
            var boss = Setup != null && Setup.IsBoss && match != null && match.State != null ? Boss.Current : null;
            if (boss != null)
            {
                Setup = setup;
                if (setup.retryEasier)
                    boss.Rules.MakeEasier();
                if (wholeFight || boss.State.Phase == BossPhase.Victory)
                    boss.RestartFight();
                else
                    boss.RetryPhase();
                flow.Show(GuiScreen.Hud);
                Time.timeScale = 1;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }
            if (match != null && match.State != null && Setup != null && setup.blueSize == Setup.blueSize && setup.redSize == Setup.redSize)
            {
                Setup = setup;
                match.Retry(setup.ToMatchSetup());
                flow.Show(GuiScreen.Hud);
                Time.timeScale = 1;
                // The click on the button is the gesture a browser asks for; the match can be played at once
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }
            LoadBattle(Current, setup);
        }

        // The ending and the credits play over Boot's backdrop
        private static bool OverBoot(CampaignStep step) => step.id == CampaignDefinition.Ending || step.kind == StepKind.Credits;

        /// <summary>Whether a step can be watched again from the Story gallery: the intro (comic panels), a beat (panels or shots), the ending, the credits or a step with a cutscene player.</summary>
        public static bool CanWatch(CampaignStep step) =>
            step.id == CampaignDefinition.Intro || FormOf(step) != BeatForm.None || OverBoot(step) || CutsceneHandler != null && step.kind == StepKind.Cutscene;

        /// <summary>How a cutscene step's beat plays (ticket 34): its comic when the story viewer has its panels, else its shots, else none.</summary>
        public static BeatForm FormOf(CampaignStep step) =>
            step.IsBattle || step.id == CampaignDefinition.Intro ? BeatForm.None : Beats.FormOf(step.id, StoryIntro.HasComic(step.id));

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
                PlayComic(step.id, () => flow.Show(GuiScreen.Story));
                return;
            }
            if (OverBoot(step))
            {
                PlayOverBoot(step, () => QuitToTitle(GuiScreen.Story));
                return;
            }
            switch (FormOf(step))
            {
                // A beat's panels need no scene behind them: replayed here, over the gallery's backdrop, as the intro is
                case BeatForm.Comic:
                    flow.Show(GuiScreen.Cutscene);
                    PlayComic(step.id, () => flow.Show(GuiScreen.Story));
                    return;
                case BeatForm.Shots:
                    PlayBeat(step, Beats.Find(step.id), replay: true);
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

        // A comic over what is up, no scene of its own (the intro over Boot, a replayed beat over the gallery): the GUI is hidden while it plays
        private void PlayComic(string comic, Action then)
        {
            // A streamed comic (the web build) may still be on its way: the Loading screen waits for it
            if (ContentStreamer.Needs(StreamManifest.ComicKey(comic)))
            {
                loader.Ensure(StreamManifest.ComicKey(comic), "Comic", ok =>
                {
                    if (ok)
                        PlayComic(comic, then);
                    else
                        then();
                });
                return;
            }
            var canvas = flow.GetComponent<Canvas>();
            canvas.enabled = false;
            StoryIntro.Play(comic, () =>
            {
                canvas.enabled = true;
                then();
            });
        }

        private void PlayCutscene(CampaignStep step)
        {
            if (step.id == CampaignDefinition.Intro)
            {
                PlayComic(step.id, () => FinishCutscene(step));
                return;
            }
            if (OverBoot(step))
            {
                // After the credits the campaign is complete and saved; the title shows the crown
                PlayOverBoot(step, () =>
                {
                    if (step.kind == StepKind.Credits)
                    {
                        Progress.CompleteCutscene(step.id);
                        WebPortal.HappyTime();
                        QuitToTitle();
                    }
                    else
                        FinishCutscene(step);
                });
                return;
            }
            switch (FormOf(step))
            {
                case BeatForm.Comic:
                    PlayBeat(step, null, replay: false);
                    return;
                case BeatForm.Shots:
                    PlayBeat(step, Beats.Find(step.id), replay: false);
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

        /// <summary>
        /// The ending (a beat over the Ending screen) or the credits, over Boot's backdrop of GUI shapes: from another scene (the boss) Boot is
        /// loaded behind the fade first, from Boot itself (the credits after the ending, a replay from the Story gallery) it starts at once.
        /// <paramref name="then"/> runs when it is over or skipped.
        /// </summary>
        private void PlayOverBoot(CampaignStep step, Action then)
        {
            Unbind();
            MatchSetup.Pending = null;
            MatchHud.Release(flow.gameObject);
            Time.timeScale = 1;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            BeatPlayer ending = null;
            // The ending's comic, once its panels are rendered (ticket 34), else its captions over the Ending screen
            var comic = step.kind != StepKind.Credits && StoryIntro.HasComic(step.id);
            void Prepare()
            {
                if (step.kind != StepKind.Credits && !comic)
                    ending = BeatPlayer.Prepare(Beats.Find(step.id), BeatWorld.Backdrop(), flow, then, handOver: false, GuiScreen.Ending);
            }
            void Begin()
            {
                if (step.kind == StepKind.Credits)
                    CreditsPlayer.Begin(flow, then);
                else if (comic)
                    PlayComic(step.id, then);
                else
                    ending.Begin();
            }
            if (SceneManager.GetActiveScene().name == BootScene)
            {
                Prepare();
                Begin();
                return;
            }
            loader.Load(BootScene, null, Prepare, Begin);
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
                // The first arena says what the game is before it starts: capture the flag, and what capturing is
                tip = setup.warmUp ? WarmUp.HowToScore : "",
            };
            loader.Load(step.scene, briefing, Bind, () =>
            {
                flow.Show(GuiScreen.Hud);
                BeginMatch();
            });
        }

        // ---------------------------------------------------------------- Beats

        // The battle that follows a beat in the same scene: the beat's scene is loaded once for both, no reload
        private CampaignStep BattleAfter(CampaignStep step)
        {
            var next = Definition.IndexOf(step.id) + 1;
            if (next <= 0 || next >= Definition.Steps.Count)
                return null;
            var battle = Definition.Steps[next];
            return battle.IsBattle && battle.scene == step.scene ? battle : null;
        }

        /// <summary>
        /// Plays a beat in its battle's scene: the scene is loaded with the battle's setup, held, the beat plays over it, and its
        /// hand-over (CLICK TO PLAY) lets the match go (or starts the warm-up). Without <paramref name="beat"/> the beat is its comic (ticket 34):
        /// the panels show over the held scene from the moment it is loaded, so the black of the load cuts straight to the first panel. A replay
        /// (the Story gallery; shots only, a comic replays over the gallery) records nothing and goes back to the gallery when done.
        /// </summary>
        private void PlayBeat(CampaignStep step, Beat beat, bool replay)
        {
            var battle = BattleAfter(step);
            if (battle == null)
            {
                Debug.LogWarning($"Campaign: {step.id} has no battle after it in {step.scene}; counted as seen");
                if (replay)
                    QuitToTitle(GuiScreen.Story);
                else
                    FinishCutscene(step);
                return;
            }
            Unbind();
            MatchHud.Release(flow.gameObject);
            if (!replay)
            {
                // From here the battle is the current step: the match records its end, the scoreboard names its arena
                Current = battle;
                EasierSteps = 0;
                fromLevelSelect = false;
            }
            var setup = MakeSetup(battle, retry: false);
            if (replay)
                setup.warmUp = false;
            Setup = replay ? Setup : setup;
            var held = setup.ToMatchSetup();
            held.held = true;
            held.warmUp = false;
            MatchSetup.Pending = held;
            BeatPlayer player = null;
            ComicBeatPlayer panels = null;
            loader.Load(step.scene, null, () =>
            {
                if (replay)
                {
                    MatchSetup.Pending = null;
                    match = null;
                }
                else
                    Bind();
                var live = Match.Current;
                if (live == null)
                    return;
                if (beat == null)
                {
                    panels = ComicBeatPlayer.Play(step.id, live, flow, () =>
                    {
                        Progress.CompleteCutscene(step.id);
                        BeginMatch();
                    });
                    return;
                }
                player = BeatPlayer.Prepare(beat, BeatWorld.Of(live), flow, () =>
                {
                    if (replay)
                    {
                        QuitToTitle(GuiScreen.Story);
                        return;
                    }
                    Progress.CompleteCutscene(step.id);
                    BeginMatch();
                }, handOver: !replay);
            }, () =>
            {
                // A comic is showing since the load; shots start now
                if (player != null)
                    player.Begin();
                else if (panels == null)
                {
                    // No match in the scene to play over: the beat is passed over
                    Debug.LogWarning($"Campaign: {step.scene} has no match; {step.id} not played");
                    if (replay)
                        QuitToTitle(GuiScreen.Story);
                    else
                    {
                        Progress.CompleteCutscene(step.id);
                        flow.Show(GuiScreen.Hud);
                    }
                }
            });
        }

        // ---------------------------------------------------------------- The boss fight's beats (ticket 33.8)

        /// <summary>
        /// Plays one of the boss fight's beats (<see cref="BossBeats"/>) in the arena over the running fight: the camera moves, the captions
        /// show and the shots' cues go to the fight (<c>cue</c>), which holds the match meanwhile. There is no hand-over click: the fight
        /// is on the HUD again as it is over or skipped (the victory goes on to the Victory screen instead), and <c>done</c> is told
        /// whether it was skipped.
        /// </summary>
        private bool PlayBossBeat(string id, Action<string, Func<string, Vector3?>> cue, Action<bool> done) =>
            BeatPlayer.PlayBossBeat(flow, match != null ? match : Match.Current, id, cue, done);

        // GuiFlow's hook: the banner is over; the final boss's victory beat plays before the Victory screen
        private bool PlayBossVictory(Action then)
        {
            var boss = Boss.Current;
            return boss != null && Current != null && Current.setup != null && Current.setup.IsBoss && boss.PlayVictoryBeat(then);
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
            // The scoreboard's subtitle and the end screens name the arena as the campaign does
            if (match.Hud != null && match.Hud.view != null && Current != null)
                match.Hud.view.arena = Current.title;
        }

        private void Unbind()
        {
            StopWarmUp();
            if (flow != null && flow.Match != null)
                flow.Match.caption.Clear();
            if (match != null)
                match.Ended -= OnMatchEnded;
            match = null;
        }

        private void StopWarmUp()
        {
            if (match != null && match.TryGetComponent<WarmUpPlayer>(out var warmUp))
                warmUp.Stop();
        }

        // The match starts: the meadow's first play begins with its warm-up (the match is held), else it just runs
        private void BeginMatch()
        {
            PrefetchAhead();
            if (match == null)
                return;
            if (Setup != null && Setup.warmUp)
                WarmUpPlayer.Begin(match, flow);
            else
                match.Release();
        }

        private void OnMatchEnded(MatchResult result)
        {
            var step = Current;
            if (step == null || !step.IsBattle)
                return;
            WinResult? win = null;
            var streak = 0;
            // The boss fight is won when Prime's last bar goes (red wins), whatever the captures say; its medal follows the lives lost
            var boss = step.setup.IsBoss ? Boss.Current : null;
            if (boss != null && result.Winner == Team.Red)
            {
                var state = boss.State;
                win = Progress.RecordBossWin(step.id, state.LivesLost, state.LivesLostIn(3), state.FightSeconds, Progress.Difficulty, EasierSteps > 0);
                WebPortal.HappyTime();
                Debug.Log($"Campaign: won {step.id} with {state.LivesLost} lives lost in {state.FightSeconds:0}s, {win.Value.medal}{(win.Value.newBest ? " (new best)" : "")}");
            }
            else if (boss != null)
            {
                streak = Progress.RecordLoss(step.id);
                Debug.Log($"Campaign: lost {step.id} in phase {boss.PhaseReached}, {streak} in a row");
            }
            else if (result.Winner == Team.Red && result.RedScore > result.BlueScore)
            {
                win = Progress.RecordWin(step.id, result.RedScore, result.BlueScore, result.Seconds, Progress.Difficulty, EasierSteps > 0);
                Debug.Log($"Campaign: won {step.id} {result.RedScore}-{result.BlueScore} in {result.Seconds:0}s, {win.Value.medal}{(win.Value.newBest ? " (new best)" : "")}");
            }
            else
            {
                streak = Progress.RecordLoss(step.id);
                Debug.Log($"Campaign: lost {step.id} {result.RedScore}-{result.BlueScore}, {streak} in a row");
            }
            // The portal's ad rule counts real battles only: the warm-up is the player's first minutes, no ad for them
            if (Setup == null || !Setup.warmUp)
                WebPortal.BattleFinished(boss != null ? boss.State.FightSeconds : result.Seconds);
            ShowEnd(step, result, win, streak);
            BattleEnded?.Invoke(step, result, win);
        }

        // What the Victory and Defeat screens say that only the campaign knows: the medal, Prime's taunt, RETRY EASIER; shown after the banner
        private void ShowEnd(CampaignStep step, MatchResult result, WinResult? win, int streak)
        {
            var end = flow != null && flow.Match != null ? flow.Match.end : null;
            if (end == null)
                return;
            end.seconds = step.setup.IsBoss && Boss.Current != null ? Boss.Current.State.FightSeconds : result.Seconds;
            end.medal = win.HasValue ? (int)win.Value.medal : 0;
            end.newBest = win.HasValue && win.Value.newBest;
            end.taunt = win.HasValue ? "" : Taunts.Pick(step.id, streak);
            // The boss's Defeat screen advises on the phase it got to (its numbers are filled in by the HUD as the match ends)
            end.tip = !win.HasValue && step.setup.IsBoss && Boss.Current != null ? BossTips.Pick(Boss.Current.PhaseReached, streak) : "";
            // Offered after two losses in a row, until it has been pressed twice (blue cannot get clumsier than that)
            end.canRetryEasier = !win.HasValue && Progress.CanRetryEasier(step.id) && EasierSteps < DifficultyRules.MaxEasierSteps;
        }
    }
}
