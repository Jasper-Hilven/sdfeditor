using System;

namespace SdfBattle
{
    /// <summary>
    /// A player's weapons (ticket 22): four slots (<see cref="WeaponSlot"/>), the one in hand, and whether the pistol slot holds two
    /// pistols. Everyone starts with a single pistol; a pistol pod makes it dual pistols, any other pod fills its weapon's slot. A pod
    /// whose weapon is already held is not taken (<see cref="CanTake"/>), so it stays for others. Death takes everything but the single
    /// pistol (<see cref="Reset"/>). Ammo is unlimited, so there is nothing else to keep.
    /// </summary>
    public sealed class Inventory
    {
        public const int SlotCount = 4;

        private readonly string[] slots = new string[SlotCount];

        public Inventory() => Reset();

        /// <summary>The slot in hand.</summary>
        public WeaponSlot Current { get; private set; }

        /// <summary>Two pistols in the pistol slot.</summary>
        public bool DualPistols { get; private set; }

        /// <summary>Something changed: a weapon taken, another selected, or everything lost.</summary>
        public event Action Changed;

        /// <summary>The weapon in a slot (its pod's id: pistol even with two), or null when the slot is empty.</summary>
        public string this[WeaponSlot slot] => slots[(int)slot];

        public bool Has(WeaponSlot slot) => slots[(int)slot] != null;

        /// <summary>Whether a weapon of this id is held (pistol also when there are two).</summary>
        public bool Holds(string id)
        {
            var slot = WeaponIds.SlotOf(id);
            return slot != null && slots[(int)slot.Value] == WeaponIds.ModelOf(id) && (id != WeaponIds.DualPistols || DualPistols);
        }

        /// <summary>The weapon in hand, as the weapon table names its numbers: dual-pistols for two pistols.</summary>
        public string CurrentWeapon => WeaponIn(Current);

        /// <summary>The weapon in a slot as the weapon table names it, or null.</summary>
        public string WeaponIn(WeaponSlot slot)
        {
            var id = slots[(int)slot];
            return slot == WeaponSlot.Pistol && DualPistols && id == WeaponIds.Pistol ? WeaponIds.DualPistols : id;
        }

        /// <summary>Everything lost but a single pistol, in hand: the start of every life.</summary>
        public void Reset()
        {
            Array.Clear(slots, 0, slots.Length);
            slots[(int)WeaponSlot.Pistol] = WeaponIds.Pistol;
            DualPistols = false;
            Current = WeaponSlot.Pistol;
            Changed?.Invoke();
        }

        /// <summary>
        /// Whether walking onto a pod with this weapon takes it: not when it is already held (a pistol when there are two already), not
        /// for an id that is no weapon.
        /// </summary>
        public bool CanTake(string id)
        {
            var slot = WeaponIds.SlotOf(id);
            if (slot == null || id == WeaponIds.DualPistols)
                return false;
            if (id == WeaponIds.Pistol)
                return !DualPistols;
            return slots[(int)slot.Value] != id;
        }

        /// <summary>
        /// Takes a pod's weapon into its slot (a second pistol makes two) and, with <paramref name="select"/>, takes it in hand. Returns
        /// whether it was taken; see <see cref="CanTake"/>.
        /// </summary>
        public bool Take(string id, bool select = true)
        {
            if (!CanTake(id))
                return false;
            var slot = WeaponIds.SlotOf(id).Value;
            if (id == WeaponIds.Pistol)
                DualPistols = true;
            else
                slots[(int)slot] = id;
            if (select)
                Current = slot;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Takes the weapon in a slot in hand; false (nothing changes) when the slot is empty.</summary>
        public bool Select(WeaponSlot slot)
        {
            if (!Has(slot))
                return false;
            if (Current != slot)
            {
                Current = slot;
                Changed?.Invoke();
            }
            return true;
        }

        /// <summary>Selects by key: 1 to 4.</summary>
        public bool SelectNumber(int number) => number >= 1 && number <= SlotCount && Select((WeaponSlot)(number - 1));

        /// <summary>
        /// The next held slot after the current one in <paramref name="direction"/> (positive: towards 4, the scroll wheel down),
        /// going round; the current one when nothing else is held.
        /// </summary>
        public WeaponSlot Next(int direction)
        {
            var step = direction >= 0 ? 1 : -1;
            var slot = (int)Current;
            for (var i = 0; i < SlotCount; i++)
            {
                slot = (slot + step + SlotCount) % SlotCount;
                if (slots[slot] != null)
                    return (WeaponSlot)slot;
            }
            return Current;
        }

        /// <summary>Selects the next held weapon in <paramref name="direction"/>; returns whether the weapon in hand changed.</summary>
        public bool Scroll(int direction)
        {
            if (direction == 0)
                return false;
            var next = Next(direction);
            if (next == Current)
                return false;
            return Select(next);
        }
    }
}
