using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The match's end screens (ticket 32.6), Victory and Defeat, drawn from <see cref="MatchView.end"/>: the arena, the score in the
    /// teams' colours, your captures, kills, deaths and the time. Victory adds the medal (with NEW BEST when it is one); Defeat adds
    /// Prime's taunt, a tip, and RETRY EASIER when the campaign offers it (the layout closes the gap when it does not).
    /// </summary>
    [ExecuteAlways]
    public sealed class GuiEndScreen : GuiView
    {
        public bool victory = true;
        public TMP_Text arena;
        public TMP_Text score;
        public TMP_Text captures;
        public TMP_Text kills;
        public TMP_Text deaths;
        public TMP_Text time;
        [Header("Victory")]
        public GameObject medal;
        public ToonShape medalShape;
        public TMP_Text medalLabel;
        public TMP_Text newBest;
        [Header("Defeat")]
        public TMP_Text taunt;
        public TMP_Text tip;
        public GameObject retryEasier;

        public static readonly Color Blue = GuiPalette.Hex(0x3D7BEA);
        public static readonly Color Silver = GuiPalette.Hex(0xC9D3DD);
        public static readonly Color Bronze = GuiPalette.Hex(0xC9843F);

        /// <summary>What the Defeat screen advises, one at a time in turn.</summary>
        public static readonly string[] Tips =
        {
            "Touch your dropped flag to send it home.",
            "Fill your spores and press Q, F or C.",
            "A sniper settles a stand-off; a minigun holds a doorway.",
            "Hold Tab to see who is winning the flag fight.",
            "Stuck? RETRY EASIER makes blue a little clumsier.",
        };

        private static int tipCounter;
        private string tipText;

        protected override void OnEnable()
        {
            if (!victory)
                tipText = Tips[tipCounter++ % Tips.Length];
            base.OnEnable();
        }

        public static string MedalName(int medal) => medal switch { 3 => "GOLD", 2 => "SILVER", 1 => "BRONZE", _ => "" };

        public static Color MedalColour(int medal) => medal switch { 3 => GuiPalette.Gold, 2 => Silver, _ => Bronze };

        protected override void Render(MatchView m)
        {
            var end = m.end;
            GuiText.Set(arena, (m.arena ?? "").ToUpperInvariant());
            GuiText.Set(score, $"<color=#{GuiPalette.HexOf(GuiPalette.Amanita)}>{end.redScore}</color>  –  <color=#{GuiPalette.HexOf(Blue)}>{end.blueScore}</color>");
            GuiText.Set(captures, end.captures.ToString());
            GuiText.Set(kills, end.kills.ToString());
            GuiText.Set(deaths, end.deaths.ToString());
            GuiText.Set(time, Duration(end.seconds));

            if (victory)
            {
                var hasMedal = end.medal > 0;
                GuiText.Show(medal, hasMedal);
                GuiText.Show(newBest, hasMedal && end.newBest);
                if (hasMedal)
                {
                    var colour = MedalColour(end.medal);
                    if (medalShape != null && medalShape.color != colour)
                        medalShape.color = colour;
                    GuiText.Set(medalLabel, MedalName(end.medal));
                }
            }
            else
            {
                GuiText.Show(taunt, !string.IsNullOrEmpty(end.taunt));
                GuiText.Set(taunt, $"“{end.taunt}”");
                GuiText.Set(tip, "TIP  " + (tipText ?? Tips[0]));
                GuiText.Show(retryEasier, end.canRetryEasier);
            }
        }

        /// <summary>A match's length: minutes and seconds, the seconds rounded down (the clock counted them up).</summary>
        public static string Duration(float seconds)
        {
            var s = Mathf.FloorToInt(Mathf.Max(0, seconds));
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
