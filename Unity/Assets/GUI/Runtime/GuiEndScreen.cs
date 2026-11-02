using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The match's end screens (ticket 32.6), Victory and Defeat, drawn from <see cref="MatchView.end"/>: the arena, the score in the
    /// teams' colours, your captures, kills, deaths and the time, and on Defeat a tip. (Ticket 41 removed the medal, Prime's taunt and
    /// RETRY EASIER with the campaign.)
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
        [Header("Defeat")]
        public TMP_Text tip;
        [Tooltip("The Defeat screen's RETRY button's label")]
        public TMP_Text retryLabel;

        public static readonly Color Blue = GuiPalette.Hex(0x3D7BEA);

        /// <summary>What the Defeat screen advises, one at a time in turn.</summary>
        public static readonly string[] Tips =
        {
            "Touch your dropped flag to send it home.",
            "Fill your rage and activate a power.",
            "A sniper settles a stand-off; a minigun holds a doorway.",
            "Hold Tab to see who is winning the flag fight.",
        };

        private static int tipCounter;
        private string tipText;

        protected override void OnEnable()
        {
            if (!victory)
                tipText = Tips[tipCounter++ % Tips.Length];
            base.OnEnable();
        }

        protected override void Render(MatchView m)
        {
            var end = m.end;
            GuiText.Set(arena, (m.arena ?? "").ToUpperInvariant());
            GuiText.Set(score, $"<color=#{GuiPalette.HexOf(GuiPalette.Amanita)}>{end.redScore}</color>  –  <color=#{GuiPalette.HexOf(Blue)}>{end.blueScore}</color>");
            GuiText.Set(captures, end.captures.ToString());
            GuiText.Set(kills, end.kills.ToString());
            GuiText.Set(deaths, end.deaths.ToString());
            GuiText.Set(time, Duration(end.seconds));
            Label(captures, "CAPTURES");
            Label(kills, "KILLS");
            Label(deaths, "DEATHS");
            Label(time, "TIME");
            if (!victory)
                GuiText.Set(tip, "TIP  " + (tipText ?? Tips[0]));
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
