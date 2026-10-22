using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 30.9: the data pulses of the computer level: where a band is on a line at a time (the SdfPulse shader's formula), who is
    /// with one (on the line, within 1.5 m of a band), and a boost that is refreshed, never stacked.
    /// </summary>
    public sealed class PulseRulesTests
    {
        private const float Tolerance = 1e-3f;

        // A pulse every 4 s at 18 m/s: heads 72 m apart, each 3 m long
        private static readonly PulseRules Rules = new PulseRules();

        // A bus like the computer's: 0.8 m wide, 40 m east, then a 45 degree jog north
        private static PulseLine Bus() => new PulseLine(new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(50, 10) }, 0.8f);

        // ---------------------------------------------------------------- The band

        [Test]
        public void Period_IsTheDistanceBetweenHeads() => Assert.AreEqual(72f, Rules.Period, Tolerance);

        [Test]
        public void Behind_IsTheDistanceBackFromTheHead_AndRepeatsEveryPeriod()
        {
            // At t = 1 a head is at 18 m
            Assert.AreEqual(0f, Rules.Behind(18, 1), Tolerance);
            Assert.AreEqual(2f, Rules.Behind(16, 1), Tolerance);
            // The point past the head has the next pulse 70 m behind
            Assert.AreEqual(70f, Rules.Behind(20, 1), Tolerance);
            // The next pulse leaves 4 s later: the same picture a period of time on, 72 m
            Assert.AreEqual(Rules.Behind(30, 1), Rules.Behind(30, 5), Tolerance);
            Assert.AreEqual(Rules.Behind(30, 1), Rules.Behind(30 + 72, 1), Tolerance);
            Assert.That(Rules.Behind(5, 123.4f), Is.InRange(0f, 72f));
            // Before time 0 too: the formula is periodic
            Assert.That(Rules.Behind(5, -3), Is.InRange(0f, 72f));
        }

        [Test]
        public void Covers_IsTheThreeMetresBehindEachHead()
        {
            Assert.IsTrue(Rules.Covers(18, 1));
            Assert.IsTrue(Rules.Covers(15.5f, 1));
            Assert.IsFalse(Rules.Covers(14.5f, 1));
            Assert.IsFalse(Rules.Covers(18.5f, 1));
            // The pulse that left 4 s later is at 18 m 4 s later
            Assert.IsTrue(Rules.Covers(16, 1 + 4));
            Assert.IsFalse(Rules.Covers(30, 1 + 4));
        }

        [Test]
        public void Covers_RunsAlongTheLineAtTheSpeed()
        {
            // The pulse is at the first corner at t = 0 and 36 m on after 2 s
            Assert.IsTrue(Rules.Covers(0, 0));
            Assert.IsTrue(Rules.Covers(36, 2));
            Assert.IsFalse(Rules.Covers(0, 2));
        }

        // ---------------------------------------------------------------- Who is with a pulse

        [Test]
        public void IsNear_ReachesOneAndAHalfMetresBeyondTheBand()
        {
            // The band at t = 1 covers arcs 15 to 18; near is 13.5 to 19.5
            Assert.IsTrue(Rules.IsNear(13.6f, 1));
            Assert.IsFalse(Rules.IsNear(13.4f, 1));
            Assert.IsTrue(Rules.IsNear(19.4f, 1));
            Assert.IsFalse(Rules.IsNear(19.6f, 1));
        }

        [Test]
        public void IsNear_CanNeverFillTheLine()
        {
            // Pulses so long and so often that the reach joins them: always near
            var crowded = new PulseRules { every = 1, speed = 10, length = 8 };
            for (var arc = 0f; arc < 30; arc += 0.7f)
                Assert.IsTrue(crowded.IsNear(arc, 3.3f));
        }

        [Test]
        public void IsBoosted_NeedsTheLine_AndAPulseNear()
        {
            var bus = Bus();

            // On the line where the band is
            Assert.IsTrue(Rules.IsBoosted(bus, new Vector2(16, 0), 1));
            // On the line, a pulse far off
            Assert.IsFalse(Rules.IsBoosted(bus, new Vector2(30, 0), 1));
            // Beside the line where the band is: half the width, 0.4 m margin
            Assert.IsTrue(Rules.IsBoosted(bus, new Vector2(16, 0.7f), 1));
            Assert.IsFalse(Rules.IsBoosted(bus, new Vector2(16, 1.0f), 1));
            Assert.IsFalse(Rules.IsBoosted(bus, new Vector2(16, -3), 1));
        }

        [Test]
        public void IsBoosted_FollowsTheLineRoundAJog()
        {
            var bus = Bus();
            // The jog starts 40 m along; at t = 2.4 the head is at 43.2 m: 3.2 m (of 14.1) round the corner
            var onTheJog = new Vector2(40, 0) + new Vector2(1, 1).normalized * 2.6f;

            Assert.IsTrue(Rules.IsBoosted(bus, onTheJog, 2.4f));
            Assert.IsFalse(Rules.IsBoosted(bus, onTheJog, 1.0f));
        }

        [Test]
        public void IsBoosted_TakesAnyLineOfTheBus()
        {
            // Three traces 1.6 m apart: a scout on the gap between two is on the bus
            var lines = new[]
            {
                new PulseLine(new[] { new Vector2(0, -1.6f), new Vector2(40, -1.6f) }, 0.8f),
                new PulseLine(new[] { new Vector2(0, 0), new Vector2(40, 0) }, 0.8f),
                new PulseLine(new[] { new Vector2(0, 1.6f), new Vector2(40, 1.6f) }, 0.8f),
            };

            Assert.IsTrue(Rules.IsBoosted(lines, new Vector2(16, 0.8f), 1));
            Assert.IsTrue(Rules.IsBoosted(lines, new Vector2(16, -2.0f), 1));
            Assert.IsFalse(Rules.IsBoosted(lines, new Vector2(16, 2.5f), 1));
            Assert.IsFalse(Rules.IsBoosted(new PulseLine[0], new Vector2(16, 0), 1));
        }

        [Test]
        public void IsBoosted_MeetsOneInEveryPeriodOfTime()
        {
            // Standing still at 25 m on the line: a pulse passes within reach for (3 + 3) / 18 s of every 4 s
            var bus = Bus();
            var near = 0;
            const int steps = 4000;
            for (var i = 0; i < steps; i++)
                if (Rules.IsBoosted(bus, new Vector2(25, 0), 100 + i * 0.004f))
                    near++;
            Assert.AreEqual(steps * (3 + 3) / 18f / 4f, near, steps * 0.01f);
        }

        // ---------------------------------------------------------------- Project

        [Test]
        public void Project_GivesTheArcAndTheDistanceOffTheLine()
        {
            var bus = Bus();

            bus.Project(new Vector2(12, 3), out var arc, out var distance);
            Assert.AreEqual(12f, arc, Tolerance);
            Assert.AreEqual(3f, distance, Tolerance);

            // Past the end, the end
            bus.Project(new Vector2(60, 20), out arc, out distance);
            Assert.AreEqual(bus.Length, arc, Tolerance);
            Assert.AreEqual(40f + 10f * Mathf.Sqrt(2), bus.Length, Tolerance);
            Assert.AreEqual(Vector2.Distance(new Vector2(60, 20), new Vector2(50, 10)), distance, Tolerance);
        }

        // ---------------------------------------------------------------- The boost

        [Test]
        public void Boost_LastsItsSecondsAfterThePulse()
        {
            var boost = new PulseBoost();
            Assert.IsFalse(boost.Active(0));
            Assert.AreEqual(1f, boost.Multiplier(0, 1.35f), Tolerance);

            boost.Refresh(10, 2.5f);

            Assert.IsTrue(boost.Active(12.4f));
            Assert.AreEqual(1.35f, boost.Multiplier(12.4f, 1.35f), Tolerance);
            Assert.AreEqual(0.1f, boost.Remaining(12.4f), Tolerance);
            Assert.IsFalse(boost.Active(12.6f));
            Assert.AreEqual(1f, boost.Multiplier(12.6f, 1.35f), Tolerance);
            Assert.AreEqual(0f, boost.Remaining(12.6f), Tolerance);
        }

        [Test]
        public void Boost_IsRefreshedByTheNextPulse_NeverStacked()
        {
            var boost = new PulseBoost();
            boost.Refresh(10, 2.5f);
            boost.Refresh(11, 2.5f);
            boost.Refresh(11.5f, 2.5f);

            // The factor is the same however many pulses, and the end is the last refresh plus the seconds
            Assert.AreEqual(14f, boost.Until, Tolerance);
            Assert.AreEqual(1.35f, boost.Multiplier(11.6f, 1.35f), Tolerance);
            Assert.IsFalse(boost.Active(14.1f));
            // A refresh never shortens it
            boost.Refresh(12, 0.5f);
            Assert.AreEqual(14f, boost.Until, Tolerance);
        }

        [Test]
        public void Boost_ClearedByADeath()
        {
            var boost = new PulseBoost();
            boost.Refresh(10, 2.5f);

            boost.Clear();

            Assert.IsFalse(boost.Active(10.1f));
        }

        [Test]
        public void CarryingTheFlag_StillGainsOverTheSlow()
        {
            // The carrier's 10% slow (0.9) and the boost multiply: faster than a walker without either
            Assert.Greater(0.9f * Rules.boost, 1f);
        }
    }
}
