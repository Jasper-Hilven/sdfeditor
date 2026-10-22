using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// One line of the kill feed: its line (<see cref="FakeKill.line"/>, SdfBattle.DeathLines: "{killer} sautéed {icon} {victim}") with the
    /// weapon's icon (a popped cap for a headshot) where its {icon} is, the text before it in <see cref="killer"/> and after it in
    /// <see cref="victim"/>; the plain line is the killer, the icon, the victim.
    /// </summary>
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
            var killerName = killerPlayer == null && !string.IsNullOrEmpty(kill.killerName) ? kill.killerName : GuiText.Name(killerPlayer);
            var line = string.IsNullOrEmpty(kill.line) ? SdfBattle.DeathLines.PlainFeed : kill.line;
            var text = SdfBattle.DeathLines.Format(line, killerName, GuiText.Name(victimPlayer), streak: kill.streak);
            var (before, after) = SdfBattle.DeathLines.SplitAtIcon(text);
            GuiText.Set(killer, before);
            GuiText.Set(victim, after);
            GuiText.Show(killer, before.Length > 0);
            GuiText.Show(victim, after.Length > 0);
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
