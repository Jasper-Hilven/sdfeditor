using System.Collections.Generic;
using SdfPlatform;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// Keeps the GUI out of the device's notches and rounded corners (ticket 57). On the canvas' root: every screen (each
    /// <see cref="GuiScreenRoot"/>, the touch controls included) is inset by the safe area, so the HUD, the touch buttons and the menus stay
    /// inside it; what fills a screen as a background (the dim, the vignette) is pushed back out by the same amount, so it still covers
    /// the whole display. Only on a touch device (a notch is a phone's), and read again whenever the screen, its safe area or the canvas changes
    /// (a rotation). The area is <c>Screen.safeArea</c> or the page's CSS <c>env(safe-area-inset-*)</c>, whichever covers more (a WebGL
    /// page reports the whole screen in <c>Screen.safeArea</c>); <see cref="Simulated"/> stands in for both in the snapshots.
    /// </summary>
    public sealed class GuiSafeArea : MonoBehaviour
    {
        /// <summary>A pretend notch for the snapshots and tests (applies on any device), or null.</summary>
        public static SafeInsets? Simulated;

        /// <summary>The insets now in force.</summary>
        public SafeInsets Current { get; private set; }

        private readonly Dictionary<RectTransform, (Vector2 min, Vector2 max)> fills = new Dictionary<RectTransform, (Vector2, Vector2)>();
        private SafeInsets applied;
        private Vector2 lastCanvas = new Vector2(-1, -1);
        private Vector2Int lastScreen;
        private Rect lastArea;
        private bool lastTouch;
        private float nextCheck;

        private void OnEnable() => Apply(true);

        private void Update()
        {
            // The page's insets are asked for a few times a second, the rest are plain reads
            var screen = new Vector2Int(Screen.width, Screen.height);
            var canvasSize = ((RectTransform)transform).rect.size;
            var touch = SdfPlatform.Device.IsTouch;
            if (screen == lastScreen && Screen.safeArea == lastArea && canvasSize == lastCanvas && touch == lastTouch && Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + 0.5f;
            Apply(false);
        }

        /// <summary>The insets this device wants: none off a touch device.</summary>
        public static SafeInsets Wanted()
        {
            if (Simulated.HasValue)
                return SafeAreaRule.Clamp(Simulated.Value);
            if (!SdfPlatform.Device.IsTouch)
                return SafeInsets.None;
            var area = Screen.safeArea;
            var fromScreen = SafeAreaRule.FromRect(area.x, area.y, area.width, area.height, Screen.width, Screen.height);
            return SafeAreaRule.Union(fromScreen, PageBridge.PageInsets());
        }

        /// <summary>Insets every screen now; <paramref name="force"/> even when nothing changed (a screen added, a snapshot sizing the canvas).</summary>
        public void Apply(bool force)
        {
            var canvas = (RectTransform)transform;
            lastScreen = new Vector2Int(Screen.width, Screen.height);
            lastArea = Screen.safeArea;
            lastCanvas = canvas.rect.size;
            lastTouch = SdfPlatform.Device.IsTouch;
            var insets = Wanted();
            if (!force && insets.Equals(applied) && fills.Count > 0)
                return;
            applied = insets;
            Current = insets;
            var size = canvas.rect.size;
            if (size.x <= 0 || size.y <= 0)
                return;
            var min = new Vector2(insets.Left * size.x, insets.Bottom * size.y);
            var max = new Vector2(insets.Right * size.x, insets.Top * size.y);
            foreach (var root in GetComponentsInChildren<GuiScreenRoot>(true))
            {
                if (root.alwaysOn)
                    continue; // the developer's test panel
                var rect = (RectTransform)root.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = min;
                rect.offsetMax = -max;
                // A child that fills the screen is a background: it keeps covering the display
                foreach (RectTransform child in rect)
                {
                    if (child.anchorMin != Vector2.zero || child.anchorMax != Vector2.one)
                        continue;
                    if (!fills.TryGetValue(child, out var home))
                        fills[child] = home = (child.offsetMin, child.offsetMax);
                    child.offsetMin = home.min - min;
                    child.offsetMax = home.max + max;
                }
            }
        }
    }
}
