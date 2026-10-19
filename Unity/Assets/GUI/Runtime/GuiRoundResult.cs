using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// The end of your round, either way: the death screen (who splattered you, with what, your place) or the round's end (the last
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
                GuiText.Set(title, GuiSettings.Gore == 0 ? "ELIMINATED" : "SPLATTERED!");
                GuiText.Set(detail, killer == null ? "The island got you" : $"by {GuiText.Name(killer)}\nwith the {FakeMatch.WeaponNames[Mathf.Clamp(m.killedByWeapon, 0, FakeMatch.WeaponNames.Length - 1)]}");
                var sprite = icons != null ? icons.Weapon(m.killedByWeapon) : null;
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
