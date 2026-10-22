using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 25: the spores through <see cref="SporeState"/> on a running <see cref="MatchState"/> with combat and the flags: what fills
    /// the meter, sporulating only when full, each power's effect and length, a death ending the power and keeping the meter, a new
    /// round emptying it, and the bots' choice.
    /// </summary>
    public sealed class SporeStateTests
    {
        private MatchState match;
        private CombatState combat;
        private CaptureTheFlagState ctf;
        private SporeState spores;
        private readonly List<string> events = new List<string>();

        private PlayerState Red => match.Players[0];
        private PlayerState Red2 => match.Players[1];
        private PlayerState Blue => match.Players[4];

        [SetUp]
        public void SetUp()
        {
            match = new MatchState(new MatchRules());
            foreach (var team in Teams.All)
                for (var i = 1; i <= match.Rules.teamSize; i++)
                    match.AddPlayer($"{team.Name()} {i}", team, i > 1 || team == Team.Blue);
            combat = new CombatState(match, WeaponTable.Default());
            ctf = new CaptureTheFlagState(match);
            spores = new SporeState(match, combat, ctf);
            events.Clear();
            spores.Filled += p => events.Add($"full {p.Name}");
            spores.Sporulated += (p, power) => events.Add($"{power} {p.Name}");
            spores.Ended += (p, power) => events.Add($"ended {power} {p.Name}");
            match.Begin();
            Run(match.Rules.countdownSeconds + 0.05f);
            Run(match.Rules.spawnProtectionSeconds + 0.05f);
            Assert.AreEqual(RoundPhase.Playing, match.Phase);
        }

        private void Run(float seconds, float step = 0.05f)
        {
            for (var left = seconds; left > 1e-4f; left -= step)
            {
                var dt = Mathf.Min(step, left);
                match.Tick(dt);
                ctf.Tick(dt);
                combat.Tick(dt);
                spores.Tick(dt);
            }
        }

        private void Kill(PlayerState victim, PlayerState killer) => combat.Hurt(victim, Damage.By(killer, WeaponIds.Pistol, 1000));

        private void Fill(PlayerState player) => spores.Gain(player, spores.Rules.full);

        [Test]
        public void Defaults_AreTheTicketsNumbers()
        {
            var rules = new SporeRules();
            Assert.AreEqual(100, rules.full);
            Assert.AreEqual((25f, 15f, 15f, 20f, 50f), (rules.kill, rules.machine, rules.flagReturn, rules.flagTake, rules.capture));
            Assert.AreEqual((1.6f, 12f), (rules.rushSpeed, rules.rushSeconds));
            Assert.AreEqual((8f, 10f), (rules.regrowthPerSecond, rules.regrowthSeconds));
            Assert.AreEqual((0.5f, 12f), (rules.armourDamage, rules.armourSeconds));
        }

        [Test]
        public void AKill_Gives25_AndFourFillTheMeter()
        {
            for (var i = 0; i < 4; i++)
            {
                Assert.IsFalse(spores.IsFull(Red));
                Kill(match.Players[4 + i], Red);
            }
            Assert.AreEqual(100, spores.MeterOf(Red).Amount, 1e-4f);
            Assert.IsTrue(spores.IsFull(Red));
            Assert.AreEqual(new[] { "full Red 1" }, events.ToArray());
            Kill(Red2, Blue);
            Assert.AreEqual(25, spores.MeterOf(Blue).Amount, 1e-4f);
            Assert.AreEqual(0, spores.MeterOf(Red2).Amount, "the victim gains nothing");
        }

        [Test]
        public void AFall_AndAMachinesKill_GiveNoSpores_ButDestroyingAMachineDoes()
        {
            match.ReportFall(Blue);
            Assert.AreEqual(0, spores.MeterOf(Blue).Amount);
            combat.Hurt(Red2, Damage.ByMachine("Blue tank", Team.Blue, "cannon", 1000));
            Assert.AreEqual(0, spores.MeterOf(Blue).Amount);
            spores.ReportMachineKilled(Damage.By(Red, WeaponIds.Pistol, 6));
            Assert.AreEqual(15, spores.MeterOf(Red).Amount, 1e-4f);
            spores.ReportMachineKilled(Damage.ByMachine("Blue tank", Team.Blue, "cannon", 6));
            Assert.AreEqual(15, spores.MeterOf(Red).Amount, 1e-4f, "no player behind it");
        }

        [Test]
        public void TheFlag_GivesSpores_ForTaking_Returning_AndCapturing()
        {
            Assert.IsTrue(ctf.TouchStand(Red, Team.Blue));
            Assert.AreEqual(20, spores.MeterOf(Red).Amount, 1e-4f, "taken");
            Assert.IsTrue(ctf.TouchStand(Red, Team.Red));
            Assert.AreEqual(70, spores.MeterOf(Red).Amount, 1e-4f, "captured");

            Assert.IsTrue(ctf.TouchStand(Blue, Team.Red));
            Kill(Blue, Red2);
            Assert.AreEqual(FlagStatus.Dropped, ctf.Flag(Team.Red).Status);
            var before = spores.MeterOf(Red2).Amount;
            Assert.IsTrue(ctf.TouchFlag(Red2, Team.Red));
            Assert.AreEqual(before + 15, spores.MeterOf(Red2).Amount, 1e-4f, "returned by touch");
        }

        [Test]
        public void Sporulating_NeedsAFullMeter_AndEmptiesIt()
        {
            spores.Gain(Red, 99);
            Assert.IsFalse(spores.Sporulate(Red, SporePower.Rush), "not full: nothing happens");
            Assert.IsFalse(spores.MeterOf(Red).Active);
            Assert.AreEqual(99, spores.MeterOf(Red).Amount, 1e-4f);
            spores.Gain(Red, 5);
            Assert.AreEqual(100, spores.MeterOf(Red).Amount, 1e-4f, "never past full");
            Assert.IsTrue(spores.Sporulate(Red, SporePower.Rush));
            Assert.AreEqual(0, spores.MeterOf(Red).Amount);
            Assert.AreEqual(SporePower.Rush, spores.MeterOf(Red).Power);
            Assert.AreEqual(12, spores.MeterOf(Red).TimeLeft, 1e-4f);
            Assert.IsFalse(spores.Sporulate(Red, SporePower.Armour), "one power at a time");
        }

        [Test]
        public void NoSporesAreGained_WhileAPowerIsOn()
        {
            Fill(Red);
            spores.Sporulate(Red, SporePower.Armour);
            Kill(Blue, Red);
            Assert.AreEqual(0, spores.MeterOf(Red).Amount);
            Run(12.1f);
            Assert.IsFalse(spores.MeterOf(Red).Active);
            Kill(match.Players[5], Red);
            Assert.AreEqual(25, spores.MeterOf(Red).Amount, 1e-4f, "it fills again once the power ends");
        }

        [Test]
        public void Rush_MakesThePlayerFaster_For12Seconds()
        {
            Fill(Red);
            Assert.AreEqual(1, combat.SpeedFactor(Red));
            spores.Sporulate(Red, SporePower.Rush);
            Assert.AreEqual(1.6f, combat.SpeedFactor(Red), 1e-4f);
            combat.Slow(Red, Team.Blue, 0.5f, 3);
            Assert.AreEqual(0.8f, combat.SpeedFactor(Red), 1e-4f, "on top of a slow");
            Run(11.9f);
            Assert.AreEqual(1.6f, combat.SpeedFactor(Red), 1e-4f);
            Run(0.2f);
            Assert.AreEqual(1, combat.SpeedFactor(Red));
            Assert.Contains("ended Rush Red 1", events);
        }

        [Test]
        public void Armour_HalvesTheDamageTaken()
        {
            Fill(Red);
            spores.Sporulate(Red, SporePower.Armour);
            Assert.AreEqual(10, combat.Hurt(Red, Damage.By(Blue, WeaponIds.Pistol, 20)), 1e-4f);
            Assert.AreEqual(90, Red.Health, 1e-4f);
            Run(12.1f);
            Assert.AreEqual(20, combat.Hurt(Red, Damage.By(Blue, WeaponIds.Pistol, 20)), 1e-4f);
        }

        [Test]
        public void Regrowth_Heals8ASecond_For10Seconds_UpToTheOverheal()
        {
            combat.Hurt(Red, Damage.By(Blue, WeaponIds.Pistol, 60));
            Assert.AreEqual(40, Red.Health, 1e-4f);
            Fill(Red);
            spores.Sporulate(Red, SporePower.Regrowth);
            Run(5);
            // 40 points in 5 s, and a little of the slow heal (1 every 2 s under 80)
            Assert.AreEqual(80, Red.Health, 2.5f);
            Run(5.2f);
            Assert.IsFalse(spores.MeterOf(Red).Active);
            // 80 points in all: 60 to 100, then 20 at half above it
            Assert.AreEqual(110, Red.Health, 2.5f);
            var after = Red.Health;
            Run(2);
            Assert.AreEqual(after, Red.Health, 1e-4f, "no more once it ended");

            Red.Health = 199;
            spores.Gain(Red, 100);
            spores.Sporulate(Red, SporePower.Regrowth);
            Run(3);
            Assert.AreEqual(200, Red.Health, 1e-4f, "never past the overheal limit");
        }

        [Test]
        public void ADeath_EndsThePower_AndKeepsTheMeter()
        {
            Fill(Red);
            spores.Sporulate(Red, SporePower.Rush);
            Kill(Red, Blue);
            Assert.IsFalse(spores.MeterOf(Red).Active);
            Assert.Contains("ended Rush Red 1", events);

            spores.Gain(Red2, 60);
            Kill(Red2, Blue);
            Assert.AreEqual(60, spores.MeterOf(Red2).Amount, 1e-4f, "kept");
            Assert.IsFalse(spores.Sporulate(Red, SporePower.Rush), "the dead cannot");
        }

        [Test]
        public void ANewRound_EmptiesEveryMeter()
        {
            Fill(Red);
            spores.Gain(Blue, 40);
            spores.Sporulate(Red, SporePower.Armour);
            for (var i = 0; i < match.Rules.capturesToWin; i++)
            {
                Assert.IsTrue(ctf.TouchStand(Blue, Team.Red));
                Assert.IsTrue(ctf.TouchStand(Blue, Team.Blue));
            }
            Assert.AreEqual(RoundPhase.RoundEnd, match.Phase);
            Run(match.Rules.resultSeconds + 0.1f);
            Assert.AreEqual(0, spores.MeterOf(Red).Amount);
            Assert.AreEqual(0, spores.MeterOf(Blue).Amount);
            Assert.IsFalse(spores.MeterOf(Red).Active);
        }

        [Test]
        public void Bots_Rush_OnAFlagRun_Regrow_WhenHurt_AndArmour_UpClose()
        {
            var rules = new SporeRules();
            Assert.AreEqual(SporePower.Rush, rules.ChooseForBot(true, 30, 100, 5));
            Assert.AreEqual(SporePower.Regrowth, rules.ChooseForBot(false, 30, 100, 5));
            Assert.AreEqual(SporePower.Armour, rules.ChooseForBot(false, 90, 100, 10));
            Assert.IsNull(rules.ChooseForBot(false, 90, 100, 40), "kept for later");
        }
    }
}
