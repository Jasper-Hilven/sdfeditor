using SdfGui;
using UnityEditor;
using UnityEngine;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// Tools > SDF Campaign > Play Credits: in Play mode, the credits roll at once with their timing and the skip hold, without finishing the
    /// campaign first. With the campaign running (Boot) they play as after the ending and go back to the title; in the GUI scene played alone,
    /// on its Credits screen, back to its title.
    /// </summary>
    public static class CreditsMenu
    {
        private const string MenuPath = "Tools/SDF Campaign/Play Credits";

        [MenuItem(MenuPath)]
        public static void Play()
        {
            if (CampaignRunner.Instance != null)
            {
                CampaignRunner.Instance.PlayCredits();
                return;
            }
            var flow = Object.FindAnyObjectByType<GuiFlow>();
            if (flow == null)
            {
                Debug.LogWarning("Play Credits: no campaign or GUI in the open scenes; play Boot or the GUI scene first");
                return;
            }
            CreditsPlayer.Begin(flow, () => flow.Show(GuiScreen.Title));
        }

        [MenuItem(MenuPath, true)]
        private static bool CanPlay() => Application.isPlaying;
    }
}
