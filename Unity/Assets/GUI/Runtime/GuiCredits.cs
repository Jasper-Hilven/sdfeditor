using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The credits screen (ticket 32.8): one block of rich text (<c>SdfCampaign.CreditsRoll</c>) that goes up the screen as the campaign's
    /// <c>CreditsPlayer</c> sets its progress, a hint and a bar for the skip hold in the corner. In the editor it shows the text part of the
    /// way up, so the screen can be looked at.
    /// </summary>
    [ExecuteAlways]
    public sealed class GuiCredits : MonoBehaviour
    {
        public RectTransform viewport;
        public TMP_Text text;
        public TMP_Text hint;
        public RectTransform skipBar;

        /// <summary>Where the text is in the editor: a third of the way.</summary>
        public const float EditorProgress = 0.3f;

        private void OnEnable()
        {
            if (!Application.isPlaying)
                Show(EditorProgress, 0);
        }

        private void Update()
        {
            if (!Application.isPlaying)
                Show(EditorProgress, 0);
        }

        /// <summary>The text <paramref name="progress"/> of the way from just under the screen to just above it, and the skip bar <paramref name="skip"/> full.</summary>
        public void Show(float progress, float skip)
        {
            if (viewport == null || text == null)
                return;
            // The text is as high as it needs to be, however the screen's size is
            var height = text.GetPreferredValues(text.text, text.rectTransform.rect.width, 0).y;
            var rect = text.rectTransform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            var offset = Mathf.Clamp01(progress) * (viewport.rect.height + height);
            rect.anchoredPosition = new Vector2(0, -viewport.rect.height + offset);
            if (skipBar != null)
            {
                skipBar.gameObject.SetActive(skip > 0.001f);
                skipBar.anchorMax = new Vector2(Mathf.Clamp01(skip), 1);
            }
        }
    }
}
