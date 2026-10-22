using UnityEngine;

namespace SdfCampaign
{
    /// <summary>
    /// The save in PlayerPrefs, as the settings are (ticket 32, section 4); on the web Unity keeps them in the browser's IndexedDB, and the
    /// <c>Save</c> after every write means a closed tab loses nothing.
    /// </summary>
    public sealed class PlayerPrefsCampaignStorage : ICampaignStorage
    {
        public bool TryGet(string key, out string value)
        {
            value = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
            return value != null;
        }

        public void Set(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
        }
    }
}
