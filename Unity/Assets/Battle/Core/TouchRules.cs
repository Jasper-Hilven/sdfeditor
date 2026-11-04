using System;

namespace SdfBattle
{
    /// <summary>
    /// The numbers of the touch controls (ticket 56) that do not need the world: how far a swipe turns the view, how much the aim
    /// assist slows it, and how long the auto-fire keeps its finger down after the crosshair leaves its target.
    /// </summary>
    public static class TouchRules
    {
        /// <summary>The setting's default: the middle of the slider.</summary>
        public const float DefaultLook = 0.5f;

        /// <summary>Degrees a swipe across the whole screen's width turns the view at the default setting.</summary>
        public const float DefaultSwipeDegrees = 180;

        /// <summary>The slider's range: the swipe across the screen turns 0.4 times the default's at 0 and 2.5 times at 1, and 1 in the middle.</summary>
        public const float SlowestFactor = 0.4f, FastestFactor = 2.5f;

        /// <summary>The look slider (0 to 1) as the number the default swipe is multiplied by: 1 at the default, geometric from the slowest to the fastest.</summary>
        public static float LookFactor(float setting) => SlowestFactor * (float)Math.Pow(FastestFactor / SlowestFactor, Clamp01(setting));

        /// <summary>The degrees the view turns for a swipe of <paramref name="pixels"/> on a screen <paramref name="screenWidth"/> wide.</summary>
        public static float SwipeDegrees(float pixels, float screenWidth, float setting) =>
            screenWidth <= 0 ? 0 : pixels / screenWidth * DefaultSwipeDegrees * LookFactor(setting);

        /// <summary>The share of the look that is left while the crosshair is as close to an enemy as the assist reaches.</summary>
        public const float AssistSlowest = 0.5f;

        /// <summary>Metres: the half width of the circle round an enemy in which the look is slowed (about its body and head).</summary>
        public const float AssistRadiusMetres = 0.9f;

        /// <summary>Degrees: the smallest and the biggest the assist's circle is seen as (a far enemy is not a point; a near one not the whole screen).</summary>
        public const float AssistMinDegrees = 1.5f, AssistMaxDegrees = 10;

        /// <summary>The angle in degrees, seen from the eye, inside which an enemy <paramref name="distance"/> metres away slows the look to its slowest.</summary>
        public static float AssistAngle(float distance)
        {
            var degrees = (float)(Math.Atan2(AssistRadiusMetres, Math.Max(0.1f, distance)) * 180 / Math.PI);
            return Math.Min(AssistMaxDegrees, Math.Max(AssistMinDegrees, degrees));
        }

        /// <summary>
        /// The look's scale for an enemy <paramref name="distance"/> metres away that the crosshair is <paramref name="angle"/> degrees from:
        /// <see cref="AssistSlowest"/> inside <see cref="AssistAngle"/>, back to 1 at twice that. It never turns the view towards the
        /// enemy (no snapping): it only slows what the player does.
        /// </summary>
        public static float AssistScale(float angle, float distance)
        {
            var inner = AssistAngle(distance);
            var t = Clamp01((angle - inner) / inner);
            return AssistSlowest + (1 - AssistSlowest) * t;
        }

        /// <summary>Seconds the auto-fire holds the trigger after the crosshair leaves the enemy (so a wobble on its edge does not stutter the minigun's spin-up).</summary>
        public const float AutoFireHold = 0.12f;

        private static float Clamp01(float x) => x < 0 ? 0 : x > 1 ? 1 : x;
    }
}
