using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The match's end screens (ticket 32.6), Victory and Defeat, drawn from <see cref="MatchView.end"/>: the arena, the score in the
    /// teams' colours, your captures, kills, deaths and the time. Victory adds the medal (with NEW BEST when it is one); Defeat adds
    /// Prime's taunt, a tip, and RETRY EASIER when the campaign offers it (the layout closes the gap when it does not).
    /// After the boss fight (<see cref="ViewEnd.boss"/>, ticket 33.7) the same screens show the phase reached and Prime's health left in
    /// place of the score, and lives lost, damage dealt, kills and the time in place of the four numbers; the Defeat screen's RETRY
    /// becomes RETRY PHASE and RESTART FIGHT appears.
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
        [Tooltip("The Defeat screen's RETRY button's label, which says RETRY PHASE after the boss fight")]
        public TMP_Text retryLabel;
        [Tooltip("RESTART FIGHT, shown after the boss fight only")]
        public GameObject restartFight;

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
            if (end.boss)
                RenderBossNumbers(end);
            else
            {
                GuiText.Set(score, $"<color=#{GuiPalette.HexOf(GuiPalette.Amanita)}>{end.redScore}</color>  –  <color=#{GuiPalette.HexOf(Blue)}>{end.blueScore}</color>");
                GuiText.Set(captures, end.captures.ToString());
                GuiText.Set(kills, end.kills.ToString());
                GuiText.Set(deaths, end.deaths.ToString());
                GuiText.Set(time, Duration(end.seconds));
                Label(captures, "CAPTURES");
                Label(kills, "KILLS");
                Label(deaths, "DEATHS");
                Label(time, "TIME");
            }

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
                GuiText.Set(tip, "TIP  " + (end.boss && !string.IsNullOrEmpty(end.tip) ? end.tip : tipText ?? Tips[0]));
                GuiText.Show(retryEasier, end.canRetryEasier);
                GuiText.Set(retryLabel, end.boss ? "RETRY PHASE" : "RETRY");
                GuiText.Show(restartFight, end.boss);
            }
        }

        // The boss fight's end: where the score was, how far it got or that Prime is beaten; the numbers are lives lost, damage dealt, kills, time
        private void RenderBossNumbers(ViewEnd end)
        {
            var line = victory
                ? "PRIME DEFEATED"
                : $"PHASE {end.bossPhase}  ·  PRIME {Mathf.CeilToInt(Mathf.Clamp01(end.bossHealthLeft) * 100)}%";
            GuiText.Set(score, $"<size=50%>{line}</size>");
            GuiText.Set(captures, end.livesLost.ToString());
            GuiText.Set(kills, end.damageDealt.ToString());
            GuiText.Set(deaths, end.kills.ToString());
            GuiText.Set(time, Duration(end.seconds));
            Label(captures, "LIVES LOST");
            Label(kills, "DAMAGE");
            Label(deaths, "KILLS");
            Label(time, "TIME");
        }

        // A stat's caption is the one text under its number
        private static void Label(TMP_Text number, string caption)
        {
            if (number != null && number.transform.childCount > 0)
                GuiText.Set(number.transform.GetChild(0).GetComponent<TMP_Text>(), caption);
        }

        /// <summary>A match's length: minutes and seconds, the seconds rounded down (the clock counted them up).</summary>
        public static string Duration(float seconds)
        {
            var s = Mathf.FloorToInt(Mathf.Max(0, seconds));
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
