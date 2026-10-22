using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>A small line of text from the match: the test panel's state of the round.</summary>
    public sealed class GuiStatus : GuiView
    {
        public TMP_Text label;

        protected override void Render(MatchView m)
        {
            GuiText.Set(label, $"{m.phase}  ·  {m.Alive} alive  ·  bots {(m.autoEvents ? "on" : "off")}\nF1 hides  ·  Tab scores  ·  Esc pause  ·  hold LMB fire  ·  R reload");
        }
    }
}
