using NUnit.Framework;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 22: the first balance (<see cref="WeaponTable.Default"/>): every weapon has its numbers, the damage falloffs over distance,
    /// the minigun's spread and spin-up, the sniper's zoom, the single pistol weaker than the rest, and the trigger's timing.
    /// </summary>
    public sealed class WeaponTableTests
    {
        private static readonly WeaponTable Table = WeaponTable.Default();

        private static WeaponStats Get(string id)
        {
            var stats = Table.Get(id);
            Assert.IsNotNull(stats, id);
            return stats;
        }

        [Test]
        public void EveryWeapon_HasItsNumbers_InItsSlot()
        {
            foreach (var id in WeaponIds.Pickups)
                Assert.AreEqual(WeaponIds.SlotOf(id), Get(id).slot, id);
            Assert.AreEqual(WeaponSlot.Pistol, Get(WeaponIds.DualPistols).slot);
            Assert.IsNull(Table.Get("banana"));
        }

        [Test]
        public void ThePistol_FallsOffPast20Metres()
        {
            var pistol = Get(WeaponIds.Pistol);

            Assert.AreEqual(12, pistol.DamageAt(5), 1e-4f);
            Assert.AreEqual(12, pistol.DamageAt(20), 1e-4f);
            Assert.AreEqual(9, pistol.DamageAt(30), 1e-4f);
            Assert.AreEqual(6, pistol.DamageAt(40), 1e-4f);
            Assert.AreEqual(6, pistol.DamageAt(100), 1e-4f, "it stays at the minimum");
            Assert.AreEqual(2.5f, pistol.shotsPerSecond);
        }

        [Test]
        public void DualPistols_FireTwiceAsFast_WithTheSameDamageAndFalloff_AndMoreSpread()
        {
            var single = Get(WeaponIds.Pistol);
            var dual = Get(WeaponIds.DualPistols);

            Assert.AreEqual(2 * single.shotsPerSecond, dual.shotsPerSecond, 1e-4f);
            Assert.AreEqual(single.DamageAt(30), dual.DamageAt(30), 1e-4f);
            Assert.Greater(dual.spread, single.spread);
        }

        [Test]
        public void TheMinigun_IsBestToMidRange_ThenFallsOff()
        {
            var minigun = Get(WeaponIds.Minigun);

            Assert.AreEqual(7, minigun.DamageAt(15), 1e-4f);
            Assert.AreEqual(7, minigun.DamageAt(35), 1e-4f);
            Assert.Less(minigun.DamageAt(50), 7);
            Assert.AreEqual(7 * minigun.falloffMin, minigun.DamageAt(80), 1e-4f);
        }

        [Test]
        public void TheMinigunsSpread_GrowsTheLongerItFires_UpToItsMost()
        {
            var minigun = Get(WeaponIds.Minigun);

            Assert.AreEqual(minigun.spread, minigun.SpreadAfter(0), 1e-4f);
            Assert.Greater(minigun.SpreadAfter(1), minigun.SpreadAfter(0.2f));
            Assert.AreEqual(minigun.spreadMax, minigun.SpreadAfter(30), 1e-4f);
        }

        [Test]
        public void TheSniper_HasNoFalloff_A120Headshot_AndIsOnlyAccurateZoomed()
        {
            var sniper = Get(WeaponIds.Sniper);

            Assert.AreEqual(85, sniper.DamageAt(5), 1e-4f);
            Assert.AreEqual(85, sniper.DamageAt(200), 1e-4f);
            Assert.AreEqual(120, sniper.DamageAt(150, head: true), 1e-4f);
            Assert.AreEqual(1.4f, sniper.Interval, 1e-4f);
            Assert.Greater(sniper.SpreadAfter(0), 2, "unzoomed: a wide spread");
            Assert.AreEqual(0, sniper.SpreadAfter(0, zoomed: true), 1e-4f);
            Assert.Greater(sniper.zoomFieldOfView, 0);
        }

        [Test]
        public void OnlyTheSniper_HasHeadshots()
        {
            foreach (var weapon in Table.weapons)
                if (weapon.id != WeaponIds.Sniper)
                    Assert.AreEqual(weapon.damage, weapon.DamageAt(1, head: true), 1e-4f, weapon.id);
        }

        [Test]
        public void TheElectroGun_Reaches60Metres_AndJumpsTwice6MetresFor20()
        {
            var electro = Get(WeaponIds.ElectroGun);

            Assert.AreEqual(FireMode.Chain, electro.mode);
            Assert.AreEqual(40, electro.DamageAt(59), 1e-4f);
            Assert.AreEqual(60, electro.range);
            Assert.AreEqual(6, electro.chainRange);
            Assert.AreEqual(20, electro.chainDamage);
            Assert.AreEqual(2, electro.chainJumps, "on to two more enemies at most");
        }

        [Test]
        public void TheFlamethrower_Deals70ASecond_InSmallTicks_To8Metres()
        {
            var flame = Get(WeaponIds.Flamethrower);

            Assert.AreEqual(FireMode.Cone, flame.mode);
            Assert.AreEqual(8, flame.range);
            Assert.AreEqual(70, flame.ConeDamagePerShot * flame.shotsPerSecond, 1e-3f);
            Assert.AreEqual(10, flame.burnPerSecond);
            Assert.AreEqual(2, flame.burnSeconds);
        }

        [Test]
        public void TheLobbedGuns_ReachTheirRange_AtA45DegreeArc()
        {
            foreach (var id in new[] { WeaponIds.PoisonGun, WeaponIds.BlobGun })
            {
                var gun = Get(id);
                var reach = gun.projectileSpeed * gun.projectileSpeed / gun.projectileGravity;
                Assert.GreaterOrEqual(reach, 0.9f * gun.range, id);
            }
            var poison = Get(WeaponIds.PoisonGun);
            Assert.AreEqual(3, poison.areaRadius);
            Assert.AreEqual(5, poison.areaSeconds);
            Assert.AreEqual(18, poison.damage);
            var blob = Get(WeaponIds.BlobGun);
            Assert.AreEqual(35, blob.damage);
            Assert.AreEqual(0.4f, blob.slow, 1e-4f);
            Assert.AreEqual(2, blob.slowSeconds);
        }

        [Test]
        public void TheSinglePistol_IsWeakerThanEveryOtherGun_ThatHitsDirectly()
        {
            // Damage a second at each gun's own good range; the poison cloud's worth is the area and the time, not the rate
            var pistol = Get(WeaponIds.Pistol).DamageAt(10) * Get(WeaponIds.Pistol).shotsPerSecond;
            foreach (var weapon in Table.weapons)
            {
                if (weapon.id == WeaponIds.Pistol || weapon.mode == FireMode.Cloud)
                    continue;
                var perSecond = weapon.mode == FireMode.Cone ? weapon.damage : weapon.DamageAt(10) * weapon.shotsPerSecond;
                Assert.Greater(perSecond, pistol, weapon.id);
            }
        }

        // ---------------------------------------------------------------- The head

        [Test]
        public void AShot_HitsTheHead_AnEllipsoidWiderThanItIsTall()
        {
            var centre = new UnityEngine.Vector3(0, 1.41f, 10);

            Assert.IsTrue(Shots.HitEllipsoid(new UnityEngine.Vector3(0, 1.41f, 0), UnityEngine.Vector3.forward, centre, 0.6f, 0.22f, out var t));
            Assert.AreEqual(9.4f, t, 1e-3f, "the cap's rim, 0.6 m before its middle");
            Assert.IsTrue(Shots.HitEllipsoid(new UnityEngine.Vector3(0.5f, 1.41f, 0), UnityEngine.Vector3.forward, centre, 0.6f, 0.22f, out _), "wide");
            Assert.IsFalse(Shots.HitEllipsoid(new UnityEngine.Vector3(0, 1.7f, 0), UnityEngine.Vector3.forward, centre, 0.6f, 0.22f, out _), "but flat");
            Assert.IsFalse(Shots.HitEllipsoid(new UnityEngine.Vector3(0, 1.41f, 20), UnityEngine.Vector3.forward, centre, 0.6f, 0.22f, out _), "not behind");
            Assert.IsFalse(Shots.HitEllipsoid(centre, UnityEngine.Vector3.forward, centre, 0.6f, 0.22f, out _), "not from inside");
        }

        // ---------------------------------------------------------------- The trigger

        private static int Hold(WeaponTrigger trigger, float seconds, bool held = true, float step = 0.01f)
        {
            var shots = 0;
            for (var left = seconds; left > 1e-5f; left -= step)
                shots += trigger.Tick(System.Math.Min(step, left), held);
            return shots;
        }

        private static WeaponTrigger Ready(string id)
        {
            var trigger = new WeaponTrigger();
            trigger.Equip(Get(id));
            Hold(trigger, 1, held: false);
            return trigger;
        }

        [Test]
        public void Trigger_FiresAtOnce_ThenAtItsRate()
        {
            var trigger = Ready(WeaponIds.Pistol);

            Assert.AreEqual(1, trigger.Tick(0.01f, true), "the first shot leaves at once");
            Assert.AreEqual(4, Hold(trigger, 1.95f), "then every 0.4 s: 0.4, 0.8, 1.2, 1.6");
        }

        [Test]
        public void Trigger_WaitsForTheDraw()
        {
            var trigger = new WeaponTrigger();
            trigger.Equip(Get(WeaponIds.Sniper));

            Assert.AreEqual(0, Hold(trigger, Get(WeaponIds.Sniper).drawSeconds - 0.05f));
            Assert.AreEqual(1, Hold(trigger, 0.1f));
        }

        [Test]
        public void Trigger_KeepsTheTimeBetweenShots_WhenLetGoAndPressedAgain()
        {
            var trigger = Ready(WeaponIds.Sniper);

            Assert.AreEqual(1, Hold(trigger, 0.05f));
            Hold(trigger, 0.3f, held: false);
            Assert.AreEqual(0, Hold(trigger, 0.9f), "1.4 s between sniper shots");
            Assert.AreEqual(1, Hold(trigger, 0.2f));
        }

        [Test]
        public void TheMinigun_FiresFor3Seconds_ThenSpinsDown_ThenUpAgain()
        {
            var minigun = Get(WeaponIds.Minigun);
            Assert.AreEqual(3, minigun.burstSeconds);
            Assert.AreEqual(0.6f, minigun.spinDown, 1e-4f);
            var trigger = Ready(WeaponIds.Minigun);

            Hold(trigger, 0.6f);
            var burst = Hold(trigger, 3.05f);
            Assert.That(burst, Is.InRange(42, 44), "3 s at 14 a second");
            Assert.IsTrue(trigger.Overheated);
            Assert.AreEqual(0, Hold(trigger, 0.55f), "spinning down, the trigger still held");
            Assert.AreEqual(0, Hold(trigger, 0.6f), "spun down at 0.6 s, then spinning up again");
            Assert.IsFalse(trigger.Overheated);
            Assert.Greater(Hold(trigger, 0.2f), 0, "firing again");
        }

        [Test]
        public void LettingGo_SpinsDownIn06Seconds_AndGivesTheBurstBack()
        {
            var trigger = Ready(WeaponIds.Minigun);
            Hold(trigger, 2.5f);
            Assert.Greater(trigger.BurstFraction, 0.5f);

            Hold(trigger, 0.65f, held: false);

            Assert.AreEqual(0, trigger.SpinFraction, 1e-4f);
            Assert.AreEqual(0, trigger.BurstFraction, 1e-4f);
        }

        [Test]
        public void ThePoisonGun_PoisonsAndSlowsFor10Seconds_AfterTheCloud()
        {
            var poison = Get(WeaponIds.PoisonGun);
            Assert.AreEqual(10, poison.burnSeconds);
            Assert.AreEqual(10, poison.slowSeconds);
            Assert.Greater(poison.burnPerSecond, 0);
            Assert.Greater(poison.slow, 0);
        }

        [Test]
        public void TheMinigun_SpinsUp_ThenFires14ASecond()
        {
            var trigger = Ready(WeaponIds.Minigun);

            Assert.AreEqual(0, Hold(trigger, 0.59f), "spinning up");
            var shots = Hold(trigger, 1.01f);
            Assert.That(shots, Is.InRange(14, 15));
            Assert.AreEqual(1, trigger.SpinFraction, 1e-4f);
            Assert.Greater(trigger.Spread(false), Get(WeaponIds.Minigun).spread, "spreading while it fires");

            Hold(trigger, 1, held: false);
            Assert.AreEqual(0, trigger.SpinFraction, 1e-4f, "spun down");
            Assert.AreEqual(Get(WeaponIds.Minigun).spread, trigger.Spread(false), 1e-4f, "and steady again");
        }
    }
}
