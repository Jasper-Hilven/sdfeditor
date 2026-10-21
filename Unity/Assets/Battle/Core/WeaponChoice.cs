using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// Which weapon a bot takes in hand for a fight (ticket 23), from ticket 22's "good for": how well each suits the distance to the
    /// target and the situation, 0 (useless) to about 1 (what it is made for). The flamethrower for very close, rooms and stairs; dual
    /// pistols close to mid; the minigun mid range; the sniper and the electro-gun long range, the electro-gun more so on a group; the
    /// poison-gun mid range, more so defending; the blob-gun to stop a flag carrier; the single pistol is never better than the fallback.
    /// </summary>
    public static class WeaponChoice
    {
        /// <summary>What the choice depends on.</summary>
        public struct Context
        {
            /// <summary>Metres to the target (or to where fights are expected).</summary>
            public float distance;
            /// <summary>The target carries a flag (ours): slow it, pick it off.</summary>
            public bool targetIsCarrier;
            /// <summary>Other enemies within a few metres of the target (the electro-gun's jumps, the cloud).</summary>
            public int groupedWithTarget;
            /// <summary>The bot is guarding its own flag.</summary>
            public bool defending;

            public Context(float distance, bool targetIsCarrier = false, int groupedWithTarget = 0, bool defending = false)
            {
                this.distance = distance;
                this.targetIsCarrier = targetIsCarrier;
                this.groupedWithTarget = groupedWithTarget;
                this.defending = defending;
            }
        }

        /// <summary>How much better another weapon must suit before a bot switches to it (switching costs the draw time).</summary>
        public const float SwitchMargin = 0.15f;

        /// <summary>How well a weapon suits the context; 0 for one that cannot reach.</summary>
        public static float Suitability(string weapon, in Context c)
        {
            var d = Mathf.Max(0, c.distance);
            var grouped = Mathf.Min(2, c.groupedWithTarget);
            switch (weapon)
            {
                case WeaponIds.Pistol:
                    return Falling(d, 20, 60, 0.3f, 0.1f);
                case WeaponIds.DualPistols:
                    return Falling(d, 20, 45, 0.65f, 0.2f);
                case WeaponIds.Minigun:
                    return d < 10 ? Mathf.Lerp(0.55f, 0.95f, d / 10) : Falling(d, 35, 70, 0.95f, 0.3f);
                case WeaponIds.Sniper:
                    return Rising(d, 12, 35, 0.15f, 1.0f) + (c.targetIsCarrier ? 0.2f : 0);
                case WeaponIds.ElectroGun:
                    if (d > 60)
                        return 0;
                    return (d < 15 ? Mathf.Lerp(0.4f, 0.9f, d / 15) : Falling(d, 50, 60, 0.9f, 0.5f)) + 0.15f * grouped;
                case WeaponIds.Flamethrower:
                    return d <= 6 ? 1.1f : d <= 8 ? Mathf.Lerp(1.1f, 0.5f, (d - 6) / 2) : 0;
                case WeaponIds.PoisonGun:
                    if (d > 30)
                        return 0;
                    return (d < 8 ? Mathf.Lerp(0.3f, 0.75f, d / 8) : Falling(d, 24, 30, 0.75f, 0.4f)) + (c.defending ? 0.2f : 0) + 0.1f * grouped;
                case WeaponIds.BlobGun:
                    if (d > 38)
                        return 0;
                    return (d < 5 ? Mathf.Lerp(0.4f, 0.6f, d / 5) : Falling(d, 30, 38, 0.6f, 0.3f)) + (c.targetIsCarrier ? 0.6f : 0);
                default:
                    return 0;
            }
        }

        /// <summary>The held weapon that suits best; the current one unless another beats it by <see cref="SwitchMargin"/>.</summary>
        public static WeaponSlot Best(Inventory inventory, in Context c)
        {
            var current = inventory.Current;
            var best = current;
            var bestScore = Suitability(inventory.WeaponIn(current), c) + SwitchMargin;
            for (var i = 0; i < Inventory.SlotCount; i++)
            {
                var slot = (WeaponSlot)i;
                if (slot == current || !inventory.Has(slot))
                    continue;
                var score = Suitability(inventory.WeaponIn(slot), c);
                if (score > bestScore)
                    (best, bestScore) = (slot, score);
            }
            return best;
        }

        /// <summary>The distance a bot holding this weapon likes to fight at, metres.</summary>
        public static float PreferredDistance(string weapon) => weapon switch
        {
            WeaponIds.Flamethrower => 4,
            WeaponIds.BlobGun => 14,
            WeaponIds.PoisonGun => 16,
            WeaponIds.DualPistols => 12,
            WeaponIds.Minigun => 20,
            WeaponIds.ElectroGun => 25,
            WeaponIds.Sniper => 40,
            _ => 12,
        };

        private static float Falling(float d, float full, float end, float high, float low) =>
            d <= full ? high : d >= end ? low : Mathf.Lerp(high, low, (d - full) / (end - full));

        private static float Rising(float d, float start, float full, float low, float high) =>
            d <= start ? low : d >= full ? high : Mathf.Lerp(low, high, (d - start) / (full - start));
    }
}
