using System;

namespace SdfCampaign
{
    public enum Difficulty { SuperEasy = 0, Easy = 1, Normal = 2, Hard = 3 }

    /// <summary>
    /// What the difficulty does to a setup (ticket 32, section 2): Super Easy takes 0.30 off blue's skill and caps blue at three players, Easy takes 0.15 off blue's skill and caps blue at four players, Hard
    /// adds 0.15 (at most 0.95). RETRY EASIER takes 0.1 more per press, two presses at most. The red bots never change.
    /// </summary>
    public static class DifficultyRules
    {
        public const float Step = 0.15f;
        public const float HardMaxSkill = 0.95f;
        public const float EasierStep = 0.1f;
        public const int MaxEasierSteps = 2;
        public const int EasyMaxBlue = 4;
        public const int SuperEasyMaxBlue = 3;

        /// <summary>A copy of <paramref name="setup"/> with the difficulty and <paramref name="easierSteps"/> (0 to 2) applied.</summary>
        public static BattleSetup Apply(BattleSetup setup, Difficulty difficulty, int easierSteps = 0)
        {
            var result = setup.Clone();
            result.difficulty = difficulty;
            result.retryEasier = easierSteps > 0;
            var skill = setup.blueSkill;
            if (difficulty == Difficulty.Easy || difficulty == Difficulty.SuperEasy)
            {
                var superEasy = difficulty == Difficulty.SuperEasy;
                // The boss fight's crew (two plus Sore Loser's two) needs four bodies, so its cap stays Easy's
                var maxBlue = superEasy && !setup.IsBoss ? SuperEasyMaxBlue : EasyMaxBlue;
                skill -= superEasy ? 2 * Step : Step;
                if (result.blueSize > maxBlue)
                {
                    result.blueSize = maxBlue;
                    Array.Resize(ref result.blueNames, Math.Min(result.blueNames.Length, maxBlue));
                }
            }
            else if (difficulty == Difficulty.Hard)
            {
                skill = Math.Min(skill + Step, Math.Max(HardMaxSkill, setup.blueSkill));
            }
            skill -= EasierStep * Math.Max(0, Math.Min(easierSteps, MaxEasierSteps));
            result.blueSkill = Math.Max(0f, Math.Min(1f, skill));
            return result;
        }

        public static string Key(Difficulty d) => d.ToString().ToLowerInvariant();

        public static Difficulty Parse(string key)
        {
            return key switch { "supereasy" => Difficulty.SuperEasy, "easy" => Difficulty.Easy, "hard" => Difficulty.Hard, _ => Difficulty.Normal };
        }
    }
}
