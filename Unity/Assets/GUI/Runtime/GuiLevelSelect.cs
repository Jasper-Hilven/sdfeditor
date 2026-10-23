using System.Linq;
using SdfCampaign;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// Level Select (ticket 32.5): the arenas in campaign order as cards, with the difficulty selector on top. Each time it shows it
    /// reads the save: medals, padlocks, scenes not in the build. PLAY on a card is a <see cref="GuiCommand"/> the runner takes over.
    /// </summary>
    [ExecuteAlways]
    public sealed class GuiLevelSelect : MonoBehaviour
    {
        public GuiLevelCard[] cards = new GuiLevelCard[0];
        public GuiSelector difficulty;

        private static readonly string[] DifficultyNames = { "Super Easy", "Easy", "Normal", "Hard" };

        private void Awake()
        {
            if (Application.isPlaying && difficulty != null)
            {
                difficulty.Changed += i => GuiCampaign.Progress.SetDifficulty((Difficulty)i);
            }
        }

        private void OnEnable() => Refresh();

        public void Refresh()
        {
            var progress = GuiCampaign.Progress;
            var battles = GuiCampaign.Definition.Battles().ToList();
            for (var i = 0; i < cards.Length; i++)
            {
                GuiText.Show(cards[i], i < battles.Count);
                if (i < battles.Count)
                    cards[i].Show(battles[i], i, progress, GuiCampaign.IsAvailable(battles[i].scene));
            }
            if (difficulty != null)
            {
                difficulty.options = DifficultyNames;
                difficulty.Set((int)progress.Difficulty);
            }
        }
    }
}
