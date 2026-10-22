using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>Small helpers the views share for text and visibility.</summary>
    public static class GuiText
    {
        /// <summary>Sets a text only when it changes, so views that set it every frame do not rebuild its mesh.</summary>
        public static void Set(TMP_Text text, string value)
        {
            if (text != null && text.text != value)
                text.text = value;
        }

        public static void Show(Component component, bool visible)
        {
            if (component != null && component.gameObject.activeSelf != visible)
                component.gameObject.SetActive(visible);
        }

        public static void Show(GameObject gameObject, bool visible)
        {
            if (gameObject != null && gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }

        /// <summary>A player's name in their colour, for rich text.</summary>
        public static string Name(ViewPlayer player) =>
            player == null ? "the island" : $"<color=#{GuiPalette.HexOf(player.color)}>{player.name}</color>";

        public static string Clock(float seconds)
        {
            var s = Mathf.CeilToInt(Mathf.Max(0, seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        public static string Ordinal(int n) =>
            n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }
}
