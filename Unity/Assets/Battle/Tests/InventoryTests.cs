using NUnit.Framework;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 22: the inventory: four slots, a single pistol to start with, pickups into their own slot, dual pistols, nothing taken
    /// twice, switching by number and scroll, and death keeping only the single pistol.
    /// </summary>
    public sealed class InventoryTests
    {
        [Test]
        public void Start_IsASinglePistolInHand()
        {
            var inventory = new Inventory();

            Assert.AreEqual(WeaponSlot.Pistol, inventory.Current);
            Assert.AreEqual(WeaponIds.Pistol, inventory.CurrentWeapon);
            Assert.IsFalse(inventory.DualPistols);
            Assert.IsTrue(inventory.Has(WeaponSlot.Pistol));
            Assert.IsFalse(inventory.Has(WeaponSlot.Minigun));
            Assert.IsFalse(inventory.Has(WeaponSlot.LongRange));
            Assert.IsFalse(inventory.Has(WeaponSlot.ShortRange));
        }

        [Test]
        public void EveryPickup_HasItsSlot()
        {
            Assert.AreEqual(WeaponSlot.Pistol, WeaponIds.SlotOf(WeaponIds.Pistol));
            Assert.AreEqual(WeaponSlot.Pistol, WeaponIds.SlotOf(WeaponIds.DualPistols));
            Assert.AreEqual(WeaponSlot.Minigun, WeaponIds.SlotOf(WeaponIds.Minigun));
            Assert.AreEqual(WeaponSlot.LongRange, WeaponIds.SlotOf(WeaponIds.Sniper));
            Assert.AreEqual(WeaponSlot.LongRange, WeaponIds.SlotOf(WeaponIds.ElectroGun));
            Assert.AreEqual(WeaponSlot.ShortRange, WeaponIds.SlotOf(WeaponIds.Flamethrower));
            Assert.AreEqual(WeaponSlot.ShortRange, WeaponIds.SlotOf(WeaponIds.PoisonGun));
            Assert.AreEqual(WeaponSlot.ShortRange, WeaponIds.SlotOf(WeaponIds.BlobGun));
            Assert.IsNull(WeaponIds.SlotOf("banana"));
            Assert.IsNull(WeaponIds.SlotOf(null));
        }

        [Test]
        public void PickUp_FillsTheWeaponsSlot_AndTakesItInHand()
        {
            var inventory = new Inventory();

            Assert.IsTrue(inventory.Take(WeaponIds.Minigun));
            Assert.IsTrue(inventory.Take(WeaponIds.Sniper));
            Assert.IsTrue(inventory.Take(WeaponIds.BlobGun));

            Assert.AreEqual(WeaponIds.Pistol, inventory[WeaponSlot.Pistol]);
            Assert.AreEqual(WeaponIds.Minigun, inventory[WeaponSlot.Minigun]);
            Assert.AreEqual(WeaponIds.Sniper, inventory[WeaponSlot.LongRange]);
            Assert.AreEqual(WeaponIds.BlobGun, inventory[WeaponSlot.ShortRange]);
            Assert.AreEqual(WeaponSlot.ShortRange, inventory.Current);
            Assert.AreEqual(WeaponIds.BlobGun, inventory.CurrentWeapon);
        }

        [Test]
        public void PickUp_WithoutSelect_KeepsTheWeaponInHand()
        {
            var inventory = new Inventory();

            Assert.IsTrue(inventory.Take(WeaponIds.ElectroGun, select: false));

            Assert.AreEqual(WeaponSlot.Pistol, inventory.Current);
            Assert.IsTrue(inventory.Holds(WeaponIds.ElectroGun));
        }

        [Test]
        public void PistolPod_MakesDualPistols_InTheSameSlot()
        {
            var inventory = new Inventory();
            inventory.Take(WeaponIds.Minigun);

            Assert.IsTrue(inventory.CanTake(WeaponIds.Pistol));
            Assert.IsTrue(inventory.Take(WeaponIds.Pistol));

            Assert.IsTrue(inventory.DualPistols);
            Assert.AreEqual(WeaponSlot.Pistol, inventory.Current);
            Assert.AreEqual(WeaponIds.DualPistols, inventory.CurrentWeapon);
            Assert.AreEqual(WeaponIds.Pistol, inventory[WeaponSlot.Pistol]);
            Assert.IsTrue(inventory.Holds(WeaponIds.DualPistols));
            Assert.AreEqual(WeaponIds.Minigun, inventory[WeaponSlot.Minigun], "the other slots are untouched");
        }

        [Test]
        public void AWeaponAlreadyHeld_IsNotTaken()
        {
            var inventory = new Inventory();
            inventory.Take(WeaponIds.Minigun);
            inventory.Take(WeaponIds.Pistol);
            inventory.Select(WeaponSlot.Minigun);
            var changes = 0;
            inventory.Changed += () => changes++;

            Assert.IsFalse(inventory.CanTake(WeaponIds.Minigun));
            Assert.IsFalse(inventory.Take(WeaponIds.Minigun));
            Assert.IsFalse(inventory.CanTake(WeaponIds.Pistol), "two pistols already");
            Assert.IsFalse(inventory.Take(WeaponIds.Pistol));
            Assert.IsFalse(inventory.CanTake(WeaponIds.DualPistols), "no pod holds dual pistols");
            Assert.IsFalse(inventory.CanTake("banana"));

            Assert.AreEqual(0, changes);
            Assert.AreEqual(WeaponSlot.Minigun, inventory.Current, "a pod left standing does not switch weapons");
        }

        [Test]
        public void AnotherWeaponOfTheSameSlot_TakesItsPlace()
        {
            // A scene has one kind per slot, but should two meet, the newer one is in the slot
            var inventory = new Inventory();
            inventory.Take(WeaponIds.Sniper);

            Assert.IsTrue(inventory.Take(WeaponIds.ElectroGun));

            Assert.AreEqual(WeaponIds.ElectroGun, inventory[WeaponSlot.LongRange]);
            Assert.IsFalse(inventory.Holds(WeaponIds.Sniper));
        }

        [Test]
        public void Select_ByNumber_OnlyHeldSlots()
        {
            var inventory = new Inventory();
            inventory.Take(WeaponIds.Flamethrower);

            Assert.IsFalse(inventory.SelectNumber(2), "no minigun");
            Assert.AreEqual(WeaponSlot.ShortRange, inventory.Current);
            Assert.IsTrue(inventory.SelectNumber(1));
            Assert.AreEqual(WeaponSlot.Pistol, inventory.Current);
            Assert.IsTrue(inventory.SelectNumber(4));
            Assert.AreEqual(WeaponSlot.ShortRange, inventory.Current);
            Assert.IsFalse(inventory.SelectNumber(0));
            Assert.IsFalse(inventory.SelectNumber(5));
        }

        [Test]
        public void Scroll_GoesRoundTheHeldSlots_SkippingEmptyOnes()
        {
            var inventory = new Inventory();
            inventory.Take(WeaponIds.Sniper, select: false);
            inventory.Take(WeaponIds.PoisonGun, select: false);

            Assert.IsTrue(inventory.Scroll(1));
            Assert.AreEqual(WeaponSlot.LongRange, inventory.Current, "past the empty minigun slot");
            Assert.IsTrue(inventory.Scroll(1));
            Assert.AreEqual(WeaponSlot.ShortRange, inventory.Current);
            Assert.IsTrue(inventory.Scroll(1));
            Assert.AreEqual(WeaponSlot.Pistol, inventory.Current, "round to the first");
            Assert.IsTrue(inventory.Scroll(-1));
            Assert.AreEqual(WeaponSlot.ShortRange, inventory.Current, "and back round to the last");
            Assert.IsFalse(inventory.Scroll(0));
        }

        [Test]
        public void Scroll_WithOnlyThePistol_StaysOnIt()
        {
            var inventory = new Inventory();

            Assert.IsFalse(inventory.Scroll(1));
            Assert.IsFalse(inventory.Scroll(-1));
            Assert.AreEqual(WeaponSlot.Pistol, inventory.Current);
        }

        [Test]
        public void Reset_OnDeath_KeepsOnlyTheSinglePistol()
        {
            var inventory = new Inventory();
            inventory.Take(WeaponIds.Minigun);
            inventory.Take(WeaponIds.Sniper);
            inventory.Take(WeaponIds.BlobGun);
            inventory.Take(WeaponIds.Pistol);

            inventory.Reset();

            Assert.IsFalse(inventory.DualPistols);
            Assert.AreEqual(WeaponIds.Pistol, inventory.CurrentWeapon);
            Assert.IsFalse(inventory.Has(WeaponSlot.Minigun));
            Assert.IsFalse(inventory.Has(WeaponSlot.LongRange));
            Assert.IsFalse(inventory.Has(WeaponSlot.ShortRange));
            Assert.IsTrue(inventory.CanTake(WeaponIds.Minigun), "the pods are worth walking to again");
        }
    }
}
