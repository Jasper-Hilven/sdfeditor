using System;
using CrazyGames;
using UnityEngine;

namespace SdfPlatform
{
    /// <summary>
    /// The device the game runs on (ticket 55), decided by <see cref="DeviceRule"/>. Known from the first read (the URL override, the
    /// platform, the editor's switch) and re-decided once when the CrazyGames SDK comes up (<see cref="WebPortal"/> calls
    /// <see cref="SdkReady"/>); <see cref="Changed"/> fires when the kind differs afterwards. The one reader of
    /// <c>Application.isMobilePlatform</c> and of the SDK's device type.
    /// </summary>
    public static class Device
    {
#if UNITY_EDITOR
        public const string EditorMobileKey = "sdf.portal.editorMobile";
#endif

        static bool known, sdkUp;
        static DeviceKind kind;
        static DeviceSource source;

        /// <summary>The kind of device, and where that was decided.</summary>
        public static DeviceKind Kind
        {
            get
            {
                Ensure();
                return kind;
            }
        }

        public static DeviceSource Source
        {
            get
            {
                Ensure();
                return source;
            }
        }

        /// <summary>A phone or a tablet: touch controls, no pointer lock.</summary>
        public static bool IsTouch => DeviceRule.IsTouch(Kind);

        /// <summary>The kind changed (once at most, when the SDK came up and said otherwise).</summary>
        public static event Action<DeviceKind> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            known = false;
            sdkUp = false;
            Changed = null;
        }

        /// <summary>The SDK is up: its device type now counts.</summary>
        internal static void SdkReady()
        {
            sdkUp = true;
            Ensure();
            Evaluate(true);
        }

        /// <summary>Decides again (the editor's switch changed, or a check set another address).</summary>
        public static void Refresh() => Evaluate(true);

        static void Ensure()
        {
            if (!known)
                Evaluate(false);
        }

        static void Evaluate(bool notify)
        {
            string sdkType = null;
            if (sdkUp && CrazySDK.IsAvailable)
            {
                try
                {
                    sdkType = CrazySDK.User.SystemInfo?.device?.type;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Device: the SDK's device type is not readable ({e.Message})");
                }
            }
            var editorMobile = false;
#if UNITY_EDITOR
            editorMobile = UnityEditor.EditorPrefs.GetBool(EditorMobileKey, false);
#endif
            var before = kind;
            var wasKnown = known;
            kind = DeviceRule.Resolve(Application.absoluteURL, sdkType, Application.isMobilePlatform, editorMobile, out source);
            known = true;
            if (!wasKnown || kind != before)
                Debug.Log($"Device: {kind} (from {source})");
            if (notify && wasKnown && kind != before)
                Changed?.Invoke(kind);
        }
    }
}
