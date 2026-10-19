using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>A player's face: their colour in a ring with the icon of their character.</summary>
    [ExecuteAlways]
    public sealed class GuiAvatar : MonoBehaviour
    {
        public ToonShape disc;
        public Image icon;
        public GuiIconSet icons;
        public Color color = GuiPalette.Amanita;
        public int avatar;
        [Tooltip("Greyed out: the player is out of the round, or not there yet")]
        public bool dimmed;

        public void Set(Color playerColor, int index, bool dim)
        {
            color = playerColor;
            avatar = index;
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
            var sprite = icons != null ? icons.Avatar(avatar) : null;
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
