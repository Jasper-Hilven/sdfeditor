using System.Linq;
using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>The matchmaking lobby: players dropping in one by one until it is full, then the countdown to the round.</summary>
    public sealed class GuiLobby : GuiView
    {
        public TMP_Text title;
        public TMP_Text count;
        public TMP_Text countdown;
        public GuiBar fillBar;
        public GuiPlayerRow[] rows = new GuiPlayerRow[0];

        protected override void Render(FakeMatch m)
        {
            var searching = m.phase == MatchPhase.Lobby;
            var dots = Application.isPlaying ? new string('.', 1 + (int)(Time.unscaledTime * 2) % 3) : "...";
            GuiText.Set(title, searching ? "FINDING PLAYERS" + dots : m.phase == MatchPhase.Countdown ? "MATCH FOUND!" : "MATCH IN PROGRESS");
            GuiText.Set(count, $"{m.Joined}/{FakeMatch.LobbySize}");
            fillBar?.Set(m.Joined / (float)FakeMatch.LobbySize);
            GuiText.Set(countdown, m.phase == MatchPhase.Countdown ? $"Dropping onto the island in {Mathf.CeilToInt(m.countdown)}" : searching ? "Waiting for mushrooms" : "Join next round");

            // Joined players first, in the order they came; you at the top
            var ordered = m.players.OrderByDescending(p => p.you).ThenByDescending(p => p.joined).ToList();
            for (var i = 0; i < rows.Length; i++)
            {
                if (rows[i] == null)
                    continue;
                GuiText.Show(rows[i], i < ordered.Count);
                if (i < ordered.Count)
                    rows[i].ShowInLobby(ordered[i], i + 1);
            }
        }
    }
}
