using System;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The four weapon slots (ticket 22): the pistol, the minigun, the long-range weapons and the short-range ones. Most scenes hold at
    /// most one kind of weapon per slot, so a pickup always has its own slot; where a level's pods hold two of one slot (the chessboard's
    /// sides have the electro-gun and the poison-gun, its neutral pods the sniper and the blob-gun), taking one swaps out the other.
    /// Keys 1 to 4 pick them in this order.
    /// </summary>
    public enum WeaponSlot
    {
        Pistol = 0,
        Minigun = 1,
        LongRange = 2,
        ShortRange = 3,
    }

    /// <summary>How a weapon's shots reach what they hit.</summary>
    public enum FireMode
    {
        /// <summary>A ray from the eye, at once: pistol, minigun, sniper.</summary>
        Hitscan,
        /// <summary>A hitscan bolt that jumps on to a second enemy near the first: the electro-gun.</summary>
        Chain,
        /// <summary>Everything in a cone in front, continuously, while the trigger is held: the flamethrower.</summary>
        Cone,
        /// <summary>A lobbed projectile that bursts into a poison cloud where it lands.</summary>
        Cloud,
        /// <summary>A lobbed blob that hurts and slows whoever it hits and leaves a slowing splat on the ground.</summary>
        Blob,
    }

    /// <summary>The weapons by id: the names of their models (Assets/Mushrooms/&lt;id&gt;.prefab) and of their GUI icons.</summary>
    public static class WeaponIds
    {
        public const string Pistol = "pistol";
        /// <summary>Two pistols in the pistol slot, after picking up a pistol pod; its own numbers, the pistol's model.</summary>
        public const string DualPistols = "dual-pistols";
        public const string Minigun = "minigun";
        public const string Sniper = "sniper";
        public const string ElectroGun = "electro-gun";
        public const string Flamethrower = "flamethrower";
        public const string PoisonGun = "poison-gun";
        public const string BlobGun = "blob-gun";

        /// <summary>The weapons a pod can hold.</summary>
        public static readonly string[] Pickups = { Pistol, Minigun, Sniper, ElectroGun, Flamethrower, PoisonGun, BlobGun };

        /// <summary>The slot a weapon goes in; null for an id that is no weapon.</summary>
        public static WeaponSlot? SlotOf(string id) => id switch
        {
            Pistol or DualPistols => WeaponSlot.Pistol,
            Minigun => WeaponSlot.Minigun,
            Sniper or ElectroGun => WeaponSlot.LongRange,
            Flamethrower or PoisonGun or BlobGun => WeaponSlot.ShortRange,
            _ => null,
        };

        /// <summary>The model and icon a weapon is drawn with: dual pistols are two pistols.</summary>
        public static string ModelOf(string id) => id == DualPistols ? Pistol : id;
    }

    /// <summary>
    /// One weapon's numbers (ticket 22's balance table), tuned in the weapon table asset (Assets/Battle/Resources/Weapons.asset), not in
    /// code. Plain data with the few rules that follow from it (damage over distance, spread while firing), so EditMode tests can check
    /// them.
    /// </summary>
    [Serializable]
    public sealed class WeaponStats
    {
        [Tooltip("The weapon's id: its model's name (pistol, minigun, ...), or dual-pistols")]
        public string id = "";
        [Tooltip("As the HUD writes it")]
        public string displayName = "";
        public WeaponSlot slot;
        public FireMode mode;

        [Header("Firing")]
        [Tooltip("Shots a second; for the cone, how often its damage is dealt")]
        public float shotsPerSecond = 1;
        [Tooltip("Seconds the barrels spin up before the first shot (minigun)")]
        public float spinUp;
        [Tooltip("Seconds it can fire on end before it stops and spins down (minigun); 0 for no limit")]
        public float burstSeconds;
        [Tooltip("Seconds the barrels take to spin down, after the burst or when let go; it fires again only once spun up anew")]
        public float spinDown;
        [Tooltip("Seconds after switching to it before it fires")]
        public float drawSeconds = 0.25f;

        [Header("Damage")]
        [Tooltip("Per shot; per second for the cone (flamethrower) and inside the cloud (poison-gun)")]
        public float damage = 10;
        [Tooltip("A shot to the head (the cap) instead; 0 for no headshots")]
        public float headDamage;
        [Tooltip("Metres up to which a shot does its full damage")]
        public float falloffStart = 1000;
        [Tooltip("Metres at which the damage has fallen to the minimum share; it stays there beyond")]
        public float falloffEnd = 1000;
        [Tooltip("The share of the damage left at and beyond the end of the falloff")]
        [Range(0, 1)] public float falloffMin = 1;
        [Tooltip("Metres a shot reaches; nothing beyond")]
        public float range = 100;

        [Header("Spread (degrees off the aim, at most)")]
        public float spread;
        [Tooltip("Degrees the spread grows per second of firing (minigun)")]
        public float spreadGrowth;
        [Tooltip("The most the spread grows to")]
        public float spreadMax;
        [Tooltip("The spread while zoomed in (sniper)")]
        public float zoomedSpread;
        [Tooltip("The camera's field of view zoomed in with the right mouse button; 0 for no zoom")]
        public float zoomFieldOfView;

        [Header("Electro-gun: the jump")]
        [Tooltip("Metres from the first hit a second enemy can be to take the jump; 0 for none")]
        public float chainRange;
        [Tooltip("Damage of each jump")]
        public float chainDamage;
        [Tooltip("How many more enemies the bolt jumps on to, each from the last one hit")]
        public int chainJumps = 2;

        [Header("Flamethrower: the cone; the burn and the afterpoison")]
        [Tooltip("Half the cone's angle, degrees")]
        public float coneAngle;
        [Tooltip("Damage a second after leaving the flame (burning) or the cloud (afterpoison)")]
        public float burnPerSecond;
        [Tooltip("Seconds the burn or the afterpoison lasts after leaving the flame or the cloud")]
        public float burnSeconds;

        [Header("Lobbed: poison cloud and blob")]
        [Tooltip("Metres a second the projectile leaves at")]
        public float projectileSpeed;
        [Tooltip("Degrees above the aim it leaves at, so a level aim still arcs")]
        public float projectileLift;
        public float projectileGravity = 9.8f;
        [Tooltip("The cloud's or the splat's radius, metres")]
        public float areaRadius;
        [Tooltip("Seconds the cloud or the splat lasts")]
        public float areaSeconds;
        [Tooltip("The share of speed a blob hit, the splat or the cloud takes away")]
        [Range(0, 1)] public float slow;
        [Tooltip("Seconds a blob hit slows; the cloud's slow, after leaving it")]
        public float slowSeconds;

        /// <summary>Seconds between shots.</summary>
        public float Interval => 1 / Mathf.Max(0.01f, shotsPerSecond);

        /// <summary>A shot's damage at <paramref name="distance"/> metres: full up to the falloff's start, then down to its minimum share.</summary>
        public float DamageAt(float distance, bool head = false)
        {
            var full = head && headDamage > 0 ? headDamage : damage;
            return full * Falloff(distance);
        }

        /// <summary>The share of the damage left at <paramref name="distance"/>: 1 up to the falloff's start, the minimum from its end.</summary>
        public float Falloff(float distance)
        {
            if (distance <= falloffStart)
                return 1;
            if (distance >= falloffEnd || falloffEnd <= falloffStart)
                return falloffMin;
            return Mathf.Lerp(1, falloffMin, (distance - falloffStart) / (falloffEnd - falloffStart));
        }

        /// <summary>The spread after <paramref name="firingSeconds"/> of firing, in degrees; zoomed in, the zoomed spread.</summary>
        public float SpreadAfter(float firingSeconds, bool zoomed = false)
        {
            if (zoomed && zoomFieldOfView > 0)
                return zoomedSpread;
            if (spreadGrowth <= 0)
                return spread;
            return Mathf.Min(Mathf.Max(spread, spreadMax), spread + spreadGrowth * Mathf.Max(0, firingSeconds));
        }

        /// <summary>The damage of one shot of the cone: its damage a second shared over its shots.</summary>
        public float ConeDamagePerShot => damage / Mathf.Max(0.01f, shotsPerSecond);

        public WeaponStats Clone() => (WeaponStats)MemberwiseClone();
    }

    /// <summary>
    /// Every weapon's numbers, one entry per id; the weapon table asset holds one, and <see cref="Default"/> is the first balance
    /// (ticket 22) the asset starts from and the tests check.
    /// </summary>
    [Serializable]
    public sealed class WeaponTable
    {
        [Tooltip("Metres from a player's feet above which a hit on the body counts as the head (the cap)")]
        public float headHeight = 1.2f;
        [Tooltip("What a health pod gives: one health per point up to the most (100), half a health per point above it")]
        public float healthPack = 40;
        [Tooltip("The most a health pod heals to, over the most a player spawns with")]
        public float overheal = 200;
        public WeaponStats[] weapons = Array.Empty<WeaponStats>();

        /// <summary>The weapon with this id; null when there is none.</summary>
        public WeaponStats Get(string id)
        {
            if (weapons == null)
                return null;
            foreach (var weapon in weapons)
                if (weapon != null && weapon.id == id)
                    return weapon;
            return null;
        }

        /// <summary>The first balance, from ticket 22's table.</summary>
        public static WeaponTable Default() => new WeaponTable
        {
            weapons = new[]
            {
                // The fallback everyone has: weaker than everything else
                new WeaponStats
                {
                    id = WeaponIds.Pistol, displayName = "Pistol", slot = WeaponSlot.Pistol, mode = FireMode.Hitscan,
                    shotsPerSecond = 2.5f, damage = 12, falloffStart = 20, falloffEnd = 40, falloffMin = 0.5f, range = 120, spread = 0.4f,
                },
                // Close to mid fights
                new WeaponStats
                {
                    id = WeaponIds.DualPistols, displayName = "Dual Pistols", slot = WeaponSlot.Pistol, mode = FireMode.Hitscan,
                    shotsPerSecond = 5, damage = 12, falloffStart = 20, falloffEnd = 40, falloffMin = 0.5f, range = 120, spread = 1.2f,
                },
                // Mid range, holding a choke point: spins up, then sprays wider the longer it fires
                new WeaponStats
                {
                    id = WeaponIds.Minigun, displayName = "Minigun", slot = WeaponSlot.Minigun, mode = FireMode.Hitscan,
                    shotsPerSecond = 14, spinUp = 0.6f, burstSeconds = 3, spinDown = 0.6f, drawSeconds = 0.4f, damage = 7, falloffStart = 35, falloffEnd = 60, falloffMin = 0.4f,
                    range = 120, spread = 0.8f, spreadGrowth = 1.5f, spreadMax = 4.5f,
                },
                // Long range, picking off the carrier: zoom in to hit anything
                new WeaponStats
                {
                    id = WeaponIds.Sniper, displayName = "Sniper", slot = WeaponSlot.LongRange, mode = FireMode.Hitscan,
                    shotsPerSecond = 1 / 1.4f, drawSeconds = 0.4f, damage = 85, headDamage = 120, range = 250, spread = 5,
                    zoomedSpread = 0, zoomFieldOfView = 20,
                },
                // Long range, groups
                new WeaponStats
                {
                    id = WeaponIds.ElectroGun, displayName = "Electro Gun", slot = WeaponSlot.LongRange, mode = FireMode.Chain,
                    shotsPerSecond = 1.2f, drawSeconds = 0.3f, damage = 40, range = 60, spread = 0.2f, chainRange = 6, chainDamage = 20, chainJumps = 2,
                },
                // Very close: rooms and stairs
                new WeaponStats
                {
                    id = WeaponIds.Flamethrower, displayName = "Flamethrower", slot = WeaponSlot.ShortRange, mode = FireMode.Cone,
                    shotsPerSecond = 10, drawSeconds = 0.3f, damage = 70, range = 8, coneAngle = 14, burnPerSecond = 10, burnSeconds = 2,
                },
                // Area denial, defending the flag: a lobbed cloud
                new WeaponStats
                {
                    id = WeaponIds.PoisonGun, displayName = "Poison Gun", slot = WeaponSlot.ShortRange, mode = FireMode.Cloud,
                    shotsPerSecond = 0.8f, drawSeconds = 0.3f, damage = 18, range = 30, projectileSpeed = 17, projectileLift = 8,
                    projectileGravity = 9.8f, areaRadius = 3, areaSeconds = 5, burnPerSecond = 3, burnSeconds = 10, slow = 0.25f, slowSeconds = 10,
                },
                // Stopping the flag carrier: a lobbed blob that slows, and a splat that slows who walks in
                new WeaponStats
                {
                    id = WeaponIds.BlobGun, displayName = "Blob Gun", slot = WeaponSlot.ShortRange, mode = FireMode.Blob,
                    shotsPerSecond = 1.25f, drawSeconds = 0.3f, damage = 35, range = 40, spread = 1, projectileSpeed = 22, projectileLift = 5,
                    projectileGravity = 12, areaRadius = 1.2f, areaSeconds = 5, slow = 0.4f, slowSeconds = 2,
                },
            },
        };
    }
}
