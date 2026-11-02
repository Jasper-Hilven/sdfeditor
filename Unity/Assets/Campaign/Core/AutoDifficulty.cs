using System;

namespace SdfCampaign
{
    /// <summary>What a match came to, in captures: yours (your team's) and the enemy's.</summary>
    public readonly struct CaptureResult
    {
        public CaptureResult(int yours, int enemy)
        {
            Yours = yours;
            Enemy = enemy;
        }

        public int Yours { get; }
        public int Enemy { get; }
    }

    /// <summary>
    /// The automatic difficulty (ticket 42): it follows your results on the Super Easy, Easy, Normal, Hard ladder, one step at a time, starting
    /// on Normal. A loss is one step easier; a win with the enemy on no captures is one step harder; a win with the enemy scoring keeps it.
    /// A draw cannot happen (sudden death), and if one came it would keep it too. Clamped at both ends.
    /// </summary>
    public static class AutoDifficulty
    {
        public const Difficulty Start = Difficulty.Normal;

        public static Difficulty Next(Difficulty current, CaptureResult result)
        {
            if (result.Yours == result.Enemy)
                return current;
            if (result.Yours < result.Enemy)
                return Step(current, -1);
            return result.Enemy == 0 ? Step(current, 1) : current;
        }

        private static Difficulty Step(Difficulty current, int by) =>
            (Difficulty)Math.Clamp((int)current + by, (int)Difficulty.SuperEasy, (int)Difficulty.Hard);
    }
}
