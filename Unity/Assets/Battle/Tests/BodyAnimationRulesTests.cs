using NUnit.Framework;
using SdfBody;

namespace SdfBattle.Tests
{
    public class BodyAnimationRulesTests
    {
        [TestCase(WeaponIds.Minigun, true)]
        [TestCase(WeaponIds.Flamethrower, true)]
        [TestCase(WeaponIds.ElectroGun, true)]
        [TestCase(WeaponIds.Sniper, false)]
        [TestCase(WeaponIds.Pistol, false)]
        [TestCase(WeaponIds.DualPistols, false)]
        [TestCase(WeaponIds.PoisonGun, false)]
        [TestCase(WeaponIds.BlobGun, false)]
        public void TheFireModeOfAWeapon(string weapon, bool automatic)
        {
            Assert.AreEqual(automatic, BodyAnimationRules.AutomaticFire(weapon));
        }

        [Test]
        public void EveryPickupWeaponHasAFireModeAndAStance()
        {
            foreach (var weapon in WeaponIds.Pickups)
            {
                Assert.DoesNotThrow(() => BodyAnimationRules.AutomaticFire(weapon), weapon);
                Assert.That(System.Enum.IsDefined(typeof(LocomotionClips.Stance), BodyAnimationRules.StanceOf(weapon)), weapon);
            }
        }

        [TestCase(WeaponIds.Pistol, LocomotionClips.Stance.Pistol)]
        [TestCase(WeaponIds.DualPistols, LocomotionClips.Stance.Pistol)]
        [TestCase(WeaponIds.Minigun, LocomotionClips.Stance.Heavy)]
        [TestCase(WeaponIds.Flamethrower, LocomotionClips.Stance.Heavy)]
        [TestCase(WeaponIds.Sniper, LocomotionClips.Stance.Rifle)]
        [TestCase(WeaponIds.ElectroGun, LocomotionClips.Stance.Rifle)]
        [TestCase(WeaponIds.PoisonGun, LocomotionClips.Stance.Rifle)]
        [TestCase(WeaponIds.BlobGun, LocomotionClips.Stance.Rifle)]
        [TestCase("nothing", LocomotionClips.Stance.Rifle)]
        public void TheStanceOfAWeapon(string weapon, LocomotionClips.Stance stance)
        {
            Assert.AreEqual(stance, BodyAnimationRules.StanceOf(weapon));
        }

        [Test]
        public void OnlyTheFlamethrowerFiresWhileHeld()
        {
            foreach (var weapon in WeaponIds.Pickups)
                Assert.AreEqual(weapon == WeaponIds.Flamethrower, BodyAnimationRules.FiresWhileHeld(weapon), weapon);
        }
    }
}
