using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 21: the flag's rules played through <see cref="CaptureTheFlagState"/> on a <see cref="MatchState"/> (EditMode, no scene):
    /// taking, carrying at 0.9 speed, capturing only with your own flag home, dropping on death, returning by touch, after 30 s, or at
    /// once where nobody can reach it, and the reset of a new round. Run them in Window > General > Test Runner, EditMode tab.
    /// </summary>
    public sealed class CaptureTheFlagStateTests
    {
        private MatchState match;
        private CaptureTheFlagState ctf;
        private PlayerState red1, red2, blue1, blue2;
        private readonly List<string> events = new List<string>();

        [SetUp]
        public void SetUp()
        {
            match = new MatchState(new MatchRules());
            foreach (var team in Teams.All)
                for (var i = 1; i <= match.Rules.teamSize; i++)
                    match.AddPlayer($"{team.Name()} {i}", team, i > 1 || team == Team.Blue);
            red1 = match.Players[0];
            red2 = match.Players[1];
            blue1 = match.Players[4];
            blue2 = match.Players[5];
            ctf = new CaptureTheFlagState(match);
            events.Clear();
            ctf.Taken += (flag, by) => events.Add($"taken {flag.Team} by {by.Name}");
            ctf.Dropped += (flag, by) => events.Add($"dropped {flag.Team} by {by.Name}");
            ctf.Returned += (flag, why, by) => events.Add($"returned {flag.Team} {why}{(by != null ? " by " + by.Name : "")}");
            ctf.Captured += (flag, by) => events.Add($"captured {flag.Team} by {by.Name}");
            match.Begin();
            Run(match.Rules.countdownSeconds + 0.05f);
            Assert.AreEqual(RoundPhase.Playing, match.Phase);
        }

        /// <summary>Ticks the match and the flags in small steps, as frames do.</summary>
        private void Run(float seconds, float step = 0.1f)
        {
            for (var left = seconds; left > 1e-4f; left -= step)
            {
                var dt = Mathf.Min(step, left);
                match.Tick(dt);
                ctf.Tick(dt);
            }
        }

        private TeamFlag RedFlag => ctf.Flag(Team.Red);
        private TeamFlag BlueFlag => ctf.Flag(Team.Blue);

        [Test]
        public void Defaults_AreTheUsersDecisions()
        {
            var rules = new FlagRules();

            Assert.AreEqual(0.9f, rules.carrierSpeed, 1e-6f);
            Assert.AreEqual(30, rules.returnSeconds);
        }

        [Test]
        public void AtTheStart_BothFlagsAreHome()
        {
            Assert.IsTrue(RedFlag.AtHome);
            Assert.IsTrue(BlueFlag.AtHome);
            Assert.IsNull(RedFlag.Carrier);
            Assert.IsFalse(ctf.IsCarrying(red1));
        }

        [Test]
        public void TouchingTheEnemyStand_TakesItsFlag()
        {
            Assert.IsTrue(ctf.TouchStand(red1, Team.Blue));

            Assert.AreEqual(FlagStatus.Carried, BlueFlag.Status);
            Assert.AreSame(red1, BlueFlag.Carrier);
            Assert.AreSame(BlueFlag, ctf.CarriedBy(red1));
            CollectionAssert.AreEqual(new[] { "taken Blue by Red 1" }, events);
        }

        [Test]
        public void TouchingYourOwnStand_DoesNotTakeYourFlag()
        {
            Assert.IsFalse(ctf.TouchStand(red1, Team.Red));

            Assert.IsTrue(RedFlag.AtHome);
            Assert.IsEmpty(events);
        }

        [Test]
        public void ACarriedFlag_CannotBeTakenFromItsStand()
        {
            ctf.TouchStand(red1, Team.Blue);

            Assert.IsFalse(ctf.TouchStand(red2, Team.Blue));
            Assert.AreSame(red1, BlueFlag.Carrier);
        }

        [Test]
        public void TheCarrier_WalksAtNinetyPercent()
        {
            Assert.AreEqual(1, ctf.SpeedMultiplier(red1));

            ctf.TouchStand(red1, Team.Blue);

            Assert.AreEqual(0.9f, ctf.SpeedMultiplier(red1), 1e-6f);
            Assert.AreEqual(1, ctf.SpeedMultiplier(red2), "only the carrier is slower");
            Assert.AreEqual(1, ctf.SpeedMultiplier(blue1));
        }

        [Test]
        public void BringingTheEnemyFlagHome_WithYourFlagHome_Captures()
        {
            ctf.TouchStand(red1, Team.Blue);

            Assert.IsTrue(ctf.TouchStand(red1, Team.Red));

            Assert.AreEqual(1, match.Score(Team.Red));
            Assert.AreEqual(0, match.Score(Team.Blue));
            Assert.AreEqual(1, red1.Captures);
            Assert.IsTrue(BlueFlag.AtHome, "the captured flag goes back to its stand");
            Assert.IsFalse(ctf.IsCarrying(red1));
            Assert.AreEqual(1, ctf.SpeedMultiplier(red1), "full speed again");
            CollectionAssert.AreEqual(new[] { "taken Blue by Red 1", "returned Blue Captured by Red 1", "captured Blue by Red 1" }, events);
        }

        [Test]
        public void NoCapture_WhileYourOwnFlagIsAway()
        {
            ctf.TouchStand(red1, Team.Blue);
            ctf.TouchStand(blue1, Team.Red);

            Assert.IsFalse(ctf.TouchStand(red1, Team.Red), "red's flag is carried by blue");
            Assert.AreEqual(0, match.Score(Team.Red));
            Assert.AreSame(red1, BlueFlag.Carrier, "the carrier keeps the flag");

            // Kill the blue carrier: red's flag lies on the ground, still not home
            match.ReportDeath(new Death(blue1, DeathCause.Killed, red2));
            Assert.AreEqual(FlagStatus.Dropped, RedFlag.Status);
            Assert.IsFalse(ctf.TouchStand(red1, Team.Red), "red's flag is dropped, not home");

            // A red player returns it; now red can score, standing on the stand
            ctf.TouchFlag(red2, Team.Red);
            Assert.IsTrue(RedFlag.AtHome);
            Assert.IsTrue(ctf.TouchStand(red1, Team.Red));
            Assert.AreEqual(1, match.Score(Team.Red));
        }

        [Test]
        public void WithoutTheEnemyFlag_YourStandDoesNothing()
        {
            Assert.IsFalse(ctf.TouchStand(red1, Team.Red));
            Assert.AreEqual(0, match.Score(Team.Red));
        }

        [Test]
        public void ThreeCaptures_WinTheRound()
        {
            for (var i = 0; i < 3; i++)
            {
                Assert.IsTrue(ctf.TouchStand(red1, Team.Blue));
                Assert.IsTrue(ctf.TouchStand(red1, Team.Red));
            }

            Assert.AreEqual(3, match.Score(Team.Red));
            Assert.AreEqual(RoundPhase.RoundEnd, match.Phase);
            Assert.AreEqual(Team.Red, match.Winner);
            Assert.IsFalse(ctf.TouchStand(red1, Team.Blue), "nothing is taken once the round is over");
        }

        [Test]
        public void NothingIsTaken_DuringTheCountdown()
        {
            match = new MatchState(new MatchRules());
            var red = match.AddPlayer("Red 1", Team.Red, false);
            match.AddPlayer("Blue 1", Team.Blue, true);
            ctf = new CaptureTheFlagState(match);
            match.Begin();

            Assert.AreEqual(RoundPhase.Countdown, match.Phase);
            Assert.IsFalse(ctf.TouchStand(red, Team.Blue));
            Assert.IsTrue(ctf.Flag(Team.Blue).AtHome);
        }

        [Test]
        public void ADeadPlayer_TouchesNothing()
        {
            match.ReportFall(red1);

            Assert.IsFalse(ctf.TouchStand(red1, Team.Blue));
            Assert.IsTrue(BlueFlag.AtHome);
        }

        [Test]
        public void TheCarriersDeath_DropsTheFlagWhereTheyDied()
        {
            var where = new Vector3(10, 2, -4);
            ctf.DropPoint = p => p == red1 ? where : (Vector3?)null;
            ctf.TouchStand(red1, Team.Blue);

            match.ReportDeath(new Death(red1, DeathCause.Killed, blue1, weapon: "pistol"));

            Assert.AreEqual(FlagStatus.Dropped, BlueFlag.Status);
            Assert.IsNull(BlueFlag.Carrier);
            Assert.AreEqual(where, BlueFlag.Position);
            Assert.AreEqual(30, BlueFlag.ReturnIn, 1e-4f);
            Assert.IsFalse(ctf.IsCarrying(red1));
            CollectionAssert.AreEqual(new[] { "taken Blue by Red 1", "dropped Blue by Red 1" }, events);
        }

        [Test]
        public void ADeathWithoutAFlag_DropsNothing()
        {
            ctf.TouchStand(red1, Team.Blue);

            match.ReportDeath(new Death(red2, DeathCause.Killed, blue1));

            Assert.AreSame(red1, BlueFlag.Carrier);
            Assert.AreEqual(1, events.Count);
        }

        [Test]
        public void TheEnemy_PicksUpADroppedFlag()
        {
            ctf.TouchStand(red1, Team.Blue);
            match.ReportDeath(new Death(red1, DeathCause.Killed, blue1));

            Assert.IsTrue(ctf.TouchFlag(red2, Team.Blue));

            Assert.AreEqual(FlagStatus.Carried, BlueFlag.Status);
            Assert.AreSame(red2, BlueFlag.Carrier);
            Assert.AreEqual(0, BlueFlag.ReturnIn);
            Assert.AreEqual(0.9f, ctf.SpeedMultiplier(red2), 1e-6f);
        }

        [Test]
        public void ItsOwnTeam_TouchingADroppedFlag_SendsItHome()
        {
            ctf.TouchStand(red1, Team.Blue);
            match.ReportDeath(new Death(red1, DeathCause.Killed, blue1));

            Assert.IsTrue(ctf.TouchFlag(blue2, Team.Blue));

            Assert.IsTrue(BlueFlag.AtHome);
            Assert.AreEqual("returned Blue Touched by Blue 2", events.Last());
        }

        [Test]
        public void OnlyADroppedFlag_ReactsToTouch()
        {
            Assert.IsFalse(ctf.TouchFlag(red1, Team.Blue), "at home the stand takes it, not the flag");
            ctf.TouchStand(red1, Team.Blue);
            Assert.IsFalse(ctf.TouchFlag(blue1, Team.Blue), "a carried flag is not returned by touching it");
            Assert.AreSame(red1, BlueFlag.Carrier);
        }

        [Test]
        public void AnUntouchedDroppedFlag_GoesHomeAfterThirtySeconds()
        {
            ctf.TouchStand(red1, Team.Blue);
            match.ReportDeath(new Death(red1, DeathCause.Killed, blue1));

            Run(29.5f);
            Assert.AreEqual(FlagStatus.Dropped, BlueFlag.Status);
            Assert.AreEqual(0.5f, BlueFlag.ReturnIn, 0.01f);

            Run(0.6f);
            Assert.IsTrue(BlueFlag.AtHome);
            Assert.AreEqual("returned Blue TimedOut", events.Last());
        }

        [Test]
        public void ARetakenFlag_StartsItsThirtySecondsAfresh()
        {
            ctf.TouchStand(red1, Team.Blue);
            match.ReportDeath(new Death(red1, DeathCause.Killed, blue1));
            Run(20);
            ctf.TouchFlag(red2, Team.Blue);
            Run(15);
            Assert.AreEqual(FlagStatus.Carried, BlueFlag.Status, "the wait does not run while carried");

            match.ReportDeath(new Death(red2, DeathCause.Killed, blue1));
            Assert.AreEqual(30, BlueFlag.ReturnIn, 1e-4f);
        }

        [Test]
        public void AFlagDroppedOutOfReach_GoesHomeAtOnce()
        {
            ctf.IsReachable = p => p.y > -10;
            ctf.DropPoint = p => new Vector3(0, -50, 0);
            ctf.TouchStand(red1, Team.Blue);

            match.ReportFall(red1);

            Assert.IsTrue(BlueFlag.AtHome);
            CollectionAssert.AreEqual(new[] { "taken Blue by Red 1", "dropped Blue by Red 1", "returned Blue Unreachable" }, events);
        }

        [Test]
        public void AFlagWithNoGroundBelow_GoesHomeAtOnce()
        {
            ctf.DropPoint = p => null;
            ctf.TouchStand(red1, Team.Blue);

            match.ReportFall(red1);

            Assert.IsTrue(BlueFlag.AtHome);
            Assert.AreEqual("returned Blue Unreachable", events.Last());
        }

        [Test]
        public void AFlagFoundUnreachableLater_GoesHome()
        {
            ctf.TouchStand(red1, Team.Blue);
            match.ReportDeath(new Death(red1, DeathCause.Killed, blue1));
            Assert.AreEqual(FlagStatus.Dropped, BlueFlag.Status);

            ctf.ReportUnreachable(Team.Blue);

            Assert.IsTrue(BlueFlag.AtHome);
        }

        [Test]
        public void ANewRound_SendsBothFlagsHome()
        {
            ctf.TouchStand(red1, Team.Blue);
            ctf.TouchStand(blue1, Team.Red);
            match.ReportDeath(new Death(blue1, DeathCause.Killed, red2));
            Assert.AreEqual(FlagStatus.Dropped, RedFlag.Status);

            // Red returns its flag and scores twice; then both flags are out again
            ctf.TouchFlag(red2, Team.Red);
            ctf.TouchStand(red1, Team.Red);
            ctf.TouchStand(red1, Team.Blue);
            ctf.TouchStand(red1, Team.Red);
            ctf.TouchStand(red1, Team.Blue);
            ctf.TouchStand(blue2, Team.Red);
            Assert.AreEqual(2, match.Score(Team.Red));
            Assert.AreSame(red1, BlueFlag.Carrier);
            Assert.AreSame(blue2, RedFlag.Carrier);

            // The clock runs out with red ahead; after the result the next round starts
            Run(match.Rules.roundSeconds + 0.1f, 1);
            Assert.AreEqual(RoundPhase.RoundEnd, match.Phase);
            Run(match.Rules.resultSeconds + 0.1f);

            Assert.AreEqual(2, match.Round);
            Assert.IsTrue(RedFlag.AtHome);
            Assert.IsTrue(BlueFlag.AtHome);
            Assert.IsFalse(ctf.IsCarrying(red1));
            Assert.IsFalse(ctf.IsCarrying(blue2));
            CollectionAssert.AreEqual(new[] { "returned Red RoundReset", "returned Blue RoundReset" }, events.Skip(events.Count - 2));
        }

        [Test]
        public void SuddenDeath_TheNextCaptureWins()
        {
            Run(match.Rules.roundSeconds + 0.1f, 1);
            Assert.IsTrue(match.SuddenDeath);

            ctf.TouchStand(blue1, Team.Red);
            ctf.TouchStand(blue1, Team.Blue);

            Assert.AreEqual(Team.Blue, match.Winner);
        }

        [Test]
        public void Detached_ItNoLongerFollowsTheMatch()
        {
            ctf.TouchStand(red1, Team.Blue);
            ctf.Detach();

            match.ReportDeath(new Death(red1, DeathCause.Killed, blue1));

            Assert.AreEqual(FlagStatus.Carried, BlueFlag.Status);
        }
    }
}
