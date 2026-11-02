using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>A line for a player on the scoreboard (head icon, name, kills, deaths and captures).</summary>
    public sealed class GuiPlayerRow : MonoBehaviour
    {
        public ToonShape background;
        public TMP_Text rank;
        public GuiAvatar avatar;
        public TMP_Text playerName;
        public TMP_Text kills;
        public TMP_Text deaths;
        public TMP_Text captures;
        public TMP_Text status;

        public void ShowOnScoreboard(ViewPlayer player, int place)
        {
            Background(player, !player.alive);
            GuiText.Set(rank, place.ToString());
            avatar?.Set(player.color, player.avatar, !player.alive, player.head);
            GuiText.Set(playerName, player.name);
            if (playerName != null)
                playerName.alpha = player.alive ? 1 : 0.7f;
            GuiText.Show(kills, true);
            GuiText.Set(kills, player.kills.ToString());
            GuiText.Show(deaths, true);
            GuiText.Set(deaths, player.deaths.ToString());
            GuiText.Show(captures, true);
            GuiText.Set(captures, player.captures.ToString());
            GuiText.Set(status, player.placement == 1 ? "WINNER" : "");
            if (status != null)
                status.color = GuiPalette.Gold;
        }

        private void Background(ViewPlayer player, bool dim)
        {
            if (background == null)
                return;
            var tone = player.you ? GuiPalette.Gold : GuiPalette.Paper;
            background.SetColor(dim ? Color.Lerp(tone, GuiPalette.Dusk, 0.55f) : tone);
        }
    }
}
