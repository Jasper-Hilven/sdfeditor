using System.Collections.Generic;
using System.Linq;
using SdfCampaign;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// What the menus read of the campaign (ticket 32.5): the definition and the save of the running <see cref="CampaignRunner"/>, or, with
    /// none (a scene played alone, the editor), a sample save so Title, Level Select and Story show every state in the scene and the
    /// snapshots. The buttons do not come through here: they are <see cref="GuiCommand"/>s that the runner takes over.
    /// </summary>
    public static class GuiCampaign
    {
        private static CampaignDefinition sampleDefinition;
        private static CampaignProgress sample;

        /// <summary>The scenes the sample save has: the three arenas built today, so the rest show "Coming soon".</summary>
        private static readonly HashSet<string> SampleScenes = new HashSet<string> { "Boot", "Meadow", "Fungal", "Chessboard" };

        private static bool Live => Application.isPlaying && CampaignRunner.Instance != null;

        public static CampaignDefinition Definition => Live ? CampaignRunner.Instance.Definition : Sample().definition;

        public static CampaignProgress Progress => Live ? CampaignRunner.Instance.Progress : Sample().progress;

        /// <summary>Whether the title offers the save: a real one, or the sample in the editor.</summary>
        public static bool ShowsSave => Live ? Progress.HasSave : !Application.isPlaying;

        /// <summary>Whether a scene can be played (is in the build); the sample says yes for the arenas built today.</summary>
        public static bool IsAvailable(string scene) => Live ? Application.CanStreamedLevelBeLoaded(scene) : SampleScenes.Contains(scene);

        private static (CampaignDefinition definition, CampaignProgress progress) Sample()
        {
            if (sample == null)
            {
                sampleDefinition = CampaignDefinition.Default();
                sample = CampaignProgress.Load(sampleDefinition, new MemoryCampaignStorage(), SampleScenes.Contains);
                sample.NewCampaign(Difficulty.Normal);
                sample.CompleteCutscene(CampaignDefinition.Intro);
                sample.RecordWin(CampaignDefinition.Meadow, 2, 0, 180, Difficulty.Normal);
                sample.CompleteCutscene("beat-fungal");
                sample.RecordWin(CampaignDefinition.Fungal, 3, 1, 420, Difficulty.Normal);
            }
            return (sampleDefinition, sample);
        }

        /// <summary>"Arena 3 of 8 · 4 medals", or "Campaign complete · 8 medals"; empty before there is a save.</summary>
        public static string ProgressText()
        {
            if (!ShowsSave)
                return "";
            var definition = Definition;
            var progress = Progress;
            var battles = definition.Battles().ToList();
            var medals = battles.Count(b => progress.BestMedal(b.id) != Medal.None);
            var tail = medals == 1 ? "1 medal" : medals + " medals";
            if (progress.Completed)
                return $"Campaign complete  ·  {tail}";
            var from = Mathf.Max(0, definition.IndexOf(progress.NextId));
            var arena = battles.FindIndex(b => definition.IndexOf(b.id) >= from);
            return $"Arena {(arena < 0 ? battles.Count : arena + 1)} of {battles.Count}  ·  {tail}";
        }
    }
}
