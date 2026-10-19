using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>One line of the kill feed: killer, the weapon's icon (a popped cap for a headshot), victim.</summary>
    public sealed class GuiKillFeedEntry : MonoBehaviour
    {
        public ToonShape background;
        public TMP_Text killer;
        public Image weapon;
        public ToonShape headshot;
        public TMP_Text victim;
        public CanvasGroup group;
        public GuiIconSet icons;

        public void Show(FakeMatch m, FakeKill kill, float alpha)
        {
            var killerPlayer = m.Player(kill.killer);
            var victimPlayer = m.Player(kill.victim);
            var involvesYou = killerPlayer is { you: true } || victimPlayer is { you: true };
            GuiText.Set(killer, GuiText.Name(killerPlayer));
            GuiText.Set(victim, GuiText.Name(victimPlayer));
            if (background != null)
                background.SetColor(involvesYou ? GuiPalette.Paper : GuiPalette.WithAlpha(GuiPalette.Night, 0.82f));
            var sprite = icons != null ? icons.Weapon(kill.weapon) : null;
            if (weapon != null && weapon.sprite != sprite)
                weapon.sprite = sprite;
            GuiText.Show(weapon, sprite != null);
            GuiText.Show(headshot, kill.headshot);
            if (group != null && !Mathf.Approximately(group.alpha, alpha))
                group.alpha = alpha;
        }
    }
}
