using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>What a piece of a burst scout is, for how it flies and what it collides as.</summary>
    public enum GibKind
    {
        /// <summary>A floating eye or hand: pops off hardest and rolls (a ball, little friction).</summary>
        Loose,
        /// <summary>The cap, broken off the stem: flies up and tumbles (boxes, more friction).</summary>
        Cap,
        /// <summary>A piece of the stem or its bulb: slumps and topples near the spot.</summary>
        Stem,
    }

    /// <summary>
    /// The numbers of a scout's death burst (the gore), apart from the scene so they can be tested: whether a death bursts at all, how
    /// hard each piece is thrown and which way (away from the hit, up, harder for a cannon, the cap and eyes highest for a headshot),
    /// how much blood flies, and the blood's colour from the team's (<see cref="Recolour"/>, the goop models' colours turned to it).
    /// Levels are the settings' (SdfGui.GuiSettings.Gore): 0 off, 1 splatty, 2 extra splatty, 3 ultra gore.
    /// </summary>
    public static class GoreRules
    {
        public const int Off = 0, Splatty = 1, ExtraSplatty = 2, UltraGore = 3;

        /// <summary>Corpses lying about at once, at most; the oldest goes first.</summary>
        public const int MaxCorpses = 6;

        /// <summary>Whether a death bursts: a kill with gore on; a fall (the scout is gone below the island) or gore off just vanishes.</summary>
        public static bool Bursts(int level, DeathCause cause) => level > Off && cause == DeathCause.Killed;

        /// <summary>How hard the burst is: a cannon's (a machine's, ticket 24) much harder, the sniper's a little.</summary>
        public static float Strength(string weapon) =>
            weapon == "cannon" ? 1.6f
            : weapon == WeaponIds.Sniper ? 1.15f
            : 1;

        /// <summary>Whether every piece flies far, trailing blood all the way (<see cref="Launch"/>'s <c>fling</c>): ultra gore.</summary>
        public static bool Flings(int level) => level >= UltraGore;

        /// <summary>
        /// Gobs of blood thrown out: lots, more at extra splatty, three times splatty's at ultra gore, more for a cannon, fewer for the
        /// flamethrower (it cooks).
        /// </summary>
        public static int BloodGobs(int level, string weapon)
        {
            if (level <= Off)
                return 0;
            var gobs = level >= UltraGore ? 28f * 3 : level >= ExtraSplatty ? 40f : 28f;
            if (weapon == WeaponIds.Flamethrower)
                gobs *= 0.4f;
            else if (weapon == "cannon")
                gobs *= 1.25f;
            return Mathf.RoundToInt(gobs);
        }

        /// <summary>
        /// The velocity a piece leaves with, metres per second. <paramref name="outward"/> is from the burst's middle to the piece's,
        /// <paramref name="away"/> the way the hit pushed (from the killer through the victim; zero when unknown), <paramref name="jitter"/>
        /// a random vector in the unit ball, <paramref name="speed01"/> a random 0 to 1 for the speed within the piece's range. With
        /// <paramref name="fling"/> (ultra gore) every piece really flies: the stem's pieces as far as a cap, all of them faster and higher.
        /// </summary>
        public static Vector3 Launch(GibKind kind, Vector3 outward, Vector3 away, bool headshot, float strength, Vector3 jitter, float speed01,
            bool fling = false)
        {
            outward.y = 0;
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.zero;
            away.y = 0;
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.zero;
            float up, push, low, high;
            switch (kind)
            {
                case GibKind.Loose:
                    (up, push, low, high) = (headshot ? 1.3f : 0.9f, 1.2f, 5f, 7.5f);
                    if (headshot)
                        (low, high) = (6.5f, 9f);
                    break;
                case GibKind.Cap:
                    (up, push, low, high) = (headshot ? 2.2f : 1.0f, 0.6f, headshot ? 6f : 3f, headshot ? 8f : 4.5f);
                    break;
                default:
                    (up, push, low, high) = fling ? (1.0f, 0.8f, 3.5f, 5.5f) : (0.25f, 0.5f, 0.6f, 1.6f);
                    break;
            }
            if (fling)
                (up, low, high) = (up + 0.4f, low * 1.6f, high * 1.6f);
            var direction = outward + away * push + Vector3.up * up + jitter * 0.35f;
            if (direction.sqrMagnitude < 1e-6f)
                direction = Vector3.up;
            return direction.normalized * (Mathf.Lerp(low, high, Mathf.Clamp01(speed01)) * strength);
        }

        /// <summary>Degrees per second of spin a piece leaves with, at most: the eyes and hands spin hardest.</summary>
        public static float Spin(GibKind kind) => kind == GibKind.Loose ? 900 : kind == GibKind.Cap ? 500 : 200;

        /// <summary>
        /// A colour of the goop models turned to <paramref name="hue"/> (0 to 1, the team's), keeping its saturation and brightness: the
        /// same glossy toon goo, in the team's colour. <paramref name="darken"/> scales the brightness (1 keeps it; the flamethrower's
        /// charred pieces use less).
        /// </summary>
        public static Color Recolour(Color colour, float? hue, float darken = 1)
        {
            Color.RGBToHSV(colour, out var h, out var s, out var v);
            var result = Color.HSVToRGB(hue ?? h, s, v * darken, true);
            result.a = colour.a;
            return result;
        }
    }

    /// <summary>
    /// The corpses lying about, oldest first, at most <see cref="Limit"/>: adding one past the limit hands back the oldest, to be
    /// cleared away.
    /// </summary>
    public sealed class CorpseBudget<T> where T : class
    {
        private readonly LinkedList<T> corpses = new LinkedList<T>();

        public CorpseBudget(int limit = GoreRules.MaxCorpses) => Limit = Mathf.Max(1, limit);

        public int Limit { get; }

        public int Count => corpses.Count;

        /// <summary>The corpses counted, oldest first.</summary>
        public IEnumerable<T> All => corpses;

        /// <summary>Adds a corpse; returns the oldest when that made one too many (it is no longer counted), else null.</summary>
        public T Add(T corpse)
        {
            corpses.AddLast(corpse);
            if (corpses.Count <= Limit)
                return null;
            var oldest = corpses.First.Value;
            corpses.RemoveFirst();
            return oldest;
        }

        /// <summary>A corpse is gone of itself (it shrank away).</summary>
        public void Remove(T corpse) => corpses.Remove(corpse);
    }
}
