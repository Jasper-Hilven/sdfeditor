using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    public enum GuiCommandId
    {
        Show,
        Play,
        StartNow,
        Back,
        Resume,
        LeaveMatch,
        Spectate,
        Quit,
        ToggleTestPanel,

        // Test panel: events of a round on demand
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

        private void Awake() => GetComponent<Button>().onClick.AddListener(Run);

        public void Run()
        {
            var flow = GuiFlow.Find(this);
            if (flow != null)
                flow.Execute(command, screen);
        }
    }
}
