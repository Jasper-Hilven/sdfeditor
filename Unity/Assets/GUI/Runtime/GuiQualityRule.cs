using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// When the quality level changes by itself, the same in every build (the browser shows at most the display's rate, so frames a second
    /// alone say little; this counts how many frames came in time): one level up when at least <see cref="Share"/> of the frames of
    /// <see cref="UpFor"/> seconds came at the level above's rate (<see cref="UpFps"/>: 60 a second up to Ultra High, 80 for Epic,
    /// 100 for Ultra Epic), one level down when that share of <see cref="DownFor"/> seconds came at <see cref="DownFps"/> or slower, two
    /// down at <see cref="TwoDownFps"/> or slower (as far as there are levels); in between, nothing. A window starts over once it is plain
    /// it cannot make the share. Quality levels go best first.
    /// </summary>
    public sealed class GuiQualityRule
    {
        public const float Share = 0.95f, UpFor = 6, DownFor = 4, DownFps = 50, TwoDownFps = 30;
        /// <summary>The leeway on a frame's time at the rate asked for: frames jitter round the display's.</summary>
        public const float Jitter = 1.1f;

        private float upTime, downTime;
        private int upFrames, upOnTime, downFrames, downLate, downVeryLate;

        /// <summary>The frames a second, 95 % of the time, that a move up to <paramref name="level"/> asks for.</summary>
        public static float UpFps(int level)
        {
            var names = QualitySettings.names;
            var name = level >= 0 && level < names.Length ? names[level] : null;
            return name switch
            {
                SdfMushrooms.QualityExtras.UltraEpic => 100,
                SdfMushrooms.QualityExtras.Epic => 80,
                _ => 60,
            };
        }

        /// <summary>Whether a frame of <paramref name="dt"/> seconds came at <paramref name="fps"/> a second, with the leeway.</summary>
        public static bool InTime(float dt, float fps) => dt <= Jitter / fps;

        public void Reset() => (upTime, upFrames, upOnTime, downTime, downFrames, downLate, downVeryLate) = (0, 0, 0, 0, 0, 0, 0);

        /// <summary>
        /// One frame of <paramref name="dt"/> seconds at quality <paramref name="level"/>: the seconds left to a change (MaxValue with none
        /// coming) and its step (−1 up, 1 or 2 down).
        /// </summary>
        public (float left, int step) Tick(float dt, int level)
        {
            var last = QualitySettings.names.Length - 1;
            var better = level > 0;
            var faster = level < last;
            var need = better ? UpFps(level - 1) : 0;
            upTime = better ? upTime + dt : 0;
            upFrames = better ? upFrames + 1 : 0;
            upOnTime = better ? upOnTime + (InTime(dt, need) ? 1 : 0) : 0;
            downTime = faster ? downTime + dt : 0;
            downFrames = faster ? downFrames + 1 : 0;
            downLate = faster ? downLate + (dt >= 1 / DownFps ? 1 : 0) : 0;
            downVeryLate = faster ? downVeryLate + (dt >= 1 / TwoDownFps ? 1 : 0) : 0;
            // More frames missed than the share allows over a whole window at that rate: this window cannot make it
            if (upFrames - upOnTime > (1 - Share) * UpFor * need)
                (upTime, upFrames, upOnTime) = (0, 0, 0);
            if (downFrames - downLate > (1 - Share) * DownFor * DownFps)
                (downTime, downFrames, downLate, downVeryLate) = (0, 0, 0, 0);
            if (upFrames > 0 && upOnTime >= Share * upFrames)
                return (UpFor - upTime, -1);
            if (downFrames > 0 && downLate >= Share * downFrames)
                return (DownFor - downTime, level < last - 1 && downVeryLate >= Share * downFrames ? 2 : 1);
            return (float.MaxValue, 0);
        }
    }
}
