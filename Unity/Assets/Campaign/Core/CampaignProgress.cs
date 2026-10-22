using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfCampaign
{
    /// <summary>One battle's best result, as saved (ticket 32, section 4). Medal and difficulty are lower case words.</summary>
    [Serializable]
    public sealed class LevelRecord
    {
        public string id;
        public bool won;
        public string medal = "none";
        public float bestSeconds;
        /// <summary>The score of the best run, red first ("2-0").</summary>
        public string bestScore = "";
        public string difficulty = "normal";
    }

    /// <summary>Losses in a row on one battle. A list rather than a map because JsonUtility cannot write a dictionary.</summary>
    [Serializable]
    public sealed class LossEntry
    {
        public string id;
        public int count;
    }

    /// <summary>The JSON of <c>campaign.v1</c>.</summary>
    [Serializable]
    public sealed class CampaignSave
    {
        public int version = 1;
        public string difficulty = "normal";
        /// <summary>The id of the first step not done.</summary>
        public string next = "";
        public bool completed;
        public List<LossEntry> lossStreak = new();
        public List<string> seen = new();
        public List<LevelRecord> levels = new();
    }

    /// <summary>What a recorded win earned.</summary>
    public readonly struct WinResult
    {
        public readonly Medal medal;
        public readonly bool newBest;

        public WinResult(Medal medal, bool newBest)
        {
            this.medal = medal;
            this.newBest = newBest;
        }
    }

    /// <summary>
    /// The player's save and the rules on top of it (ticket 32, section 4): which step comes next, what is unlocked, the medals and the
    /// loss streaks. Stored as one JSON string through <see cref="ICampaignStorage"/> so tests need no PlayerPrefs. A save that cannot be
    /// read is kept under <see cref="BrokenKey"/> and a fresh one starts; it is never a crash.
    /// </summary>
    public sealed class CampaignProgress
    {
        public const string Key = "campaign.v1";
        public const string BrokenKey = "campaign.broken";
        const int CurrentVersion = 1;
        /// <summary>Losses in a row after which RETRY EASIER is offered.</summary>
        public const int LossesForEasier = 2;

        readonly CampaignDefinition definition;
        readonly ICampaignStorage storage;
        readonly Func<string, bool> isAvailable;
        CampaignSave data;

        /// <summary>Why the stored save was dropped on load, or null.</summary>
        public string LoadProblem { get; private set; }
        /// <summary>Whether a save exists in the storage (Continue and Level Select depend on it).</summary>
        public bool HasSave { get; private set; }

        CampaignProgress(CampaignDefinition definition, ICampaignStorage storage, Func<string, bool> isAvailable)
        {
            this.definition = definition;
            this.storage = storage;
            this.isAvailable = isAvailable ?? (_ => true);
        }

        /// <summary>
        /// Reads the save, or starts a fresh one. <paramref name="isAvailable"/> says whether a scene (by name) can be played; steps whose
        /// scene is not can be skipped by <see cref="NextStep"/>. Without it every scene counts as available.
        /// </summary>
        public static CampaignProgress Load(CampaignDefinition definition, ICampaignStorage storage, Func<string, bool> isAvailable = null)
        {
            var progress = new CampaignProgress(definition, storage, isAvailable);
            if (storage.TryGet(Key, out var json) && !string.IsNullOrEmpty(json))
            {
                var parsed = Parse(json, out var problem);
                if (parsed != null)
                {
                    progress.data = parsed;
                    progress.HasSave = true;
                }
                else
                {
                    storage.Set(BrokenKey, json);
                    progress.LoadProblem = problem;
                }
            }
            progress.data ??= progress.Fresh();
            return progress;
        }

        static CampaignSave Parse(string json, out string problem)
        {
            problem = null;
            CampaignSave save;
            try
            {
                save = JsonUtility.FromJson<CampaignSave>(json);
            }
            catch (Exception e)
            {
                problem = "unreadable JSON: " + e.Message;
                return null;
            }
            if (save == null)
            {
                problem = "empty save";
                return null;
            }
            // Older versions would be migrated here, one case per version; for now only the current one exists.
            if (save.version != CurrentVersion)
            {
                problem = "unknown version " + save.version;
                return null;
            }
            save.lossStreak ??= new List<LossEntry>();
            save.seen ??= new List<string>();
            save.levels ??= new List<LevelRecord>();
            return save;
        }

        CampaignSave Fresh() => new() { next = definition.Steps.Count > 0 ? definition.Steps[0].id : "" };

        public void Save()
        {
            storage.Set(Key, JsonUtility.ToJson(data));
            HasSave = true;
        }

        // --- state ---

        public Difficulty Difficulty => DifficultyRules.Parse(data.difficulty);

        public void SetDifficulty(Difficulty difficulty)
        {
            data.difficulty = DifficultyRules.Key(difficulty);
            Save();
        }

        /// <summary>The id of the first step not done; after the credits the last one.</summary>
        public string NextId => data.next;

        public bool Completed => data.completed;

        public bool Seen(string id) => data.seen.Contains(id);

        public LevelRecord Level(string id) => data.levels.Find(l => l.id == id);

        public bool Won(string id) => Level(id) is { won: true };

        public Medal BestMedal(string id) => Level(id) is { } l ? Medals.Parse(l.medal) : Medal.None;

        /// <summary>The number of won arenas and of medals (a won arena has one) for the title's progress line.</summary>
        public int WonCount()
        {
            var n = 0;
            foreach (var b in definition.Battles())
                if (Won(b.id)) n++;
            return n;
        }

        // --- order and unlocks ---

        /// <summary>
        /// The step to play next: the first at or after <see cref="NextId"/> whose scene is available. A step skipped for want of a scene
        /// stays where it is, so it is reached as soon as its scene exists. Null once the campaign is complete.
        /// </summary>
        public CampaignStep NextStep()
        {
            if (data.completed) return null;
            var start = Math.Max(0, definition.IndexOf(data.next));
            for (var i = start; i < definition.Steps.Count; i++)
                if (IsAvailable(definition.Steps[i])) return definition.Steps[i];
            return null;
        }

        bool IsAvailable(CampaignStep step) => isAvailable(step.scene);

        /// <summary>
        /// Whether a step can be played from Level Select or the Story gallery. A battle needs its scene and the previous available battle
        /// won (the first one is always open); a cutscene or the credits needs its scene and to be seen or reached.
        /// </summary>
        public bool IsUnlocked(string id)
        {
            var index = definition.IndexOf(id);
            if (index < 0) return false;
            var step = definition.Steps[index];
            if (!IsAvailable(step)) return false;
            if (step.IsBattle)
            {
                if (Won(id)) return true;
                for (var i = index - 1; i >= 0; i--)
                {
                    var before = definition.Steps[i];
                    if (before.IsBattle && IsAvailable(before)) return Won(before.id);
                }
                return true;
            }
            return Seen(id) || index <= definition.IndexOf(data.next);
        }

        // --- progress ---

        /// <summary>New Campaign: back to the start with the chosen difficulty; wins and medals stay.</summary>
        public void NewCampaign(Difficulty difficulty)
        {
            data.difficulty = DifficultyRules.Key(difficulty);
            data.next = definition.Steps.Count > 0 ? definition.Steps[0].id : "";
            data.completed = false;
            data.seen.Clear();
            data.lossStreak.Clear();
            Save();
        }

        /// <summary>A cutscene or the credits was watched: it is seen, and the campaign moves on unless this was a replay.</summary>
        public void CompleteCutscene(string id)
        {
            if (definition.IndexOf(id) < 0) return;
            if (!data.seen.Contains(id)) data.seen.Add(id);
            AdvancePast(id);
            Save();
        }

        void AdvancePast(string id)
        {
            var index = definition.IndexOf(id);
            if (index < definition.IndexOf(data.next)) return; // a replay of something already behind
            if (index + 1 < definition.Steps.Count)
            {
                data.next = definition.Steps[index + 1].id;
            }
            else
            {
                data.next = id;
                data.completed = true;
            }
        }

        /// <summary>
        /// A battle was won (red <paramref name="redCaptures"/> to blue <paramref name="blueCaptures"/> in <paramref name="seconds"/>):
        /// records the medal when it is a new best, clears the loss streak and moves the campaign on. Saves.
        /// </summary>
        public WinResult RecordWin(string id, int redCaptures, int blueCaptures, float seconds, Difficulty difficulty, bool retriedEasier = false)
        {
            var step = definition.Find(id);
            if (step == null || !step.IsBattle) throw new ArgumentException("not a battle: " + id);
            if (redCaptures <= blueCaptures) throw new ArgumentException("not a win");

            var medal = Medals.Of(redCaptures, blueCaptures, seconds, step.setup.timeLimitSeconds, difficulty, retriedEasier);
            return Record(id, medal, redCaptures + "-" + blueCaptures, seconds, difficulty);
        }

        /// <summary>
        /// The boss fight was won (ticket 33): the medal follows the lives lost, not the captures (<see cref="Medals.OfBoss"/>), and the
        /// best score reads "N lives lost". Otherwise as <see cref="RecordWin"/>.
        /// </summary>
        public WinResult RecordBossWin(string id, int livesLost, int livesLostInPhase3, float seconds, Difficulty difficulty, bool retriedEasier = false)
        {
            var step = definition.Find(id);
            if (step == null || !step.IsBattle) throw new ArgumentException("not a battle: " + id);
            var medal = Medals.OfBoss(livesLost, livesLostInPhase3, seconds, difficulty, retriedEasier);
            return Record(id, medal, livesLost + (livesLost == 1 ? " life lost" : " lives lost"), seconds, difficulty);
        }

        WinResult Record(string id, Medal medal, string score, float seconds, Difficulty difficulty)
        {
            var record = Level(id);
            if (record == null)
            {
                record = new LevelRecord { id = id };
                data.levels.Add(record);
            }
            var old = Medals.Parse(record.medal);
            var newBest = !record.won || medal > old || medal == old && seconds < record.bestSeconds;
            if (newBest)
            {
                record.medal = Medals.Key(medal);
                record.bestSeconds = seconds;
                record.bestScore = score;
                record.difficulty = DifficultyRules.Key(difficulty);
            }
            record.won = true;

            data.lossStreak.RemoveAll(l => l.id == id);
            AdvancePast(id);
            Save();
            return new WinResult(medal, newBest);
        }

        /// <summary>A battle was lost; returns the losses in a row on it. Saves.</summary>
        public int RecordLoss(string id)
        {
            var entry = data.lossStreak.Find(l => l.id == id);
            if (entry == null)
            {
                entry = new LossEntry { id = id };
                data.lossStreak.Add(entry);
            }
            entry.count++;
            Save();
            return entry.count;
        }

        public int LossStreak(string id) => data.lossStreak.Find(l => l.id == id)?.count ?? 0;

        /// <summary>Whether the defeat screen offers RETRY EASIER.</summary>
        public bool CanRetryEasier(string id) => LossStreak(id) >= LossesForEasier;
    }
}
