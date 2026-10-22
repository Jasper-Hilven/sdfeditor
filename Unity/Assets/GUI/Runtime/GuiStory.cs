using System.Linq;
using SdfCampaign;
using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The player's gallery (ticket 32.5): the intro, the beats, the ending and the credits as rows, each to watch again once seen and
    /// when the game can play it (<see cref="CampaignRunner.CanWatch"/>; the beats and the ending come with 32.7 and 32.8).
    /// </summary>
    [ExecuteAlways]
    public sealed class GuiStory : MonoBehaviour
    {
        public TMP_Text[] titles = new TMP_Text[0];
        public TMP_Text[] states = new TMP_Text[0];
        public GuiCommand[] watch = new GuiCommand[0];

        private void OnEnable() => Refresh();

        public void Refresh()
        {
            var progress = GuiCampaign.Progress;
            var steps = GuiCampaign.Definition.Steps.Where(s => !s.IsBattle).ToList();
            for (var i = 0; i < titles.Length; i++)
            {
                var has = i < steps.Count;
                GuiText.Show(titles[i].transform.parent, has);
                if (!has)
                    continue;
                var step = steps[i];
                var seen = progress.Seen(step.id);
                var can = seen && (!Application.isPlaying || CampaignRunner.CanWatch(step));
                GuiText.Set(titles[i], step.title);
                GuiText.Set(states[i], seen ? "Seen" : "Not seen yet");
                GuiText.Show(states[i], !can);
                GuiText.Show(watch[i], can);
                watch[i].level = step.id;
            }
        }
    }
}
