using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 22: health and damage through <see cref="CombatState"/> on a running <see cref="MatchState"/>: hurting and killing with
    /// kill credit, friendly fire off, spawn protection, the countdown, healing up to 100, losing everything on death, machines'
    /// kills and their own <see cref="Health"/>, the flamethrower's burn, the blob's slow, poison clouds and splats.
    /// </summary>
    public sealed class CombatStateTests
    {
        private MatchState match;
        private CombatState combat;
        private readonly Dictionary<PlayerState, Vector3> positions = new Dictionary<PlayerState, Vector3>();
        private readonly List<Death> deaths = new List<Death>();

        // Red 1 (you), Red 2-4, Blue 1-4
        private PlayerState Red => match.Players[0];
        private PlayerState Red2 => match.Players[1];
        private PlayerState Blue => match.Players[4];
        private PlayerState Blue2 => match.Players[5];

        [SetUp]
        public void SetUp()
        {
            match = new MatchState(new MatchRules());
            foreach (var team in Teams.All)
                for (var i = 1; i <= match.Rules.teamSize; i++)
                    match.AddPlayer($"{team.Name()} {i}", team, i > 1 || team == Team.Blue);
            combat = new CombatState(match, WeaponTable.Default());
            positions.Clear();
            deaths.Clear();
            // Everyone far apart, out of each other's clouds, unless a test moves them
            foreach (var player in match.Players)
                positions[player] = new Vector3(player.Index * 100, 0, 0);
            combat.PositionOf = p => positions[p];
            match.Died += deaths.Add;
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
                combat.Tick(dt);
            }
        }

        private static Damage Shot(PlayerState by, float amount, string weapon = WeaponIds.Pistol, bool head = false) =>
            Damage.By(by, weapon, amount, Vector3.zero, head);

        [Test]
        public void AHit_TakesHealth()
        {
            var taken = combat.Hurt(Blue, Shot(Red, 12));

            Assert.AreEqual(12, taken);
            Assert.AreEqual(88, Blue.Health);
            Assert.IsTrue(Blue.Alive);
        }

        [Test]
        public void AKill_ReportsTheDeath_WithKillerWeaponAndHeadshot_AndCountsTheKill()
        {
            combat.Hurt(Blue, Shot(Red, 85, WeaponIds.Sniper));
            var taken = combat.Hurt(Blue, Shot(Red, 120, WeaponIds.Sniper, head: true));

            Assert.AreEqual(15, taken, "no more than was left");
            Assert.AreEqual(0, Blue.Health);
            Assert.IsFalse(Blue.Alive);
            Assert.AreEqual(1, deaths.Count);
            Assert.AreEqual(Blue, deaths[0].Victim);
            Assert.AreEqual(Red, deaths[0].Killer);
            Assert.AreEqual(DeathCause.Killed, deaths[0].Cause);
            Assert.AreEqual(WeaponIds.Sniper, deaths[0].Weapon);
            Assert.IsTrue(deaths[0].Headshot);
            Assert.AreEqual(1, Red.Kills);
        }

        [Test]
        public void TheDead_TakeNoMoreDamage_AndDieOnce()
        {
            combat.Hurt(Blue, Shot(Red, 150));
            var again = combat.Hurt(Blue, Shot(Red2, 50));

            Assert.AreEqual(0, again);
            Assert.AreEqual(1, deaths.Count);
            Assert.AreEqual(0, Red2.Kills);
        }

        [Test]
        public void DualPistols_AreReportedAsPistols_InTheKillFeed()
        {
            combat.Hurt(Blue, Shot(Red, 100, WeaponIds.DualPistols));

            Assert.AreEqual(WeaponIds.Pistol, deaths[0].Weapon);
        }

        [Test]
        public void FriendlyFire_IsOff()
        {
            Assert.AreEqual(0, combat.Hurt(Red2, Shot(Red, 50)));
            Assert.AreEqual(100, Red2.Health);
        }

        [Test]
        public void SpawnProtection_BlocksDamage_UntilItRunsOut()
        {
            combat.Hurt(Blue, Shot(Red, 200));
            Run(match.Rules.respawnSeconds + 0.05f);
            Assert.IsTrue(Blue.Alive);
            Assert.IsTrue(Blue.Protected);

            Assert.AreEqual(0, combat.Hurt(Blue, Shot(Red, 50)));
            Run(match.Rules.spawnProtectionSeconds + 0.1f);
            Assert.AreEqual(50, combat.Hurt(Blue, Shot(Red, 50)));
        }

        [Test]
        public void Firing_EndsSpawnProtection()
        {
            combat.Hurt(Blue, Shot(Red, 200));
            Run(match.Rules.respawnSeconds + 0.05f);
            Assert.IsTrue(Blue.Protected);

            combat.Fired(Blue);

            Assert.IsFalse(Blue.Protected);
            Assert.AreEqual(12, combat.Hurt(Blue, Shot(Red, 12)));
        }

        [Test]
        public void NoDamage_InTheCountdown()
        {
            var fresh = new MatchState(new MatchRules { spawnProtectionSeconds = 0 });
            var red = fresh.AddPlayer("Red 1", Team.Red, false);
            var blue = fresh.AddPlayer("Blue 1", Team.Blue, true);
            var rules = new CombatState(fresh, WeaponTable.Default());
            fresh.Begin();

            Assert.AreEqual(0, rules.Hurt(blue, Shot(red, 50)));
            Assert.AreEqual(100, blue.Health);
        }

        [Test]
        public void AHealthPod_HealsOneForOneTo100_ThenHalfTo200()
        {
            var pack = combat.Weapons.healthPack;
            Assert.AreEqual(40, pack);

            combat.Hurt(Red, Shot(Blue, 70));
            Assert.AreEqual(40, combat.Heal(Red, pack), "30 to 70");
            Assert.AreEqual(70, Red.Health);
            Assert.AreEqual(35, combat.Heal(Red, pack), 1e-4f, "70 to 105: 30 up to 100, then 5 for the 10 points left");
            Assert.AreEqual(105, Red.Health, 1e-4f);
            Assert.AreEqual(20, combat.Heal(Red, pack), 1e-4f, "above 100: half");
            Assert.AreEqual(125, Red.Health, 1e-4f);
            for (var i = 0; i < 4; i++)
                combat.Heal(Red, pack);
            Assert.AreEqual(200, Red.Health, 1e-4f, "never past 200");
            Assert.IsFalse(combat.NeedsHealth(Red), "at 200 the pod stays for others");
            Assert.AreEqual(0, combat.Heal(Red, pack));
        }

        [Test]
        public void HealthAfterAPod_FromTheUsersExample()
        {
            Assert.AreEqual(110, CombatState.HealthAfter(80, 100, 200, 40), 1e-4f);
            Assert.AreEqual(120, CombatState.HealthAfter(100, 100, 200, 40), 1e-4f);
            Assert.AreEqual(60, CombatState.HealthAfter(20, 100, 200, 40), 1e-4f);
            Assert.AreEqual(200, CombatState.HealthAfter(190, 100, 200, 40), 1e-4f);
            Assert.AreEqual(210, CombatState.HealthAfter(210, 100, 200, 40), 1e-4f, "never lowered");
        }

        [Test]
        public void OverhealedHealth_TakesDamageLikeAnyOther_AndARespawnIsBackTo100()
        {
            combat.Heal(Red, 40);
            Assert.AreEqual(120, Red.Health, 1e-4f);
            combat.Hurt(Red, Shot(Blue, 85, WeaponIds.Sniper));
            Assert.AreEqual(35, Red.Health, 1e-4f);
            combat.Hurt(Red, Shot(Blue, 85, WeaponIds.Sniper));
            Run(match.Rules.respawnSeconds + 0.1f);
            Assert.AreEqual(100, Red.Health);
        }

        [Test]
        public void TheDead_AreNotHealed()
        {
            combat.Hurt(Red, Shot(Blue, 100));

            Assert.IsFalse(combat.NeedsHealth(Red));
            Assert.AreEqual(0, combat.Heal(Red, 40));
        }

        [Test]
        public void UnderEighty_TheLivingHealOneEveryTwoSeconds_UpTo80()
        {
            combat.Hurt(Blue, Shot(Red, 25));
            var healed = 0f;
            combat.Healed += (p, amount) => healed += p == Blue ? amount : 0;

            Run(1.9f);
            Assert.AreEqual(75, Blue.Health, 1e-4f, "not before two seconds");
            Run(0.2f);
            Assert.AreEqual(76, Blue.Health, 1e-4f);
            Run(20);
            Assert.AreEqual(80, Blue.Health, 1e-4f, "stops at 80");
            Assert.AreEqual(5, healed, 1e-4f, "reported as healing");
        }

        [Test]
        public void AtOrAboveEighty_TheSlowHealDoesNothing()
        {
            combat.Hurt(Blue, Shot(Red, 15));

            Run(10);

            Assert.AreEqual(85, Blue.Health, 1e-4f);
        }

        [Test]
        public void TheDead_DoNotHealSlowly()
        {
            combat.Hurt(Blue, Shot(Red, 100));

            Run(match.Rules.respawnSeconds - 0.1f);

            Assert.IsFalse(Blue.Alive);
            Assert.AreEqual(0, Blue.Health);
        }

        [Test]
        public void Death_LosesEverything_ButTheSinglePistol_AndTheRespawnHasIt()
        {
            combat.Take(Blue, WeaponIds.Minigun);
            combat.Take(Blue, WeaponIds.Pistol);
            combat.Take(Blue, WeaponIds.Flamethrower);
            Assert.AreEqual(WeaponIds.Flamethrower, combat.WeaponOf(Blue).id);

            combat.Hurt(Blue, Shot(Red, 100));
            var inventory = combat.InventoryOf(Blue);
            Assert.AreEqual(WeaponIds.Pistol, inventory.CurrentWeapon);
            Assert.IsFalse(inventory.DualPistols);
            Assert.IsFalse(inventory.Has(WeaponSlot.Minigun));
            Assert.IsFalse(combat.CanTake(Blue, WeaponIds.Minigun), "the dead pick nothing up");

            Run(match.Rules.respawnSeconds + 0.05f);
            Assert.IsTrue(Blue.Alive);
            Assert.AreEqual(WeaponIds.Pistol, combat.InventoryOf(Blue).CurrentWeapon);
            Assert.IsFalse(combat.InventoryOf(Blue).Has(WeaponSlot.ShortRange));
            Assert.IsTrue(combat.CanTake(Blue, WeaponIds.Minigun));
        }

        [Test]
        public void ANewRound_StartsEveryoneWithASinglePistol()
        {
            combat.Take(Red, WeaponIds.Sniper);
            for (var i = 0; i < match.Rules.capturesToWin; i++)
                match.ReportCapture(Team.Red);
            Run(match.Rules.resultSeconds + 0.05f);

            Assert.AreEqual(RoundPhase.Countdown, match.Phase);
            Assert.IsFalse(combat.InventoryOf(Red).Has(WeaponSlot.LongRange));
        }

        [Test]
        public void AMachinesKill_NamesTheMachine_ButCountsForNoPlayer()
        {
            combat.Hurt(Red, Damage.ByMachine("Blue tank", Team.Blue, "cannon", 6, Vector3.zero));
            Assert.AreEqual(94, Red.Health);

            combat.Hurt(Red, Damage.ByMachine("Blue tank", Team.Blue, "cannon", 200, Vector3.zero));

            Assert.AreEqual(1, deaths.Count);
            Assert.IsNull(deaths[0].Killer);
            Assert.AreEqual("Blue tank", deaths[0].KillerName);
            Assert.AreEqual(Team.Blue, deaths[0].KillerTeam);
            Assert.AreEqual("cannon", deaths[0].Weapon);
            Assert.IsFalse(deaths[0].CountsAsKill);
        }

        [Test]
        public void AMachine_DoesNotHurtItsOwnTeam()
        {
            Assert.AreEqual(0, combat.Hurt(Blue, Damage.ByMachine("Blue turret", Team.Blue, "cannon", 6)));
        }

        [Test]
        public void MachineHealth_HurtsHealsAndResets()
        {
            var health = new Health(150);

            Assert.AreEqual(100, health.Hurt(100));
            Assert.AreEqual(50, health.Current);
            Assert.AreEqual(20, health.Heal(20));
            Assert.AreEqual(70, health.Hurt(85), "no more than is left");
            Assert.IsFalse(health.Alive);
            Assert.AreEqual(0, health.Hurt(10));
            Assert.AreEqual(0, health.Heal(10), "nothing heals the dead");

            health.Reset();
            Assert.AreEqual(150, health.Current);
            Assert.IsTrue(health.Alive);
        }

        [Test]
        public void Machines_AreHurtByTheOtherTeamOnly()
        {
            Assert.IsTrue(combat.CanHurt(Team.Blue, Team.Red));
            Assert.IsFalse(combat.CanHurt(Team.Blue, Team.Blue));
        }

        [Test]
        public void TheFlamesBurn_10ASecond_For2Seconds_CreditedToTheFlamethrower()
        {
            var flame = combat.Weapons.Get(WeaponIds.Flamethrower);
            combat.Burn(Blue, Damage.By(Red, WeaponIds.Flamethrower, flame.burnPerSecond), flame.burnSeconds);
            Assert.IsTrue(combat.IsBurning(Blue));

            Run(1.1f);
            Assert.AreEqual(92.5f, Blue.Health, 0.01f, "the first step passes it by, then 2.5 a step");
            Run(1.5f);
            Assert.AreEqual(80, Blue.Health, 0.01f, "20 in all");
            Assert.IsFalse(combat.IsBurning(Blue));
        }

        [Test]
        public void HeldInTheFlame_TheBurnWaits_ThenBurnsOnAfterLeavingIt()
        {
            // The flame's ticks, ten a second for a second, each setting the burn anew
            for (var i = 0; i < 10; i++)
            {
                combat.Burn(Blue, Damage.By(Red, WeaponIds.Flamethrower, 10), 2);
                Run(0.1f);
            }
            Assert.AreEqual(100, Blue.Health, 0.01f, "no burn while in the flame");

            Run(2.6f);
            Assert.AreEqual(80, Blue.Health, 0.01f);
        }

        [Test]
        public void ABurnThatKills_CreditsTheFlamethrower()
        {
            combat.Hurt(Blue, Shot(Red, 95, WeaponIds.Flamethrower));
            combat.Burn(Blue, Damage.By(Red, WeaponIds.Flamethrower, 10), 2);

            Run(1);

            Assert.IsFalse(Blue.Alive);
            Assert.AreEqual(Red, deaths[0].Killer);
            Assert.AreEqual(WeaponIds.Flamethrower, deaths[0].Weapon);
        }

        [Test]
        public void ABurn_StopsAtDeath()
        {
            combat.Burn(Blue, Damage.By(Red, WeaponIds.Flamethrower, 10), 2);
            combat.Hurt(Blue, Shot(Red, 100));

            Assert.IsFalse(combat.IsBurning(Blue));
        }

        [Test]
        public void ABlobHit_Slows40Percent_For2Seconds()
        {
            var blob = combat.Weapons.Get(WeaponIds.BlobGun);
            combat.Slow(Blue, Team.Red, blob.slow, blob.slowSeconds);

            Assert.AreEqual(0.6f, combat.SpeedFactor(Blue), 1e-4f);
            Run(1.9f);
            Assert.AreEqual(0.6f, combat.SpeedFactor(Blue), 1e-4f);
            Run(0.2f);
            Assert.AreEqual(1, combat.SpeedFactor(Blue));
        }

        [Test]
        public void TheSlow_SparesTheBlobbersOwnTeam()
        {
            combat.Slow(Red2, Team.Red, 0.4f, 2);

            Assert.AreEqual(1, combat.SpeedFactor(Red2));
        }

        private Area Cloud(Vector3 at, PlayerState by)
        {
            var poison = combat.Weapons.Get(WeaponIds.PoisonGun);
            return combat.AddArea(new Area(FireMode.Cloud, at, poison.areaRadius, poison.areaSeconds, Damage.By(by, WeaponIds.PoisonGun, poison.damage),
                poison.slow, poison.slowSeconds, poison.burnPerSecond, poison.burnSeconds));
        }

        [Test]
        public void APoisonCloud_Hurts18ASecond_TheEnemiesInIt()
        {
            positions[Blue] = new Vector3(1, 0, 0);
            positions[Red2] = new Vector3(0, 0, 1);
            positions[Blue2] = new Vector3(10, 0, 0);
            Cloud(new Vector3(0, 1, 0), Red);

            Run(1.1f);

            Assert.AreEqual(82, Blue.Health, 0.01f);
            Assert.AreEqual(100, Red2.Health, "not its own team");
            Assert.AreEqual(100, Blue2.Health, "not outside it");
        }

        [Test]
        public void APoisonCloud_PoisonsAndSlows_For10SecondsAfterLeavingIt()
        {
            var poison = combat.Weapons.Get(WeaponIds.PoisonGun);
            // Overhealed, so the slow heal under 80 stays out of the sums
            Blue.Health = 200;
            positions[Blue] = new Vector3(1, 0, 0);
            Cloud(new Vector3(0, 1, 0), Red);

            Run(1.1f);
            Assert.AreEqual(182, Blue.Health, 0.01f, "in the cloud: only the cloud's 18 a second");
            Assert.IsTrue(combat.IsPoisoned(Blue));
            Assert.AreEqual(1 - poison.slow, combat.SpeedFactor(Blue), 1e-4f);

            positions[Blue] = new Vector3(50, 0, 0);
            Run(5);
            Assert.AreEqual(182 - 5 * poison.burnPerSecond, Blue.Health, 0.3f, "the afterpoison");
            Assert.AreEqual(1 - poison.slow, combat.SpeedFactor(Blue), 1e-4f, "still slowed");
            Run(5.3f);
            Assert.AreEqual(182 - poison.burnSeconds * poison.burnPerSecond, Blue.Health, 0.01f);
            Assert.IsFalse(combat.IsPoisoned(Blue));
            Assert.AreEqual(1, combat.SpeedFactor(Blue), "10 s after leaving");
        }

        [Test]
        public void AShortStrongSlow_EndsOnTime_UnderALongWeakOne()
        {
            combat.Slow(Blue, Team.Red, 0.25f, 10, FireMode.Cloud);
            combat.Slow(Blue, Team.Red, 0.4f, 2);

            Assert.AreEqual(0.6f, combat.SpeedFactor(Blue), 1e-4f);
            Run(2.1f);
            Assert.AreEqual(0.75f, combat.SpeedFactor(Blue), 1e-4f);
        }

        [Test]
        public void PoisonClouds_DoNotAddUp()
        {
            positions[Blue] = new Vector3(0.5f, 0, 0);
            Cloud(new Vector3(0, 1, 0), Red);
            Cloud(new Vector3(1, 1, 0), Red2);

            Run(1.1f);

            Assert.AreEqual(82, Blue.Health, 0.01f);
        }

        [Test]
        public void APoisonCloud_LastsFiveSeconds()
        {
            var cloud = Cloud(new Vector3(0, 1, 0), Red);
            var ended = 0;
            combat.AreaEnded += area => ended += area == cloud ? 1 : 0;

            Run(4.9f);
            Assert.AreEqual(1, combat.Areas.Count);
            Run(0.2f);
            Assert.AreEqual(0, combat.Areas.Count);
            Assert.AreEqual(1, ended);

            positions[Blue] = new Vector3(0, 0, 0);
            Run(1);
            Assert.AreEqual(100, Blue.Health);
        }

        [Test]
        public void APoisonCloud_ReachesABodyUnderIt_AsACapsule()
        {
            var area = new Area(FireMode.Cloud, new Vector3(0, 4, 0), 3, 5, default);

            Assert.IsTrue(area.Contains(new Vector3(0, 0, 0)), "the head is 1.75 high, 2.25 under the middle");
            Assert.IsFalse(area.Contains(new Vector3(0, -2, 0)));
            Assert.IsTrue(area.Contains(new Vector3(3.2f, 3, 0)), "the body's radius reaches in");
            Assert.IsFalse(area.Contains(new Vector3(3.4f, 3, 0)));
        }

        [Test]
        public void ASplat_SlowsTheEnemiesWhoWalkIn()
        {
            var blob = combat.Weapons.Get(WeaponIds.BlobGun);
            combat.AddArea(new Area(FireMode.Blob, Vector3.zero, blob.areaRadius, blob.areaSeconds, Damage.By(Red, WeaponIds.BlobGun, 0), blob.slow));
            positions[Blue] = new Vector3(0.5f, 0, 0);
            positions[Red2] = new Vector3(0, 0, 0.5f);

            Run(0.3f);
            Assert.AreEqual(0.6f, combat.SpeedFactor(Blue), 1e-4f);
            Assert.AreEqual(1, combat.SpeedFactor(Red2));
            Assert.AreEqual(100, Blue.Health, "a splat only slows");

            positions[Blue] = new Vector3(5, 0, 0);
            Run(0.6f);
            Assert.AreEqual(1, combat.SpeedFactor(Blue), "it wears off soon after stepping out");
        }
    }
}
