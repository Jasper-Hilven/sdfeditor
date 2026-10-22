using System;

namespace SdfStory
{
    /// <summary>Where on the screen the 16:9 panel goes, as fractions of the screen (0 to 1), so the rest is bars.</summary>
    public readonly struct NormalizedRect
    {
        public readonly float X, Y, Width, Height;

        public NormalizedRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// The intro viewer's logic without any engine types: which panel shows, what Next and Skip do, what the Next button reads, and
    /// where the panel sits on a screen of any shape. <see cref="StoryIntro"/> wraps it in a screen.
    /// </summary>
    public sealed class StoryIntroLogic
    {
        public const float PanelAspect = 16f / 9f;

        public StoryIntroLogic(int panelCount) => Count = Math.Max(0, panelCount);

        public int Count { get; }
        /// <summary>The panel on show, from 0.</summary>
        public int Index { get; private set; }
        public bool Finished { get; private set; }
        /// <summary>Whether the intro ended through Skip rather than by reaching the end.</summary>
        public bool Skipped { get; private set; }

        public bool OnLast => Count == 0 || Index >= Count - 1;
        public string NextLabel => OnLast ? "PLAY" : "NEXT";

        /// <summary>Goes to the next panel; on the last one (or with no panels) it ends the intro. Returns whether the panel changed.</summary>
        public bool Next()
        {
            if (Finished)
                return false;
            if (OnLast)
            {
                Finished = true;
                return false;
            }
            Index++;
            return true;
        }

        /// <summary>Ends the intro at once, from any panel.</summary>
        public void Skip()
        {
            if (Finished)
                return;
            Finished = true;
            Skipped = true;
        }

        /// <summary>
        /// The 16:9 panel fitted inside a screen: full width with bars above and below on a taller screen, full height with bars at
        /// the sides on a wider one.
        /// </summary>
        public static NormalizedRect Letterbox(float screenWidth, float screenHeight, float contentAspect = PanelAspect)
        {
            if (screenWidth <= 0 || screenHeight <= 0 || contentAspect <= 0)
                return new NormalizedRect(0, 0, 1, 1);
            var screenAspect = screenWidth / screenHeight;
            if (screenAspect > contentAspect)
            {
                var width = contentAspect / screenAspect;
                return new NormalizedRect((1 - width) * 0.5f, 0, width, 1);
            }
            var height = screenAspect / contentAspect;
            return new NormalizedRect(0, (1 - height) * 0.5f, 1, height);
        }
    }
}
