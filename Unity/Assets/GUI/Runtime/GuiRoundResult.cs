using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// The death screen: who splattered you, with what, and the wait until you are back. (The match's end has its own screens,
    /// <see cref="GuiEndScreen"/>, since ticket 32.6.)
    /// </summary>
    public sealed class GuiRoundResult : GuiView
    {
        public TMP_Text title;
        public TMP_Text detail;
        public TMP_Text back;
        public Image weapon;
        public GuiAvatar avatar;
        public GuiIconSet icons;

        protected override void Render(MatchView m)
        {
            var killer = m.Player(m.killedBy);
            // Killed by something that is no player (a machine): its name, no weapon icon
            var named = killer == null && !string.IsNullOrEmpty(m.killedByName);
            var weaponIndex = Mathf.Clamp(m.killedByWeapon, 0, MatchView.WeaponNames.Length - 1);
            // How you died, in the words picked for it (SdfBattle.DeathLines); the plain words before any were picked
            GuiText.Set(title, !string.IsNullOrEmpty(m.killedTitle) ? m.killedTitle : GuiSettings.Gore == 0 ? "OUT!" : "SPLATTERED!");
            GuiText.Set(detail, !string.IsNullOrEmpty(m.killedDetail)
                ? SdfBattle.DeathLines.Format(m.killedDetail, named ? m.killedByName : GuiText.Name(killer),
                    weapon: SdfBattle.DeathLines.WeaponName(MatchView.Weapons[weaponIndex]), streak: m.killedStreak)
                : named ? $"by {m.killedByName}" : killer == null ? "The island got you" : $"by {GuiText.Name(killer)}\nwith the {MatchView.WeaponNames[weaponIndex]}");
            // A fall has no weapon to show either
            var sprite = icons != null && killer != null ? icons.Weapon(m.killedByWeapon) : null;
            if (weapon != null && weapon.sprite != sprite)
                weapon.sprite = sprite;
            GuiText.Show(weapon, sprite != null);
            if (killer != null)
                avatar?.Set(killer.color, killer.avatar, false);
            GuiText.Show(avatar, killer != null);
            GuiText.Show(back, m.respawnIn > 0);
            if (m.respawnIn > 0)
                GuiText.Set(back, $"Back in {Mathf.CeilToInt(m.respawnIn)}");
        }
    }
}
