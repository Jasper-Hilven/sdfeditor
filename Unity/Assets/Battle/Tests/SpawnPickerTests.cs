using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>Ticket 20: a player spawns at a free spot in the side's zone, never inside another player.</summary>
    public sealed class SpawnPickerTests
    {
        private static readonly Vector2 Centre = new Vector2(-57, 0);

        [Test]
        public void TheSpot_KeepsThePlayerInsideTheZone()
        {
            var random = new System.Random(1);
            for (var i = 0; i < 500; i++)
            {
                var spot = SpawnPicker.Pick(Centre, 3, null, random);
                Assert.LessOrEqual(Vector2.Distance(spot, Centre), 3 - SpawnPicker.BodyRadius + 1e-4f);
            }
        }

        [Test]
        public void TheSpots_SpreadOverTheZone()
        {
            var random = new System.Random(2);
            var far = 0;
            for (var i = 0; i < 200; i++)
                if (Vector2.Distance(SpawnPicker.Pick(Centre, 3, null, random), Centre) > 1.5f)
                    far++;
            // Uniform over a disc of reach 2.7 m: about 69% of the spots lie beyond 1.5 m
            Assert.Greater(far, 100);
        }

        [Test]
        public void EightPlayers_SpawnClearOfEachOther()
        {
            var random = new System.Random(3);
            var placed = new List<Vector2>();
            for (var i = 0; i < 8; i++)
            {
                var spot = SpawnPicker.Pick(Centre, 3, placed, random);
                Assert.GreaterOrEqual(SpawnPicker.Room(spot, placed), SpawnPicker.Clearance, $"player {i + 1}");
                placed.Add(spot);
            }
        }

        [Test]
        public void AFullZone_TakesTheLeastCrowdedSpot()
        {
            var random = new System.Random(4);
            // Someone on the middle of a zone barely bigger than a player: every spot is too near, the furthest is taken
            var occupied = new[] { Centre };

            var spot = SpawnPicker.Pick(Centre, 0.8f, occupied, random);

            Assert.Greater(Vector2.Distance(spot, Centre), 0.3f);
            Assert.LessOrEqual(Vector2.Distance(spot, Centre), 0.5f + 1e-4f);
        }

        [Test]
        public void ATinyZone_IsItsMiddle()
        {
            Assert.AreEqual(Centre, SpawnPicker.Pick(Centre, SpawnPicker.BodyRadius, null, new System.Random(5)));
        }

        [Test]
        public void TheSameRandom_PicksTheSameSpot()
        {
            var occupied = new[] { Centre + Vector2.right };

            Assert.AreEqual(SpawnPicker.Pick(Centre, 3, occupied, new System.Random(6)), SpawnPicker.Pick(Centre, 3, occupied, new System.Random(6)));
        }
    }
}
