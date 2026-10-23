using System.Text;
using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>What the Loading screen tells about the battle coming: the briefing that the lobby used to be.</summary>
    public sealed class LoadingBriefing
    {
        public string arena = "";
        public string goal = "";
        public string[] red = new string[0];
        public string[] blue = new string[0];
    }

    /// <summary>
    /// The Loading screen (ticket 32.4): the arena's name, the goal, both rosters by name, a tip and a progress bar; when the scene is
    /// ready (and it has been up long enough) it asks for a click, which the loader waits for. It draws what it was given; the loader
    /// drives it.
    /// </summary>
    public sealed class GuiLoading : MonoBehaviour
    {
        public TMP_Text arena;
        public TMP_Text goal;
        public TMP_Text redNames;
        public TMP_Text blueNames;
        public TMP_Text tip;
        public GuiBar progress;
        [Tooltip("Shown instead of the bar once the scene is ready")]
        public TMP_Text clickToPlay;

        public static readonly string[] Tips =
        {
            "Hold Tab to see the scoreboard.",
            "Walk onto a pod to take its weapon.",
            "Low on health? Walk over a health pod.",
            "Carry their flag home while yours is at its base.",
            "Fill your spores and activate Mycelium Rush.",
        };

        private static int tipCounter;

        public bool Ready { get; private set; }

        public void Begin(LoadingBriefing briefing)
        {
            GuiText.Set(arena, briefing.arena.ToUpperInvariant());
            GuiText.Set(goal, briefing.goal);
            GuiText.Set(redNames, Roster("RED TEAM", GuiPalette.Amanita, briefing.red));
            GuiText.Set(blueNames, Roster("BLUE TEAM", GuiPalette.Sky, briefing.blue));
            GuiText.Set(tip, "TIP  " + Tips[tipCounter++ % Tips.Length]);
            Set(0, false);
        }

        /// <summary>The bar's fill (0 to 1), and whether to ask for the click instead of showing it.</summary>
        public void Set(float fraction, bool ready)
        {
            Ready = ready;
            progress?.Set(fraction);
            GuiText.Show(progress, !ready);
            GuiText.Show(clickToPlay, ready);
        }

        private static string Roster(string header, Color color, string[] names)
        {
            var text = new StringBuilder($"<color=#{GuiPalette.HexOf(color)}>{header}</color>");
            foreach (var name in names)
                text.Append('\n').Append(name);
            return text.ToString();
        }
    }
}
