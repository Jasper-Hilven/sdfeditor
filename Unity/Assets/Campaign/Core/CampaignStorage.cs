using System.Collections.Generic;

namespace SdfCampaign
{
    /// <summary>Where the save lives; the game backs it with PlayerPrefs, tests with <see cref="MemoryCampaignStorage"/>.</summary>
    public interface ICampaignStorage
    {
        bool TryGet(string key, out string value);
        void Set(string key, string value);
    }

    public sealed class MemoryCampaignStorage : ICampaignStorage
    {
        public readonly Dictionary<string, string> values = new();

        public bool TryGet(string key, out string value) => values.TryGetValue(key, out value);

        public void Set(string key, string value) => values[key] = value;
    }

    /// <summary>
    /// A save read from <c>primary</c> and, where that has nothing, from <c>fallback</c>; every write goes to <c>primary</c> only. The web
    /// build puts the portal's data store first and the browser's PlayerPrefs behind it, so a save made before the portal existed is found
    /// (and moves over on the next write) while nothing is deleted.
    /// </summary>
    public sealed class LayeredCampaignStorage : ICampaignStorage
    {
        readonly ICampaignStorage primary, fallback;

        public LayeredCampaignStorage(ICampaignStorage primary, ICampaignStorage fallback)
        {
            this.primary = primary;
            this.fallback = fallback;
        }

        public bool TryGet(string key, out string value) => primary.TryGet(key, out value) || fallback.TryGet(key, out value);

        public void Set(string key, string value) => primary.Set(key, value);
    }
}
