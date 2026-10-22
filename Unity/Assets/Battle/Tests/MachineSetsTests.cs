using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 27.2: the names of a machine set's models and prefabs (the default set unchanged, another set <c>turret-toys-red</c>), and a room's
    /// limits for the helicopters: 6 to 12 m up, 16 m over bases, 3 m off the walls, under the level's flight ceiling.
    /// </summary>
    public sealed class MachineSetsTests
    {
        // ---------------------------------------------------------------- Names

        [Test]
        public void TheDefaultSet_KeepsTodaysNames()
        {
            Assert.AreEqual("mushroom", MachineSets.Default);
            Assert.AreEqual("mushroom", MachineSets.Of(null));
            Assert.AreEqual("mushroom", MachineSets.Of(""));
            Assert.AreEqual("toys", MachineSets.Of("toys"));
            Assert.AreEqual("turret-red", MachineSets.Name("turret", MachineSets.Default, "red"));
            Assert.AreEqual("turret-red", MachineSets.Name("turret", null, "red"));
            Assert.AreEqual("tank-blue-barrel", MachineSets.PieceName("tank", MachineSets.Default, "blue", "barrel"));
            Assert.AreEqual("helicopter-red-tail-rotor", MachineSets.PieceName("helicopter", "mushroom", "red", "tail-rotor"));
        }

        [Test]
        public void AnotherSet_NamesKindSetTeam()
        {
            Assert.AreEqual("turret-toys-red", MachineSets.Name("turret", "toys", "red"));
            Assert.AreEqual("turret-toys-red-foot", MachineSets.PieceName("turret", "toys", "red", "foot"));
            Assert.AreEqual("helicopter-toys-blue-tail-rotor", MachineSets.PieceName("helicopter", "toys", "blue", "tail-rotor"));
        }

        [Test]
        public void ThePrefabPath_FollowsTheSet()
        {
            Assert.AreEqual("Assets/Machines/Prefabs/tank-red.prefab", MachineSets.PrefabPath("tank", null, "red"));
            Assert.AreEqual("Assets/Machines/Prefabs/tank-red.prefab", MachineSets.PrefabPath("tank", "mushroom", "red"));
            Assert.AreEqual("Assets/Machines/Prefabs/helicopter-toys-blue.prefab", MachineSets.PrefabPath("helicopter", "toys", "blue"));
            Assert.AreEqual("Assets/Machines/Prefabs/turret-toys-red.prefab", MachineSets.PrefabPath("turret", "toys", "red"));
        }

        [TestCase("tank-red-barrel", "tank", "mushroom", "red", "barrel")]
        [TestCase("helicopter-blue-tail-rotor", "helicopter", "mushroom", "blue", "tail-rotor")]
        [TestCase("turret-toys-red-foot", "turret", "toys", "red", "foot")]
        [TestCase("helicopter-toys-blue-tail-rotor", "helicopter", "toys", "blue", "tail-rotor")]
        [TestCase("turret-toy-red-foot", "turret", "toy", "red", "foot")]
        [TestCase("tank-toy-blue-barrel", "tank", "toy", "blue", "barrel")]
        [TestCase("helicopter-toy-red-tail-rotor", "helicopter", "toy", "red", "tail-rotor")]
        public void APiecesModelName_IsReadBack(string model, string kind, string set, string team, string piece)
        {
            Assert.IsTrue(MachineSets.TryParsePiece(model, out var k, out var s, out var t, out var p));
            Assert.AreEqual((kind, set, team, piece), (k, s, t, p));
        }

        [TestCase("turret-red")]
        [TestCase("castle0")]
        [TestCase("tank-green-body")]
        [TestCase("")]
        public void AnotherName_IsNotAPiece(string model) => Assert.IsFalse(MachineSets.TryParsePiece(model, out _, out _, out _, out _));

        // ---------------------------------------------------------------- Helicopter limits in a room

        private static readonly Vector2 Min = new Vector2(-75, -50);
        private static readonly Vector2 Max = new Vector2(75, 50);

        /// <summary>A 5 m high base block in the middle of the field.</summary>
        private static float Base(float x, float z) => Mathf.Abs(x) < 10 && Mathf.Abs(z) < 16 ? 5 : 0;

        private static HelicopterPilot RoomPilot(System.Func<float, float, float> surface, float ceiling, int seed)
        {
            var pilot = new HelicopterPilot(Min, Max, surface, new System.Random(seed));
            HelicopterLimits.Room(ceiling).Apply(pilot, 0);
            pilot.Start(new Vector3(-60, 0, 0), 90);
            return pilot;
        }

        [Test]
        public void TheRoomLimits_AreSixToTwelve_SixteenOverBases_ThreeOffTheWalls_AndTheCeiling()
        {
            var limits = HelicopterLimits.Room(20);
            Assert.AreEqual(6, limits.MinHeight);
            Assert.AreEqual(12, limits.MaxHeight);
            Assert.AreEqual(16, limits.OverBases);
            Assert.AreEqual(3, limits.WallClearance);
            var pilot = new HelicopterPilot(Min, Max, null);
            limits.Apply(pilot, 2);
            Assert.AreEqual(16, pilot.OverBases);
            Assert.AreEqual(22, pilot.Ceiling, "the ceiling is over the floor (here at 2 m)");
            Assert.AreEqual(2, pilot.Floor);
            Assert.AreEqual(4, pilot.Margin, "waypoints one metre inside the 3 m clearance");
        }

        [Test]
        public void AHelicopterWithNoLimits_IsAsInTicket24()
        {
            var pilot = new HelicopterPilot(Min, Max, null);
            Assert.IsNull(pilot.OverBases);
            Assert.IsNull(pilot.Ceiling);
            Assert.AreEqual(6, pilot.Margin);
            Assert.AreEqual(6, pilot.MinHeight);
            Assert.AreEqual(12, pilot.MaxHeight);
        }

        [Test]
        public void InARoom_TheHelicopterFlies6To12MetresUp_UnderTheCeiling()
        {
            var pilot = RoomPilot((x, z) => 0, 20, 3);
            var settled = 0;
            for (var t = 0f; t < 300; t += 0.05f)
            {
                pilot.Tick(0.05f);
                Assert.LessOrEqual(pilot.Position.y, 20 + 1e-3f, $"at {t} s");
                if (!pilot.TakingOff && t > 10)
                {
                    Assert.That(pilot.Position.y, Is.InRange(5.5f, 12.5f), $"at {t} s");
                    settled++;
                }
            }
            Assert.Greater(settled, 1000);
        }

        [Test]
        public void InARoom_TheHelicopterFliesSixteenMetresUp_OverABase()
        {
            var pilot = RoomPilot(Base, 20, 5);
            var over = 0;
            for (var t = 0f; t < 900; t += 0.05f)
            {
                pilot.Tick(0.05f);
                var p = pilot.Position;
                Assert.LessOrEqual(p.y, 20 + 1e-3f, $"at {t} s");
                if (Base(p.x, p.z) > 0 && !pilot.TakingOff)
                {
                    over++;
                    Assert.That(p.y, Is.InRange(15.5f, 16.5f), $"over the base at {t} s");
                }
            }
            Assert.Greater(over, 0, "it did cross the base");
        }

        [Test]
        public void InARoom_TheCeilingHoldsItDown_EvenOverATallBase()
        {
            float Tall(float x, float z) => Mathf.Abs(x) < 10 && Mathf.Abs(z) < 16 ? 12 : 0;
            var pilot = RoomPilot(Tall, 17, 7);
            var over = 0;
            for (var t = 0f; t < 900; t += 0.05f)
            {
                pilot.Tick(0.05f);
                Assert.LessOrEqual(pilot.Position.y, 17 + 1e-3f, $"at {t} s");
                if (Tall(pilot.Position.x, pilot.Position.z) > 0)
                    over++;
            }
            Assert.Greater(over, 0);
        }

        [Test]
        public void InARoom_TheHelicopterKeepsThreeMetresOffTheWalls_ButGoesCloserThanIn24()
        {
            var pilot = RoomPilot((x, z) => 0, 20, 11);
            var farthest = 0f;
            for (var t = 0f; t < 1200; t += 0.05f)
            {
                pilot.Tick(0.05f);
                var p = pilot.Position;
                Assert.That(p.x, Is.InRange(Min.x + 3 - 1e-3f, Max.x - 3 + 1e-3f), $"at {t} s");
                Assert.That(p.z, Is.InRange(Min.y + 3 - 1e-3f, Max.y - 3 + 1e-3f), $"at {t} s");
                farthest = Mathf.Max(farthest, Mathf.Abs(pilot.Waypoint.x));
            }
            Assert.Greater(farthest, 69.5f, "waypoints reach past the 6 m margin of ticket 24 (69 m)");
        }
    }
}
