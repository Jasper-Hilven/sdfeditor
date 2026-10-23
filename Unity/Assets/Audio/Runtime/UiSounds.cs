using SdfGui;

namespace SdfAudio
{
    /// <summary>The menus' sounds: the GUI's buttons play OK, or CANCEL for the ones that go back.</summary>
    public static class UiSounds
    {
        /// <summary>The cue a button's command plays: cancel for the ones that go back or say no, none for the test panel's, OK for the rest.</summary>
        public static string CueFor(GuiCommandId command)
        {
            switch (command)
            {
                case GuiCommandId.Back:
                case GuiCommandId.Cancel:
                case GuiCommandId.QuitToMenu:
                case GuiCommandId.Quit:
                    return Cues.UiCancel;
                case GuiCommandId.ToggleTestPanel:
                case GuiCommandId.NewRound:
                case GuiCommandId.Damage:
                case GuiCommandId.Heal:
                case GuiCommandId.YouKill:
                case GuiCommandId.YouHeadshot:
                case GuiCommandId.BotKill:
                case GuiCommandId.TogglePickup:
                case GuiCommandId.Reload:
                case GuiCommandId.Die:
                case GuiCommandId.Win:
                case GuiCommandId.ToggleAutoEvents:
                    return null;
                default:
                    return Cues.UiOkay;
            }
        }

        /// <summary>A button with this command was clicked: its sound plays on the screen.</summary>
        public static void Click(GuiCommandId command)
        {
            var cue = CueFor(command);
            if (cue != null)
                Audio.Play2D(cue);
        }
    }
}
