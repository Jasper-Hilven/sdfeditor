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
}
