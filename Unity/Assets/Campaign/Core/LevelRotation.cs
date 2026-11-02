using System;
using System.Collections.Generic;
using System.Linq;

namespace SdfCampaign
{
    /// <summary>
    /// The endless rotation (ticket 42): the seven battle levels, and the last few played. The next one is random among the levels not
    /// among those last <see cref="Memory"/>, so no level comes back within four matches. Plain data and a seeded <see cref="Random"/>.
    /// The level ids are the scene names. The Meadow is the first level a game starts on (Boot).
    /// </summary>
    public sealed class LevelRotation
    {
        public const int Memory = 3;

        public static readonly string[] Levels = { "Meadow", "Fungal", "Chessboard", "Toybox", "Kitchen", "Bathroom", "Computer" };

        private readonly List<string> history = new List<string>();

        /// <summary>A rotation with the levels played before, the last of them last (any beyond the last three, or unknown, are dropped).</summary>
        public LevelRotation(IEnumerable<string> played = null)
        {
            if (played == null)
                return;
            foreach (var level in played.Where(level => Levels.Contains(level)))
                Played(level);
        }

        /// <summary>The last levels played, oldest first (at most <see cref="Memory"/>).</summary>
        public IReadOnlyList<string> History => history;

        /// <summary>Records a level as played; the history keeps the last <see cref="Memory"/>.</summary>
        public void Played(string level)
        {
            history.Add(level);
            while (history.Count > Memory)
                history.RemoveAt(0);
        }

        /// <summary>A level picked evenly from the ones not among the last <see cref="Memory"/>. Does not record it.</summary>
        public string Next(Random random)
        {
            var candidates = Levels.Where(level => !history.Contains(level)).ToList();
            if (candidates.Count == 0)
                candidates = Levels.ToList();
            return candidates[random.Next(candidates.Count)];
        }
    }
}
