using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 28.10: the kitchen's hob heats up. The cycle (back pair 20 s into a round: 3 s warming, 12 s hot, 3 s cooling; then the front
    /// pair; the cold glass to 45 s), what a hot ring does (10 damage a second to a scout or tank, nobody's kill, "the hob" in the kill feed),
    /// the glow's colours (never scarlet) and the cost bots and tanks pay to cross a ring, with where they step to to leave one.
    /// </summary>
    public sealed class HeatRulesTests
    {
        private const float Tolerance = 1e-3f;

        // The kitchen's: the large rings at z = 3 (back, group 0), the small at z = -15 (front, group 1), mirrored
        private static readonly HeatRules Rules = new HeatRules { groups = 2, cause = "the hob" };

        private static readonly HeatRing[] Rings =
        {
            new HeatRing(new Vector2(-10.5f, 3), 5.4f, 0), new HeatRing(new Vector2(10.5f, 3), 5.4f, 0),
            new HeatRing(new Vector2(-10.5f, -15), 4.2f, 1), new HeatRing(new Vector2(10.5f, -15), 4.2f, 1),
        };

        // ---------------------------------------------------------------- The cycle

        [Test]
        public void TheTurns_TakeSpanSeconds_AndTheCycleLeavesNineSecondsOfColdGlass()
        {
            Assert.AreEqual(18f, Rules.Span, Tolerance);
            Assert.AreEqual(45f - 2 * Rules.Span, 9f, Tolerance);
        }

        [Test]
        public void TheBackPair_WarmsAt20_IsHotFrom23_CoolsFrom35_AndIsColdFrom38()
        {
            Assert.AreEqual(HeatPhase.Cold, Rules.PhaseOf(0, 0));
            Assert.AreEqual(HeatPhase.Cold, Rules.PhaseOf(0, 19.99f));
            Assert.AreEqual(HeatPhase.Warming, Rules.PhaseOf(0, 20));
            Assert.AreEqual(HeatPhase.Warming, Rules.PhaseOf(0, 22.99f));
            Assert.AreEqual(HeatPhase.Hot, Rules.PhaseOf(0, 23));
            Assert.AreEqual(HeatPhase.Hot, Rules.PhaseOf(0, 34.99f));
            Assert.AreEqual(HeatPhase.Cooling, Rules.PhaseOf(0, 35));
            Assert.AreEqual(HeatPhase.Cooling, Rules.PhaseOf(0, 37.99f));
            Assert.AreEqual(HeatPhase.Cold, Rules.PhaseOf(0, 38));
        }

        [Test]
        public void TheFrontPair_TakesOver_WhenTheBackPairIsDone()
        {
            Assert.AreEqual(HeatPhase.Cold, Rules.PhaseOf(1, 37.99f));
            Assert.AreEqual(HeatPhase.Warming, Rules.PhaseOf(1, 38));
            Assert.AreEqual(HeatPhase.Hot, Rules.PhaseOf(1, 41));
            Assert.AreEqual(HeatPhase.Hot, Rules.PhaseOf(1, 52.99f));
            Assert.AreEqual(HeatPhase.Cooling, Rules.PhaseOf(1, 53));
            Assert.AreEqual(HeatPhase.Cold, Rules.PhaseOf(1, 56));
        }

        [Test]
        public void TheCycle_Repeats_EveryFortyFiveSeconds()
        {
            Assert.AreEqual(HeatPhase.Cold, Rules.PhaseOf(0, 56));
            Assert.AreEqual(HeatPhase.Cold, Rules.PhaseOf(0, 64.99f));
            Assert.AreEqual(HeatPhase.Warming, Rules.PhaseOf(0, 65));
            Assert.AreEqual(HeatPhase.Hot, Rules.PhaseOf(0, 65 + 3 + 6));
            Assert.AreEqual(HeatPhase.Hot, Rules.PhaseOf(1, 38 + 45 + 5));
            for (var t = 20f; t < 200; t += 0.37f)
                for (var group = 0; group < 2; group++)
                    Assert.AreEqual(Rules.PhaseOf(group, t), Rules.PhaseOf(group, t + 45), $"group {group} at {t}");
        }

        [Test]
        public void OnlyOneGroup_IsEverWarmOrHot_AtATime()
        {
            for (var t = 0f; t < 300; t += 0.1f)
            {
                var back = Rules.PhaseOf(0, t);
                var front = Rules.PhaseOf(1, t);
                Assert.IsFalse(HeatRules.Avoided(back) && HeatRules.Avoided(front), $"both warm or hot at {t}");
                Assert.IsFalse(HeatRules.Hurts(back) && HeatRules.Hurts(front), $"both hot at {t}");
            }
        }

        [Test]
        public void ThePhase_ReportsTheSecondsIntoIt()
        {
            Rules.PhaseOf(0, 21, out var warming);
            Rules.PhaseOf(0, 30, out var hot);
            Rules.PhaseOf(0, 36.5f, out var cooling);

            Assert.AreEqual(1f, warming, Tolerance);
            Assert.AreEqual(7f, hot, Tolerance);
            Assert.AreEqual(1.5f, cooling, Tolerance);
        }

        [Test]
        public void ARoundStartingOver_StartsTheCycleOver()
        {
            // The round clock starts at 0 again: the glass is cold for 20 s whatever it was before
            Assert.AreEqual(HeatPhase.Hot, Rules.PhaseOf(0, 30));
            Assert.AreEqual(HeatPhase.Cold, Rules.PhaseOf(0, 0.5f));
        }

        [Test]
        public void AGroupThatDoesNotExist_IsCold()
        {
            Assert.AreEqual(HeatPhase.Cold, Rules.PhaseOf(2, 30));
            Assert.AreEqual(HeatPhase.Cold, Rules.PhaseOf(-1, 30));
        }

        [Test]
        public void Heat_RisesInTheWarming_ReachesYellowInTheHot_AndFallsInTheCooling()
        {
            Assert.AreEqual(0f, Rules.Heat(0, 10), Tolerance);
            Assert.AreEqual(0f, Rules.Heat(0, 20), Tolerance);
            Assert.AreEqual(HeatRules.EmberHeat / 2, Rules.Heat(0, 21.5f), Tolerance);
            Assert.AreEqual(HeatRules.EmberHeat, Rules.Heat(0, 23), Tolerance);
            Assert.AreEqual(1f, Rules.Heat(0, 29), Tolerance);
            Assert.AreEqual(1f, Rules.Heat(0, 34.9f), Tolerance);
            Assert.AreEqual(1f, Rules.Heat(0, 35), Tolerance);
            Assert.AreEqual(0.5f, Rules.Heat(0, 36.5f), Tolerance);
            Assert.AreEqual(0f, Rules.Heat(0, 38), Tolerance);
            // Smooth: no jump between frames
            var last = 0f;
            for (var t = 0f; t < 100; t += 0.02f)
            {
                var heat = Rules.Heat(0, t);
                Assert.Less(Mathf.Abs(heat - last), 0.05f, $"a jump at {t}");
                last = heat;
            }
        }

        // ---------------------------------------------------------------- Mirrored pairs and the kettle

        [Test]
        public void MirroredRings_AreHotTogether()
        {
            for (var t = 0f; t < 120; t += 0.5f)
            {
                var west = Rules.IsHot(Rings, new Vector2(-10.5f, 3), t);
                var east = Rules.IsHot(Rings, new Vector2(10.5f, 3), t);
                Assert.AreEqual(west, east, $"the back rings at {t}");
                Assert.AreEqual(Rules.IsHot(Rings, new Vector2(-10.5f, -15), t), Rules.IsHot(Rings, new Vector2(10.5f, -15), t), $"the front rings at {t}");
            }
            Assert.IsTrue(Rules.IsHot(Rings, new Vector2(-10.5f, 3), 30) && Rules.IsHot(Rings, new Vector2(10.5f, 3), 30));
            Assert.IsFalse(Rules.IsHot(Rings, new Vector2(-10.5f, -15), 30) || Rules.IsHot(Rings, new Vector2(10.5f, -15), 30));
            Assert.IsTrue(Rules.IsHot(Rings, new Vector2(-10.5f, -15), 45) && Rules.IsHot(Rings, new Vector2(10.5f, -15), 45));
        }

        [Test]
        public void TheKettlesPlace_IsNeverInARing_SoTheMiddleIsAlwaysSafe()
        {
            // The kettle at the hob's middle is 9.6 m long and 9.3 m across: its whole footprint (a 4.8 m circle) clears every ring
            foreach (var ring in Rings)
                Assert.Greater(Vector2.Distance(Vector2.zero, ring.centre), ring.radius + 4.8f, $"the ring at {ring.centre}");
            for (var t = 0f; t < 200; t += 0.25f)
            {
                Assert.IsFalse(Rules.IsHot(Rings, Vector2.zero, t), $"hot at the kettle at {t}");
                Assert.IsFalse(Rules.IsAvoided(Rings, Vector2.zero, t), $"avoided at the kettle at {t}");
            }
        }

        [Test]
        public void ARing_IsHot_OnlyInsideItsRadius()
        {
            Assert.IsTrue(Rules.IsHot(Rings, new Vector2(-10.5f + 5.3f, 3), 30));
            Assert.IsFalse(Rules.IsHot(Rings, new Vector2(-10.5f + 5.5f, 3), 30));
            Assert.IsFalse(Rules.IsHot(Rings, new Vector2(-10.5f, 3 + 5.5f), 30));
        }

        [Test]
        public void ACoolingOrWarmingRing_DoesNotHurt()
        {
            var inside = new Vector2(-10.5f, 3);

            Assert.IsFalse(Rules.IsHot(Rings, inside, 21), "warming");
            Assert.IsFalse(Rules.IsHot(Rings, inside, 36), "cooling");
            Assert.IsFalse(Rules.IsHot(Rings, inside, 10), "cold");
            Assert.IsTrue(Rules.IsHot(Rings, inside, 25), "hot");
            Assert.IsFalse(HeatRules.Hurts(HeatPhase.Warming) || HeatRules.Hurts(HeatPhase.Cooling) || HeatRules.Hurts(HeatPhase.Cold));
        }

        // ---------------------------------------------------------------- The burn

        [Test]
        public void StandingInAHotRing_BurnsTenAsecond()
        {
            var burn = new HeatBurn();
            var total = 0f;
            for (var i = 0; i < 102; i++)
                total += burn.Step(0.01f, true, Rules.damage);

            Assert.AreEqual(10f, total, Tolerance);
        }

        [Test]
        public void TheBurn_ComesInTicks_AndALongFrameCatchesUp()
        {
            var burn = new HeatBurn();

            Assert.AreEqual(0f, burn.Step(0.2f, true, 10), Tolerance);
            Assert.AreEqual(2.5f, burn.Step(0.06f, true, 10), Tolerance);
            Assert.AreEqual(0f, burn.Step(0.1f, true, 10), Tolerance);
            Assert.AreEqual(7.5f, burn.Step(0.7f, true, 10), Tolerance, "three ticks in one long frame");
        }

        [Test]
        public void LeavingARing_LosesTheTickInProgress()
        {
            var burn = new HeatBurn();
            burn.Step(0.2f, true, 10);
            burn.Step(0.1f, false, 10);

            Assert.AreEqual(0f, burn.Step(0.2f, true, 10), Tolerance);
        }

        private MatchState match;
        private CombatState combat;
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
            }
        }

        private float Burn(PlayerState player, float seconds)
        {
            var burn = new HeatBurn();
            var taken = 0f;
            for (var left = seconds; left > 1e-4f; left -= 0.05f)
            {
                var amount = burn.Step(Mathf.Min(0.05f, left), true, Rules.damage);
                if (amount > 0)
                    taken += combat.Hurt(player, Damage.ByHeat(Rules.cause, amount));
            }
            return taken;
        }

        [Test]
        public void AScout_InAHotRing_LosesTenHealthASecond_OfEitherTeam()
        {
            StartMatch();

            Assert.AreEqual(10f, Burn(Red, 1), Tolerance);
            Assert.AreEqual(90f, Red.Health, Tolerance);
            Assert.AreEqual(30f, Burn(Blue, 3), Tolerance);
            Assert.AreEqual(70f, Blue.Health, Tolerance);
        }

        [Test]
        public void TenSecondsInAHotRing_Kill_CreditedToNobody_AsTheHob()
        {
            StartMatch();
            var kills = new int[match.Players.Count];

            Burn(Red, 10.5f);

            Assert.AreEqual(1, deaths.Count);
            var death = deaths[0];
            Assert.AreSame(Red, death.Victim);
            Assert.AreEqual(DeathCause.Killed, death.Cause);
            Assert.IsTrue(death.Heat);
            Assert.IsNull(death.Killer);
            Assert.AreEqual("the hob", death.KillerName);
            Assert.IsNull(death.KillerTeam);
            Assert.IsNull(death.Weapon);
            Assert.IsFalse(death.CountsAsKill);
            for (var i = 0; i < kills.Length; i++)
                Assert.AreEqual(0, match.Players[i].Kills, match.Players[i].Name);
            Assert.AreEqual(DeathKind.Heat, DeathLines.KindOf(death));
        }

        [Test]
        public void TheHobsKill_AppearsInTheKillFeedAndTheDeathScreen_NextToFell()
        {
            StartMatch();
            Burn(Blue, 10.5f);
            var hob = deaths[0];
            var picker = new DeathLines(3) { GenericShare = 0 };

            var kind = DeathLines.KindOf(hob);
            var feed = DeathLines.SplitAtIcon(DeathLines.Format(picker.Feed(kind, GoreRules.Off, 20), hob.KillerName, "Blue 1"));
            Assert.AreEqual("Blue 1", feed.before);
            StringAssert.EndsWith("the hob", feed.after);
            StringAssert.Contains("the hob", DeathLines.Format(DeathLines.PlainHeatFeed, hob.KillerName, "Blue 1"));
            StringAssert.DoesNotContain("{", DeathLines.Format(picker.Detail(kind, GoreRules.Off), hob.KillerName));
            StringAssert.DoesNotContain("{", DeathLines.Format(picker.Title(kind, GoreRules.Off), hob.KillerName));
            // And a fall is still a fall
            Assert.AreEqual(DeathKind.Fall, DeathLines.KindOf(new Death(Blue, DeathCause.Fell)));
            Assert.AreEqual("Blue 1 fell", DeathLines.Format(DeathLines.PlainFallFeed, victim: "Blue 1"));
        }

        [Test]
        public void TheBurn_WaitsForTheRound_SparesTheDead_AndHurtsMachinesOfEitherTeam()
        {
            match = new MatchState(new MatchRules());
            foreach (var team in Teams.All)
                for (var i = 1; i <= match.Rules.teamSize; i++)
                    match.AddPlayer($"{team.Name()} {i}", team, i > 1 || team == Team.Blue);
            combat = new CombatState(match, WeaponTable.Default());
            match.Begin();
            var hit = Damage.ByHeat("the hob", 2.5f);

            Assert.AreEqual(0, combat.Hurt(Red, hit), "countdown");
            Assert.IsFalse(combat.CanHurt(Team.Red, hit), "a tank in the countdown");
            Run(match.Rules.countdownSeconds + 0.05f);
            Run(match.Rules.spawnProtectionSeconds + 0.05f);
            Assert.IsTrue(combat.CanHurt(Team.Red, hit), "a red tank");
            Assert.IsTrue(combat.CanHurt(Team.Blue, hit), "a blue tank");
            combat.Hurt(Red, Damage.ByHeat("the hob", 500));
            Assert.AreEqual(0, combat.Hurt(Red, hit), "dead");
        }

        // ---------------------------------------------------------------- The glow

        [Test]
        public void TheGlow_IsEmberThenOrangeThenYellow_AndNeverScarlet()
        {
            for (var heat = 0.05f; heat <= 1.001f; heat += 0.01f)
            {
                var colour = HeatGlow.Of(heat);
                Color.RGBToHSV(colour, out var hue, out var saturation, out var value);
                // Hue in degrees: scarlet is under 15, orange 20 to 35, yellow 45 to 55
                var degrees = hue * 360;
                Assert.GreaterOrEqual(degrees, 20f, $"too red at heat {heat}");
                Assert.LessOrEqual(degrees, 55f, $"too green at heat {heat}");
                Assert.GreaterOrEqual(saturation, 0.6f, $"washed out at heat {heat}");
                Assert.GreaterOrEqual(value, 0.55f, $"too dark at heat {heat}");
            }
            Assert.Less(HeatGlow.Of(HeatRules.EmberHeat).g, HeatGlow.Of(0.65f).g, "orange is yellower than ember");
            Assert.Less(HeatGlow.Of(0.65f).g, HeatGlow.Of(1).g, "yellow is yellower than orange");
        }

        [Test]
        public void TheGlow_IsNothingWhenCold_AndTakesOverTheMetalAsItHeats()
        {
            Assert.AreEqual(0f, HeatGlow.Of(0).a, Tolerance);
            Assert.Greater(HeatGlow.Of(HeatRules.EmberHeat).a, 0.5f);
            Assert.AreEqual(1f, HeatGlow.Of(1).a, Tolerance);
            var last = 0f;
            for (var heat = 0f; heat <= 1.001f; heat += 0.01f)
            {
                Assert.GreaterOrEqual(HeatGlow.Of(heat).a, last - Tolerance, $"the glow dims while heating at {heat}");
                last = HeatGlow.Of(heat).a;
            }
        }

        // ---------------------------------------------------------------- Bots and tanks

        [Test]
        public void ThePathCost_IsFiftyWhileAGroupWarmsAndIsHot_AndOneOtherwise()
        {
            Assert.AreEqual(1f, Rules.PathCost(0, 10), Tolerance, "cold");
            Assert.AreEqual(50f, Rules.PathCost(0, 21), Tolerance, "warming");
            Assert.AreEqual(50f, Rules.PathCost(0, 30), Tolerance, "hot");
            Assert.AreEqual(1f, Rules.PathCost(0, 36), Tolerance, "cooling: it is spent, so bots cross freely");
            Assert.AreEqual(1f, Rules.PathCost(0, 40), Tolerance, "the other group's turn");
            Assert.AreEqual(50f, Rules.PathCost(1, 40), Tolerance);
            Assert.AreEqual(1f, Rules.PathCost(1, 5), Tolerance);
        }

        [Test]
        public void ACostBelowOne_IsOne_SoARingNeverPaysBotsToCross()
        {
            Assert.AreEqual(1f, new HeatRules { groups = 1, cost = 0.1f }.PathCost(0, 30), Tolerance);
        }

        [Test]
        public void WithTheCostUp_TheWayRoundIsCheaperThanTheWayAcross()
        {
            // Over a ring of radius r a straight line costs cost * 2r, a way round its edge at most half the circle
            foreach (var ring in Rings)
            {
                var across = Rules.PathCost(ring.group, ring.group == 0 ? 30 : 45) * 2 * ring.radius;
                var round = Mathf.PI * ring.radius;
                Assert.Less(round, across, $"the ring at {ring.centre}");
                Assert.AreEqual(2 * ring.radius, Rules.PathCost(ring.group, 5) * 2 * ring.radius, Tolerance, "cold: straight across costs only the metres");
            }
        }

        [Test]
        public void ABotInAWarmingOrHotRing_MustStepOff_ButNotInACoolingOrColdOne()
        {
            var inside = new Vector2(-8, 3);

            Assert.IsTrue(Rules.IsAvoided(Rings, inside, 21), "warming");
            Assert.IsTrue(Rules.IsAvoided(Rings, inside, 30), "hot");
            Assert.IsFalse(Rules.IsAvoided(Rings, inside, 36), "cooling");
            Assert.IsFalse(Rules.IsAvoided(Rings, inside, 5), "cold");
            Assert.IsFalse(Rules.IsAvoided(Rings, new Vector2(-8, -15), 30), "a ring of the other group");
            Assert.AreEqual(0, Rules.RingOn(Rings, inside, 30, false));
            Assert.AreEqual(-1, Rules.RingOn(Rings, inside, 36, false));
        }

        [Test]
        public void TheStepOff_LeadsOutOfTheRing_ByTheShortestWay()
        {
            var ring = Rings[0];
            var from = new Vector2(-8, 3);

            var direction = HeatRules.StepOff(ring, from, 1, out var distance);

            Assert.AreEqual(1f, direction.x, Tolerance, "east, away from the centre at -10.5");
            Assert.AreEqual(0f, direction.y, Tolerance);
            Assert.AreEqual(5.4f - 2.5f + 1, distance, Tolerance);
            Assert.IsFalse(ring.Contains(from + direction * (distance + 0.01f)));
            // From the very middle any way will do
            HeatRules.StepOff(ring, ring.centre, 1, out var fromMiddle);
            Assert.AreEqual(6.4f, fromMiddle, Tolerance);
        }
    }
}
