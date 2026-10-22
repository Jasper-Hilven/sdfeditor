using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 27.10: what a train's cars do to what they meet (knock aside, 20 damage, once a second per car, the "Toy train" hazard
    /// damage that gives no kill credit and no spores), when it puffs, and when a bot has to wait at the track's edge.
    /// </summary>
    public sealed class MoverRulesTests
    {
        private const float Tolerance = 1e-3f;

        private static readonly MoverImpact Train = new MoverImpact("Toy train", 3, 20);

        // The toy box's loop: straights along z at x = +-20 (z -12 to 12), half-circle ends of radius 20, 7 m/s; here a rectangle of the same length class
        private static ClosedPath Loop(params Vector2[] heights) => new ClosedPath(new[]
        {
            new Vector2(20, -10), new Vector2(20, 10), new Vector2(-20, 10), new Vector2(-20, -10),
        }, heights);

        // Head 4.8 m, then 4.2 and 4.2 behind it with gaps of 0.8: 14.8 m
        private static PathTimetable Timetable(float phase = 0) => new PathTimetable(Loop(), 7, phase, 0.8f, new[] { 4.8f, 4.2f, 4.2f });

        // ---------------------------------------------------------------- Who is knocked aside

        [Test]
        public void ScoutsAndTanks_AreKnockedAside_ButFlagsPodsTurretsAndHelicoptersAreNot()
        {
            Assert.IsTrue(MoverImpact.Affects(MoverTarget.Scout));
            Assert.IsTrue(MoverImpact.Affects(MoverTarget.Tank));
            foreach (var other in new[] { MoverTarget.Flag, MoverTarget.Pod, MoverTarget.Turret, MoverTarget.Helicopter, MoverTarget.Other })
                Assert.IsFalse(MoverImpact.Affects(other), other.ToString());
        }

        [Test]
        public void Impact_TakesItsNumbers_AndAMoverWithoutPushOrDamageHitsNothing()
        {
            Assert.AreEqual("Toy train", Train.Name);
            Assert.AreEqual(3f, Train.Distance);
            Assert.AreEqual(20f, Train.Amount);
            Assert.AreEqual(1f, Train.Cooldown);
            Assert.IsTrue(Train.Hits);
            Assert.IsFalse(new MoverImpact("Cart", 0, 0).Hits);
            Assert.AreEqual("Mover", new MoverImpact(null, 1, 1).Name);
        }

        // ---------------------------------------------------------------- Pushing

        [Test]
        public void APush_PutsTheVictimThreeMetresPastTheCarsSide_OnTheSideItIsOn()
        {
            // A car facing +z at the origin, 0.9 m either side of its middle: right is +x
            var right = Train.PushedTo(new Vector3(0.3f, 0, 2), Vector3.zero, 0, 0.9f);
            var left = Train.PushedTo(new Vector3(-0.3f, 0, 2), Vector3.zero, 0, 0.9f);

            Assert.AreEqual(3.9f, right.x, Tolerance);
            Assert.AreEqual(2f, right.z, Tolerance);
            Assert.AreEqual(-3.9f, left.x, Tolerance);
            Assert.AreEqual(2f, left.z, Tolerance);
        }

        [Test]
        public void APush_KeepsTheHeight_AndTurnsWithTheCar()
        {
            // A car facing +x (yaw 90): its right is -z
            var pushed = Train.PushedTo(new Vector3(5, 1.5f, -0.2f), new Vector3(4, 0, 0), 90, 0.9f);

            Assert.AreEqual(5f, pushed.x, Tolerance);
            Assert.AreEqual(-3.9f, pushed.z, Tolerance);
            Assert.AreEqual(1.5f, pushed.y, Tolerance);
        }

        [Test]
        public void APush_NeverPullsAVictimAlreadyFurtherOutBackIn_AndAMiddleVictimGoesRight()
        {
            var far = Train.PushedTo(new Vector3(6, 0, 1), Vector3.zero, 0, 0.9f);
            var middle = Train.PushedTo(new Vector3(0, 0, 1), Vector3.zero, 0, 0.9f);

            Assert.AreEqual(6f, far.x, Tolerance);
            Assert.AreEqual(3.9f, middle.x, Tolerance);
        }

        // ---------------------------------------------------------------- Once a second per car

        [Test]
        public void ACar_HitsTheSameVictimAtMostOncePerCooldown_ButAnotherCarOrVictimAtOnce()
        {
            var log = new MoverHitLog();
            var scout = new object();
            var tank = new object();
            var engine = new object();
            var wagon = new object();

            Assert.IsTrue(log.TryHit(scout, engine, 10, 1));
            Assert.IsFalse(log.TryHit(scout, engine, 10.5f, 1));
            Assert.IsTrue(log.TryHit(scout, wagon, 10.5f, 1), "another car of the train");
            Assert.IsTrue(log.TryHit(tank, engine, 10.5f, 1), "another victim");
            Assert.IsFalse(log.TryHit(scout, engine, 10.99f, 1));
            Assert.IsTrue(log.TryHit(scout, engine, 11.01f, 1));
        }

        [Test]
        public void TheHitLog_ForgetsWhenTheClockGoesBack_ForANewRound()
        {
            var log = new MoverHitLog();
            var scout = new object();
            var car = new object();
            log.TryHit(scout, car, 200, 1);

            Assert.IsTrue(log.TryHit(scout, car, 0.2f, 1));
        }

        // ---------------------------------------------------------------- The hazard's damage

        private MatchState match;
        private CombatState combat;
        private SporeState spores;
        private readonly List<Death> deaths = new List<Death>();

        private PlayerState Red => match.Players[0];
        private PlayerState Blue => match.Players[4];

        private void StartMatch()
        {
            match = new MatchState(new MatchRules());
            foreach (var team in Teams.All)
                for (var i = 1; i <= match.Rules.teamSize; i++)
                    match.AddPlayer($"{team.Name()} {i}", team, i > 1 || team == Team.Blue);
            combat = new CombatState(match, WeaponTable.Default());
            spores = new SporeState(match, combat, new CaptureTheFlagState(match));
            deaths.Clear();
            match.Died += deaths.Add;
            match.Begin();
            Run(match.Rules.countdownSeconds + 0.05f);
            Run(match.Rules.spawnProtectionSeconds + 0.05f);
        }

        private void Run(float seconds)
        {
            for (var left = seconds; left > 1e-4f; left -= 0.05f)
            {
                match.Tick(Mathf.Min(0.05f, left));
                combat.Tick(Mathf.Min(0.05f, left));
                spores?.Tick(Mathf.Min(0.05f, left));
            }
        }

        [Test]
        public void TheTrainsDamage_HurtsEitherTeam_ByTwenty()
        {
            StartMatch();

            Assert.AreEqual(20, combat.Hurt(Red, Train.DamageFrom(Vector3.zero)), Tolerance);
            Assert.AreEqual(20, combat.Hurt(Blue, Train.DamageFrom(Vector3.zero)), Tolerance);
            Assert.AreEqual(80, Red.Health, Tolerance);
            Assert.AreEqual(80, Blue.Health, Tolerance);
        }

        [Test]
        public void TheTrainsKill_IsNamedToyTrain_WithNoWeaponKillerTeamOrCredit()
        {
            StartMatch();

            combat.Hurt(Red, Damage.ByHazard("Toy train", 200));

            Assert.AreEqual(1, deaths.Count);
            var death = deaths[0];
            Assert.AreEqual(DeathCause.Killed, death.Cause);
            Assert.IsNull(death.Killer);
            Assert.AreEqual("Toy train", death.KillerName);
            Assert.IsNull(death.KillerTeam);
            Assert.IsNull(death.Weapon);
            Assert.IsFalse(death.CountsAsKill);
            Assert.AreEqual(DeathKind.Hazard, DeathLines.KindOf(death));
        }

        [Test]
        public void TheTrainsKill_GivesNoSpores_AndNoKillToAnyone()
        {
            StartMatch();
            var before = new float[match.Players.Count];
            for (var i = 0; i < before.Length; i++)
                before[i] = spores.MeterOf(match.Players[i]).Amount;

            combat.Hurt(Blue, Damage.ByHazard("Toy train", 200));

            for (var i = 0; i < before.Length; i++)
                Assert.AreEqual(before[i], spores.MeterOf(match.Players[i]).Amount, Tolerance, match.Players[i].Name);
            foreach (var player in match.Players)
                Assert.AreEqual(0, player.Kills, player.Name);
        }

        [Test]
        public void TheTrain_DoesNotHurtInTheCountdown_OrTheDead()
        {
            match = new MatchState(new MatchRules());
            foreach (var team in Teams.All)
                for (var i = 1; i <= match.Rules.teamSize; i++)
                    match.AddPlayer($"{team.Name()} {i}", team, i > 1 || team == Team.Blue);
            combat = new CombatState(match, WeaponTable.Default());
            match.Begin();

            Assert.AreEqual(0, combat.Hurt(Red, Damage.ByHazard("Toy train", 20)), "countdown");
            Run(match.Rules.countdownSeconds + 0.05f);
            Assert.AreEqual(20, combat.Hurt(Red, Damage.ByHazard("Toy train", 20)), Tolerance);
            combat.Hurt(Red, Damage.ByHazard("Toy train", 500));
            Assert.AreEqual(0, combat.Hurt(Red, Damage.ByHazard("Toy train", 20)), "dead");
        }

        [Test]
        public void TheTrain_HurtsAMachineOfEitherTeam_OnceTheRoundIsOn()
        {
            StartMatch();
            var hit = Train.DamageFrom(Vector3.zero);

            Assert.IsTrue(combat.CanHurt(Team.Red, hit));
            Assert.IsTrue(combat.CanHurt(Team.Blue, hit));
            Assert.IsFalse(combat.CanHurt(Team.Blue, Damage.ByMachine("Blue tank", Team.Blue, "cannon", 6)), "a team's own machines are still safe from friends");
        }

        // ---------------------------------------------------------------- Puffs

        [Test]
        public void TheChimney_PuffsEverySecond_AndNotWhenTheClockGoesBack()
        {
            Assert.AreEqual(1, MoverCues.Ticks(1, 3.9f, 4.1f));
            Assert.AreEqual(0, MoverCues.Ticks(1, 4.1f, 4.9f));
            Assert.AreEqual(3, MoverCues.Ticks(1, 0.5f, 3.5f));
            Assert.AreEqual(0, MoverCues.Ticks(1, 50, 0.1f));
            Assert.AreEqual(0, MoverCues.Ticks(0, 0, 10), "no chimney");
        }

        [Test]
        public void ClimbStarts_AreTheKeysWhereTheHeightRises()
        {
            var path = Loop(new Vector2(0, 0.3f), new Vector2(8, 3.8f), new Vector2(16, 3.8f), new Vector2(24, 0.3f), new Vector2(60, 0.3f), new Vector2(68, 3.8f), new Vector2(76, 3.8f), new Vector2(84, 0.3f));

            CollectionAssert.AreEqual(new[] { 0f, 60f }, path.ClimbStarts(1));
            CollectionAssert.IsEmpty(Loop().ClimbStarts(1), "a flat path");
        }

        [Test]
        public void TheWhistle_BlowsTwoSecondsBeforeTheHeadReachesAClimb_OnEveryLap()
        {
            var timetable = Timetable();
            var climbs = new List<float> { 30f };
            // The head is at 30 - 14 = 16 m after 16/7 s
            var blowsAt = (30 - 7 * 2f) / 7;

            Assert.IsFalse(MoverCues.Whistles(timetable, climbs, 2, blowsAt - 0.2f, blowsAt - 0.05f));
            Assert.IsTrue(MoverCues.Whistles(timetable, climbs, 2, blowsAt - 0.05f, blowsAt + 0.05f));
            Assert.IsFalse(MoverCues.Whistles(timetable, climbs, 2, blowsAt + 0.05f, blowsAt + 0.5f));
            // The next lap
            var again = blowsAt + timetable.LapSeconds;
            Assert.IsTrue(MoverCues.Whistles(timetable, climbs, 2, again - 0.05f, again + 0.05f));
            Assert.IsFalse(MoverCues.Whistles(timetable, climbs, 0, blowsAt - 0.05f, blowsAt + 0.05f), "no lead, no whistle");
            Assert.IsFalse(MoverCues.Whistles(timetable, climbs, 2, 100, 0), "the clock went back");
        }

        // ---------------------------------------------------------------- Where the walks cross the track

        [Test]
        public void ACrossing_IsWhereAWalkMeetsTheTrack_NearestFirst()
        {
            var path = Loop();

            var crossings = path.Crossings(new Vector2(0, 0), new Vector2(30, 0));

            Assert.AreEqual(1, crossings.Count);
            Assert.AreEqual(20f, crossings[0].Distance, Tolerance);
            Assert.AreEqual(10f, crossings[0].Arc, Tolerance, "the east straight, 10 m up from its south end");

            var across = path.Crossings(new Vector2(-30, 0), new Vector2(30, 0));
            Assert.AreEqual(2, across.Count);
            Assert.AreEqual(10f, across[0].Distance, Tolerance);
            Assert.AreEqual(50f, across[1].Distance, Tolerance);
        }

        [Test]
        public void AWalk_ThatStaysInsideOrAlongTheTrack_CrossesNothing()
        {
            var path = Loop();

            CollectionAssert.IsEmpty(path.Crossings(new Vector2(-5, 0), new Vector2(5, 0)));
            CollectionAssert.IsEmpty(path.Crossings(new Vector2(20, -5), new Vector2(20, 5)), "along the straight");
            CollectionAssert.IsEmpty(path.Crossings(new Vector2(25, 0), new Vector2(40, 0)), "outside it");
        }

        [Test]
        public void DistanceTo_IsTheNearestLegsDistance_AndWhereOnThePathThatIs()
        {
            var path = Loop();

            Assert.AreEqual(3f, path.DistanceTo(new Vector2(17, 0), out var arc), Tolerance);
            Assert.AreEqual(10f, arc, Tolerance);
            Assert.AreEqual(0f, path.DistanceTo(new Vector2(0, 10), out arc), Tolerance);
            Assert.AreEqual(40f, arc, Tolerance);
            Assert.AreEqual(10f, path.DistanceTo(new Vector2(0, 0), out _), Tolerance, "the middle of the loop is 10 m from the long legs");
        }

        // ---------------------------------------------------------------- The bots wait

        [Test]
        public void ABot_WaitsWhenTheHeadIsWithin25MetresOfTheCrossing_NotBeyond()
        {
            var timetable = Timetable();
            // The crossing 30 m along the path; the head at clock 0 is at 0
            Assert.IsFalse(MoverCrossing.Check(timetable, 0, 30, 25, 0).Wait, "30 m away");
            Assert.IsTrue(MoverCrossing.Check(timetable, 0, 24, 25, 0).Wait, "24 m away");
            // A second later the head is 7 m on: 23 m away
            Assert.IsTrue(MoverCrossing.Check(timetable, 1, 30, 25, 0).Wait);
        }

        [Test]
        public void ABot_WaitsUntilTheWholeTrainHasPassed_AndThenGoes()
        {
            var timetable = Timetable();
            const float crossing = 30f;
            // The head reaches it at 30 / 7 s; the tail, 14.8 m behind, passes (30 + 14.8) / 7 s after clock 0
            var clear = (crossing + timetable.TrainLength) / 7;

            var early = MoverCrossing.Check(timetable, 1, crossing, 25, 0);
            Assert.IsTrue(early.Wait);
            Assert.AreEqual(clear - 1, early.SecondsUntilClear, Tolerance);

            var onIt = MoverCrossing.Check(timetable, 30f / 7 + 0.5f, crossing, 25, 0);
            Assert.IsTrue(onIt.Wait, "the head is past, the rest is on it");
            Assert.AreEqual(clear - (30f / 7 + 0.5f), onIt.SecondsUntilClear, Tolerance);

            Assert.IsTrue(MoverCrossing.Check(timetable, clear - 0.05f, crossing, 25, 0).Wait, "the last car is just leaving");
            Assert.IsFalse(MoverCrossing.Check(timetable, clear + 0.05f, crossing, 25, 0).Wait);
        }

        [Test]
        public void TheMargin_KeepsTheBotBackFromTheTrainsEnds()
        {
            var timetable = Timetable();
            var clear = (30 + timetable.TrainLength) / 7;

            Assert.IsFalse(MoverCrossing.Check(timetable, clear + 0.05f, 30, 25, 0).Wait);
            Assert.IsTrue(MoverCrossing.Check(timetable, clear + 0.05f, 30, 25, 2).Wait, "2 m behind the tail is still too close");
            Assert.IsFalse(MoverCrossing.Check(timetable, clear + 2f / 7 + 0.05f, 30, 25, 2).Wait);
        }

        [Test]
        public void ABot_WhoseWalkIsFarFromTheTrain_DoesNotWait_AnyLap()
        {
            var timetable = Timetable();
            // The crossing is half a lap from the head at clock 0, and half a lap again a lap on
            var half = timetable.Path.Length / 2;

            Assert.IsFalse(MoverCrossing.Check(timetable, 0, half, 25, 2).Wait);
            Assert.IsFalse(MoverCrossing.Check(timetable, timetable.LapSeconds, half, 25, 2).Wait);
            Assert.IsTrue(MoverCrossing.Check(timetable, timetable.LapSeconds, 10, 25, 2).Wait);
        }

        [Test]
        public void CheckWalk_FindsTheCrossingOnTheWalk_AndIgnoresAWalkThatDoesNotCross()
        {
            var timetable = Timetable();
            // East straight x = 20, from z = -10 (arc 0) north; a walk east along z = 0 meets it at arc 10, 10 m from the head
            var wait = MoverCrossing.CheckWalk(timetable, 0, new Vector2(10, 0), new Vector2(30, 0), 25);
            Assert.IsTrue(wait.Wait);
            Assert.Greater(wait.SecondsUntilClear, 0);

            Assert.IsFalse(MoverCrossing.CheckWalk(timetable, 0, new Vector2(-5, 0), new Vector2(5, 0), 25).Wait);
            // The west straight is half a lap from the head: no wait for a walk across it
            Assert.IsFalse(MoverCrossing.CheckWalk(timetable, 0, new Vector2(-30, 0), new Vector2(-10, 0), 25).Wait);
        }
    }
}
