using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The caption box (ticket 32.7): a speaker's name over one line of text on a dark panel low on the screen, faded by
    /// <see cref="ViewCaption.alpha"/>, with a skip hint and bar in the corner. Used over the HUD (the warm-up's prompts) and alone in a
    /// story beat, which adds letterbox bars (<see cref="bars"/>). Reads <see cref="MatchView.caption"/>.
    /// </summary>
    [ExecuteAlways]
    public sealed class GuiCaption : GuiView
    {
        public CanvasGroup box;
        public TMP_Text speaker;
        public TMP_Text line;
        public TMP_Text hint;
        public RectTransform skipBar;
        public GameObject bars;

        protected override void Render(MatchView m)
        {
            var c = m.caption;
            if (box != null)
                box.alpha = c.Visible ? Mathf.Clamp01(c.alpha) : 0;
            GuiText.Set(speaker, c.speaker);
            if (speaker != null)
                speaker.gameObject.SetActive(!string.IsNullOrEmpty(c.speaker));
            GuiText.Set(line, c.line);
            GuiText.Set(hint, c.hint);
            if (skipBar != null)
            {
                skipBar.gameObject.SetActive(c.skip > 0.001f);
                skipBar.anchorMax = new Vector2(Mathf.Clamp01(c.skip), 1);
            }
        }
    }
}
