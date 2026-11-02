using SdfPlatform;

namespace SdfCampaign
{
    /// <summary>
    /// Where the game's save lives (ticket 42): read from the portal's data store first, once the portal is up (it syncs a logged-in player's
    /// save across devices), then from PlayerPrefs. A write goes to both: the portal takes it only when it is up (<see cref="WebPortal"/> does
    /// nothing before), so a save made before the portal is up is in PlayerPrefs and in the portal once <see cref="GameLoop"/> writes again.
    /// </summary>
    public sealed class GameStorage : ICampaignStorage
    {
        private readonly PortalCampaignStorage portal = new PortalCampaignStorage();
        private readonly PlayerPrefsCampaignStorage prefs = new PlayerPrefsCampaignStorage();

        public bool TryGet(string key, out string value) => portal.TryGet(key, out value) || prefs.TryGet(key, out value);

        public void Set(string key, string value)
        {
            prefs.Set(key, value);
            portal.Set(key, value);
        }
    }
}
