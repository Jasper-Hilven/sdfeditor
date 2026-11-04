using System;

namespace SdfPlatform
{
    /// <summary>The kind of device the game runs on (ticket 55): touch is Mobile or Tablet.</summary>
    public enum DeviceKind
    {
        Desktop,
        Mobile,
        Tablet,
    }

    /// <summary>Where a <see cref="DeviceKind"/> came from, for the log.</summary>
    public enum DeviceSource
    {
        /// <summary>Nothing said otherwise: a desktop.</summary>
        Default,
        /// <summary>The page's address: <c>?device=mobile</c>, <c>tablet</c> or <c>desktop</c>.</summary>
        Url,
        /// <summary>The editor's Tools > Portal > Simulate mobile.</summary>
        Editor,
        /// <summary>The CrazyGames SDK's <c>SystemInfo.device.type</c>.</summary>
        Sdk,
        /// <summary>Unity's <c>Application.isMobilePlatform</c> (a mobile browser).</summary>
        Platform,
    }

    /// <summary>
    /// The plain rule that decides the device kind, from the most to the least trusted: the URL override (for testing), the editor's
    /// Simulate mobile switch (an explicit choice, so it outranks what the simulated SDK says), the SDK's device type, then
    /// <c>Application.isMobilePlatform</c>; else a desktop.
    /// </summary>
    public static class DeviceRule
    {
        public const string UrlParameter = "device";

        public static DeviceKind Resolve(string url, string sdkType, bool isMobilePlatform, bool editorSimulatesMobile, out DeviceSource source)
        {
            if (TryParse(UrlValue(url), out var kind))
            {
                source = DeviceSource.Url;
                return kind;
            }
            if (editorSimulatesMobile)
            {
                source = DeviceSource.Editor;
                return DeviceKind.Mobile;
            }
            if (TryParse(sdkType, out kind))
            {
                source = DeviceSource.Sdk;
                return kind;
            }
            if (isMobilePlatform)
            {
                source = DeviceSource.Platform;
                return DeviceKind.Mobile;
            }
            source = DeviceSource.Default;
            return DeviceKind.Desktop;
        }

        public static bool IsTouch(DeviceKind kind) => kind != DeviceKind.Desktop;

        /// <summary>"mobile", "tablet" or "desktop" (any case) to its kind; anything else is not a kind.</summary>
        public static bool TryParse(string text, out DeviceKind kind)
        {
            kind = DeviceKind.Desktop;
            if (string.IsNullOrEmpty(text))
                return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case "desktop":
                    return true;
                case "mobile":
                    kind = DeviceKind.Mobile;
                    return true;
                case "tablet":
                    kind = DeviceKind.Tablet;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The value of the <c>device</c> query parameter of an address, or null.</summary>
        public static string UrlValue(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;
            var hash = url.IndexOf('#');
            if (hash >= 0)
                url = url.Substring(0, hash);
            var query = url.IndexOf('?');
            if (query < 0)
                return null;
            foreach (var pair in url.Substring(query + 1).Split('&'))
            {
                var equals = pair.IndexOf('=');
                if (equals <= 0)
                    continue;
                if (string.Equals(pair.Substring(0, equals), UrlParameter, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(pair.Substring(equals + 1));
            }
            return null;
        }
    }
}
