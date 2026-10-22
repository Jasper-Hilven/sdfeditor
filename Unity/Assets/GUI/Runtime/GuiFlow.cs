using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SdfGui
{
    public enum GuiScreen
    {
        MainMenu,
        Hud,
        Scoreboard,
        Pause,
        Settings,
        Death,
        RoundEnd,
        Gallery,
    }

    /// <summary>
    /// Which screens of the GUI show. Each screen is a prefab under the GUI's canvas with a <see cref="GuiScreenRoot"/>; this turns
    /// them on and off for <see cref="screen"/> (some show over the HUD, like the pause menu), in the editor too, so picking a screen in
    /// the inspector shows it in the scene exactly as it will be at runtime. At runtime it also follows the match (the round starting,
    /// you being eliminated, the round ending) and the keys: Escape pauses, Tab holds the scoreboard, F1 hides the test panel.
    /// <para>
    /// Pausing stops the match: the pause screen (and the settings opened from it) sets <c>Time.timeScale</c> to 0 and pauses the audio;
    /// the GUI itself runs on unscaled time. Losing the pointer lock (the browser gives it up on Escape) or the window's focus while the
    /// HUD is up pauses too.
    /// </para>
    /// </summary>
    [ExecuteAlways]
    public sealed class GuiFlow : MonoBehaviour
    {
        public GuiScreen screen = GuiScreen.Hud;
        [Tooltip("Where Back from the settings goes")]
        public GuiScreen settingsReturn = GuiScreen.MainMenu;
        public bool testPanel = true;

        private MatchView match;
        private MatchPhase lastPhase;
        private bool wasAlive = true;
        private GuiScreen held = GuiScreen.Hud;
        private bool wasLocked;
        private bool paused;
        private float timeScaleBefore = 1;

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
            }
            Apply();
        }

        private void OnDisable() => Hold(false);

        // The browser lets the pointer go on Escape without the key reaching the game; a lock lost while playing is a pause
        private void PauseOnLostLock()
        {
            var locked = Cursor.lockState == CursorLockMode.Locked;
            if (screen == GuiScreen.Hud && wasLocked && !locked)
                screen = GuiScreen.Pause;
            wasLocked = locked;
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus && Application.isPlaying && !Application.isBatchMode && screen == GuiScreen.Hud && Match != null && Match.driven)
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
                if (phase == MatchPhase.Playing && screen == GuiScreen.RoundEnd)
                    screen = GuiScreen.Hud;
                if (phase == MatchPhase.Over && (Match.YouAreAlive || Match.driven) && (IsInRound(screen) || (Match.driven && screen == GuiScreen.Death)))
                    screen = GuiScreen.RoundEnd;
                // A real match's next round counts down on the HUD
                if (phase == MatchPhase.Countdown && Match.driven && screen == GuiScreen.RoundEnd)
                    screen = GuiScreen.Hud;
                lastPhase = phase;
            }
            if (wasAlive && !Match.YouAreAlive && IsInRound(screen))
                screen = GuiScreen.Death;
            // In a real match you respawn: back to the HUD
            if (!wasAlive && Match.YouAreAlive && screen == GuiScreen.Death)
                screen = GuiScreen.Hud;
            wasAlive = Match.YouAreAlive;
        }

        private void Keys()
        {
            if (Input.GetKeyDown(KeyCode.F1))
                testPanel = !testPanel;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (screen == GuiScreen.Hud)
                    screen = GuiScreen.Pause;
                else if (screen == GuiScreen.Pause)
                    screen = GuiScreen.Hud;
                else if (screen == GuiScreen.Settings)
                    screen = settingsReturn;
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

        /// <summary>Whether a screen shows while <see cref="screen"/> is up: the HUD stays under the overlays of a round.</summary>
        public bool Shows(GuiScreen root)
        {
            if (root == screen)
                return true;
            if (root != GuiScreen.Hud)
                return false;
            return screen == GuiScreen.Scoreboard || screen == GuiScreen.Pause || screen == GuiScreen.Death || screen == GuiScreen.RoundEnd ||
                   (screen == GuiScreen.Settings && settingsReturn == GuiScreen.Pause);
        }

        private void Apply()
        {
            foreach (var root in GetComponentsInChildren<GuiScreenRoot>(true))
            {
                // The gallery fills the whole screen and has its own way back, so the test panel leaves it alone
                var visible = root.alwaysOn ? testPanel && screen != GuiScreen.Gallery : Shows(root.screen);
                if (root.gameObject.activeSelf != visible)
                    root.gameObject.SetActive(visible);
            }
        }

        public void Show(GuiScreen next)
        {
            if (next == GuiScreen.Settings && screen != GuiScreen.Settings)
                settingsReturn = screen == GuiScreen.Pause ? GuiScreen.Pause : GuiScreen.MainMenu;
            screen = next;
            Apply();
        }

        /// <summary>What a <see cref="GuiCommand"/> button does.</summary>
        public void Execute(GuiCommandId command, GuiScreen argument)
        {
            var m = Match;
            switch (command)
            {
                case GuiCommandId.Show:
                    Show(argument);
                    break;
                case GuiCommandId.Play:
                case GuiCommandId.StartNow:
                    m?.StartPlaying();
                    Show(GuiScreen.Hud);
                    break;
                case GuiCommandId.Back:
                    Show(screen == GuiScreen.Settings ? settingsReturn : GuiScreen.MainMenu);
                    break;
                case GuiCommandId.Resume:
                    Show(GuiScreen.Hud);
                    break;
                case GuiCommandId.LeaveMatch:
                    Show(GuiScreen.MainMenu);
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
