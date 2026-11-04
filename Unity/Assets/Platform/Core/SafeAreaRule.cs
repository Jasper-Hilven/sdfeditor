using System;

namespace SdfPlatform
{
    /// <summary>How much of each screen edge a notch, a rounded corner or a system bar covers, as a fraction of the screen's width (left and right) or height (bottom and top).</summary>
    public readonly struct SafeInsets : IEquatable<SafeInsets>
    {
        public readonly float Left, Bottom, Right, Top;

        public SafeInsets(float left, float bottom, float right, float top)
        {
            Left = left;
            Bottom = bottom;
            Right = right;
            Top = top;
        }

        public static readonly SafeInsets None = new SafeInsets(0, 0, 0, 0);

        public bool IsNone => Left <= 0 && Bottom <= 0 && Right <= 0 && Top <= 0;

        public bool Equals(SafeInsets other) => Left == other.Left && Bottom == other.Bottom && Right == other.Right && Top == other.Top;
        public override bool Equals(object obj) => obj is SafeInsets other && Equals(other);
        public override int GetHashCode() => (Left, Bottom, Right, Top).GetHashCode();
        public override string ToString() => $"safe insets L{Left:0.###} B{Bottom:0.###} R{Right:0.###} T{Top:0.###}";
    }

    /// <summary>
    /// The plain rules of the safe area (ticket 57): the GUI keeps its content inside the part of the screen the device leaves free.
    /// Unity's <c>Screen.safeArea</c> gives a rectangle (it is the whole screen in a WebGL page), the page's CSS <c>env(safe-area-inset-*)</c> gives
    /// four distances; both become <see cref="SafeInsets"/>, the larger of the two wins, and absurd values are clamped so a wrong
    /// answer can never push the GUI off the screen.
    /// </summary>
    public static class SafeAreaRule
    {
        /// <summary>No inset may be more than this fraction of the screen, and the two sides of an axis together at most <see cref="MaxAxis"/>.</summary>
        public const float MaxEach = 0.3f, MaxAxis = 0.5f;

        /// <summary>The insets of a safe rectangle (pixels, origin at the bottom left like Unity's) inside a screen.</summary>
        public static SafeInsets FromRect(float x, float y, float width, float height, float screenWidth, float screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0 || width <= 0 || height <= 0)
                return SafeInsets.None;
            return Clamp(new SafeInsets(
                x / screenWidth,
                y / screenHeight,
                (screenWidth - (x + width)) / screenWidth,
                (screenHeight - (y + height)) / screenHeight));
        }

        /// <summary>The insets from CSS distances (px) over a viewport.</summary>
        public static SafeInsets FromCss(float left, float top, float right, float bottom, float viewportWidth, float viewportHeight)
        {
            if (viewportWidth <= 0 || viewportHeight <= 0)
                return SafeInsets.None;
            return Clamp(new SafeInsets(left / viewportWidth, bottom / viewportHeight, right / viewportWidth, top / viewportHeight));
        }

        /// <summary>Each edge covered as much as either says.</summary>
        public static SafeInsets Union(SafeInsets a, SafeInsets b) =>
            new SafeInsets(Math.Max(a.Left, b.Left), Math.Max(a.Bottom, b.Bottom), Math.Max(a.Right, b.Right), Math.Max(a.Top, b.Top));

        /// <summary>No negative inset, none over <see cref="MaxEach"/>, and an axis' two sides scaled down together to at most <see cref="MaxAxis"/>.</summary>
        public static SafeInsets Clamp(SafeInsets insets)
        {
            float One(float v) => float.IsNaN(v) ? 0 : Math.Min(MaxEach, Math.Max(0, v));
            float l = One(insets.Left), r = One(insets.Right), b = One(insets.Bottom), t = One(insets.Top);
            if (l + r > MaxAxis)
            {
                var k = MaxAxis / (l + r);
                l *= k;
                r *= k;
            }
            if (b + t > MaxAxis)
            {
                var k = MaxAxis / (b + t);
                b *= k;
                t *= k;
            }
            return new SafeInsets(l, b, r, t);
        }
    }
}
