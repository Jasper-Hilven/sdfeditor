using NUnit.Framework;

namespace SdfMushrooms.Tests
{
    /// <summary>The "Press H to increase quality" offer: a steady frame rate in the band the automatic rule leaves alone.</summary>
    public sealed class QualityHintRuleTests
    {
        private const float At60 = 1 / 60f;

        /// <summary>Runs <paramref name="seconds"/> of frames at <paramref name="dt"/>; the last answer.</summary>
        private static bool Run(QualityHintRule rule, float seconds, float dt, int level = 2, bool eligible = true, bool upDue = false)
        {
            var shown = false;
            for (var t = 0f; t < seconds; t += dt)
                shown = rule.Tick(dt, level, eligible, upDue);
            return shown;
        }

        [Test]
        public void SteadySixtyShowsAfterTheWholeWindowAndNotBefore()
        {
            var rule = new QualityHintRule();
            Assert.IsFalse(Run(rule, QualityHintRule.For - 0.5f, At60));
            Assert.IsTrue(Run(rule, 1, At60));
        }

        [Test]
        public void StaysWhileTheConditionHolds()
        {
            var rule = new QualityHintRule();
            Run(rule, QualityHintRule.For + 0.1f, At60);
            Assert.IsTrue(Run(rule, 20, At60));
        }

        [Test]
        public void SlowFramesNeverShow()
        {
            Assert.IsFalse(Run(new QualityHintRule(), 30, 1 / 40f));
        }

        [Test]
        public void LeavingTheBandHidesAtOnce_AndItTakesAWholeWindowToComeBack()
        {
            var rule = new QualityHintRule();
            Run(rule, 7, At60);
            Assert.IsTrue(rule.Visible);
            Assert.IsFalse(Run(rule, 1, 1 / 30f));
            Assert.IsFalse(Run(rule, QualityHintRule.For - 0.5f, At60));
            Assert.IsTrue(Run(rule, 1, At60));
        }

        [Test]
        public void AFewLateFramesAreForgiven()
        {
            var rule = new QualityHintRule();
            Run(rule, 3, At60);
            rule.Tick(0.1f, 2, true, false);
            Assert.IsTrue(Run(rule, 4, At60));
        }

        [Test]
        public void NotWhenNotEligible_TheTopLevelOrAutoQualityOff()
        {
            Assert.IsFalse(Run(new QualityHintRule(), 10, At60, eligible: false));
        }

        [Test]
        public void NotWhileTheAutomaticRuleIsClimbing()
        {
            var rule = new QualityHintRule();
            Assert.IsFalse(Run(rule, 10, At60, upDue: true));
            Assert.IsFalse(rule.Visible);
        }

        [Test]
        public void ResetHidesAndStartsTheWindowOver_AsAfterAStepOrAPause()
        {
            var rule = new QualityHintRule();
            Run(rule, 7, At60);
            rule.Reset();
            Assert.IsFalse(rule.Visible);
            Assert.IsFalse(Run(rule, QualityHintRule.For - 0.5f, At60));
        }

        [Test]
        public void ANewLevelStartsTheWindowOver()
        {
            var rule = new QualityHintRule();
            Run(rule, 7, At60, level: 3);
            Assert.IsFalse(rule.Tick(At60, 2, true, false));
            Assert.IsFalse(Run(rule, QualityHintRule.For - 0.5f, At60, level: 2));
            Assert.IsTrue(Run(rule, 1, At60, level: 2));
        }

        [Test]
        public void NotOfferedBackAfterSteppingDownFromThatLevel_UntilTheLevelGoesUp()
        {
            var rule = new QualityHintRule();
            Run(rule, 7, At60, level: 2);
            // Stepped down from 2 to 3: the offer would be 2 again
            Assert.IsFalse(Run(rule, 20, At60, level: 3));
            // Going up forgets it
            Run(rule, 1, At60, level: 2);
            Assert.IsTrue(Run(rule, 10, At60, level: 2));
            Run(rule, 1, At60, level: 3);
            Assert.IsFalse(Run(rule, 20, At60, level: 3));
        }
    }
}
