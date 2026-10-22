using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 29.6: the helicopters' no-fly boxes (<see cref="NoFlyZones"/>, <see cref="HelicopterPilot.NoFly"/>). The bathroom's tub (16.5 m),
    /// giant duck and pyramids (9 m) and tall bottles (6.6 m) stand in the flight band under a 20 m ceiling, so a helicopter may never be
    /// inside one, goes round the tub by its corners, and still reaches its waypoints.
    /// </summary>
    public sealed class NoFlyZonesTests
    {
        private static NoFlyZones Tub()
        {
            // The bathroom's tub: x -25.5 to 25.5, z 27.5 to 50, yaw 180
            var zones = new NoFlyZones();
            zones.Add(new Vector2(0, 38.75f), new Vector2(51, 22.5f), 180);
            return zones;
        }

        /// <summary>The bathroom's blockers: the tub, the duck, the two pyramids and a clump of tall bottles.</summary>
        private static NoFlyZones Bathroom()
        {
            var zones = Tub();
            zones.Add(new Vector2(0, 0), new Vector2(7.5f, 10.5f), 0);
            zones.Add(new Vector2(-26, -15), new Vector2(10.8f, 3.6f), 10);
            zones.Add(new Vector2(26, -19), new Vector2(10.8f, 3.6f), -170);
            zones.Add(new Vector2(-30, 8), new Vector2(14, 6), 25);
            return zones;
        }

        // ---------------------------------------------------------------- The box

        [Test]
        public void ABox_KnowsItsInsideAndItsMargin_TurnedByItsYaw()
        {
            var zones = new NoFlyZones();
            // 20 along x, 4 along z, turned 90 degrees: it lies along z, 4 across x
            zones.Add(new Vector2(10, 0), new Vector2(20, 4), 90);
            Assert.IsTrue(zones.Contains(new Vector2(10, 9)), "its long end, turned onto z");
            Assert.IsFalse(zones.Contains(new Vector2(19, 0)), "not along x any more");
            Assert.IsTrue(zones.Contains(new Vector2(11.5f, 0)));
            Assert.IsFalse(zones.Contains(new Vector2(12.5f, 0)));
            Assert.IsTrue(zones.Blocked(new Vector2(12.5f, 0), 1), "inside the margin");
            Assert.IsFalse(zones.Blocked(new Vector2(14, 0), 1));
        }

        [Test]
        public void TheTub_IsWhereTheLevelPlacesIt()
        {
            var zones = Tub();
            Assert.IsTrue(zones.Contains(new Vector2(0, 28)));
            Assert.IsTrue(zones.Contains(new Vector2(-25, 49.5f)));
            Assert.IsFalse(zones.Contains(new Vector2(0, 27)));
            Assert.IsFalse(zones.Contains(new Vector2(26, 38)));
        }

        // ---------------------------------------------------------------- The route

        [Test]
        public void AClearWay_IsStraight()
        {
            var route = Tub().Route(new Vector2(-60, 0), new Vector2(60, 10));
            Assert.AreEqual(1, route.Count);
            Assert.AreEqual(new Vector2(60, 10), route[0]);
        }

        [Test]
        public void NoBoxes_IsStraight()
        {
            var route = new NoFlyZones().Route(Vector2.zero, new Vector2(5, 5));
            Assert.AreEqual(new[] { new Vector2(5, 5) }, route.ToArray());
        }

        [Test]
        public void AWayThroughTheTub_GoesRoundIt_KeepingTheMarginAndEndingAtTheGoal()
        {
            var zones = Tub();
            // The play area's walls: a way round the back of the tub would go past them
            zones.AreaMin = new Vector2(-71, -46);
            zones.AreaMax = new Vector2(71, 46);
            var from = new Vector2(-40, 40);
            var to = new Vector2(40, 40);
            var route = zones.Route(from, to);

            Assert.Greater(route.Count, 1, "a corner at least");
            Assert.AreEqual(to, route[route.Count - 1]);
            var previous = from;
            var length = 0f;
            foreach (var point in route)
            {
                Assert.IsFalse(zones.Blocked(point, zones.Margin), $"{point} is in the margin");
                Assert.IsFalse(zones.Crosses(previous, point, zones.Margin), $"{previous} to {point} crosses the tub");
                length += Vector2.Distance(previous, point);
                previous = point;
            }
            // Round the near (south) side, 28 m or so: not the far way round and not a wander
            Assert.Less(length, 80 + 2 * 12 + 2 * 12);
            for (var i = 0; i < route.Count - 1; i++)
                Assert.Less(route[i].y, 38.75f, "the shorter way is along the front (z 27.5), not round the wall side");
        }

        [Test]
        public void ARoute_RoundSeveralBoxes_NeverCrossesOne()
        {
            var zones = Bathroom();
            var random = new System.Random(5);
            var routes = 0;
            for (var i = 0; i < 300; i++)
            {
                var from = new Vector2(Mathf.Lerp(-70, 70, (float)random.NextDouble()), Mathf.Lerp(-45, 45, (float)random.NextDouble()));
                var to = new Vector2(Mathf.Lerp(-70, 70, (float)random.NextDouble()), Mathf.Lerp(-45, 45, (float)random.NextDouble()));
                if (zones.Blocked(from, zones.Margin) || zones.Blocked(to, zones.Margin))
                    continue;
                var route = zones.Route(from, to);
                routes++;
                Assert.AreEqual(to, route[route.Count - 1]);
                var previous = from;
                foreach (var point in route)
                {
                    Assert.IsFalse(zones.Crosses(previous, point, 0), $"{from} to {to}: {previous} to {point} goes through a box");
                    previous = point;
                }
            }
            Assert.Greater(routes, 150);
        }

        [Test]
        public void AStartInsideTheMargin_LeavesFirst_AndAGoalInsideItIsBroughtOut()
        {
            var zones = Tub();
            var route = zones.Route(new Vector2(0, 26), new Vector2(0, 40));
            Assert.IsFalse(zones.Blocked(route[0], zones.Margin), "first out of the margin");
            Assert.IsFalse(zones.Blocked(route[route.Count - 1], zones.Margin), "the goal inside the tub is brought out");
            Assert.IsFalse(zones.Contains(zones.PushOut(new Vector2(3, 33), 0.1f)));
        }

        [Test]
        public void PushOut_LeavesAPointOutsideAlone_AndBringsOneInOutThroughTheNearestSide()
        {
            var zones = Tub();
            Assert.AreEqual(new Vector2(0, 20), zones.PushOut(new Vector2(0, 20), 1));
            var out1 = zones.PushOut(new Vector2(0, 29), 0);
            Assert.AreEqual(27.5f, out1.y, 1e-3f, "the front side is nearest");
            Assert.AreEqual(0, out1.x, 1e-3f);
        }

        // ---------------------------------------------------------------- The pilot

        /// <summary>The bathroom's surfaces: the box tops (the tub 16.5 m, the rest 9 m) over the floor.</summary>
        private static float Surface(float x, float z, NoFlyZones zones)
        {
            for (var i = 0; i < zones.Boxes.Count; i++)
            {
                var local = zones.Boxes[i].ToLocal(new Vector2(x, z));
                if (Mathf.Abs(local.x) < zones.Boxes[i].Half.x && Mathf.Abs(local.y) < zones.Boxes[i].Half.y)
                    return i == 0 ? 16.5f : 9f;
            }
            return 0;
        }

        private static HelicopterPilot Pilot(NoFlyZones zones, int seed)
        {
            var pilot = new HelicopterPilot(new Vector2(-75, -50), new Vector2(75, 50), (x, z) => Surface(x, z, zones), new System.Random(seed));
            HelicopterLimits.Room(20).Apply(pilot, 0);
            pilot.NoFly = zones;
            return pilot;
        }

        [Test]
        public void APilot_NeverFliesIntoABox_AndReachesItsWaypoints()
        {
            var zones = Bathroom();
            for (var seed = 1; seed <= 6; seed++)
            {
                var pilot = Pilot(zones, seed);
                pilot.Start(new Vector3(seed % 2 == 0 ? -67 : 67, 0, 32), 90);
                var waypoint = pilot.Waypoint;
                var closest = float.MaxValue;
                var arrivals = 0;
                for (var t = 0f; t < 900; t += 0.05f)
                {
                    pilot.Tick(0.05f);
                    var p = new Vector2(pilot.Position.x, pilot.Position.z);
                    Assert.IsFalse(zones.Contains(p), $"seed {seed}: inside a box at {p}, t={t}");
                    Assert.IsFalse(zones.Blocked(new Vector2(pilot.Waypoint.x, pilot.Waypoint.z), 0), "a waypoint inside a box");
                    if (pilot.Waypoint != waypoint)
                    {
                        if (!pilot.TakingOff && closest < 4)
                            arrivals++;
                        waypoint = pilot.Waypoint;
                        closest = float.MaxValue;
                    }
                    closest = Mathf.Min(closest, Vector2.Distance(p, new Vector2(waypoint.x, waypoint.z)));
                }
                Assert.GreaterOrEqual(arrivals, 8, $"seed {seed}: it reached only {arrivals} waypoints in 15 minutes");
                Assert.AreEqual(0, pilot.Pushes, $"seed {seed}: its route should keep it out, not the push-out");
            }
        }

        [Test]
        public void APilotWithoutBoxes_FliesAsBefore()
        {
            var pilot = new HelicopterPilot(new Vector2(-75, -50), new Vector2(75, 50), (x, z) => 0, new System.Random(3));
            HelicopterLimits.Room(20).Apply(pilot, 0);
            pilot.Start(new Vector3(0, 0, 0), 0);
            var moved = 0f;
            for (var t = 0f; t < 120; t += 0.05f)
            {
                pilot.Tick(0.05f);
                moved = Mathf.Max(moved, new Vector2(pilot.Position.x, pilot.Position.z).magnitude);
            }
            Assert.Greater(moved, 20);
            Assert.AreEqual(0, pilot.Pushes);
        }

        [Test]
        public void APilotCarriedIntoABox_IsPushedOutAtOnce()
        {
            var zones = Tub();
            var pilot = Pilot(zones, 2);
            pilot.Start(new Vector3(0, 0, 40), 0);
            pilot.Tick(0.05f);
            Assert.IsFalse(zones.Contains(new Vector2(pilot.Position.x, pilot.Position.z)));
            Assert.GreaterOrEqual(pilot.Pushes, 1);
        }
    }
}
