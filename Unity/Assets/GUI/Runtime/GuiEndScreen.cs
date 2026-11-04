using SdfBattle;
using SdfCampaign;
using SdfHeads;
using SdfLevel;
using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The match's end card (tickets 32.6 and 48), Victory or Defeat, drawn from <see cref="MatchView.end"/>: the arena, the score in the
    /// teams' colours, your captures, kills, deaths and the time. Under it the countdown to the next level (<see cref="NextLevelCountdown"/>,
    /// 10 s, Space or Enter skips it, it stops while the pause menu is open): the next level's name, a line when the difficulty changed,
    /// your head on your body with ◀ ▶ (and A and D by place, with the layout's letters), NEW when this win unlocked a head. When the
    /// countdown is over the midgame ad is asked (<see cref="GameLoop.ContinueAfterCountdown"/>), then the next level loads. The
    /// winning side's bots dance behind the card (44's victory stand-in). On Defeat the tip is the one line of advice.
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
        [Header("Next level")]
        public TMP_Text nextLevel;
        public TMP_Text difficultyLine;
        public TMP_Text countdownLine;
        public TMP_Text keyHint;
        [Header("Your head")]
        public GuiHeadPreview headPreview;
        public TMP_Text headCaption;
        public TMP_Text newHead;

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
        private NextLevelCountdown timer = new NextLevelCountdown();
        private string shownHead;
        private bool previousHeld, nextHeld, skipHeld;

        /// <summary>The seconds left before the next level (a check reads it).</summary>
        public float CountdownRemaining => timer.Remaining;

        /// <summary>The countdown is over (the next level is asked for or loading).</summary>
        public bool CountdownDone => timer.Done;

        /// <summary>Whether the NEW reveal is up, and its text.</summary>
        public bool NewHeadShown => newHead != null && newHead.gameObject.activeSelf;
        public string NewHeadText => newHead != null ? newHead.text : "";

        /// <summary>Whether the difficulty line is up, and its text.</summary>
        public bool DifficultyShown => difficultyLine != null && difficultyLine.gameObject.activeSelf;
        public string DifficultyText => difficultyLine != null ? difficultyLine.text : "";

        protected override void OnEnable()
        {
            if (!victory)
                tipText = Tips[tipCounter++ % Tips.Length];
            // A new card counts down from the start
            timer = new NextLevelCountdown();
            shownHead = null;
            // Keys already held when the card shows do not count as presses
            previousHeld = PlayerInput.HeadPreviousHeld;
            nextHeld = PlayerInput.HeadNextHeld;
            skipHeld = PlayerInput.SkipHeld;
            base.OnEnable();
        }

        protected override void Update()
        {
            base.Update();
            if (!Application.isPlaying || !isActiveAndEnabled || timer.Done)
                return;
            var flow = GuiFlow.Find(this);
            if (flow == null)
                return;
            // The pause menu (and the settings or credits from it) holds the card still
            var paused = flow.IsPaused;
            // Space or Enter going down skips the rest of the countdown
            var skipHeldNow = PlayerInput.SkipHeld;
            var skip = skipHeldNow && !skipHeld;
            skipHeld = skipHeldNow;
            // A and D by place: a press (the key going down) steps the head once, however long the key is held
            var previous = PlayerInput.HeadPreviousHeld;
            var next = PlayerInput.HeadNextHeld;
            if (!paused)
            {
                if (previous && !previousHeld)
                    flow.Execute(GuiCommandId.HeadPrevious, GuiScreen.Hud);
                if (next && !nextHeld)
                    flow.Execute(GuiCommandId.HeadNext, GuiScreen.Hud);
                Dance();
            }
            previousHeld = previous;
            nextHeld = next;
            // Once: the countdown finishes on one call only, so the next level is asked for once
            var finished = skip && timer.Skip(paused);
            finished |= timer.Tick(Time.unscaledDeltaTime, paused);
            if (finished)
                flow.Execute(GuiCommandId.NextLevel, GuiScreen.Hud);
        }

        protected override void Render(MatchView m)
        {
            var end = m.end;
            GuiText.Set(arena, (m.arena ?? "").ToUpperInvariant());
            GuiText.Set(score, $"<color=#{GuiPalette.HexOf(GuiPalette.Red)}>{end.redScore}</color>  –  <color=#{GuiPalette.HexOf(Blue)}>{end.blueScore}</color>");
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
            RenderNext();
        }

        // The countdown, the next level, the difficulty line, the head and the NEW reveal. Outside the game (the editor's preview) it shows a sample.
        private void RenderNext()
        {
            var loop = Application.isPlaying ? GameLoop.Instance : null;
            GuiText.Set(countdownLine, NextLevelCard.CountdownLine(loop != null ? timer.Shown : (int)NextLevelCountdown.Seconds));
            GuiText.Set(nextLevel, "NEXT  " + (loop != null ? NextLevelCard.LevelName(loop.Upcoming) : "KITCHEN"));
            var change = loop != null ? NextLevelCard.DifficultyLine(loop.DifficultyBefore, loop.Difficulty) : null;
            GuiText.Set(difficultyLine, change ?? "");
            GuiText.Show(difficultyLine, change != null);
            GuiText.Set(keyHint, $"{GameKeys.Label(KeyPlace.A)} and {GameKeys.Label(KeyPlace.D)} change head");

            var head = loop != null ? loop.CurrentHead : HeadStandIn.Of("pumpkin") ?? "";
            GuiText.Set(headCaption, HeadStandIn.Label(head));
            if (headPreview != null && loop != null && head != shownHead)
            {
                shownHead = head;
                headPreview.Show(head, loop.YourTeam == Team.Blue ? SdfRig.SoldierModels.BlueSkin : SdfRig.SoldierModels.RedSkin);
            }
            // Outside the game the sample shows the reveal with the longest name, to check it fits
            var unlocked = loop != null ? loop.LastUnlockedHead : Application.isPlaying ? null : "pumpkin";
            GuiText.Show(newHead, unlocked != null);
            if (unlocked != null)
                GuiText.Set(newHead, "NEW HEAD!\n" + HeadStandIn.Label(unlocked).ToUpperInvariant());
        }

        // The winning side's bots dance behind the card: 44's victory emote, or its dance stand-in while there is no victory clip. A body
        // that is moving does not start one (the emote refuses), so it is asked again each frame until it dances.
        private void Dance()
        {
            var match = SdfBattle.Match.Current;
            var view = Match;
            if (match == null || view == null || !view.end.set)
                return;
            var winners = view.end.winnerBlue ? Team.Blue : Team.Red;
            foreach (var player in match.Players)
            {
                if (player == null || player.IsYou || player.soldier == null || player.Team != winners)
                    continue;
                var animator = player.soldier.Animator;
                if (animator == null || animator.IsEmoting)
                    continue;
                animator.Emote("body_victory");
                if (!animator.IsEmoting)
                    animator.Emote("soldier_emote_dance_1");
            }
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
