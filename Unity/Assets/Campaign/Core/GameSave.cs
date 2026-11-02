using System;
using UnityEngine;

namespace SdfCampaign
{
    /// <summary>
    /// The game's save (ticket 42): the automatic difficulty and the last levels played, which is all the endless game keeps between
    /// sessions. It is JSON under <see cref="Key"/> in the campaign storage (the portal's data store first, PlayerPrefs behind it on the
    /// web). A save of another version, a broken one, or none reads as the default. The old campaign save has another key and is ignored.
    /// Ticket 47 adds its head fields here, with its own version number.
    /// </summary>
    [Serializable]
    public sealed class GameSave
    {
        public const string Key = "minigun.save";
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        /// <summary>The difficulty's key ("supereasy", "easy", "normal", "hard"); <see cref="Difficulty"/> reads and writes it.</summary>
        public string difficulty = DifficultyRules.Key(SdfCampaign.Difficulty.Normal);
        /// <summary>The last levels played, oldest first (the rotation's history).</summary>
        public string[] lastLevels = Array.Empty<string>();

        public Difficulty Difficulty
        {
            get => DifficultyRules.Parse(difficulty);
            set => difficulty = DifficultyRules.Key(value);
        }

        public static GameSave Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new GameSave();
            GameSave save;
            try
            {
                save = JsonUtility.FromJson<GameSave>(json);
            }
            catch (ArgumentException)
            {
                return new GameSave();
            }
            if (save == null || save.version != CurrentVersion)
                return new GameSave();
            save.lastLevels ??= Array.Empty<string>();
            return save;
        }

        public string ToJson() => JsonUtility.ToJson(this);

        public static GameSave Load(ICampaignStorage storage) =>
            storage != null && storage.TryGet(Key, out var json) ? Parse(json) : new GameSave();

        public void Save(ICampaignStorage storage) => storage.Set(Key, ToJson());
    }
}
