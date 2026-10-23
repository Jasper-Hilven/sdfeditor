using System.Linq;
using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The title's state (ticket 32.5): CONTINUE only with a save that has a step left, LEVEL SELECT once the first arena is won (no QUIT:
    /// the game is left as any window or tab is), and NEW CAMPAIGN as the big button when there is nothing to continue; the progress line at the bottom left and
    /// the version at the bottom right. Drawn from <see cref="GuiCampaign"/> each time the title shows (every frame in the editor).
    /// </summary>
    [ExecuteAlways]
    public sealed class GuiTitle : MonoBehaviour
    {
        public GameObject continueButton;
        public ToonShape newCampaignShape;
        public GameObject levelSelectButton;
        [Tooltip("The gold crown on the logo's cap, shown once the campaign is complete")]
        public GameObject crown;
        public TMP_Text progress;
        public TMP_Text version;

        private static readonly Vector2 Primary = new Vector2(420, 100);
        private static readonly Vector2 Secondary = new Vector2(380, 72);

        private void OnEnable() => Refresh();

        private void Update()
        {
            if (!Application.isPlaying)
                Refresh();
        }

        public void Refresh()
        {
            var save = GuiCampaign.ShowsSave;
            var campaign = GuiCampaign.Progress;
            var firstBattle = GuiCampaign.Definition.Battles().FirstOrDefault();
            var canContinue = GuiCampaign.CanContinue;
            var anyWon = firstBattle != null && campaign.Won(firstBattle.id);

            GuiText.Show(continueButton, canContinue);
            GuiText.Show(levelSelectButton, save && anyWon);
            GuiText.Show(crown, GuiCampaign.PreviewComplete || save && campaign.Completed);
            if (newCampaignShape != null)
            {
                // The one to press is the big one
                var rect = newCampaignShape.rectTransform;
                var size = canContinue ? Secondary : Primary;
                if (rect.sizeDelta != size)
                    rect.sizeDelta = size;
                var colour = canContinue ? GuiPalette.Moss : GuiPalette.Amanita;
                if (newCampaignShape.color != colour)
                    newCampaignShape.color = colour;
            }
            GuiText.Set(progress, GuiCampaign.ProgressText());
            GuiText.Set(version, Application.version);
        }
    }
}
