using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>Ticket 23: a bot takes the weapon that suits the distance, by ticket 22's "good for".</summary>
    public sealed class WeaponChoiceTests
    {
        private static Inventory Full(string longRange = WeaponIds.Sniper, string shortRange = WeaponIds.Flamethrower)
        {
            var inventory = new Inventory();
            inventory.Take(WeaponIds.Pistol);
            inventory.Take(WeaponIds.Minigun);
            inventory.Take(longRange);
            inventory.Take(shortRange);
            return inventory;
        }

        private static string Best(Inventory inventory, WeaponChoice.Context c)
        {
            inventory.Select(WeaponChoice.Best(inventory, c));
            return inventory.CurrentWeapon;
        }

        [Test]
        public void OnlyThePistol_StaysThePistol()
        {
            var inventory = new Inventory();
            Assert.AreEqual(WeaponSlot.Pistol, WeaponChoice.Best(inventory, new WeaponChoice.Context(5)));
            Assert.AreEqual(WeaponSlot.Pistol, WeaponChoice.Best(inventory, new WeaponChoice.Context(80)));
        }

        [Test]
        public void ByDistance_FlameCloseMinigunMidSniperFar()
        {
            var inventory = Full();
            Assert.AreEqual(WeaponIds.Flamethrower, Best(inventory, new WeaponChoice.Context(4)));
            Assert.AreEqual(WeaponIds.Minigun, Best(inventory, new WeaponChoice.Context(22)));
            Assert.AreEqual(WeaponIds.Sniper, Best(inventory, new WeaponChoice.Context(50)));
        }

        [Test]
        public void TheFlamethrower_NeverBeyondItsReach()
        {
            Assert.AreEqual(0, WeaponChoice.Suitability(WeaponIds.Flamethrower, new WeaponChoice.Context(9)));
            Assert.AreEqual(0, WeaponChoice.Suitability(WeaponIds.ElectroGun, new WeaponChoice.Context(61)));
            Assert.AreEqual(0, WeaponChoice.Suitability(WeaponIds.PoisonGun, new WeaponChoice.Context(31)));
            Assert.AreEqual(0, WeaponChoice.Suitability(WeaponIds.BlobGun, new WeaponChoice.Context(39)));
        }

        [Test]
        public void TheBlobGun_IsForTheCarrier()
        {
            var inventory = Full(WeaponIds.ElectroGun, WeaponIds.BlobGun);
            Assert.AreEqual(WeaponIds.Minigun, Best(inventory, new WeaponChoice.Context(20)));
            Assert.AreEqual(WeaponIds.BlobGun, Best(inventory, new WeaponChoice.Context(20, targetIsCarrier: true)));
        }

        [Test]
        public void TheElectroGun_LikesAGroup()
        {
            var alone = WeaponChoice.Suitability(WeaponIds.ElectroGun, new WeaponChoice.Context(30));
            var grouped = WeaponChoice.Suitability(WeaponIds.ElectroGun, new WeaponChoice.Context(30, groupedWithTarget: 2));
            Assert.Greater(grouped, alone);
            var inventory = Full(WeaponIds.ElectroGun, WeaponIds.PoisonGun);
            Assert.AreEqual(WeaponIds.ElectroGun, Best(inventory, new WeaponChoice.Context(40, groupedWithTarget: 2)));
        }

        [Test]
        public void ThePoisonGun_DefendsMidRange()
        {
            var inventory = new Inventory();
            inventory.Take(WeaponIds.Pistol);
            inventory.Take(WeaponIds.PoisonGun);
            Assert.AreEqual(WeaponIds.PoisonGun, Best(inventory, new WeaponChoice.Context(15, defending: true)));
        }

        [Test]
        public void DualPistols_BeatTheSinglePistolEverywhere_AndThePistolIsNeverBest()
        {
            for (var d = 0f; d <= 100; d += 5)
            {
                var c = new WeaponChoice.Context(d);
                var pistol = WeaponChoice.Suitability(WeaponIds.Pistol, c);
                Assert.Greater(WeaponChoice.Suitability(WeaponIds.DualPistols, c), pistol, $"{d} m");
                Assert.Greater(Mathf.Max(WeaponChoice.Suitability(WeaponIds.Minigun, c), WeaponChoice.Suitability(WeaponIds.Sniper, c)), pistol, $"{d} m");
            }
        }

        [Test]
        public void ACloseCall_KeepsTheWeaponInHand()
        {
            var inventory = Full();
            inventory.Select(WeaponSlot.Minigun);
            // At 36 m the sniper suits a little better than the minigun, but not by the margin
            var c = new WeaponChoice.Context(36);
            Assert.Greater(WeaponChoice.Suitability(WeaponIds.Sniper, c), WeaponChoice.Suitability(WeaponIds.Minigun, c));
            Assert.AreEqual(WeaponSlot.Minigun, WeaponChoice.Best(inventory, c));
        }
    }

    /// <summary>Ticket 23: the aim that lands a lobbed shot where the bot wants it.</summary>
    public sealed class BallisticsTests
    {
        private static Vector3 Land(WeaponStats weapon, Vector3 from, float yaw, float pitch, float targetY)
        {
            // The shot leaves projectileLift degrees above the aim, as Combat launches it
            var elevation = (weapon.projectileLift - pitch) * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Cos(yaw * Mathf.Deg2Rad) * Mathf.Cos(elevation));
            var velocity = direction * weapon.projectileSpeed;
            var position = from;
            const float dt = 0.002f;
            for (var t = 0f; t < 10; t += dt)
            {
                var next = position + velocity * dt;
                velocity += Vector3.down * weapon.projectileGravity * dt;
                if (velocity.y < 0 && next.y <= targetY)
                    return next;
                position = next;
            }
            return position;
        }

        [TestCase(WeaponIds.PoisonGun, 10f, 0f)]
        [TestCase(WeaponIds.PoisonGun, 25f, -1.5f)]
        [TestCase(WeaponIds.BlobGun, 18f, 0f)]
        [TestCase(WeaponIds.BlobGun, 30f, 3f)]
        public void TheAim_LandsTheShotOnTheTarget(string id, float across, float up)
        {
            var weapon = WeaponTable.Default().Get(id);
            var from = new Vector3(0, 1.5f, 0);
            var to = from + new Vector3(across * 0.6f, up, across * 0.8f);
            Assert.IsTrue(Ballistics.AimPitch(weapon, from, to, out var pitch, out var flight));
            Assert.Greater(flight, 0);
            var yaw = Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
            var landed = Land(weapon, from, yaw, pitch, to.y);
            Assert.Less(Vector2.Distance(new Vector2(landed.x, landed.z), new Vector2(to.x, to.z)), 0.35f, $"landed at {landed}, aimed at {to}");
        }

        [Test]
        public void BeyondReach_IsSaid()
        {
            var weapon = WeaponTable.Default().Get(WeaponIds.PoisonGun);
            Assert.IsFalse(Ballistics.AimPitch(weapon, Vector3.zero, new Vector3(0, 0, 80), out _, out _));
        }

        [Test]
        public void TheLowArc_IsTheFlatterOne()
        {
            Assert.IsTrue(Ballistics.LowArc(20, 0, 20, 9.8f, out var elevation));
            Assert.Less(elevation, Mathf.PI / 4);
            // Range on level ground: v² sin 2θ / g
            Assert.AreEqual(20, 20 * 20 * Mathf.Sin(2 * elevation) / 9.8f, 0.01f);
        }
    }
}
