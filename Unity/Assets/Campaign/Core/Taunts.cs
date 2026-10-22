using System;
using System.Collections.Generic;

namespace SdfCampaign
{
    /// <summary>
    /// Chanterelle Prime's taunts on the Defeat screen (ticket 32.6): five per arena, as data, so a level added to the campaign adds
    /// its five here and nothing else. The one shown follows the losses in a row on the battle, so a second loss never repeats the first.
    /// </summary>
    public static class Taunts
    {
        public const int PerArena = 5;

        private static readonly Dictionary<string, string[]> ByArena = new Dictionary<string, string[]>
        {
            [CampaignDefinition.Meadow] = new[]
            {
                "Silver suits you.",
                "Did you bring a minigun? I can't tell from here.",
                "Last year's champion. This year's doormat.",
                "The meadow sends its regards. It was laughing.",
                "Losing builds character. You must be so full of it.",
            },
            [CampaignDefinition.Fungal] = new[]
            {
                "Machines are in the rulebook now. Try reading it.",
                "Night is when champions sleep. You are right on schedule.",
                "Spores are not a personality.",
                "Fourth place is a lovely number. Frame it.",
                "The moon saw that. The moon has opinions.",
            },
            [CampaignDefinition.Chessboard] = new[]
            {
                "Checkmate. Or the board-game version of it.",
                "You can't win on your own coffee ring.",
                "Board games are a sport now. You are not.",
                "A pawn would have played that better.",
                "That move was banned. For board-game reasons.",
            },
            [CampaignDefinition.ToyBox] = new[]
            {
                "Your toys work for me now. Better dental.",
                "Sergeant Clank sends his thanks.",
                "Someone should tidy up in here. Not you, apparently.",
                "Back in the box, champion.",
                "Play nicely. Oh, you tried.",
            },
            [CampaignDefinition.Kitchen] = new[]
            {
                "Too hot to handle. Literally.",
                "You left the hob on. I only took the credit.",
                "Done. Well done. Slightly overdone.",
                "Mind the heat. Or don't. I'm not your mother.",
                "Another one for the compost bin.",
            },
            [CampaignDefinition.Bathroom] = new[]
            {
                "Five against four. That's strategy.",
                "Participation Award says hi.",
                "Slippery slope, isn't it?",
                "Wash up and try again. You're already wet.",
                "My fifth player got a sticker. You got nothing.",
            },
            [CampaignDefinition.Computer] = new[]
            {
                "Edit the results? Already did.",
                "Respawn in three seconds. Yours is five. Rules.",
                "You have been deleted. Press any key to continue.",
                "Error 42: champion not found.",
                "I reorganised your cutlery again.",
            },
            [CampaignDefinition.Boss] = new[]
            {
                "Second place is a lovely place. I would know.",
                "I fixed the results. And the game. And you.",
                "Best of three? I'm up one.",
                "Look at the size of me. Look at the size of you.",
                "Champion? You're a footnote.",
            },
        };

        /// <summary>The ids that have taunts.</summary>
        public static IEnumerable<string> Arenas => ByArena.Keys;

        /// <summary>The arena's five taunts; empty for an arena with none.</summary>
        public static IReadOnlyList<string> Of(string arenaId) =>
            arenaId != null && ByArena.TryGetValue(arenaId, out var lines) ? lines : Array.Empty<string>();

        /// <summary>
        /// The taunt after the <paramref name="lossesInARow"/>th loss on the arena (1 is the first): they go round in order; "" for an
        /// arena with none.
        /// </summary>
        public static string Pick(string arenaId, int lossesInARow)
        {
            var lines = Of(arenaId);
            return lines.Count == 0 ? "" : lines[(Math.Max(1, lossesInARow) - 1) % lines.Count];
        }
    }
}
