using System;
using SdfBattle;

namespace SdfCampaign
{
    public enum Difficulty { SuperEasy = 0, Easy = 1, Normal = 2, Hard = 3 }

    /// <summary>
    /// What the difficulty does to the enemy side of a setup (ticket 42): Super Easy takes 0.30 off the enemy's skill and caps the enemy at three
    /// players, Easy takes 0.15 off and caps it at four, Hard adds 0.15 (at most 0.95). Your own side never changes, whichever side you are on.
    /// The difficulty is set by <see cref="AutoDifficulty"/> after each match.
    /// </summary>
    public static class DifficultyRules
    {
        public const float Step = 0.15f;
        public const float HardMaxSkill = 0.95f;
        public const int EasyMaxSize = 4;
        public const int SuperEasyMaxSize = 3;

        /// <summary>A copy of <paramref name="setup"/> with the difficulty applied to <paramref name="enemySide"/>.</summary>
        public static BattleSetup Apply(BattleSetup setup, Difficulty difficulty, Team enemySide)
        {
            var result = setup.Clone();
            result.difficulty = difficulty;
            var skill = SkillOf(setup, enemySide);
            var size = SizeOf(setup, enemySide);
            if (difficulty == Difficulty.Easy || difficulty == Difficulty.SuperEasy)
            {
                var superEasy = difficulty == Difficulty.SuperEasy;
                var maxSize = superEasy ? SuperEasyMaxSize : EasyMaxSize;
                skill -= superEasy ? 2 * Step : Step;
                if (size > maxSize)
                {
                    size = maxSize;
                    CutNames(result, enemySide, maxSize);
                }
            }
            else if (difficulty == Difficulty.Hard)
            {
                skill = Math.Min(skill + Step, Math.Max(HardMaxSkill, skill));
            }
            skill = Math.Max(0f, Math.Min(1f, skill));
            SetSkill(result, enemySide, skill);
            SetSize(result, enemySide, size);
            return result;
        }

        private static float SkillOf(BattleSetup setup, Team team) => team == Team.Red ? setup.redSkill : setup.blueSkill;

        private static int SizeOf(BattleSetup setup, Team team) => team == Team.Red ? setup.redSize : setup.blueSize;

        private static void SetSkill(BattleSetup setup, Team team, float skill)
        {
            if (team == Team.Red)
                setup.redSkill = skill;
            else
                setup.blueSkill = skill;
        }

        private static void SetSize(BattleSetup setup, Team team, int size)
        {
            if (team == Team.Red)
                setup.redSize = size;
            else
                setup.blueSize = size;
        }

        private static void CutNames(BattleSetup setup, Team team, int size)
        {
            if (team == Team.Red)
                Array.Resize(ref setup.redNames, Math.Min(setup.redNames.Length, size));
            else
                Array.Resize(ref setup.blueNames, Math.Min(setup.blueNames.Length, size));
        }

        public static string Key(Difficulty d) => d.ToString().ToLowerInvariant();

        public static Difficulty Parse(string key)
        {
            return key switch { "supereasy" => Difficulty.SuperEasy, "easy" => Difficulty.Easy, "hard" => Difficulty.Hard, _ => Difficulty.Normal };
        }
    }
}
