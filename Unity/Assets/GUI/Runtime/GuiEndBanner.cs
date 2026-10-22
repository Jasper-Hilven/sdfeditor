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
            // The boss fight has one side to beat: "PRIME DEFEATED!" or, when 42 falls, "PRIME WINS!"
            GuiText.Set(label, m.end.boss ? (blue ? "PRIME WINS!" : "PRIME DEFEATED!") : blue ? "BLUE WINS!" : "RED WINS!");
            var colour = blue ? GuiEndScreen.Blue : GuiPalette.Amanita;
            if (shape != null && shape.color != colour)
                shape.color = colour;
        }
    }
}
