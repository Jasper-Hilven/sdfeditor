using System;
using SdfBattle;
using SdfGui;
using SdfLevel;
using SdfStory;
using UnityEngine;

namespace SdfCampaign
{
    /// <summary>
    /// Plays a story beat as comic panels (ticket 34) over its battle's scene, loaded and held, where <see cref="BeatPlayer"/> plays the
    /// beats that have no panels yet: the GUI is hidden and your view held still while the story viewer (<see cref="StoryIntro.Play"/>)
    /// shows the panels with Next and Skip; after Play or Skip the GUI is back on the HUD and CLICK TO PLAY waits for the click a browser
    /// needs to lock the pointer, as after a beat of shots, and only then <c>onDone</c> runs (the campaign lets the match go there).
    /// </summary>
    public sealed class ComicBeatPlayer : MonoBehaviour
    {
        private GuiFlow flow;
        private Action onDone;
        private Canvas canvas;
        private FirstPersonController readsInput;
        private bool inputWas;
        private bool waiting, finished;

        /// <summary>The panels are over and the click was made.</summary>
        public bool Finished => finished;

        /// <summary>The panels are over; waiting for the click that hands the scene over.</summary>
        public bool WaitingForClick => waiting;

        /// <summary>The comic's name (its campaign step's id).</summary>
        public string Comic { get; private set; }

        /// <summary>Starts the comic over the held scene of <paramref name="live"/>; <paramref name="onDone"/> runs after the hand-over click.</summary>
        public static ComicBeatPlayer Play(string comic, Match live, GuiFlow flow, Action onDone)
        {
            var player = new GameObject("Comic " + comic).AddComponent<ComicBeatPlayer>();
            player.Comic = comic;
            player.flow = flow;
            player.onDone = onDone;
            player.Begin(live);
            return player;
        }

        private void Begin(Match live)
        {
            // The view stays where the scene left it, in your eyes, for the hand-over; the mouse must not turn it under the panels
            var camera = live != null && live.You != null ? live.You.GetComponentInChildren<Camera>() : null;
            readsInput = camera != null ? camera.GetComponentInParent<FirstPersonController>() : null;
            if (readsInput != null)
            {
                inputWas = readsInput.readsInput;
                readsInput.readsInput = false;
            }
            if (flow != null)
            {
                // The Cutscene screen: Escape is the viewer's Skip, not a pause
                flow.Show(GuiScreen.Cutscene);
                if (flow.Match != null)
                    flow.Match.caption.Clear();
                canvas = flow.GetComponent<Canvas>();
                if (canvas != null)
                    canvas.enabled = false;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            StoryIntro.Play(Comic, Panelled);
        }

        // The last panel's Play or Skip: back to the scene, the view in your eyes, and the click to play
        private void Panelled()
        {
            if (canvas != null)
                canvas.enabled = true;
            if (flow != null)
            {
                flow.Show(GuiScreen.Hud);
                if (flow.Match != null)
                    flow.Match.caption.Set("", BeatPlayer.ClickToPlay, 1, 0, "");
            }
            waiting = true;
        }

        private void Update()
        {
            // A click on the viewer's last button must not count as the hand-over's too: the down of the click came before it
            if (!waiting || finished)
                return;
            if (SceneLoader.AutoPlay || Input.GetMouseButtonDown(0) || Cursor.lockState == CursorLockMode.Locked)
                Handed();
        }

        private void Handed()
        {
            waiting = false;
            finished = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Restore();
            if (flow != null && flow.Match != null)
                flow.Match.caption.Clear();
            var callback = onDone;
            onDone = null;
            callback?.Invoke();
            Destroy(gameObject);
        }

        private void Restore()
        {
            if (readsInput != null)
                readsInput.readsInput = inputWas;
            readsInput = null;
            if (canvas != null)
                canvas.enabled = true;
        }

        private void OnDestroy()
        {
            // Left in the middle (the scene unloaded): the GUI and the input as they were
            if (!finished)
                Restore();
        }
    }
}
