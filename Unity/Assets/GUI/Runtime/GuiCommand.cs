using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    public enum GuiCommandId
    {
        Show,
        Back,
        Resume,
        QuitToMenu,
        Spectate,
        Quit,
        ToggleTestPanel,

        // The campaign's (ticket 32.5): the GuiFlow.Campaign hook runs them where a campaign is, and without one they start the showcase round
        Continue,
        NewCampaign,
        BeginCampaign,
        PlayLevel,
        Restart,
        WatchCutscene,
        Confirm,
        Cancel,
        // The end screens' (ticket 32.6): CONTINUE after a win, RETRY, and RETRY EASIER after two losses in a row
        Next,
        Retry,
        RetryEasier,

        // Test panel: events of a round on demand
        NewRound,
        Damage,
        Heal,
        YouKill,
        YouHeadshot,
        BotKill,
        TogglePickup,
        Reload,
        Die,
        Win,
        ToggleAutoEvents,

        // After the test panel's, so the saved values of the others stay: the boss's Defeat screen's second retry (ticket 33.7); RETRY there is RETRY PHASE
        RestartFight,
    }

    /// <summary>
    /// What a button does, as data: the button's click runs the command on the <see cref="GuiFlow"/> it is under. This keeps the button
    /// and screen prefabs free of references into a scene, so they can be dropped into any.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class GuiCommand : MonoBehaviour
    {
        public GuiCommandId command;
        [Tooltip("The screen for Show")]
        public GuiScreen screen;
        [Tooltip("The level (a campaign step id) for PlayLevel and WatchCutscene")]
        public string level = "";
        [Tooltip("BeginCampaign: the difficulty, 0 Super Easy, 1 Easy, 2 Normal, 3 Hard")]
        public int value;
        [Tooltip("When set, the yes/no card asks this first and the command runs on yes")]
        public string confirm = "";

        private void Awake() => GetComponent<Button>().onClick.AddListener(Run);

        public void Run()
        {
            SdfAudio.UiSounds.Click(command);
            var flow = GuiFlow.Find(this);
            if (flow == null)
                return;
            // Copied: the card runs it later, when this button may have been given another level
            var (c, s, l, v) = (command, screen, level, value);
            if (!string.IsNullOrEmpty(confirm))
                flow.Ask(confirm, () => flow.Execute(c, s, l, v));
            else
                flow.Execute(c, s, l, v);
        }
    }
}
