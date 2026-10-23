using System;
using System.Collections;
using SdfGui;
using SdfPlatform;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfCampaign
{
    /// <summary>
    /// Loads the scenes of the campaign (ticket 32.4), one at a time and never two together: fade to black (unscaled time, the sound with
    /// it), the Loading screen when there is a briefing, <c>LoadSceneAsync</c> in Single mode with its progress on the screen, then the
    /// unused assets and the garbage go while the scene is still covered, and the way back in: a fade in, or, with a briefing, the click
    /// to play (the web allows pointer lock only from a click) that locks the pointer. While a briefing is up the game's time is held at
    /// zero, so the match behind it does not start.
    /// </summary>
    public sealed class SceneLoader : MonoBehaviour
    {
        public const float FadeSeconds = 0.35f;
        public const float MinLoadingSeconds = 1.5f;
        /// <summary>The share of the Loading bar for a download, when there is one.</summary>
        public const float DownloadShare = 0.7f;

        /// <summary>Plays without waiting for the click (the batch-mode checks have no mouse).</summary>
        public static bool AutoPlay;

        public GuiFlow flow;

        /// <summary>A load is under way.</summary>
        public bool Busy { get; private set; }

        /// <summary>
        /// Loads <paramref name="scene"/>. With a <paramref name="briefing"/> the Loading screen shows it and the player's click ends the
        /// wait; without one the scene is covered in black only. <paramref name="onLoaded"/> runs once the scene is live and still
        /// covered, <paramref name="onPlay"/> when the player may play (after the click or the fade in).
        /// </summary>
        public void Load(string scene, LoadingBriefing briefing, Action onLoaded = null, Action onPlay = null)
        {
            if (Busy)
            {
                Debug.LogWarning($"SceneLoader: busy, {scene} not loaded");
                return;
            }
            StartCoroutine(Run(scene, briefing, onLoaded, onPlay));
        }

        private IEnumerator Run(string scene, LoadingBriefing briefing, Action onLoaded, Action onPlay)
        {
            Busy = true;
            WebPortal.LoadingStart();
            var fade = flow.GetComponentInChildren<GuiFade>(true);
            var loading = flow.GetComponentInChildren<GuiLoading>(true);
            yield return FadeTo(fade, 1);

            var held = briefing != null && loading != null;
            if (held)
            {
                loading.Begin(briefing);
                flow.Show(GuiScreen.Loading);
                // The match behind the screen waits for the click
                Time.timeScale = 0;
                yield return FadeTo(fade, 0);
            }
            // The web build's later arenas are bundles: fetched here (or already, by the prefetch), with the Loading screen's bar
            var sceneKey = StreamManifest.SceneKey(scene);
            var streamed = ContentStreamer.Needs(sceneKey);
            var previous = flow.screen;
            var shownForDownload = false;
            if (streamed && !held && loading != null)
            {
                // A beat's load has no briefing and is black; a download must not be
                shownForDownload = true;
                loading.Begin(new LoadingBriefing { arena = scene });
                flow.Show(GuiScreen.Loading);
                yield return FadeTo(fade, 0);
            }
            var downloadShare = streamed ? DownloadShare : 0;
            var cancelled = false;
            if (streamed)
                yield return Fetching(sceneKey, loading, downloadShare, ok => cancelled = !ok);
            if (shownForDownload)
            {
                yield return FadeTo(fade, 1);
                loading.ClearMessage();
                flow.Show(previous);
            }
            if (cancelled)
            {
                loading?.ClearMessage();
                Time.timeScale = 1;
                if (fade != null)
                    fade.Alpha = 0;
                Busy = false;
                WebPortal.LoadingStop();
                if (CampaignRunner.Instance != null)
                    CampaignRunner.Instance.QuitToTitle();
                else
                    flow.Show(GuiScreen.Title);
                yield break;
            }
            var started = Time.unscaledTime;

            var operation = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            if (operation == null)
            {
                Debug.LogError($"SceneLoader: no scene {scene} in the build");
                Time.timeScale = 1;
                flow.Show(GuiScreen.Title);
                if (fade != null)
                    fade.Alpha = 0;
                Busy = false;
                WebPortal.LoadingStop();
                yield break;
            }
            while (!operation.isDone)
            {
                // The operation stops at 0.9 until it activates; the last tenth is the unloading below
                if (held)
                    loading.Set(downloadShare + Mathf.Clamp01(operation.progress / 0.9f) * (0.9f - downloadShare), false);
                yield return null;
            }
            // The scene's Start has run by now (the match reads its setup there)
            yield return null;
            yield return null;
            onLoaded?.Invoke();

            // The bundles of the arenas left behind go (the next arena's, being fetched, stay)
            ContentStreamer.Release(streamed || ContentStreamer.IsReady(sceneKey) ? sceneKey : "");
            yield return Resources.UnloadUnusedAssets();
            GC.Collect();
            LogMemory(scene);

            if (held)
            {
                while (Time.unscaledTime - started < MinLoadingSeconds)
                {
                    loading.Set(0.9f + 0.1f * Mathf.Clamp01((Time.unscaledTime - started) / MinLoadingSeconds), false);
                    yield return null;
                }
                loading.Set(1, true);
                while (!AutoPlay && !Input.GetMouseButtonDown(0))
                    yield return null;
                // The click is the web's one chance to lock the pointer
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                Time.timeScale = 1;
            }
            else
            {
                yield return FadeTo(fade, 0);
            }
            Busy = false;
            WebPortal.LoadingStop();
            onPlay?.Invoke();
        }

        /// <summary>
        /// Fetches a streamed key with the Loading screen's bar over the first <paramref name="share"/> of it (when the screen is there). A failure
        /// never freezes: the message replaces the bar, a click tries again, Escape gives up (<paramref name="result"/> false).
        /// </summary>
        private static IEnumerator Fetching(string key, GuiLoading loading, float share, Action<bool> result)
        {
            while (true)
            {
                var request = ContentStreamer.Fetch(key);
                while (!request.Done)
                {
                    if (loading != null)
                        loading.Set(request.Progress * share, false);
                    yield return null;
                }
                if (!request.Failed)
                {
                    result(true);
                    yield break;
                }
                Debug.LogWarning($"SceneLoader: download for {key} failed: {request.Error}");
                if (loading == null)
                {
                    result(false);
                    yield break;
                }
                loading.Message("DOWNLOAD FAILED - CLICK TO RETRY");
                loading.Set(0, true);
                while (!Input.GetMouseButtonDown(0) && !Input.GetKeyDown(KeyCode.Escape))
                    yield return null;
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    loading.ClearMessage();
                    result(false);
                    yield break;
                }
                loading.ClearMessage();
                loading.Set(0, false);
            }
        }

        /// <summary>
        /// Makes sure a streamed key (a comic) is in, behind the Loading screen when it is not; <paramref name="done"/> gets false when the
        /// player gave up. Already in, it runs at once.
        /// </summary>
        public void Ensure(string key, string title, Action<bool> done)
        {
            if (!ContentStreamer.Needs(key))
            {
                done(true);
                return;
            }
            StartCoroutine(EnsureRun(key, title, done));
        }

        private IEnumerator EnsureRun(string key, string title, Action<bool> done)
        {
            Busy = true;
            var fade = flow.GetComponentInChildren<GuiFade>(true);
            var loading = flow.GetComponentInChildren<GuiLoading>(true);
            var previous = flow.screen;
            if (loading != null)
            {
                loading.Begin(new LoadingBriefing { arena = title });
                flow.Show(GuiScreen.Loading);
            }
            var ok = true;
            yield return Fetching(key, loading, 1, result => ok = result);
            if (loading != null)
            {
                loading.ClearMessage();
                flow.Show(previous);
            }
            Busy = false;
            done(ok);
        }

        private static IEnumerator FadeTo(GuiFade fade, float target)
        {
            if (fade == null)
                yield break;
            var from = fade.Alpha;
            for (var t = 0f; t < 1; t += Time.unscaledDeltaTime / FadeSeconds)
            {
                fade.Alpha = Mathf.Lerp(from, target, t);
                yield return null;
            }
            fade.Alpha = target;
        }

        // For the web check (ticket 32.10): what a load costs in memory, in the editor and development builds only
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void LogMemory(string scene)
        {
            Debug.Log($"SceneLoader: loaded {scene}, managed {GC.GetTotalMemory(false) / 1048576f:0.0} MB, " +
                      $"total {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f:0.0} MB");
        }
    }
}
