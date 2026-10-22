using System.Linq;
using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>The scoreboard (hold Tab): players still in first, by kills, then the eliminated by how far they got.</summary>
    public sealed class GuiScoreboard : GuiView
    {
        public TMP_Text subtitle;
        public GuiPlayerRow[] rows = new GuiPlayerRow[0];

        protected override void Render(MatchView m)
        {
            GuiText.Set(subtitle, $"{m.arena}  ·  {m.goal}  ·  {GuiText.Clock(m.timeLeft)}".ToUpperInvariant());
            var ordered = m.players
                .OrderBy(p => p.placement == 1 ? 0 : p.alive ? 1 : 2)
                .ThenByDescending(p => p.alive ? p.kills : -p.placement)
                .ToList();
            for (var i = 0; i < rows.Length; i++)
            {
                if (rows[i] == null)
                    continue;
                GuiText.Show(rows[i], i < ordered.Count);
                if (i < ordered.Count)
                    rows[i].ShowOnScoreboard(ordered[i], i + 1);
            }
        }
    }
}
