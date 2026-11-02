using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>A player's face: their team's colour in a ring with the icon of the monster head they wear.</summary>
    [ExecuteAlways]
    public sealed class GuiAvatar : MonoBehaviour
    {
        public ToonShape disc;
        public Image icon;
        public GuiIconSet icons;
        public Color color = GuiPalette.Red;
        public int avatar;
        [Tooltip("The monster head's id (its icon in the icon set); empty falls back to the avatar index")]
        public string head;
        [Tooltip("Greyed out: the player is out of the round, or not there yet")]
        public bool dimmed;

        public void Set(Color playerColor, int index, bool dim, string headId = null)
        {
            color = playerColor;
            avatar = index;
            head = headId;
            dimmed = dim;
            Apply();
        }

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        private void Apply()
        {
            if (disc != null)
                disc.SetColor(dimmed ? Color.Lerp(color, GuiPalette.Dusk, 0.75f) : color);
            if (icon == null)
                return;
            var sprite = icons != null ? (string.IsNullOrEmpty(head) ? null : icons.Get(head)) ?? icons.Avatar(avatar) : null;
            if (icon.sprite != sprite)
                icon.sprite = sprite;
            var tint = dimmed ? new Color(0.45f, 0.45f, 0.5f, 0.8f) : Color.white;
            if (icon.color != tint)
                icon.color = tint;
            if (icon.enabled != (sprite != null))
                icon.enabled = sprite != null;
        }
    }
}
