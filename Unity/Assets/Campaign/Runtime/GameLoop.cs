using System.Linq;
using SdfBattle;
using SdfGui;
using SdfHeads;
using SdfPlatform;
using UnityEngine;

namespace SdfCampaign
{
    /// <summary>
    /// The game's loop (ticket 42), on the persistent Campaign object of Boot.unity next to the <see cref="SceneLoader"/> and the GUI. Boot
    /// loads the first level (the rotation's pick, see <see cref="LevelRotation.First"/>; in the web build its bundles are fetched behind the Loading screen) with a match on a random team (the loading tip, then the click to play). A first play (nothing in the save) starts in the Fungal level with the warm-up (<see cref="LevelRotation.WarmUpOn"/>). Each
    /// match is on the level the rotation picked when the one before it went live (<see cref="LevelRotation"/>), prefetched during the match
    /// (<see cref="ContentStreamer"/>); when a match ends its result moves the automatic difficulty (<see cref="AutoDifficulty"/>) and the save
    /// is written (<see cref="GameSave"/>). The end screens' buttons (CONTINUE, RETRY, MENU) start the next match; RESTART in the pause menu
    /// starts this one again; CREDITS in the pause menu plays the credits and returns to the pause menu.
    /// </summary>
    public sealed class GameLoop : MonoBehaviour
    {
        public GuiFlow flow;
        public SceneLoader loader;

        private static GameLoop instance;

        /// <summary>The loop, or null before Boot (the editor's other scenes have none).</summary>
        public static GameLoop Instance => instance;

        private ICampaignStorage storage;
        private GameSave save;
        private LevelRotation rotation;
        private System.Random random;
        // The match being played (set when its scene is live) and the level picked for the next one
        private string current, upcoming;
        private Team currentTeam;
        // The end card's next-level step runs once per match end (ticket 48)
        private readonly OncePerMatch nextShot = new OncePerMatch();

        // The loop lives across every scene load (with the GUI and the loader under it), so Boot is loaded once; a second copy gives way
        private void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            if (flow == null)
                flow = GetComponentInChildren<GuiFlow>(true);
            if (loader == null)
                loader = GetComponent<SceneLoader>();
            loader.flow = flow;
            GuiFlow.Persistent = flow;
        }

        private void Start()
        {
            GuiFlow.Campaign = Command;
            SceneLoader.Abandoned = AbandonedLoad;
            storage = new GameStorage();
            save = GameSave.Load(storage);
            rotation = new LevelRotation(save.lastLevels);
            random = new System.Random();
            HeadSelection.Yours = HeadUnlocks.Current(Now, save.heads);
            // The portal's SDK starts here; the game does not wait for it. Once it is up the save is written to its data store too.
            WebPortal.Init(OnPortalReady);
            LaunchFirst(Application.absoluteURL);
        }

        // Boot's first match, and the one after a reset: the rotation's first pick (the Fungal level with the warm-up on a first play)
        private bool LaunchFirst(string url)
        {
            var first = rotation.First(random, url);
            return Launch(first, RandomTeam(), rotation.WarmUpOn(first), new LoadingBriefing { arena = first, goal = "CAPTURE THE FLAG", tip = WarmUp.HowToScore }, true);
        }

        /// <summary>
        /// Settings' RESET ALL PROGRESS (ticket 63): the save is deleted in every layer (the portal's data store and PlayerPrefs) and a fresh one,
        /// the default, written back; then a first play starts at once (the Fungal level with the warm-up). The match being played is left
        /// without a result: no difficulty change, no head unlock, no level recorded. Settings (volume, quality, gore...) are not progress and stay.
        /// False, with nothing changed, while a load is under way.
        /// </summary>
        public bool ResetProgress()
        {
            if (loader == null || loader.Busy)
                return false;
            // A pending end-card countdown must not start another match over the fresh first play
            nextShot.Take();
            var live = Match.Current;
            if (live != null)
                live.Ended -= OnEnded;
            save = GameSave.Reset(storage);
            rotation = new LevelRotation(save.lastLevels);
            HeadSelection.Yours = CurrentHead;
            LastUnlockedHead = null;
            DifficultyBefore = save.Difficulty;
            upcoming = null;
            ContentStreamer.ForgetPrefetch();
            return LaunchFirst(null);
        }

