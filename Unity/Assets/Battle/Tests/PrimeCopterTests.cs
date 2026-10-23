using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 33.5: the Prime-copter's flight and Prime's hang. The copter takes its numbers from <see cref="BossRules"/>: its body
    /// 14 to 18 m over the floor, 9 m or more over anything under or ahead of it, 6 m/s, 8 m inside the wall; the dive goes to 6 m over
    /// its circle and back; the crash ends at the wreck's place when the warning ends; Prime's pendulum swings against the copter's
    /// acceleration and settles.
    /// </summary>
    public sealed class PrimeCopterTests
    {
        // The arena's floor, x +-60 and z +-40 (the walls' inner edge), flat
        private static readonly Vector2 Min = new Vector2(-60, -40), Max = new Vector2(60, 40);

        private static PrimeCopterFlight Flight(System.Func<float, float, float> surface = null, int seed = 7, BossRules rules = null) =>
            new PrimeCopterFlight(rules ?? new BossRules(), Min, Max, 0, surface ?? ((x, z) => 0), new System.Random(seed));

        private static void Fly(PrimeCopterFlight flight, float seconds, float dt = 0.05f)
        {
            for (var t = 0f; t < seconds; t += dt)
                flight.Tick(dt);
        }

        [Test]
        public void TheCopter_RisesFirst_ThenFliesItsLaps_FourteenToEighteenMetresUp_EightMetresInsideTheWall()
        {
            var rules = new BossRules();
            var flight = Flight();
            flight.Takeoff(new Vector3(50, 0, 0), -90);
            Assert.AreEqual(CopterMove.Rising, flight.Move);
            Fly(flight, 1);
            Assert.AreEqual(50, flight.Position.x, 1e-3f, "straight up first");
            Assert.AreEqual(CopterMove.Rising, flight.Move);
            Fly(flight, 6);
            Assert.AreEqual(CopterMove.Laps, flight.Move);
            var waypoints = new System.Collections.Generic.HashSet<Vector3>();
            for (var t = 0f; t < 300; t += 0.05f)
            {
                flight.Tick(0.05f);
                var p = flight.Position;
                // The pilot clamps a metre inside the margin
                Assert.That(p.x, Is.InRange(Min.x + rules.copterMargin - 1, Max.x - rules.copterMargin + 1), $"x at {t} s");
                Assert.That(p.z, Is.InRange(Min.y + rules.copterMargin - 1, Max.y - rules.copterMargin + 1), $"z at {t} s");
                if (t > 5)
                    Assert.That(p.y, Is.InRange(rules.copterMinHeight - 0.5f, rules.copterMaxHeight + 0.5f), $"y at {t} s");
                var flat = new Vector2(flight.Velocity.x, flight.Velocity.z);
                Assert.LessOrEqual(flat.magnitude, rules.copterSpeed + 0.3f, $"speed at {t} s");
                waypoints.Add(flight.Waypoint);
            }
            Assert.Greater(waypoints.Count, 5, "roams from point to point");
        }

        [Test]
        public void TheCopter_NeverGoesUnderNineMetresOverAnything_OverASixMetreShelterOrTheDais()
        {
            var rules = new BossRules();
            // Shelters (5.5 m) at (-14, 12) and (14, -20), a pillar (6 m) at (6, 0) and the Royal Box's dais (4 m) from x 41, as in the arena
            float Surface(float x, float z)
            {
                var top = 0f;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(-14, 12)) < 4 || Vector2.Distance(new Vector2(x, z), new Vector2(14, -20)) < 4)
                    top = 5.5f;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(6, 0)) < 1.1f)
                    top = 6;
                if (x > 41 && Mathf.Abs(z) < 14)
                    top = 4;
                return top;
            }
            var over = 0;
            foreach (var seed in new[] { 1, 2, 3 })
            {
                var flight = Flight(Surface, seed);
                flight.Takeoff(new Vector3(-50, 0, 20), 90);
                for (var t = 0f; t < 600; t += 0.05f)
                {
                    flight.Tick(0.05f);
                    var p = flight.Position;
                    var under = Surface(p.x, p.z);
                    if (under > 0)
                        over++;
                    if (t > 3)
                        Assert.GreaterOrEqual(p.y, under + rules.copterClearance - 0.5f, $"seed {seed}: over {under} m at {t} s");
                }
            }
            Assert.Greater(over, 0, "it did fly over something");
        }

        [Test]
        public void TheDive_MovesOverTheCircle_DropsToSixMetres_ThenClimbsBack_AndFliesOn()
        {
            var rules = new BossRules();
            var flight = Flight();
            flight.Takeoff(new Vector3(40, 0, 0), -90);
            Fly(flight, 15);
            Assert.AreEqual(CopterMove.Laps, flight.Move);
            var height = flight.Position.y;
            var circle = new Vector3(-10, 0, 8);
            var spec = rules.SpecOf(BossAttackKind.DowndraftDive);
            var bottomed = new System.Collections.Generic.List<Vector3>();
            flight.DiveBottomed += bottomed.Add;
            Assert.IsTrue(flight.BeginDive(circle, spec.windUp, spec.active, spec.recover));
            Assert.IsFalse(flight.BeginDive(circle, 1, 1, 1), "one dive at a time");
            Assert.IsTrue(flight.Diving);

            // The wind-up: over the circle by its end, still high
            Fly(flight, spec.windUp + 0.1f, 0.02f);
            Assert.AreEqual(CopterMove.DiveDown, flight.Move);
            Assert.AreEqual(circle.x, flight.Position.x, 0.1f);
            Assert.AreEqual(circle.z, flight.Position.z, 0.1f);
            Assert.AreEqual(height, flight.Position.y, 0.5f);
            // The drop: 6 m over the floor at its end, the slam raised once
            Fly(flight, spec.active, 0.02f);
            Assert.AreEqual(1, bottomed.Count);
            Assert.AreEqual(circle, bottomed[0]);
            Assert.AreEqual(rules.diveHeight, flight.Position.y, 0.3f);
            // The climb: back where it was within the recovery, then the laps go on
            Fly(flight, spec.recover + 0.1f, 0.02f);
            Assert.AreEqual(CopterMove.Laps, flight.Move);
            Assert.AreEqual(height, flight.Position.y, 0.6f);
            Assert.IsFalse(flight.Diving);
            Fly(flight, 20);
            Assert.AreEqual(1, bottomed.Count);
        }

        [Test]
        public void TheDive_StaysThreeMetresOverASurfaceUnderTheCircle()
        {
            var flight = Flight((x, z) => x < 0 ? 5.5f : 0);
            flight.Takeoff(new Vector3(40, 0, 0), -90);
            Fly(flight, 15);
            var bottom = float.MaxValue;
            flight.DiveBottomed += _ => bottom = flight.Position.y;
            flight.BeginDive(new Vector3(-5, 0, 0), 1, 1, 1);
            Fly(flight, 3.2f, 0.02f);
            Assert.AreEqual(5.5f + 3, bottom, 0.3f, "6 m over the floor would be inside a 5.5 m cap's top");
        }

        [Test]
        public void TheCrash_FallsToTheWrecksPlace_InTheWarningsSeconds_AndLandsOnce()
        {
            var rules = new BossRules();
            var flight = Flight();
            flight.Takeoff(new Vector3(0, 0, -20), 0);
            Fly(flight, 20);
            var crash = new Vector3(6, 0, 0);
            var landed = 0;
            flight.Landed += () => landed++;
            flight.BeginCrash(crash, rules.crashSeconds);
            Assert.AreEqual(CopterMove.Crashing, flight.Move);
            Assert.IsFalse(flight.Flying);
            Fly(flight, rules.crashSeconds - 0.3f, 0.02f);
            Assert.AreEqual(0, landed);
            Assert.Greater(flight.Position.y, 0.5f, "still falling");
            Assert.Less(flight.Spin, 1, "sputtering");
            Fly(flight, 0.5f, 0.02f);
            Assert.AreEqual(1, landed);
            Assert.AreEqual(CopterMove.Down, flight.Move);
            Assert.AreEqual(crash, flight.Position);
            // Down for good: the rotors wind down, nothing moves it, a second crash is ignored
            flight.BeginCrash(new Vector3(0, 0, 0), 1);
            Fly(flight, 6);
            Assert.AreEqual(crash, flight.Position);
            Assert.AreEqual(0, flight.Spin, 1e-3f);
            Assert.AreEqual(1, landed);
        }

        [Test]
        public void TheCrash_LandsAtOnce_WhenTheStateMachineSaysItLanded()
        {
            var flight = Flight();
            flight.Takeoff(new Vector3(0, 0, -20), 0);
            Fly(flight, 20);
            var landed = 0;
            flight.Landed += () => landed++;
            flight.BeginCrash(new Vector3(6, 0, 0), 2);
            Fly(flight, 1);
            flight.Land();
            flight.Land();
            Assert.AreEqual(1, landed);
            Assert.AreEqual(new Vector3(6, 0, 0), flight.Position);
        }

        [Test]
        public void ATakeoffAfterTheDaze_RisesFromTheWreck_AndFliesAgain()
        {
            var flight = Flight();
            flight.Takeoff(new Vector3(0, 0, -20), 0);
            Fly(flight, 20);
            flight.BeginCrash(new Vector3(6, 0, 0), 2);
            Fly(flight, 3);
            Assert.AreEqual(CopterMove.Down, flight.Move);
            flight.Takeoff(flight.Position, flight.Yaw);
            Fly(flight, 10);
            Assert.AreEqual(CopterMove.Laps, flight.Move);
            Assert.GreaterOrEqual(flight.Position.y, 10);
            Assert.AreEqual(1, flight.Spin, 1e-3f);
        }

        // ---------------------------------------------------------------- Prime's hang

        [Test]
        public void ThePendulum_HangsStraightDown_Still()
        {
            var pendulum = new HangingPendulum();
            pendulum.Step(5, Vector3.zero);
            Assert.AreEqual(0, pendulum.Angle, 1e-6f);
            Assert.That(Vector3.Distance(Vector3.down, pendulum.Direction), Is.LessThan(1e-5f));
        }

        [Test]
        public void ThePendulum_LagsTheCoptersAcceleration_ThenSwingsBack_AndSettles()
        {
            var pendulum = new HangingPendulum();
            // The copter speeds up towards +x for a second: the body is left behind, on -x
            for (var t = 0f; t < 1; t += 0.02f)
                pendulum.Step(0.02f, new Vector3(2, 0, 0));
            Assert.Less(pendulum.X, -0.02f);
            Assert.AreEqual(0, pendulum.Z, 1e-4f);
            Assert.Less(pendulum.Direction.x, 0);
            var peak = Mathf.Abs(pendulum.X);
            // Then it flies steady: it swings through and back, each swing smaller
            var crossed = false;
            var largest = 0f;
            for (var t = 0f; t < 40; t += 0.02f)
            {
                pendulum.Step(0.02f, Vector3.zero);
                if (pendulum.X > 0)
                {
                    crossed = true;
                    largest = Mathf.Max(largest, pendulum.X);
                }
            }
            Assert.IsTrue(crossed, "swings past the middle");
            Assert.Less(largest, peak * 1.5f);
            Assert.Less(pendulum.Angle, 0.01f, "damped out");
        }

        [Test]
        public void ThePendulum_SwingsInAboutThreeAndAHalfSeconds_AndStaysInItsLimit()
        {
            var pendulum = new HangingPendulum();
            pendulum.Kick(new Vector3(1, 0, 0));
            var peaks = 0;
            var last = 0f;
            var lastPeakAt = 0f;
            var firstPeakAt = -1f;
            for (var t = 0f; t < 20; t += 0.01f)
            {
                pendulum.Step(0.01f, Vector3.zero);
                var x = pendulum.X;
                if (last > 0 && x <= 0 && firstPeakAt < 0)
                    firstPeakAt = t;
                if (last > 0 && x <= 0)
                {
                    peaks++;
                    lastPeakAt = t;
                }
                last = x;
            }
            Assert.Greater(peaks, 1);
            var period = (lastPeakAt - firstPeakAt) / (peaks - 1);
            Assert.AreEqual(2 * Mathf.PI * Mathf.Sqrt(pendulum.Length / pendulum.Gravity), period, 0.4f);

            // A violent shove stops at the limit
            pendulum.Reset();
            pendulum.Kick(new Vector3(60, 0, 40));
            for (var t = 0f; t < 3; t += 0.02f)
            {
                pendulum.Step(0.02f, new Vector3(30, 0, 20));
                Assert.LessOrEqual(pendulum.Angle, pendulum.MaxAngle + 1e-4f);
            }
        }

        [Test]
        public void ThePendulum_SurvivesALongFrame()
        {
            var pendulum = new HangingPendulum();
            pendulum.Step(3, new Vector3(8, 0, 8));
            Assert.IsFalse(float.IsNaN(pendulum.X) || float.IsNaN(pendulum.Z));
            Assert.LessOrEqual(pendulum.Angle, pendulum.MaxAngle + 1e-4f);
        }

        // ---------------------------------------------------------------- The tank drop's tanks

        [Test]
        public void AStowedTank_StaysOutOfPlay_UntilItIsBroughtBack_AndThenRespawnsAsAnyTank()
        {
            var tank = new MachineState(0, MachineKind.Tank, Team.Blue);
            var back = 0;
            tank.Respawned += _ => back++;
            var died = 0;
            tank.Died += (_, _) => died++;
            tank.Stow();
            Assert.IsFalse(tank.Alive);
            Assert.AreEqual(0, died, "stowing is no death");
            for (var t = 0; t < 600; t++)
                Assert.IsFalse(tank.Tick(1), "never comes back by itself");
            Assert.AreEqual(0, back);
            // Dropped: back at full health
            tank.Reset();
            Assert.IsTrue(tank.Alive);
            Assert.AreEqual(tank.Stats.health, tank.Health.Current, 1e-4f);
            Assert.AreEqual(1, back);
            // Killed after the drop it waits the usual 30 s
            tank.Hurt(Damage.ByMachine("x", Team.Red, "test", 1000));
            Assert.AreEqual(30, tank.RespawnIn, 1e-4f);
            Assert.IsTrue(tank.Tick(31));
            Assert.AreEqual(2, back);
        }

        [Test]
        public void TheRulesFor_TheCopter_ScaleTheCrashWithTheDifficulty()
        {
            Assert.AreEqual(50, BossRules.For(BossDifficulty.Normal).CrashDamage, 1e-4f);
            Assert.AreEqual(30, BossRules.For(BossDifficulty.Easy).CrashDamage, 1e-4f);
            Assert.AreEqual(65, BossRules.For(BossDifficulty.Hard).CrashDamage, 1e-4f);
            var rules = new BossRules();
            Assert.AreEqual(14, rules.copterMinHeight);
            Assert.AreEqual(18, rules.copterMaxHeight);
            Assert.AreEqual(9, rules.copterClearance);
            Assert.AreEqual(6, rules.copterSpeed);
            Assert.AreEqual(8, rules.copterMargin);
            Assert.AreEqual(6, rules.diveHeight);
        }
    }
}
