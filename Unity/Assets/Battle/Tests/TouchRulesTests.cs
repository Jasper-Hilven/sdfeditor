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

        private static void Run(TouchRules.AutoFire a, object enemy, float seconds, float rate = 0, float dt = 0.02f)
        {
            for (var t = 0f; t < seconds - 1e-4f; t += dt)
                a.Update(enemy, rate, dt);
        }

        [Test]
        public void AutoFireWaitsForTheDwellThenFires()
        {
            var a = new TouchRules.AutoFire();
            var enemy = new object();
            Run(a, enemy, TouchRules.AutoFireDwell * 0.5f);
            Assert.IsFalse(a.Firing);
            Run(a, enemy, 0.1f);
            Assert.IsTrue(a.Firing);
        }

        [Test]
        public void ASwipeAcrossAnEnemyNeverFires()
        {
            var a = new TouchRules.AutoFire();
            var enemy = new object();
            Run(a, enemy, 0.1f, TouchRules.SwipeRate * 3);
            Assert.IsFalse(a.Firing);
            Run(a, enemy, 0.2f, TouchRules.SwipeRate * 3);
            Assert.IsFalse(a.Firing);
        }

        [Test]
        public void AFastTurnResetsTheDwellAndStopsAShotInProgress()
        {
            var a = new TouchRules.AutoFire();
            var enemy = new object();
            Run(a, enemy, TouchRules.AutoFireDwell * 0.5f);
            Run(a, enemy, 0.02f, TouchRules.SwipeRate * 2);
            Run(a, enemy, TouchRules.AutoFireDwell * 0.5f);
            Assert.IsFalse(a.Firing, "the dwell started again");
            Run(a, enemy, TouchRules.AutoFireDwell);
            Assert.IsTrue(a.Firing);
            a.Update(enemy, TouchRules.SwipeRate * 2, 0.02f);
            Assert.IsFalse(a.Firing);
        }

        [Test]
        public void ANewEnemyRestartsTheDwell()
        {
            var a = new TouchRules.AutoFire();
            Run(a, new object(), TouchRules.AutoFireDwell + 0.1f);
            Assert.IsTrue(a.Firing);
            var other = new object();
            a.Update(other, 0, 0.005f);
            Assert.IsFalse(a.Firing);
            Run(a, other, TouchRules.AutoFireDwell * 0.5f, 0, 0.005f);
            Assert.IsFalse(a.Firing);
            Run(a, other, 0.15f);
            Assert.IsTrue(a.Firing);
        }

        [Test]
        public void AutoFireHoldsAfterTheCrosshairLeavesThenStops()
        {
            var a = new TouchRules.AutoFire();
            var enemy = new object();
            Run(a, enemy, TouchRules.AutoFireDwell + 0.05f);
            Assert.IsTrue(a.Firing);
            Run(a, null, TouchRules.AutoFireHold - 0.05f);
            Assert.IsTrue(a.Firing);
            Run(a, null, 0.1f);
            Assert.IsFalse(a.Firing);
        }

        [Test]
        public void AWobbleOffTheEdgeDoesNotRestartTheDwell()
        {
            var a = new TouchRules.AutoFire();
            var enemy = new object();
            Run(a, enemy, TouchRules.AutoFireDwell * 0.7f);
            Run(a, null, TouchRules.AutoFireDwell * 0.2f);
            Run(a, enemy, TouchRules.AutoFireDwell * 0.7f);
            Assert.IsTrue(a.Firing);
        }

        [Test]
        public void TheMinigunStartsAtOnceOnAnEnemyNearTheCrosshair()
        {
            var a = new TouchRules.AutoFire();
            a.Update(new object(), 0, 0.016f, TouchRules.MinigunDwell);
            Assert.IsTrue(a.Firing, "no dwell for the minigun");
        }

        [Test]
        public void AFastSwipeStillDoesNotStartTheMinigun()
        {
            var a = new TouchRules.AutoFire();
            a.Update(new object(), TouchRules.SwipeRate * 2, 0.016f, TouchRules.MinigunDwell);
            Assert.IsFalse(a.Firing);
        }

        [Test]
        public void TheMinigunNearAngleIsWiderThanTheAssistAndGrowsWithTheEnemy()
        {
            Assert.Greater(TouchRules.MinigunNearAngle(20), TouchRules.AssistAngle(20));
            Assert.Greater(TouchRules.MinigunNearAngle(5), TouchRules.MinigunNearAngle(40));
            Assert.LessOrEqual(TouchRules.MinigunNearAngle(0.5f), TouchRules.AssistMaxDegrees * TouchRules.MinigunNearScale + 0.01f);
        }
    }
}
