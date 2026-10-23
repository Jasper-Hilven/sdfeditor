using SdfPlatform;

namespace SdfCampaign
{
    /// <summary>The save in the portal's data store (CrazyGames' Data module, which syncs a logged-in player's save across devices); see <see cref="WebPortal"/>.</summary>
    public sealed class PortalCampaignStorage : ICampaignStorage
    {
        public bool TryGet(string key, out string value) => WebPortal.TryGetData(key, out value);

        public void Set(string key, string value) => WebPortal.SetData(key, value);
    }
}
