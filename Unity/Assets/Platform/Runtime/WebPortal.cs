using System;
using CrazyGames;
using UnityEngine;

namespace SdfPlatform
{
    /// <summary>
    /// The one place the game talks to the web portal it is published on (ticket 36): CrazyGames' Unity SDK. Everything is safe to call
    /// anywhere: in a desktop build, a plain WebGL build off the portal, or the editor in batch mode, <see cref="Active"/> is false and each
    /// call does nothing (or just calls its continuation). In the editor (not batch) and on localhost the SDK simulates the portal.
    /// <para>
    /// The SDK exists only on CrazyGames, in the editor and on localhost (<c>CrazySDK.IsAvailable</c>); it is initialised once at boot
    /// (<see cref="Init"/>) and the game does not wait for it. What the game reports: <see cref="SetPlaying"/> (gameplayStart / gameplayStop
    /// from where the player is), <see cref="LoadingStart"/> and <see cref="LoadingStop"/> (the Unity SDK has no loading events: they only
    /// stop gameplay), <see cref="MidgameBreak"/> (an ad between a finished battle and the next step), <see cref="HappyTime"/>, and the save
    /// (<see cref="TryGetData"/> / <see cref="SetData"/>, used on the web only).
    /// </para>
    /// </summary>
    public static class WebPortal
    {
        /// <summary>The SDK is initialised and usable.</summary>
        public static bool Active { get; private set; }

        /// <summary>A midgame ad is being requested or shown; the game's UI must not act meanwhile.</summary>
        public static bool AdPlaying { get; private set; }

        /// <summary>The save goes through the portal's data store: a web build with the SDK up (on CrazyGames or localhost).</summary>
        public static bool CloudSave => Active && Application.platform == RuntimePlatform.WebGLPlayer;

        static readonly GameplayTracker tracker = new();
        static readonly AdBreakPolicy ads = new();
        static bool initStarted, initDone, wantPlaying, loading;
        static Action onReady;

#if UNITY_EDITOR
        public const string EditorAdsKey = "sdf.portal.editorAds";
#endif

        static bool Supported
        {
            get
            {
                if (Application.isEditor)
                    return !Application.isBatchMode;
                return Application.platform == RuntimePlatform.WebGLPlayer;
            }
        }

        /// <summary>
        /// Whether the game shows ads at all: off for the initial launch, which CrazyGames does not allow ads in. Every ad goes through
        /// <see cref="MidgameBreak"/>, which then simply goes on; set it true once ads are allowed (the editor's Simulate ads follows it).
        /// </summary>
        public static readonly bool AdsAllowed = false;

        static bool AdsEnabled
        {
            get
            {
                if (!AdsAllowed)
                    return false;
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(EditorAdsKey, false);
#else
                return true;
#endif
            }
        }

        /// <summary>
        /// Starts the SDK (once) and calls <paramref name="ready"/> when it is up, or at once when there is no SDK here. The game does not
        /// wait: until it is up every other call is a no-op.
        /// </summary>
        public static void Init(Action ready = null)
        {
            if (initStarted)
            {
                if (initDone)
                    ready?.Invoke();
                else
                    onReady += ready;
                return;
            }
            initStarted = true;
            if (!Supported || !CrazySDK.IsAvailable)
            {
                initDone = true;
                ready?.Invoke();
                return;
            }
            onReady = ready;
            try
            {
                CrazySDK.Init(() =>
                {
                    Active = true;
                    initDone = true;
                    tracker.Forget();
                    Apply();
                    var callback = onReady;
                    onReady = null;
                    callback?.Invoke();
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"WebPortal: the SDK did not start ({e.Message}); playing without it");
                initDone = true;
                var callback = onReady;
                onReady = null;
                callback?.Invoke();
            }
        }

        // ---------------------------------------------------------------- Gameplay and loading

        /// <summary>Whether the player is playing a live round right now; called every frame by the GUI, reports only changes.</summary>
        public static void SetPlaying(bool playing)
        {
            if (wantPlaying == playing)
                return;
            wantPlaying = playing;
            Apply();
        }

        /// <summary>A scene load begins: gameplay stops until <see cref="LoadingStop"/> (the Unity SDK has no loading events of its own).</summary>
        public static void LoadingStart()
        {
            loading = true;
            Apply();
        }

        public static void LoadingStop()
        {
            loading = false;
            Apply();
        }

        static void Apply()
        {
            if (!Active)
                return;
            var change = tracker.Set(wantPlaying && !loading && !AdPlaying);
            if (change == GameplayChange.None)
                return;
            try
            {
                if (change == GameplayChange.Start)
                    CrazySDK.Game.GameplayStart();
                else
                    CrazySDK.Game.GameplayStop();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"WebPortal: gameplay {change} not sent ({e.Message})");
            }
        }

        // ---------------------------------------------------------------- Ads

        /// <summary>A battle (not a warm-up) ended after <paramref name="seconds"/> of play; counts towards the next ad.</summary>
        public static void BattleFinished(float seconds) => ads.BattleEnded(seconds);

        /// <summary>
        /// A natural break (just after a finished level, before its Victory or Defeat screen): a midgame ad when one is earned, then
        /// <paramref name="then"/>; at once when not. The SDK pauses the game and mutes the sound while the ad is up; <see cref="AdPlaying"/> is
        /// true until it ends, whatever way it ends (finished, no fill, ad blocker, error). Never call it from a menu button or mid-round.
        /// </summary>
        public static void MidgameBreak(Action then)
        {
            if (!Active || !AdsEnabled || AdPlaying || !ads.ShouldAsk(Time.realtimeSinceStartup))
            {
                then?.Invoke();
                return;
            }
            ads.Asked(Time.realtimeSinceStartup);
            AdPlaying = true;
            Apply();
            var done = false;
            void End()
            {
                if (done)
                    return;
                done = true;
                AdPlaying = false;
                Apply();
                then?.Invoke();
            }
            try
            {
                CrazySDK.Ad.RequestAd(CrazyAdType.Midgame, null, _ => End(), End);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"WebPortal: no ad ({e.Message})");
                End();
            }
        }

        /// <summary>The portal celebrates (confetti): a special moment only (the boss beaten, the story finished).</summary>
        public static void HappyTime()
        {
            if (!Active)
                return;
            try
            {
                CrazySDK.Game.HappyTime();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"WebPortal: happytime not sent ({e.Message})");
            }
        }

        // ---------------------------------------------------------------- Save

        /// <summary>A string saved in the portal's data store; false when there is none or the store is not in use (<see cref="CloudSave"/>).</summary>
        public static bool TryGetData(string key, out string value)
        {
            value = null;
            if (!CloudSave)
                return false;
            try
            {
                if (!CrazySDK.Data.HasKey(key))
                    return false;
                value = CrazySDK.Data.GetString(key);
                return value != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"WebPortal: save not read ({e.Message})");
                return false;
            }
        }

        public static void SetData(string key, string value)
        {
            if (!CloudSave)
                return;
            try
            {
                CrazySDK.Data.SetString(key, value);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"WebPortal: save not written ({e.Message})");
            }
        }
    }
}
