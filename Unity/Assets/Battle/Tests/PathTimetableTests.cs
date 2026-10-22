using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 27.3: a train on a closed path and a timetable from the round clock: the lap, the cars' spacing round a loop, their heading
    /// and height, the wrap-around, and what standing on a moving platform carries.
    /// </summary>
    public sealed class PathTimetableTests
    {
        private const float Tolerance = 1e-3f;

        // A 40 x 20 rectangle, anticlockwise (x east, z north) from (20, -10): 120 m round
        private static ClosedPath Rectangle(params Vector2[] heights) => new ClosedPath(new[]
        {
            new Vector2(20, -10), new Vector2(20, 10), new Vector2(-20, 10), new Vector2(-20, -10),
        }, heights);

        private static PathTimetable Train(float speed = 6, float phase = 0, float gap = 1, params float[] cars) =>
            new PathTimetable(Rectangle(), speed, phase, gap, cars.Length == 0 ? new[] { 4f, 3f, 3f } : cars);

        // ---------------------------------------------------------------- The path

        [Test]
        public void Length_IsTheLoopAllRound() => Assert.AreEqual(120f, Rectangle().Length, Tolerance);

        [Test]
        public void PointAt_WalksTheLegs_AndWrapsAround()
        {
            var path = Rectangle();

            AssertPoint(new Vector3(20, 0, -10), path.PointAt(0));
            AssertPoint(new Vector3(20, 0, 0), path.PointAt(10));
            AssertPoint(new Vector3(0, 0, 10), path.PointAt(40));
            AssertPoint(new Vector3(-20, 0, 0), path.PointAt(70));
            AssertPoint(new Vector3(20, 0, -10), path.PointAt(120));
            // A lap on, and a lap back
            AssertPoint(path.PointAt(33), path.PointAt(33 + 120 * 3));
            AssertPoint(path.PointAt(110), path.PointAt(-10));
        }

        [Test]
        public void HeightAt_IsLinearBetweenKeys_AndRoundTheLoop()
        {
            var path = Rectangle(new Vector2(10, 0), new Vector2(20, 4), new Vector2(30, 4), new Vector2(40, 0));

            Assert.AreEqual(0f, path.HeightAt(10), Tolerance);
            Assert.AreEqual(2f, path.HeightAt(15), Tolerance);
            Assert.AreEqual(4f, path.HeightAt(25), Tolerance);
            Assert.AreEqual(0f, path.HeightAt(90), Tolerance);
            // From the last key (40, 0) to the first a lap on (130, 0) is flat; a different last key slopes across the seam
            var sloped = Rectangle(new Vector2(10, 0), new Vector2(100, 3));
            Assert.AreEqual(1.5f, sloped.HeightAt(55), Tolerance);
            Assert.AreEqual(1.5f, sloped.HeightAt(115), Tolerance);
            Assert.AreEqual(0f, Rectangle().HeightAt(77), Tolerance);
            Assert.AreEqual(4f, new ClosedPath(new[] { new Vector2(0, 0), new Vector2(5, 0), new Vector2(0, 5) }, new[] { new Vector2(0, 4) }).HeightAt(3), Tolerance);
        }

        [Test]
        public void Path_NeedsThreeCorners_AndALength()
        {
            Assert.Throws<System.ArgumentException>(() => new ClosedPath(new[] { Vector2.zero, Vector2.one }));
            Assert.Throws<System.ArgumentException>(() => new ClosedPath(new[] { Vector2.zero, Vector2.zero, Vector2.zero }));
        }

        // ---------------------------------------------------------------- The timetable

        [Test]
        public void ALap_TakesLengthOverSpeed()
        {
            var train = Train(speed: 6);

            Assert.AreEqual(20f, train.LapSeconds, Tolerance);
            for (var car = 0; car < train.CarCount; car++)
                AssertPose(train.PoseOf(car, 3.5f), train.PoseOf(car, 3.5f + train.LapSeconds));
        }

        [Test]
        public void TheHead_IsSpeedTimesTheClock_AndThePhaseSkipsAhead()
        {
            Assert.AreEqual(42f, Train(speed: 6).HeadDistance(7), Tolerance);
            Assert.AreEqual(54f, Train(speed: 6, phase: 2).HeadDistance(7), Tolerance);
            // A train 2 s ahead is where the other will be 2 s on
            AssertPose(Train(speed: 6, phase: 2).PoseOf(1, 5), Train(speed: 6).PoseOf(1, 7));
        }

        [Test]
        public void Cars_KeepTheirSpacing_RoundTheLoop()
        {
            var train = Train(speed: 6, gap: 1, cars: new[] { 4f, 3f, 3f });

            for (var clock = 0f; clock < 25; clock += 0.37f)
            {
                // Front to front: the car ahead's length plus a gap, along the path
                Assert.AreEqual(Wrapped(train, train.FrontArc(0, clock) - train.FrontArc(1, clock)), 4 + 1, Tolerance);
                Assert.AreEqual(Wrapped(train, train.FrontArc(1, clock) - train.FrontArc(2, clock)), 3 + 1, Tolerance);
                // A car's middle is half its length behind its front
                Assert.AreEqual(Wrapped(train, train.FrontArc(2, clock) - train.CentreArc(2, clock)), 1.5f, Tolerance);
            }
            Assert.AreEqual(4 + 3 + 3 + 2, train.TrainLength, Tolerance);
        }

        private static float Wrapped(PathTimetable train, float distance) => train.Path.Wrap(distance);

        [Test]
        public void ACar_FacesAlongTheLeg_AndTurnsRoundTheCorner()
        {
            var train = Train(speed: 1, gap: 1, cars: new[] { 4f });
            // Front at 12 m on the east leg: the middle is at 10 m, heading north (+z, yaw 0)
            AssertPose(train.PoseOf(0, 12), new Vector3(20, 0, 0), 0);
            // Straight along the north leg (arc 20..60), heading west (yaw -90)
            AssertPose(train.PoseOf(0, 42), new Vector3(0, 0, 10), -90);
            // The south leg heads east: yaw 90
            AssertPose(train.PoseOf(0, 112), new Vector3(10, 0, -10), 90);
            // Across the corner (front 2 m past it, rear 2 m before): it cuts it, at 45 degrees
            var corner = train.PoseOf(0, 22);
            Assert.AreEqual(-45f, corner.Yaw, 0.1f);
            Assert.AreEqual(19f, corner.Position.x, 0.01f);
            Assert.AreEqual(9f, corner.Position.z, 0.01f);
        }

        [Test]
        public void ACar_PitchesOnTheSlope_AndSitsAtItsHeight()
        {
            var path = Rectangle(new Vector2(0, 0), new Vector2(10, 5), new Vector2(20, 5));
            var train = new PathTimetable(path, 1, 0, 1, new[] { 2f });

            // Rear at 4, front at 6 on the ramp rising 0.5 per metre: atan(0.5)
            var onRamp = train.PoseOf(0, 6);
            Assert.AreEqual(Mathf.Atan(0.5f) * Mathf.Rad2Deg, onRamp.Pitch, 0.01f);
            Assert.AreEqual(2.5f, onRamp.Position.y, Tolerance);
            Assert.AreEqual(0f, train.PoseOf(0, 16).Pitch, 0.01f);
            Assert.AreEqual(5f, train.PoseOf(0, 16).Position.y, Tolerance);
        }

        [Test]
        public void ClockZero_NeedsNothingSaved_TheSameClockIsTheSamePlace()
        {
            var a = Train(speed: 7, phase: 1.5f);
            var b = Train(speed: 7, phase: 1.5f);

            for (var car = 0; car < a.CarCount; car++)
                AssertPose(a.PoseOf(car, 123.4f), b.PoseOf(car, 123.4f));
        }

        // ---------------------------------------------------------------- Standing on it

        [Test]
        public void StandingOnAPlatform_MovesWithItsCentre_WhenItDoesNotTurn()
        {
            var from = new PathPose { Position = new Vector3(0, 0, 0), Yaw = 90 };
            var to = new PathPose { Position = new Vector3(0.1f, 0.02f, 0), Yaw = 90 };

            var carried = PlatformCarry.Displacement(from, to, new Vector3(0, 1, 2));

            Assert.AreEqual(0.1f, carried.x, Tolerance);
            Assert.AreEqual(0.02f, carried.y, Tolerance);
            Assert.AreEqual(0f, carried.z, Tolerance);
            Assert.AreEqual(0f, PlatformCarry.YawChange(from, to), Tolerance);
        }

        [Test]
        public void StandingOnAPlatform_SwingsRoundItsCentre_WhenItTurns()
        {
            var from = new PathPose { Position = new Vector3(5, 0, 5), Yaw = 0 };
            var to = new PathPose { Position = new Vector3(5, 0, 5), Yaw = 90 };

            // 2 m ahead of the centre (+z) ends 2 m to its right (+x) when the platform turns 90 degrees
            var carried = PlatformCarry.Displacement(from, to, new Vector3(5, 0, 7));

            Assert.AreEqual(2f, carried.x, Tolerance);
            Assert.AreEqual(-2f, carried.z, Tolerance);
            Assert.AreEqual(90f, PlatformCarry.YawChange(from, to), Tolerance);
        }

        [Test]
        public void ACarriedPlayer_StaysPutOnTheCar_AllRoundACorner()
        {
            var train = Train(speed: 6, gap: 1, cars: new[] { 4f });
            // The player stands 1 m ahead and 0.5 m to the right of the car's middle, in the car's frame
            var local = new Vector3(0.5f, 1, 1);
            var pose = train.PoseOf(0, 10);
            var player = pose.Position + PlatformCarry.RotateAboutY(local, pose.Yaw);

            for (var clock = 0f; clock < 30; clock += 1f / 60)
            {
                var next = train.PoseOf(0, clock + 1f / 60);
                player += PlatformCarry.Displacement(pose, next, player);
                pose = next;
                var expected = pose.Position + PlatformCarry.RotateAboutY(local, pose.Yaw);
                Assert.AreEqual(0f, Vector3.Distance(expected, player), 0.01f, $"off the car at {clock} s");
            }
        }

        // ---------------------------------------------------------------- Helpers

        private static void AssertPoint(Vector3 expected, Vector3 actual) =>
            Assert.AreEqual(0f, Vector3.Distance(expected, actual), 0.01f, $"expected {expected}, got {actual}");

        private static void AssertPose(PathPose actual, Vector3 position, float yaw)
        {
            AssertPoint(position, actual.Position);
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw, actual.Yaw), 0.1f, $"yaw {actual.Yaw}, expected {yaw}");
        }

        private static void AssertPose(PathPose expected, PathPose actual)
        {
            AssertPoint(expected.Position, actual.Position);
            Assert.AreEqual(0f, Mathf.DeltaAngle(expected.Yaw, actual.Yaw), 0.1f);
            Assert.AreEqual(expected.Pitch, actual.Pitch, 0.1f);
        }
    }
}
