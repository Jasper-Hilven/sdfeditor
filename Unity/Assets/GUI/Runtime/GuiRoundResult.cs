using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// The end of your round, either way: the death screen (who splattered you, with what) or the round's end (the last
    /// mushroom standing, your place and kills, the wait for the next round).
    /// </summary>
    public sealed class GuiRoundResult : GuiView
    {
        public bool death = true;
        public TMP_Text title;
        public TMP_Text detail;
        public TMP_Text placement;
        public TMP_Text kills;
        public TMP_Text next;
        public Image weapon;
        public GuiAvatar avatar;
        public GuiIconSet icons;

        protected override void Render(FakeMatch m)
        {
            var you = m.You;
            if (death)
            {
                var killer = m.Player(m.killedBy);
                // Killed by something that is no player (a machine): its name, no weapon icon
                var named = killer == null && !string.IsNullOrEmpty(m.killedByName);
                var weaponIndex = Mathf.Clamp(m.killedByWeapon, 0, FakeMatch.WeaponNames.Length - 1);
                // How you died, in the words picked for it (SdfBattle.DeathLines); the plain words before any were picked
                GuiText.Set(title, !string.IsNullOrEmpty(m.killedTitle) ? m.killedTitle : GuiSettings.Gore == 0 ? "ELIMINATED" : "SPLATTERED!");
                GuiText.Set(detail, !string.IsNullOrEmpty(m.killedDetail)
                    ? SdfBattle.DeathLines.Format(m.killedDetail, named ? m.killedByName : GuiText.Name(killer),
                        weapon: SdfBattle.DeathLines.WeaponName(FakeMatch.Weapons[weaponIndex]), streak: m.killedStreak)
                    : named ? $"by {m.killedByName}" : killer == null ? "The island got you" : $"by {GuiText.Name(killer)}\nwith the {FakeMatch.WeaponNames[weaponIndex]}");
                // A fall has no weapon to show either
                var sprite = icons != null && killer != null ? icons.Weapon(m.killedByWeapon) : null;
                if (weapon != null && weapon.sprite != sprite)
                    weapon.sprite = sprite;
                GuiText.Show(weapon, sprite != null);
                if (killer != null)
                    avatar?.Set(killer.color, killer.avatar, false);
                GuiText.Show(avatar, killer != null);
            }
            else
            {
                var winner = m.players.Find(p => p.placement == 1) ?? (m.YouAreAlive ? you : null);
                var won = winner != null && winner.you;
                GuiText.Set(title, won ? "LAST MUSHROOM STANDING!" : "ROUND OVER");
                GuiText.Set(detail, winner == null ? "Nobody survived the island" : won ? "You outlasted everyone" : $"{GuiText.Name(winner)} is the last mushroom standing");
                if (winner != null)
                    avatar?.Set(winner.color, winner.avatar, false);
                GuiText.Show(avatar, winner != null);
                GuiText.Set(next, m.phase == MatchPhase.Over ? $"Next round in {Mathf.CeilToInt(m.countdown)}" : "Next round soon");
            }
            var place = m.YourPlacement;
            GuiText.Set(placement, place > 0 ? $"#{place}<size=60%> of {m.Joined}</size>" : "-");
            GuiText.Set(kills, (you?.kills ?? 0).ToString());
        }
    }
}
