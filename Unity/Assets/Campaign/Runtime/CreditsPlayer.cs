using System;
using SdfGui;
using UnityEngine;

namespace SdfCampaign
{
    /// <summary>
    /// Plays the credits (ticket 32.8) on the GUI's Credits screen over Boot's backdrop: <see cref="CreditsScroll"/> times the 45 s, and
    /// holding Space, Escape or Start for 0.7 s ends it early (on the first viewing too). When it is over, or skipped, <c>onDone</c> runs
    /// once and the player removes itself.
    /// </summary>
    public sealed class CreditsPlayer : MonoBehaviour
    {
        private CreditsScroll scroll;
        private GuiCredits view;
        private Action onDone;

        public CreditsScroll Scroll => scroll;

        public bool Finished { get; private set; }

        public static CreditsPlayer Begin(GuiFlow flow, Action onDone)
        {
            var player = new GameObject("Credits").AddComponent<CreditsPlayer>();
            player.scroll = new CreditsScroll();
            player.onDone = onDone;
            player.view = flow.GetComponentInChildren<GuiCredits>(true);
            flow.Show(GuiScreen.Credits);
            if (player.view != null)
                player.view.Show(0, 0);
            return player;
        }

        private void Update()
        {
            if (Finished)
                return;
            var held = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Escape) || Input.GetKey(KeyCode.JoystickButton7);
            scroll.Tick(Time.unscaledDeltaTime, held);
            if (view != null)
                view.Show(scroll.Progress, scroll.SkipProgress);
            if (!scroll.Done)
                return;
            Finished = true;
            var callback = onDone;
            onDone = null;
            if (callback != null)
                callback();
            Destroy(gameObject);
        }
    }
}
