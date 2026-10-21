using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SdfBattle.Tests
{
    /// <summary>Ticket 23: each bot picks its own goal from its temperament and the situation.</summary>
    public sealed class BotBrainTests
    {
        private static readonly BotDecision NoGoal = new BotDecision(BotGoal.None);

        private static BotSituation AtSpawn() => new BotSituation
        {
            toOwnStand = 4,
            toOwnFlag = 4,
            toEnemyFlag = 120,
        };

        private static BotGoal Goal(BotStyle style, BotSituation s) => BotBrain.Choose(Temperament.Of(style), s, NoGoal).goal;

        [Test]
        public void ACarrier_AlwaysHeadsHome()
        {
            var s = AtSpawn();
            s.carrying = true;
            s.ownFlag = FlagStatus.Carried;
            s.health = 10;
            s.pods.Add(new PodOption { index = 0, health = true, distance = 2, available = true, useful = true });
            foreach (var style in Temperament.Rotation)
                Assert.AreEqual(BotGoal.BringFlagHome, Goal(style, s), style.ToString());
        }

        [Test]
        public void AtTheStart_EachStyleGoesItsOwnWay()
        {
            var s = AtSpawn();
            Assert.AreEqual(BotGoal.TakeFlag, Goal(BotStyle.Raider, s));
            Assert.AreEqual(BotGoal.DefendFlag, Goal(BotStyle.Guardian, s));
            Assert.AreEqual(BotGoal.Hunt, Goal(BotStyle.Hunter, s));
            s.pods.Add(new PodOption { index = 3, weapon = WeaponIds.Minigun, distance = 40, available = true, useful = true });
            var scavenger = BotBrain.Choose(Temperament.Of(BotStyle.Scavenger), s, NoGoal);
            Assert.AreEqual(BotGoal.FetchWeapon, scavenger.goal);
            Assert.AreEqual(3, scavenger.pod);
        }

        [Test]
        public void TheirFlagTaken_GuardiansAndHuntersChaseTheCarrier()
        {
            var s = AtSpawn();
            s.ownFlag = FlagStatus.Carried;
            s.toOwnFlag = 30;
            Assert.AreEqual(BotGoal.ChaseCarrier, Goal(BotStyle.Guardian, s));
            Assert.AreEqual(BotGoal.ChaseCarrier, Goal(BotStyle.Hunter, s));
            Assert.AreEqual(BotGoal.ChaseCarrier, Goal(BotStyle.AllRounder, s));
        }

        [Test]
        public void TheirFlagDropped_NearbyBotsReturnIt()
        {
            var s = AtSpawn();
            s.ownFlag = FlagStatus.Dropped;
            s.toOwnFlag = 15;
            Assert.AreEqual(BotGoal.ReturnFlag, Goal(BotStyle.Guardian, s));
            Assert.AreEqual(BotGoal.ReturnFlag, Goal(BotStyle.AllRounder, s));
        }

        [Test]
        public void ADroppedEnemyFlag_NearbyIsTakenEvenByAHunter()
        {
            var s = AtSpawn();
            s.enemyFlag = FlagStatus.Dropped;
            s.toEnemyFlag = 5;
            Assert.AreEqual(BotGoal.TakeFlag, Goal(BotStyle.Hunter, s));
        }

        [Test]
        public void ATeammateCarrying_NobodyElseGoesForTheEnemyFlag()
        {
            var s = AtSpawn();
            s.enemyFlag = FlagStatus.Carried;
            s.toEnemyFlag = 5;
            foreach (var style in Temperament.Rotation)
                Assert.AreNotEqual(BotGoal.TakeFlag, Goal(style, s), style.ToString());
        }

        [Test]
        public void LowHealth_FetchesANearHealthPod()
        {
            var s = AtSpawn();
            s.toEnemyFlag = 60;
            s.health = 20;
            s.pods.Add(new PodOption { index = 1, health = true, distance = 12, available = true, useful = true });
            foreach (var style in new[] { BotStyle.Raider, BotStyle.Hunter, BotStyle.Scavenger, BotStyle.AllRounder })
                Assert.AreEqual(BotGoal.FetchHealth, Goal(style, s), style.ToString());
        }

        [Test]
        public void FullHealth_GoesForTheOverhealOnlyWhenNothingElseCalls()
        {
            var s = AtSpawn();
            s.pods.Add(new PodOption { index = 1, health = true, distance = 5, available = true, useful = true });
            Assert.AreNotEqual(BotGoal.FetchHealth, Goal(BotStyle.Raider, s));
            Assert.Greater(BotBrain.HealthNeed(s), 0);
            s.health = 200;
            Assert.AreEqual(0, BotBrain.HealthNeed(s));
            s.health = 150;
            Assert.Less(BotBrain.HealthNeed(s), 0.1f);
        }

        [Test]
        public void EmptyOrUselessPods_AreNotFetched()
        {
            var s = AtSpawn();
            s.health = 10;
            s.pods.Add(new PodOption { index = 0, health = true, distance = 3, available = false, useful = true });
            s.pods.Add(new PodOption { index = 1, weapon = WeaponIds.Minigun, distance = 3, available = true, useful = false });
            foreach (var style in Temperament.Rotation)
            {
                var goal = Goal(style, s);
                Assert.AreNotEqual(BotGoal.FetchHealth, goal, style.ToString());
                Assert.AreNotEqual(BotGoal.FetchWeapon, goal, style.ToString());
            }
        }

        [Test]
        public void TheGoalItHas_IsKeptOverACloseCall()
        {
            // An all-rounder at spawn: defending scores a little over hunting; once hunting, it keeps hunting
            var s = AtSpawn();
            var t = Temperament.Of(BotStyle.AllRounder);
            Assert.AreEqual(BotGoal.DefendFlag, BotBrain.Choose(t, s, NoGoal).goal);
            Assert.AreEqual(BotGoal.Hunt, BotBrain.Choose(t, s, new BotDecision(BotGoal.Hunt)).goal);
        }

        [Test]
        public void ATeam_IsAMix()
        {
            var random = new System.Random(5);
            for (var start = 0; start < Temperament.Rotation.Length; start++)
            {
                var styles = Enumerable.Range(0, 4).Select(place => Temperament.StyleFor(place, start)).ToList();
                Assert.AreEqual(4, styles.Distinct().Count());
                var s = AtSpawn();
                s.pods.Add(new PodOption { index = 0, weapon = WeaponIds.Minigun, distance = 40, available = true, useful = true });
                var goals = new HashSet<BotGoal>(styles.Select(style => BotBrain.Choose(Temperament.Varied(style, random), s, NoGoal, random).goal));
                Assert.GreaterOrEqual(goals.Count, 2);
            }
        }

        [Test]
        public void Variation_StaysWithinItsShare()
        {
            var random = new System.Random(9);
            var style = Temperament.Of(BotStyle.Raider);
            for (var i = 0; i < 100; i++)
            {
                var t = Temperament.Varied(BotStyle.Raider, random, 0.25f);
                Assert.That(t.attack, Is.InRange(style.attack * 0.75f - 1e-4f, style.attack * 1.25f + 1e-4f));
                Assert.That(t.fetch, Is.InRange(style.fetch * 0.75f - 1e-4f, style.fetch * 1.25f + 1e-4f));
            }
        }
    }
}
