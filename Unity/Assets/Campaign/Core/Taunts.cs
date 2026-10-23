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
                "You can't win with a glued-on king.",
                "Board games are a sport now. You are not.",
                "A pawn would have played that better.",
                "That move was banned. For board-game reasons.",
            },
            [CampaignDefinition.ToyBox] = new[]
            {
                "Your toys work for me now. I let them stay up late.",
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
            // Prime's own, in his arena (ticket 33, section 8)
            [CampaignDefinition.Boss] = new[]
            {
                "Silver suits you.",
                "Have you considered second place? It's lovely.",
                "That was my <i>warm-up</i> crown.",
                "The crowd is cheering. For me.",
                "Best of one, final answer.",
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

    /// <summary>
    /// What the boss's Defeat screen advises, by the phase reached (ticket 33, section 8): a few per phase, one at a time in turn like
    /// the taunts. Plain data.
    /// </summary>
    public static class BossTips
    {
        private static readonly string[][] ByPhase =
        {
            new[]
            {
                "Capture Prime's flag: the shield breaks and he kneels. Shoot the gills!",
                "Prime cannot see you under the balcony.",
                "Pillars and toadstools stop the Royal Glare. Keep one between you and his eyes.",
            },
            new[]
            {
                "The electro-gun jumps between the sacs.",
                "Hide under a toadstool when the cannon's eyes flash.",
                "When the copter crashes, run in and shoot the gills.",
            },
            new[]
            {
                "Jump the shockwave.",
                "Take the crown home: Prime takes half damage while he wears it.",
                "Shoot the eyes when they are open. They shut during a wind-up.",
            },
        };

        /// <summary>The tips of a phase, 1 to 3 (others are clamped).</summary>
        public static IReadOnlyList<string> Of(int phase) => ByPhase[Math.Max(1, Math.Min(3, phase)) - 1];

        /// <summary>The tip after the <paramref name="lossesInARow"/>th loss in a phase (1 is the first): they go round in order.</summary>
        public static string Pick(int phase, int lossesInARow)
        {
            var tips = Of(phase);
            return tips[(Math.Max(1, lossesInARow) - 1) % tips.Count];
        }
    }
}
