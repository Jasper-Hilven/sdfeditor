using SdfGui;
using TMPro;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The "Overclocked" tag of the data pulses (ticket 30.9): a small panel on the left of the HUD, 5 px from its edge and 5 px over the
    /// spore panels, shown while you have the speed-up. It is made from the HUD's own spore power line (the same panel, font and ink),
    /// so no HUD prefab changes and a level without pulses never makes it.
    /// </summary>
    public sealed class PulseHud
    {
        public const string Title = "OVERCLOCKED";

        /// <summary>The tag's size and its gap to the edge and to the panels under it, in HUD pixels.</summary>
        public static readonly Vector2 Size = new Vector2(230, 44);
        public const float Gap = 5;

        private readonly GameObject tag;
        private readonly TMP_Text label;
        private int shownPercent = -1;

        private PulseHud(GameObject tag, TMP_Text label)
        {
            this.tag = tag;
            this.label = label;
            label.color = DataPulses.StreakColour;
            tag.SetActive(false);
        }

        /// <summary>Makes the tag in the match's HUD; null when the GUI has no spore power line to copy.</summary>
        public static PulseHud Create(Match match)
        {
            var hud = match.Hud != null && match.Hud.view != null ? match.Hud.view.GetComponentInChildren<GuiHud>(true) : null;
            if (hud == null || hud.sporePower == null || hud.sporePowerLabel == null)
                return null;
            var power = (RectTransform)hud.sporePower.transform;
            var copy = Object.Instantiate(hud.sporePower, hud.transform);
            copy.name = "Overclocked";
            var rect = (RectTransform)copy.transform;
            rect.sizeDelta = Size;
            // The power panel's corner is its anchor's (bottom left, pivoted on its middle): over it, 5 px from the screen's left edge
            rect.anchoredPosition = new Vector2(Gap + Size.x * 0.5f, power.anchoredPosition.y + power.sizeDelta.y * 0.5f + Gap + Size.y * 0.5f);
            var text = copy.GetComponentInChildren<TMP_Text>(true);
            if (text == null)
                return null;
            return new PulseHud(copy, text);
        }

        /// <summary>Shows the tag while <paramref name="on"/>, with the speed-up as a percentage.</summary>
        public void Show(bool on, float boost)
        {
            if (tag == null)
                return;
            if (tag.activeSelf != on)
                tag.SetActive(on);
            var percent = Mathf.RoundToInt((boost - 1) * 100);
            if (on && percent != shownPercent)
            {
                shownPercent = percent;
                GuiText.Set(label, $"{Title}  +{percent}%");
            }
        }
    }
}
