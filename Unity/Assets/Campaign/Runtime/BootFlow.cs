using SdfGui;
using UnityEngine;

namespace SdfCampaign
{
    /// <summary>
    /// The game's flow from Boot (ticket 41, temporary until 42): Boot goes straight into the Meadow, and a match's end (Next, Retry, Restart, the
    /// menu) goes back to the Meadow. It sits on the persistent Campaign object of Boot.unity next to the <see cref="SceneLoader"/> and the GUI,
    /// and takes the GUI's commands through <see cref="GuiFlow.Campaign"/>. Ticket 42 replaces it with the endless rotation.
    /// </summary>
    public sealed class BootFlow : MonoBehaviour
    {
        public const string Level = "Meadow";

        public GuiFlow flow;
        public SceneLoader loader;

        private void Start()
        {
            GuiFlow.Campaign = Command;
            SceneLoader.Abandoned = () => Play();
            Play();
        }

        private void OnDestroy()
        {
            if (GuiFlow.Campaign == Command)
                GuiFlow.Campaign = null;
            SceneLoader.Abandoned = null;
        }

        /// <summary>Loads the Meadow with a match on it. Returns false when the load was not started (a load is under way).</summary>
        public bool Play()
        {
            if (loader == null || loader.Busy)
                return false;
            loader.Load(Level, null);
            return true;
        }

        // Every command that starts or restarts a battle goes to the Meadow; the rest (showing a screen, the settings) is the GUI's own
        private bool Command(GuiCommandId command, string level, int value)
        {
            switch (command)
            {
                case GuiCommandId.Continue:
                case GuiCommandId.NewCampaign:
                case GuiCommandId.BeginCampaign:
                case GuiCommandId.PlayLevel:
                case GuiCommandId.Next:
                case GuiCommandId.Retry:
                case GuiCommandId.RetryEasier:
                case GuiCommandId.Restart:
                case GuiCommandId.RestartFight:
                case GuiCommandId.QuitToMenu:
                    return Play();
                default:
                    return false;
            }
        }
    }
}
