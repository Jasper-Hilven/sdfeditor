using System;
using SdfBattle;
using SdfGui;
using SdfLevel;
using SdfPickups;
using UnityEngine;

namespace SdfCampaign
{
    /// <summary>
    /// The warm-up in the loaded battle scene (ticket 32.7; the Fungal level on a first play since 2026-11-02): the match is not held (it starts at once, the clock running and the bots moving), and the caption box
    /// shows one prompt at a time ("WASD to walk, mouse to look", "SPACE to jump", "Hold the left button to fire"; picking up a weapon is
    /// optional, so there is no pod step), each cleared by doing it (the rules are <see cref="WarmUp"/>'s). When the last is
    /// cleared a tip says what to do. Holding Space or Start for 0.7 s skips it (Escape pauses, as in a
    /// match). Only on a first play: the runner does not start it on a retry, a replay or a later session.
    /// </summary>
    public sealed class WarmUpPlayer : MonoBehaviour
    {
        public const string SkipHint = "Hold SPACE to skip";
        public const float WalkMetres = 2.5f, LookDegrees = 25, CloserSeconds = 6;

        private Match match;
        private GuiFlow flow;
        private WarmUp rules;
        private readonly SkipHold skip = new SkipHold();
        private int prompt = -1;
        private Vector3 walkFrom;
        private float yawFrom, looked;
        private int jumpsSeen = -1;
        private float closing = -1;

        public WarmUp Rules => rules;

        /// <summary>The warm-up is over (done or skipped).</summary>
        public bool Over { get; private set; }

        /// <summary>Starts the prompts beside the running match.</summary>
        public static WarmUpPlayer Begin(Match match, GuiFlow flow)
        {
            var player = match.gameObject.AddComponent<WarmUpPlayer>();
            player.match = match;
            player.flow = flow;
            player.rules = WarmUp.Default(SdfPlatform.Device.IsTouch);
            return player;
        }

        /// <summary>Ends the warm-up where it is (a retry, leaving).</summary>
        public void Stop()
        {
            if (!Over)
                Finish(false);
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (flow != null && flow.Match != null)
                flow.Match.caption.Clear();
        }

        private void Update()
        {
            if (match == null || match.You == null)
                return;
            if (Over)
            {
                // The tip stays for a few seconds while the match plays
                closing -= Time.deltaTime;
                if (closing <= 0)
                    Destroy(this);
                return;
            }
            if (skip.Tick(Time.deltaTime, !GameKeys.Blocked && PlayerInput.SkipHeld))
            {
                Finish(false);
                return;
            }
            Watch();
            rules.Tick(Time.deltaTime);
            if (rules.Finished)
            {
                Finish(true);
                return;
            }
            var view = flow != null ? flow.Match : null;
            if (view != null)
                view.caption.Set("", rules.Prompt.text, 1, skip.Progress, SdfPlatform.Device.IsTouch ? "" : SkipHint);
        }

        // What you do, as the rules want to hear it
        private void Watch()
        {
            var you = match.You;
            if (rules.Index != prompt)
            {
                prompt = rules.Index;
                walkFrom = you.transform.position;
                yawFrom = you.Controller.Yaw;
                looked = 0;
            }
            var flat = you.transform.position - walkFrom;
            flat.y = 0;
            if (flat.magnitude >= WalkMetres)
                rules.Report(WarmUpAction.Walk);
            var yaw = you.Controller.Yaw;
            looked += Mathf.Abs(Mathf.DeltaAngle(yawFrom, yaw));
            yawFrom = yaw;
            if (looked >= LookDegrees)
                rules.Report(WarmUpAction.Look);
            // A jump the controller launched since the last look (it may have moved already this frame, off the ground by now)
            var jumps = you.Controller.Jumps;
            if (jumpsSeen >= 0 && jumps != jumpsSeen)
                rules.Report(WarmUpAction.Jump);
            jumpsSeen = jumps;
            var arsenal = you.GetComponent<Arsenal>();
            if (arsenal != null && arsenal.Firing)
                rules.Report(WarmUpAction.Fire);
        }

        private void Finish(bool completed)
        {
            Over = true;
            var view = flow != null ? flow.Match : null;
            if (view == null)
                return;
            // Done or skipped, the tip (no speaker) says what the game is: skipping the prompts must not skip how a point is scored
            view.caption.Set("", WarmUp.Closer, 1, 0, "");
            closing = CloserSeconds;
        }
    }
}
