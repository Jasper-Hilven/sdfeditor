using SdfPlatform;
using UnityEditor;

namespace SdfPlatform
{
    /// <summary>The editor's portal switches: whether play mode also plays the CrazyGames SDK's simulated midgame ads (3 s, game paused), and whether it simulates a phone; both off by default.</summary>
    public static class PortalMenu
    {
        const string Path = "Tools/Portal/Simulate ads in play mode";

        [MenuItem(Path)]
        static void Toggle() => EditorPrefs.SetBool(WebPortal.EditorAdsKey, !EditorPrefs.GetBool(WebPortal.EditorAdsKey, false));

        [MenuItem(Path, true)]
        static bool Check()
        {
            Menu.SetChecked(Path, EditorPrefs.GetBool(WebPortal.EditorAdsKey, false));
            return true;
        }

        const string MobilePath = "Tools/Portal/Simulate mobile";

        /// <summary>The game takes the editor for a phone (touch device, no pointer lock) while this is on; the URL override still wins.</summary>
        [MenuItem(MobilePath)]
        static void ToggleMobile()
        {
            EditorPrefs.SetBool(Device.EditorMobileKey, !EditorPrefs.GetBool(Device.EditorMobileKey, false));
            Device.Refresh();
        }

        [MenuItem(MobilePath, true)]
        static bool CheckMobile()
        {
            Menu.SetChecked(MobilePath, EditorPrefs.GetBool(Device.EditorMobileKey, false));
            return true;
        }
    }
}
