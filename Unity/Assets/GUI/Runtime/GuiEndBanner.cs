using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>The 2 s banner between the deciding capture and Victory or Defeat (ticket 32.6): RED WINS! or BLUE WINS!, in that team's colour.</summary>
    [ExecuteAlways]
    public sealed class GuiEndBanner : GuiView
    {
        public ToonShape shape;
        public TMP_Text label;

        protected override void Render(MatchView m)
        {
            var blue = m.end.set && m.end.winnerBlue;
            var team = blue ? "BLUE" : "RED";
            // A win on kills says so, smaller to fit the banner
            GuiText.Set(label, m.end.onKills ? $"<size=70%>{team} WINS ON KILLS!</size>" : $"{team} WINS!");
            var colour = blue ? GuiEndScreen.Blue : GuiPalette.Amanita;
            if (shape != null && shape.color != colour)
                shape.color = colour;
        }
    }
}
