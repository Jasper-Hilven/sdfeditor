using SdfPlatform;
using UnityEditor;

namespace SdfPlatform
{
    /// <summary>Whether the editor's play mode also plays the CrazyGames SDK's simulated midgame ads (3 s, game paused); off by default.</summary>
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
    }
}
