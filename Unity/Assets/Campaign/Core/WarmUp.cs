using System;
using System.Collections.Generic;

namespace SdfCampaign
{
    /// <summary>What the player can be asked to do in the warm-up.</summary>
    [Flags]
    public enum WarmUpAction { None = 0, Walk = 1, Look = 2, Jump = 4, TakeMinigun = 8, Fire = 16 }

    /// <summary>One prompt of the warm-up: its words, what clears it and, when above 0, the seconds after which it goes by itself.</summary>
    public sealed class WarmUpPrompt
    {
        public readonly string text;
        public readonly WarmUpAction needs;
        public readonly float seconds;

        public WarmUpPrompt(string text, WarmUpAction needs, float seconds = 0)
        {
            this.text = text;
            this.needs = needs;
            this.seconds = seconds;
        }
    }

    /// <summary>
    /// The meadow's warm-up (ticket 32.7, section 2 of 32) as plain rules: prompts shown one at a time, each cleared by doing what it
    /// says (reported by the scene's warm-up player). A thing done before its prompt appears does not count, except taking the minigun
    /// (the pod can be walked onto early): the prompt for it is then cleared at once.
    /// </summary>
    public sealed class WarmUp
    {
        /// <summary>Mushroom 7 after the warm-up, done or skipped: what the game is and how a point is scored.</summary>
        public const string Closer = "It's capture the flag! Grab blue's flag and bring it to ours. That's a point.";

        /// <summary>The meadow's loading tip: the same, before the first match.</summary>
        public const string HowToScore = "Grab blue's flag and carry it to your own flag to score.";
        public const string CloserSpeaker = "Mushroom 7";

        /// <summary>The jump prompt goes by itself after this long.</summary>
        public const float JumpSeconds = 10;

        private readonly IReadOnlyList<WarmUpPrompt> prompts;
        private WarmUpAction seen;
        private bool tookMinigun;
        private float shown;

        public WarmUp(IReadOnlyList<WarmUpPrompt> prompts) => this.prompts = prompts;

        /// <summary>The prompts of the meadow; without a minigun pod to walk onto, only the movement ones.</summary>
        public static WarmUp Default(bool minigunPod)
        {
            var list = new List<WarmUpPrompt>
            {
                new WarmUpPrompt("WASD to walk, mouse to look", WarmUpAction.Walk | WarmUpAction.Look),
                new WarmUpPrompt("SPACE to jump", WarmUpAction.Jump, JumpSeconds),
            };
            if (minigunPod)
            {
                list.Add(new WarmUpPrompt("Walk onto the MINIGUN pod", WarmUpAction.TakeMinigun));
                list.Add(new WarmUpPrompt("Hold the left button to fire", WarmUpAction.Fire));
            }
            return new WarmUp(list);
        }

        public int Index { get; private set; }

        public int Count => prompts.Count;

        public bool Finished => Index >= prompts.Count;

        /// <summary>The prompt on screen; null once all are cleared.</summary>
        public WarmUpPrompt Prompt => Finished ? null : prompts[Index];

        /// <summary>The player did something. Clears the current prompt when everything it needs has been done since it appeared.</summary>
        public void Report(WarmUpAction action)
        {
            if (Finished || action == WarmUpAction.None)
                return;
            if ((action & WarmUpAction.TakeMinigun) != 0)
                tookMinigun = true;
            seen |= action;
            Settle();
        }

        /// <summary>Time passes. Clears the current prompt once it has been on screen for its seconds, if it has any.</summary>
        public void Tick(float seconds)
        {
            if (Finished)
                return;
            shown += seconds;
            var limit = prompts[Index].seconds;
            if (limit <= 0 || shown < limit)
                return;
            Advance();
            Settle();
        }

        private void Advance()
        {
            Index++;
            seen = WarmUpAction.None;
            shown = 0;
        }

        private void Settle()
        {
            while (!Finished)
            {
                var needs = prompts[Index].needs;
                var done = seen | (tookMinigun ? WarmUpAction.TakeMinigun : WarmUpAction.None);
                if ((done & needs) != needs)
                    return;
                Advance();
            }
        }
    }
}