        private void OnPortalReady()
        {
            if (this != null && save != null && WebPortal.CloudSave)
                save.Save(storage);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
                GuiFlow.Persistent = null;
            }
            if (GuiFlow.Campaign == Command)
                GuiFlow.Campaign = null;
            SceneLoader.Abandoned = null;
        }

        /// <summary>The difficulty the next match is made for, as saved.</summary>
        public Difficulty Difficulty => save != null ? save.Difficulty : AutoDifficulty.Start;

        /// <summary>The device's local date and time; the head rules read the month from it.</summary>
        public static System.Func<System.DateTime> Clock = () => System.DateTime.Now;

        private static System.DateTime Now => Clock();

        /// <summary>The head unlocked by the match that just ended (ticket 48 shows it), or null when it was lost or unlocked nothing.</summary>
        public string LastUnlockedHead { get; private set; }

        /// <summary>The head you wear.</summary>
        public string CurrentHead => save != null ? HeadUnlocks.Current(Now, save.heads) : HeadUnlocks.HeadOfMonth(Now);

        /// <summary>The heads you can wear, in calendar order.</summary>
        public System.Collections.Generic.List<string> AvailableHeads => HeadUnlocks.Available(Now, save.heads);

        /// <summary>The difficulty the match just ended was made for (the end card's line says whether the next one is tougher or easier).</summary>
        public Difficulty DifficultyBefore { get; private set; } = Difficulty.Normal;

        /// <summary>The unlocked head before / after the current one (wrapping).</summary>
        public string PreviousHead() => HeadUnlocks.Previous(Now, save.heads, CurrentHead);
        public string NextHead() => HeadUnlocks.Next(Now, save.heads, CurrentHead);

        /// <summary>Picks the head to wear from the next match on, and saves it. False when it is not unlocked.</summary>
        public bool SetHead(string id)
        {
            if (!HeadUnlocks.SetCurrent(Now, save.heads, id))
                return false;
            HeadSelection.Yours = id;
            save.Save(storage);
            return true;
        }

        /// <summary>The level of the match being played, or loading; empty before Boot's load.</summary>
        public string Current => current ?? "";

        /// <summary>The level picked for the match after this one; empty until this match is live.</summary>
        public string Upcoming => upcoming ?? "";

        /// <summary>The team you play in in the match being played.</summary>
        public Team YourTeam => currentTeam;

        /// <summary>Starts the next match (the rotation's pick, a random team). Returns false when a load is under way.</summary>
        public bool Next()
        {
            var level = upcoming ?? rotation.Next(random);
            if (!Launch(level, RandomTeam(), false, null, true))
                return false;
            upcoming = null;
            return true;
        }

        /// <summary>Starts the current match again, from its level and team, with nothing recorded (the pause menu's RESTART).</summary>
        public bool Restart()
        {
            if (current == null)
                return false;
            return Launch(current, currentTeam, false, null, false);
        }

        private Team RandomTeam() => random.Next(2) == 0 ? Team.Red : Team.Blue;

        // A load that was given up: only with no Loading screen to retry on, which the game's Boot always has
        private void AbandonedLoad() => Debug.LogError("GameLoop: a level's download was given up");

        /// <summary>
        /// Loads <paramref name="level"/> with the match set up for <paramref name="team"/> at the current difficulty. <paramref name="record"/>
        /// says whether the level counts as played (a new match, not a restart). False when the loader is busy.
        /// </summary>
        private bool Launch(string level, Team team, bool warmUp, LoadingBriefing briefing, bool record)
        {
            if (loader == null || loader.Busy)
                return false;
            HeadSelection.Yours = CurrentHead;
            var setup = new BattleSetup { yourTeam = team, warmUp = warmUp };
            setup = DifficultyRules.Apply(setup, save.Difficulty, team.Other());
            MatchSetup.Pending = setup.ToMatchSetup();
            // The HUD comes back once the match is playable: after the click on the Loading screen, or the fade in (the end screens and the pause menu leave it)
            loader.Load(level, briefing, onLoaded: () => Live(level, team, warmUp, record), onPlay: () =>
            {
                flow.Show(GuiScreen.Hud);
                // The HUD is up, so gameplayStart has gone: the level's LOD0 meshes come in the background (ticket 60)
                Lod0Streamer.Begin(level);
            });
            return true;
        }

        // The scene is live and still covered: the match exists, so its end and the warm-up attach to it
        private void Live(string level, Team team, bool warmUp, bool record)
        {
            MatchSetup.Pending = null;
            Lod0Streamer.Prepare(level);
            current = level;
            currentTeam = team;
            nextShot.Arm();
            if (record)
            {
                HeadUnlocks.RecordPlayed(Now, save.heads);
                rotation.Played(level);
                save.lastLevels = rotation.History.ToArray();
                save.Save(storage);
                // Picked now (the end card names it); downloaded only when this match ends (OnEnded), not during play
                upcoming = rotation.Next(random);
            }
            var match = Match.Current;
            if (match == null)
                return;
            match.Ended += OnEnded;
            // The Loading screen is still up, waiting for the click: the rosters are known now the match has its players
            var loading = flow != null ? flow.GetComponentInChildren<GuiLoading>(true) : null;
            if (loading != null)
                loading.Rosters(match.State.Players.Where(p => p.Team == Team.Red).Select(p => p.Name).ToArray(),
                    match.State.Players.Where(p => p.Team == Team.Blue).Select(p => p.Name).ToArray());
            if (warmUp)
                WarmUpPlayer.Begin(match, flow);
        }

        // The match's result: your captures against the enemy's move the difficulty for the next match, and the save is written
        private void OnEnded(MatchResult result)
        {
            var red = result.RedScore;
            var blue = result.BlueScore;
            var yours = currentTeam == Team.Red ? red : blue;
            var enemy = currentTeam == Team.Red ? blue : red;
            LastUnlockedHead = null;
            if (yours > enemy)
            {
                LastUnlockedHead = HeadUnlocks.OnMatchWon(Now, save.heads);
                // The new head is the one you wear from now on (the end card shows it; you can switch back there)
                if (LastUnlockedHead != null)
                {
                    HeadUnlocks.SetCurrent(Now, save.heads, LastUnlockedHead);
                    WebPortal.HappyTime();
                }
            }
            DifficultyBefore = save.Difficulty;
            save.Difficulty = AutoDifficulty.Next(save.Difficulty, new CaptureResult(yours, enemy));
            save.Save(storage);
            WebPortal.BattleFinished(result.Seconds);
            // The next level downloads from now: under the result banner and the end card (and, once ads are on, the ad break), the
            // next Loading screen showing only what is left
            if (!string.IsNullOrEmpty(upcoming))
                ContentStreamer.Prefetch(new[] { StreamManifest.SceneKey(upcoming) });
            Debug.Log($"GameLoop: {current} ended {yours}-{enemy} for {currentTeam}; next difficulty {DifficultyRules.Key(save.Difficulty)}");
        }

        /// <summary>
        /// The end card's countdown is over (ticket 48): the midgame ad when <see cref="AdBreakPolicy"/> earns one, then the next level. Once
        /// per match end; false when it was already asked for.
        /// </summary>
        public bool ContinueAfterCountdown()
        {
            if (!nextShot.Take())
                return false;
            WebPortal.MidgameBreak(() => Next());
            return true;
        }

        /// <summary>The head before / after the current one, picked and saved (the end card's ◀ and ▶, and its A and D keys).</summary>
        public void StepHead(int by)
        {
            if (by < 0)
                SetHead(PreviousHead());
            else
                SetHead(NextHead());
        }

        // Every command that starts a match goes to the next one; RESTART goes back to this one; CREDITS plays the credits over the pause menu
        private bool Command(GuiCommandId command, string level, int value)
        {
            switch (command)
            {
                case GuiCommandId.Restart:
                case GuiCommandId.RestartFight:
                    return Restart();
                case GuiCommandId.Continue:
                case GuiCommandId.NewCampaign:
                case GuiCommandId.BeginCampaign:
                case GuiCommandId.PlayLevel:
                case GuiCommandId.Next:
                case GuiCommandId.Retry:
                case GuiCommandId.RetryEasier:
                case GuiCommandId.QuitToMenu:
                    return Next();
                case GuiCommandId.ResetProgress:
                    return ResetProgress();
                case GuiCommandId.Credits:
                    CreditsPlayer.Begin(flow, () => flow.Show(GuiScreen.Pause));
                    return true;
                case GuiCommandId.NextLevel:
                    return ContinueAfterCountdown();
                case GuiCommandId.HeadPrevious:
                    StepHead(-1);
                    return true;
                case GuiCommandId.HeadNext:
                    StepHead(1);
                    return true;
                default:
                    return false;
            }
        }
    }
}
