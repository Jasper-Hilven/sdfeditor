using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>A line for a player, in the lobby (joined or still being searched for) and on the scoreboard (alive or out, kills).</summary>
    public sealed class GuiPlayerRow : MonoBehaviour
    {
        public ToonShape background;
        public TMP_Text rank;
        public GuiAvatar avatar;
        public TMP_Text playerName;
        public TMP_Text kills;
        public TMP_Text status;
        public GuiPingBars ping;

        public void ShowInLobby(FakePlayer player, int place)
        {
            var joined = player.joined;
            Background(player, !joined);
            GuiText.Set(rank, place.ToString());
            avatar?.Set(player.color, player.avatar, !joined);
            GuiText.Set(playerName, joined ? player.name : "searching...");
            if (playerName != null)
                playerName.alpha = joined ? 1 : 0.55f;
            GuiText.Show(kills, false);
            GuiText.Set(status, joined ? (player.you ? "YOU" : "READY") : "");
            ShowPing(player, joined);
        }

        public void ShowOnScoreboard(FakePlayer player, int place)
        {
            Background(player, !player.alive);
            GuiText.Set(rank, place.ToString());
            avatar?.Set(player.color, player.avatar, !player.alive);
            GuiText.Set(playerName, player.name);
            if (playerName != null)
                playerName.alpha = player.alive ? 1 : 0.7f;
            GuiText.Show(kills, true);
            GuiText.Set(kills, player.kills.ToString());
            GuiText.Set(status, player.placement == 1 ? "WINNER" : player.alive ? "ALIVE" : "OUT");
            if (status != null)
                status.color = player.placement == 1 ? GuiPalette.Gold : player.alive ? GuiPalette.Moss : GuiPalette.Amanita;
            ShowPing(player, true);
        }

        private void Background(FakePlayer player, bool dim)
        {
            if (background == null)
                return;
            var tone = player.you ? GuiPalette.Gold : GuiPalette.Paper;
            background.SetColor(dim ? Color.Lerp(tone, GuiPalette.Dusk, 0.55f) : tone);
        }

        private void ShowPing(FakePlayer player, bool visible)
        {
            GuiText.Show(ping, visible);
            if (visible && ping != null)
                ping.Set(player.ping);
        }
    }
}
