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
    /// loads the Meadow with a match on a random team (the loading tip, then the click to play, and the warm-up on the first match). Each
    /// match is on the level the rotation picked when the one before it went live (<see cref="LevelRotation"/>), prefetched during the match
    /// (<see cref="ContentStreamer"/>); when a match ends its result moves the automatic difficulty (<see cref="AutoDifficulty"/>) and the save
    /// is written (<see cref="GameSave"/>). The end screens' buttons (CONTINUE, RETRY, MENU) start the next match; RESTART in the pause menu
    /// starts this one again; CREDITS in the pause menu plays the credits and returns to the pause menu.
    /// </summary>
    public sealed class GameLoop : MonoBehaviour
    {
        /// <summary>The level Boot starts on; it is in the initial web download, so it never waits for a download.</summary>
        public const string FirstLevel = "Meadow";

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
        private bool begun;

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
            Launch(FirstLevel, RandomTeam(), !begun, new LoadingBriefing { arena = FirstLevel, tip = WarmUp.HowToScore }, true);
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
        public Difficulty Difficulty => save != null ? save.Difficulty : SdfCampaign.Difficulty.Normal;

        /// <summary>The device's local date and time; the head rules read the month from it.</summary>
        public static System.Func<System.DateTime> Clock = () => System.DateTime.Now;

        private static System.DateTime Now => Clock();

        /// <summary>The head unlocked by the match that just ended (ticket 48 shows it), or null when it was lost or unlocked nothing.</summary>
        public string LastUnlockedHead { get; private set; }

        /// <summary>The head you wear.</summary>
        public string CurrentHead => save != null ? HeadUnlocks.Current(Now, save.heads) : HeadUnlocks.HeadOfMonth(Now);

        /// <summary>The heads you can wear, in calendar order.</summary>
        public System.Collections.Generic.List<string> AvailableHeads => HeadUnlocks.Available(Now, save.heads);

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

        // A load that was given up (its download failed and the player stopped it): the Meadow, which is in the player, starts instead
        private void AbandonedLoad() => Launch(FirstLevel, RandomTeam(), !begun, null, true);

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
            loader.Load(level, briefing, onLoaded: () => Live(level, team, warmUp, record), onPlay: () => flow.Show(GuiScreen.Hud));
            return true;
        }

        // The scene is live and still covered: the match exists, so its end and the warm-up attach to it
        private void Live(string level, Team team, bool warmUp, bool record)
        {
            MatchSetup.Pending = null;
            current = level;
            currentTeam = team;
            begun = true;
            if (record)
            {
                HeadUnlocks.RecordPlayed(Now, save.heads);
                rotation.Played(level);
                save.lastLevels = rotation.History.ToArray();
                save.Save(storage);
                upcoming = rotation.Next(random);
                ContentStreamer.Prefetch(new[] { StreamManifest.SceneKey(upcoming) });
            }
            var match = Match.Current;
            if (match == null)
                return;
            match.Ended += OnEnded;
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
                if (LastUnlockedHead != null)
                    WebPortal.HappyTime();
            }
            save.Difficulty = AutoDifficulty.Next(save.Difficulty, new CaptureResult(yours, enemy));
            save.Save(storage);
            Debug.Log($"GameLoop: {current} ended {yours}-{enemy} for {currentTeam}; next difficulty {DifficultyRules.Key(save.Difficulty)}");
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
                case GuiCommandId.Credits:
                    CreditsPlayer.Begin(flow, () => flow.Show(GuiScreen.Pause));
                    return true;
                default:
                    return false;
            }
        }
    }
}
