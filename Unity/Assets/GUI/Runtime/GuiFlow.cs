using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SdfGui
{
    public enum GuiScreen
    {
        Title,
        Hud,
        Scoreboard,
        Pause,
        Settings,
        Death,
        /// <summary>Your team won: the VICTORY! screen with the medal and the stats (ticket 32.6; it took RoundEnd's place).</summary>
        Victory,
        Gallery,
        Loading,
        LevelSelect,
        /// <summary>A small yes/no card over whatever screen is up; <see cref="GuiFlow.screen"/> is never this, see <see cref="GuiFlow.Ask"/>.</summary>
        Confirm,
        /// <summary>The New Campaign card: Easy, Normal or Hard.</summary>
        Difficulty,
        /// <summary>The player's gallery: the cutscenes seen so far, to watch again.</summary>
        Story,
        /// <summary>The lost match's screen: DEFEATED, Prime's taunt, RETRY, RETRY EASIER and MENU.</summary>
        Defeat,
        /// <summary>The 2 s banner over the HUD between the match's last capture and Victory or Defeat.</summary>
        MatchEnd,
        /// <summary>The caption box over the HUD (the warm-up's prompts); <see cref="GuiFlow.screen"/> is never this, it shows while the HUD does and the caption has a line.</summary>
        Caption,
        /// <summary>A story beat: the scene alone with the caption box and letterbox bars, no HUD (ticket 32.7).</summary>
        Cutscene,
        /// <summary>The ending (ticket 32.8): 42's desk and the monitor drawn from the GUI's own shapes, with the caption box; plays over Boot.</summary>
        Ending,
        /// <summary>The credits (ticket 32.8): a 45 s scroll, skippable by holding Space.</summary>
        Credits,
    }

    /// <summary>
    /// Which screens of the GUI show. Each screen is a prefab under the GUI's canvas with a <see cref="GuiScreenRoot"/>; this turns
    /// them on and off for <see cref="screen"/> (some show over the HUD, like the pause menu), in the editor too, so picking a screen in
    /// the inspector shows it in the scene exactly as it will be at runtime. At runtime it also follows the match (the round starting,
    /// you being eliminated, the match ending) and the keys: Escape pauses, Tab holds the scoreboard, F1 hides the test panel.
    /// <para>
    /// Pausing stops the match: the pause screen (and the settings opened from it) sets <c>Time.timeScale</c> to 0 and pauses the audio;
    /// the GUI itself runs on unscaled time. Losing the pointer lock (the browser gives it up on Escape) or the window's focus while the
    /// HUD is up pauses too.
    /// </para>
    /// <para>
    /// The match's end (ticket 32.6) is a short sequence on unscaled time: the time slows to <see cref="SlowScale"/> for
    /// <see cref="SlowSeconds"/>, then the RED WINS or BLUE WINS banner shows for <see cref="BannerSeconds"/> over the HUD, then comes
    /// Victory or Defeat with the pointer freed. A scene played alone (no campaign) stays on the HUD after the banner: its next round comes by itself.
    /// </para>
    /// </summary>
    [ExecuteAlways]
    public sealed class GuiFlow : MonoBehaviour
    {
        public GuiScreen screen = GuiScreen.Hud;
        [Tooltip("Where Back from the settings goes")]
        public GuiScreen settingsReturn = GuiScreen.Title;
        public bool testPanel = true;

        /// <summary>The match end's slow-down: this time scale, for this many seconds, then the banner this many.</summary>
        public const float SlowScale = 0.3f, SlowSeconds = 1, BannerSeconds = 2;

        /// <summary>The GUI that survives scene loads (the campaign's, ticket 32.4); null where a scene plays alone with its own.</summary>
        public static GuiFlow Persistent { get; set; }

        /// <summary>
        /// The campaign's say in the buttons (the GUI holds no scene references): given a command first, returns true when it took it over.
        /// Set by the campaign runner while it lives.
        /// </summary>
        public static System.Func<GuiCommandId, string, int, bool> Campaign;

        /// <summary>Whether the developer's tools show: the test panel and the component gallery (the editor and development builds).</summary>
        public static bool DevTools => Debug.isDebugBuild;

        /// <summary>Whether the yes/no card is up over <see cref="screen"/> (<see cref="Ask"/>).</summary>
        public bool ConfirmOpen { get; private set; }

        /// <summary>What the card asks.</summary>
        public string ConfirmQuestion { get; private set; } = "Are you sure?";

        private System.Action confirmYes;

        private MatchView match;
        private MatchPhase lastPhase;
        private bool wasAlive = true;
        private GuiScreen held = GuiScreen.Hud;
        private bool wasLocked;
        private bool paused;
        private float timeScaleBefore = 1;
        private bool slowing, bannering;
        private float endTime;

        public MatchView Match => match != null ? match : match = GetComponentInParent<MatchView>();

        private void OnEnable()
        {
            if (Match != null)
            {
                lastPhase = Match.phase;
                wasAlive = Match.YouAreAlive;
            }
            Apply();
        }

#if UNITY_EDITOR
        // Turning objects on and off is not allowed during OnValidate itself
        private void OnValidate() => UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
                Apply();
        };
#endif

        private void Update()
        {
            if (Application.isPlaying)
            {
                FollowMatch();
                Keys();
                PauseOnLostLock();
                HoldTime();
                TickEnd();
            }
            Apply();
        }

        private void OnDisable()
        {
            CancelEnd();
            Hold(false);
        }

        /// <summary>Whether the match's end sequence (the slow-down and the banner) is running.</summary>
        public bool Ending => slowing || bannering;

        // The browser lets the pointer go on Escape without the key reaching the game; a lock lost while playing is a pause
        private void PauseOnLostLock()
        {
            var locked = Cursor.lockState == CursorLockMode.Locked;
            if (screen == GuiScreen.Hud && !Ending && wasLocked && !locked)
                screen = GuiScreen.Pause;
            wasLocked = locked;
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus && Application.isPlaying && !Application.isBatchMode && screen == GuiScreen.Hud && !Ending && Match != null && Match.driven)
                Pause();
        }

        private void Pause()
        {
            screen = GuiScreen.Pause;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Apply();
        }

        /// <summary>Whether the pause screen, or the settings opened from it, is up.</summary>
        public bool IsPaused => screen == GuiScreen.Pause || (screen == GuiScreen.Settings && settingsReturn == GuiScreen.Pause);

        private void HoldTime() => Hold(IsPaused);

        // Stops the game's time and sound while paused, and puts back what was there
        private void Hold(bool hold)
        {
            if (hold == paused)
                return;
            paused = hold;
            if (hold)
                timeScaleBefore = Time.timeScale;
            Time.timeScale = hold ? 0 : timeScaleBefore;
            AudioListener.pause = hold;
        }

        private void FollowMatch()
        {
            if (Match == null)
                return;
            var phase = Match.phase;
            if (phase != lastPhase)
            {
                // A new round (a retry, or the next round of a scene played alone): back to the HUD
                if (phase != MatchPhase.Over && (Ending || IsEndScreen(screen)))
                {
                    CancelEnd();
                    screen = GuiScreen.Hud;
                }
                if (phase == MatchPhase.Over && (Match.YouAreAlive || Match.driven) && (IsInRound(screen) || (Match.driven && screen == GuiScreen.Death)))
                    BeginEnd();
                lastPhase = phase;
            }
            if (wasAlive && !Match.YouAreAlive && IsInRound(screen) && !Ending)
                screen = GuiScreen.Death;
            // In a real match you respawn: back to the HUD
            if (!wasAlive && Match.YouAreAlive && screen == GuiScreen.Death)
                screen = GuiScreen.Hud;
            wasAlive = Match.YouAreAlive;
        }

        private void Keys()
        {
            if (ConfirmOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                    CloseConfirm();
                return;
            }
            if (Input.GetKeyDown(KeyCode.F1) && DevTools)
                testPanel = !testPanel;
            if (Input.GetKeyDown(KeyCode.Escape) && !Ending)
            {
                if (screen == GuiScreen.Hud)
                    screen = GuiScreen.Pause;
                else if (screen == GuiScreen.Pause)
                    screen = GuiScreen.Hud;
                else if (screen == GuiScreen.Settings)
                    screen = settingsReturn;
                else if (screen == GuiScreen.LevelSelect || screen == GuiScreen.Difficulty || screen == GuiScreen.Story)
                    screen = GuiScreen.Title;
            }
            if (Input.GetKeyDown(KeyCode.Tab) && screen == GuiScreen.Hud)
            {
                held = screen;
                screen = GuiScreen.Scoreboard;
            }
            if (Input.GetKeyUp(KeyCode.Tab) && screen == GuiScreen.Scoreboard)
                screen = held;

            // Hold the left button to fire, R to reload, E to take what is offered, while the HUD is up and the pointer not on a button
            if (screen == GuiScreen.Hud && Match != null)
            {
                var overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (Input.GetMouseButton(0) && !overUi)
                    Match.Fire(Time.deltaTime);
                if (Input.GetKeyDown(KeyCode.R))
                    Match.Reload();
                if (Input.GetKeyDown(KeyCode.E) && !string.IsNullOrEmpty(Match.pickup))
                    Match.pickup = "";
            }
        }

        private static bool IsInRound(GuiScreen s) => s == GuiScreen.Hud || s == GuiScreen.Scoreboard || s == GuiScreen.Pause;

        private static bool IsEndScreen(GuiScreen s) => s == GuiScreen.MatchEnd || s == GuiScreen.Victory || s == GuiScreen.Defeat;

        // ---------------------------------------------------------------- The match's end

        private void BeginEnd()
        {
            // The showcase round has no match to fill the end screens: made up from the round
            if (!Match.driven && !Match.end.set)
                Match.SampleEnd(Match.YourTeamWon());
            if (screen == GuiScreen.Scoreboard || screen == GuiScreen.Pause || screen == GuiScreen.Death)
                screen = GuiScreen.Hud;
            slowing = true;
            bannering = false;
            endTime = 0;
            Time.timeScale = SlowScale;
        }

        private void TickEnd()
        {
            if (!Ending)
                return;
            if (Match == null)
            {
                CancelEnd();
                return;
            }
            endTime += Time.unscaledDeltaTime;
            if (slowing && endTime >= SlowSeconds)
            {
                slowing = false;
                bannering = true;
                endTime = 0;
                RestoreTime();
                screen = GuiScreen.MatchEnd;
            }
            else if (bannering && endTime >= BannerSeconds)
            {
                bannering = false;
                // A scene played alone has no campaign to retry or go on in: its next round starts by itself
                if (Match.driven && Campaign == null)
                {
                    screen = GuiScreen.Hud;
                    return;
                }
                screen = Match.end.won ? GuiScreen.Victory : GuiScreen.Defeat;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// <summary>Stops the end sequence where it is and gives the time back (a retry, leaving, a new round).</summary>
        private void CancelEnd()
        {
            if (!Ending)
                return;
            slowing = bannering = false;
            RestoreTime();
        }

        private void RestoreTime()
        {
            if (!paused)
                Time.timeScale = 1;
        }

        /// <summary>Whether a screen shows while <see cref="screen"/> is up: the HUD stays under the overlays of a round.</summary>
        public bool Shows(GuiScreen root)
        {
            if (root == GuiScreen.Confirm)
                return ConfirmOpen;
            if (root == GuiScreen.Caption)
                return screen == GuiScreen.Hud && Match != null && Match.caption.Visible;
            if (root == screen)
                return true;
            if (root != GuiScreen.Hud)
                return false;
            return screen == GuiScreen.Scoreboard || screen == GuiScreen.Pause || screen == GuiScreen.Death || screen == GuiScreen.MatchEnd ||
                   (screen == GuiScreen.Settings && settingsReturn == GuiScreen.Pause);
        }

        private void Apply()
        {
            foreach (var root in GetComponentsInChildren<GuiScreenRoot>(true))
            {
                // The gallery fills the whole screen and has its own way back, so the test panel leaves it alone
                var visible = root.alwaysOn ? testPanel && DevTools && screen != GuiScreen.Gallery : Shows(root.screen);
                if (root.gameObject.activeSelf != visible)
                    root.gameObject.SetActive(visible);
            }
        }

        public void Show(GuiScreen next)
        {
            // The component gallery is the developer's
            if (next == GuiScreen.Gallery && !DevTools)
                return;
            if (next == GuiScreen.Confirm)
            {
                // Picked from the editor or the test panel: the card over what is up, with nothing to do on yes
                Ask(ConfirmQuestion, null);
                return;
            }
            // The showcase's samples of the caption box (a real match's caption is written by the campaign): the HUD with a line, or the beat's screen
            if (Match != null && !Match.driven)
            {
                Match.caption.Clear();
                if (next == GuiScreen.Caption || next == GuiScreen.Cutscene || next == GuiScreen.Ending)
                    Match.caption.Set("Mushroom 7", "Fill your spores and press Q. Trust me.", 1, next != GuiScreen.Caption ? 0.4f : 0, "Hold SPACE to skip");
            }
            if (next == GuiScreen.Caption)
                next = GuiScreen.Hud;
            ConfirmOpen = false;
            CancelEnd();
            // Picked from the editor or the test panel: the showcase round's end, made up
            if ((next == GuiScreen.Victory || next == GuiScreen.Defeat) && Match != null && !Match.driven)
                Match.SampleEnd(next == GuiScreen.Victory);
            if (next == GuiScreen.Settings && screen != GuiScreen.Settings)
                settingsReturn = screen == GuiScreen.Pause ? GuiScreen.Pause : GuiScreen.Title;
            screen = next;
            Apply();
        }

        /// <summary>Puts the yes/no card over the screen that is up; yes runs <paramref name="onYes"/>, no (or Escape) closes it.</summary>
        public void Ask(string question, System.Action onYes)
        {
            ConfirmQuestion = question;
            confirmYes = onYes;
            ConfirmOpen = true;
            Apply();
        }

        private void CloseConfirm()
        {
            ConfirmOpen = false;
            confirmYes = null;
            Apply();
        }

        /// <summary>What a <see cref="GuiCommand"/> button does.</summary>
        public void Execute(GuiCommandId command, GuiScreen argument, string level = null, int value = 0)
        {
            if (Campaign != null && Application.isPlaying && Campaign(command, level, value))
                return;
            var m = Match;
            switch (command)
            {
                case GuiCommandId.Show:
                    Show(argument);
                    break;
                case GuiCommandId.NewCampaign:
                    // With a save the story starts again only after a yes (medals and unlocked levels stay)
                    if (GuiCampaign.ShowsSave)
                        Ask("Start the story again?\nYour medals and unlocked levels stay.", () => Show(GuiScreen.Difficulty));
                    else
                        Show(GuiScreen.Difficulty);
                    break;
                // Without a campaign (the GUI scene played alone) these start the showcase round
                case GuiCommandId.Continue:
                case GuiCommandId.BeginCampaign:
                case GuiCommandId.PlayLevel:
                case GuiCommandId.Restart:
                case GuiCommandId.Retry:
                case GuiCommandId.RetryEasier:
                case GuiCommandId.Next:
                case GuiCommandId.NewRound:
                    m?.StartPlaying();
                    Show(GuiScreen.Hud);
                    break;
                case GuiCommandId.WatchCutscene:
                    break;
                case GuiCommandId.Back:
                    Show(screen == GuiScreen.Settings ? settingsReturn : GuiScreen.Title);
                    break;
                case GuiCommandId.Resume:
                    Show(GuiScreen.Hud);
                    break;
                case GuiCommandId.QuitToMenu:
                    Show(GuiScreen.Title);
                    break;
                case GuiCommandId.Confirm:
                {
                    var yes = confirmYes;
                    CloseConfirm();
                    yes?.Invoke();
                    break;
                }
                case GuiCommandId.Cancel:
                    CloseConfirm();
                    break;
                case GuiCommandId.Spectate:
                    Show(GuiScreen.Hud);
                    break;
                case GuiCommandId.Quit:
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;
                case GuiCommandId.ToggleTestPanel:
                    testPanel = !testPanel;
                    break;
                default:
                    if (m != null && Application.isPlaying)
                        Test(m, command);
                    break;
            }
        }

        private void Test(MatchView m, GuiCommandId command)
        {
            // The round's events want a round going; start one rather than ignore the button
            if (m.phase != MatchPhase.Playing && command != GuiCommandId.ToggleAutoEvents && command != GuiCommandId.TogglePickup)
            {
                m.StartPlaying();
                Show(GuiScreen.Hud);
            }
            switch (command)
            {
                case GuiCommandId.Damage:
                    m.Damage(20);
                    break;
                case GuiCommandId.Heal:
                    m.Heal(30);
                    break;
                case GuiCommandId.YouKill:
                    m.YouKill(false);
                    break;
                case GuiCommandId.YouHeadshot:
                    m.YouKill(true);
                    break;
                case GuiCommandId.BotKill:
                    m.BotKill();
                    break;
                case GuiCommandId.TogglePickup:
                    m.TogglePickup();
                    break;
                case GuiCommandId.Reload:
                    m.ammo = Mathf.Min(m.ammo, 40);
                    m.Reload();
                    break;
                case GuiCommandId.Die:
                    m.Die();
                    break;
                case GuiCommandId.Win:
                    m.Win();
                    break;
                case GuiCommandId.ToggleAutoEvents:
                    m.autoEvents = !m.autoEvents;
                    break;
            }
            if (screen != GuiScreen.Hud && IsInRound(screen))
                Show(GuiScreen.Hud);
        }

        public static GuiFlow Find(Component from)
        {
            var flow = from.GetComponentInParent<GuiFlow>(true);
            return flow != null ? flow : FindObjectsByType<GuiFlow>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        }
    }
}
