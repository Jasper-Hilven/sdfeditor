using System;

namespace SdfCampaign
{
    public enum Difficulty { SuperEasy = 0, Easy = 1, Normal = 2, Hard = 3 }

    /// <summary>
    /// What the difficulty does to a setup (ticket 32, section 2): Super Easy takes 0.30 off blue's skill and caps blue at three players, Easy takes 0.15 off blue's skill and caps blue at four players, Hard
    /// adds 0.15 (at most 0.95). The red bots never change. (Ticket 42 turns this into the automatic difficulty of the enemy side.)
    /// </summary>
    public static class DifficultyRules
    {
        public const float Step = 0.15f;
        public const float HardMaxSkill = 0.95f;
        public const int EasyMaxBlue = 4;
        public const int SuperEasyMaxBlue = 3;

        /// <summary>A copy of <paramref name="setup"/> with the difficulty applied.</summary>
        public static BattleSetup Apply(BattleSetup setup, Difficulty difficulty)
        {
            var result = setup.Clone();
            result.difficulty = difficulty;
            var skill = setup.blueSkill;
            if (difficulty == Difficulty.Easy || difficulty == Difficulty.SuperEasy)
            {
                var superEasy = difficulty == Difficulty.SuperEasy;
                var maxBlue = superEasy ? SuperEasyMaxBlue : EasyMaxBlue;
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
