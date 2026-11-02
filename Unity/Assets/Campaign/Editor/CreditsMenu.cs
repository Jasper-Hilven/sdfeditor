using SdfGui;
using UnityEditor;
using UnityEngine;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// Tools > SDF Campaign > Play Credits: in Play mode, the credits roll at once with their timing and the skip hold, then back to the HUD.
    /// </summary>
    public static class CreditsMenu
    {
        private const string MenuPath = "Tools/SDF Campaign/Play Credits";

        [MenuItem(MenuPath)]
        public static void Play()
        {
            var flow = Object.FindAnyObjectByType<GuiFlow>();
            if (flow == null)
            {
                Debug.LogWarning("Play Credits: no GUI in the open scenes; play Boot or the GUI scene first");
                return;
            }
            CreditsPlayer.Begin(flow, () => flow.Show(GuiScreen.Hud));
        }

        [MenuItem(MenuPath, true)]
        private static bool CanPlay() => Application.isPlaying;
    }
}
