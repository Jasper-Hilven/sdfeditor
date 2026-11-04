using NUnit.Framework;

namespace SdfBattle.Tests
{
    public class TouchRulesTests
    {
        [Test]
        public void TheDefaultSettingTurnsHalfAWholeTurnAcrossTheScreen()
        {
            Assert.AreEqual(1f, TouchRules.LookFactor(TouchRules.DefaultLook), 0.001f);
            Assert.AreEqual(180f, TouchRules.SwipeDegrees(800, 800, TouchRules.DefaultLook), 0.01f);
            Assert.AreEqual(90f, TouchRules.SwipeDegrees(400, 800, TouchRules.DefaultLook), 0.01f);
        }

        [Test]
        public void TheSliderGoesFromSlowToFastAndStaysInRange()
        {
            Assert.AreEqual(TouchRules.SlowestFactor, TouchRules.LookFactor(0), 0.001f);
            Assert.AreEqual(TouchRules.FastestFactor, TouchRules.LookFactor(1), 0.001f);
            Assert.AreEqual(TouchRules.SlowestFactor, TouchRules.LookFactor(-3), 0.001f);
            Assert.AreEqual(TouchRules.FastestFactor, TouchRules.LookFactor(9), 0.001f);
            Assert.Less(TouchRules.LookFactor(0.3f), TouchRules.LookFactor(0.7f));
        }

        [Test]
        public void NoScreenTurnsNothing() => Assert.AreEqual(0f, TouchRules.SwipeDegrees(50, 0, 0.5f));

        [TestCase(2f, 1.5f)]
        [TestCase(30f, 1.72f)]
        [TestCase(0.1f, 10f)]
        public void TheAssistCircleShrinksWithDistanceWithinItsLimits(float distance, float approx)
        {
            var angle = TouchRules.AssistAngle(distance);
            Assert.GreaterOrEqual(angle, TouchRules.AssistMinDegrees);
            Assert.LessOrEqual(angle, TouchRules.AssistMaxDegrees);
            if (distance > 2)
                Assert.AreEqual(approx, angle, 0.1f);
        }

        [Test]
        public void TheAssistSlowsTheLookNearAnEnemyAndNowhereElse()
        {
            var inner = TouchRules.AssistAngle(20);
            Assert.AreEqual(TouchRules.AssistSlowest, TouchRules.AssistScale(0, 20), 0.001f);
            Assert.AreEqual(TouchRules.AssistSlowest, TouchRules.AssistScale(inner, 20), 0.001f);
            Assert.AreEqual(1f, TouchRules.AssistScale(inner * 2, 20), 0.001f);
            Assert.AreEqual(1f, TouchRules.AssistScale(90, 20), 0.001f);
            var mid = TouchRules.AssistScale(inner * 1.5f, 20);
            Assert.Greater(mid, TouchRules.AssistSlowest);
            Assert.Less(mid, 1f);
        }
    }
}
