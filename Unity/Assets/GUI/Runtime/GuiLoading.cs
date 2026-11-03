using System.Text;
using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>What the Loading screen tells about the battle coming: the arena, the goal and both teams by name.</summary>
    public sealed class LoadingBriefing
    {
        public string arena = "";
        public string goal = "";
        public string[] red = new string[0];
        public string[] blue = new string[0];
        /// <summary>A tip of its own (the first meadow explains the game), instead of the next of <see cref="GuiLoading.Tips"/>; empty for those.</summary>
        public string tip = "";
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
            "Fill your rage and activate Frenzy.",
            "Every win unlocks a new monster head.",
            "On the end card, A and D change your head.",
        };

        private static int tipCounter;

        public bool Ready { get; private set; }

        public void Begin(LoadingBriefing briefing)
        {
            GuiText.Set(arena, briefing.arena.ToUpperInvariant());
            GuiText.Set(goal, briefing.goal);
            Rosters(briefing.red, briefing.blue);
            GuiText.Set(tip, "TIP  " + (string.IsNullOrEmpty(briefing.tip) ? Tips[tipCounter++ % Tips.Length] : briefing.tip));
            Set(0, false);
        }

        /// <summary>Both teams by name, once the match behind the screen has its players (the briefing is made before it exists).</summary>
        public void Rosters(string[] red, string[] blue)
        {
            GuiText.Set(redNames, Roster("RED TEAM", GuiPalette.Red, red));
            GuiText.Set(blueNames, Roster("BLUE TEAM", GuiPalette.Sky, blue));
        }

        /// <summary>The bar's fill (0 to 1), and whether to ask for the click instead of showing it.</summary>
        public void Set(float fraction, bool ready)
        {
            Ready = ready;
            progress?.Set(fraction);
            GuiText.Show(progress, !ready);
            GuiText.Show(clickToPlay, ready);
        }

        private string clickText;

        /// <summary>Shows a message (a failed download) in place of the click to play, until <see cref="ClearMessage"/>.</summary>
        public void Message(string text)
        {
            if (clickToPlay == null)
                return;
            clickText ??= clickToPlay.text;
            GuiText.Set(clickToPlay, text);
        }

        public void ClearMessage()
        {
            if (clickText != null)
                GuiText.Set(clickToPlay, clickText);
            clickText = null;
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
