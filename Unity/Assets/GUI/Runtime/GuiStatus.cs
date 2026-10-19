using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// Small lines of text from the match: the fake "players online" of the main menu, which drifts at runtime, and the test panel's
    /// state of the round.
    /// </summary>
    public sealed class GuiStatus : GuiView
    {
        public enum What
        {
            PlayersOnline,
            TestState,
        }

        public What what;
        public TMP_Text label;
        public int playersOnline = 12483;

        protected override void Render(FakeMatch m)
        {
            if (what == What.PlayersOnline)
            {
                var online = playersOnline + (Application.isPlaying ? Mathf.RoundToInt(Mathf.Sin(Time.unscaledTime * 0.3f) * 140 + Mathf.Sin(Time.unscaledTime * 1.7f) * 12) : 0);
                GuiText.Set(label, $"<color=#{GuiPalette.HexOf(GuiPalette.Moss)}>●</color> {online:N0} players online");
            }
            else
            {
                GuiText.Set(label, $"{m.phase}  ·  {m.Alive} alive  ·  bots {(m.autoEvents ? "on" : "off")}\nF1 hides  ·  Tab scores  ·  Esc pause  ·  hold LMB fire  ·  R reload");
            }
        }
    }
}
