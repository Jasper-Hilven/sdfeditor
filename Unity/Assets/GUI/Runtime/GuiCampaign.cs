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

        /// <summary>The editor's snapshot of the title with the campaign complete (the sample save is not).</summary>
        public static bool PreviewComplete;

        /// <summary>The editor's snapshot of every arena won and every comic seen (ticket 35), in place of the sample save.</summary>
        public static bool PreviewFull;

        private static CampaignProgress fullSample;

        private static bool Live => Application.isPlaying && CampaignRunner.Instance != null;

        public static CampaignDefinition Definition => Live ? CampaignRunner.Instance.Definition : Sample().definition;

        /// <summary>
        /// The save the menus are reading right now: the running runner's, or null with none (the sample is not counted). The runner swaps it
        /// when the portal's data store comes up after the title is already shown, and a screen that drew itself from the old one compares this to redraw.
        /// </summary>
        public static CampaignProgress LiveProgress => Live ? CampaignRunner.Instance.Progress : null;

        public static CampaignProgress Progress => Live ? CampaignRunner.Instance.Progress : PreviewFull ? Full() : Sample().progress;

        /// <summary>Whether the title offers the save: a real one, or the sample in the editor.</summary>
        public static bool ShowsSave => Live ? Progress.HasSave : !Application.isPlaying;

        /// <summary>
        /// Whether the title offers CONTINUE: a save with a step left (in the editor's sample, always).
        /// </summary>
        public static bool CanContinue => ShowsSave && (!Application.isPlaying || Progress.NextStep() != null);

        /// <summary>Whether a scene can be played (is in the build); the sample says yes for the arenas built today.</summary>
        public static bool IsAvailable(string scene) => Live ? SdfCampaign.ContentStreamer.CanLoad(scene) : SampleScenes.Contains(scene);

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

        private static CampaignProgress Full()
        {
            if (fullSample == null)
            {
                var definition = Sample().definition;
                fullSample = CampaignProgress.Load(definition, new MemoryCampaignStorage(), _ => true);
                fullSample.NewCampaign(Difficulty.Normal);
                foreach (var step in definition.Steps)
                {
                    if (step.IsBattle)
                        fullSample.RecordWin(step.id, 3, 0, 300, Difficulty.Normal);
                    else
                        fullSample.CompleteCutscene(step.id);
                }
            }
            return fullSample;
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
