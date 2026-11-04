using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// Keeps the smallest text readable on a small screen (ticket 64). The canvas scales with the screen at match 0.5 from a 1920 x 1080
    /// reference; a short, wide window (a phone at 780 x 360) would scale below the 12 px that the smallest text (29 pt, ticket 54) needs
    /// (<see cref="Floor"/>). There the reference resolution shrinks, with its aspect, until the scale is the floor: the canvas is then
    /// fewer units across, and the screens that do not fit it scale themselves down (<see cref="GuiFitInside"/>). Larger screens keep 1920 x 1080.
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class GuiScaleFloor : MonoBehaviour
    {
        public const float MinPixels = 12;
        public const float MinPoints = 29;
        /// <summary>The canvas scale (px per unit) under which the smallest text is under 12 px.</summary>
        public const float Floor = MinPixels / MinPoints;
        public static readonly Vector2 Reference = new Vector2(1920, 1080);

        private Vector2 last;

        /// <summary>The reference resolution for a display of this size (the 1920 x 1080 one, or smaller on a very small display).</summary>
        public static Vector2 ReferenceFor(Vector2 display)
        {
            if (display.x <= 0 || display.y <= 0)
                return Reference;
            // Match 0.5: the scale is the geometric mean of the width's and the height's
            var scale = Mathf.Sqrt(display.x / Reference.x * (display.y / Reference.y));
            return scale >= Floor ? Reference : Reference * (scale / Floor);
        }

        private void OnEnable() => Apply();

        private void Update() => Apply();

        /// <summary>Sets the scaler's reference for the canvas' display now.</summary>
        public void Apply()
        {
            var canvas = GetComponent<Canvas>();
            var display = canvas != null ? (Vector2)canvas.renderingDisplaySize : new Vector2(Screen.width, Screen.height);
            if (display == last && last != Vector2.zero)
                return;
            last = display;
            GetComponent<CanvasScaler>().referenceResolution = ReferenceFor(display);
        }
    }
}
