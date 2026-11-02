using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// What a piece of a burst head is, for how it flies and what it collides as (ticket 51: only heads burst, so there is one kind; the
    /// body has none). Ticket 46's per-head debris adds its own kinds here.
    /// </summary>
    public enum GibKind
    {
        /// <summary>A piece of the monster head, cut from its own mesh: flies up and away, tumbles, then lies.</summary>
        Piece,
    }

    /// <summary>
    /// The numbers of a monster head's death burst (the gore, ticket 51), apart from the scene so they can be tested: whether a kill bursts
    /// the head with pieces, how hard each piece is thrown and which way (away from the hit, up, harder for a cannon, highest for a
    /// headshot), how much blood flies, and the blood's colour from the head (<see cref="BloodOf"/>, the goop models' shades turned to it
    /// by <see cref="Tint"/>). The human body never bursts or bleeds at any level (<see cref="BodyBursts"/>): it ragdolls. PEGI 12.
    /// Levels are the settings' (SdfGui.GuiSettings.Gore): 0 off (a cartoon puff in the blood colour), 1 splatty, 2 extra splatty, 3 ultra gore.
    /// </summary>
    public static class GoreRules
    {
        public const int Off = 0, Splatty = 1, ExtraSplatty = 2, UltraGore = 3;

        /// <summary>Corpses lying about at once, at most; the oldest goes first.</summary>
        public const int MaxCorpses = 6;

        /// <summary>The team colours of the two sides (BattleSide.ColorOf), which no blood may be mistaken for.</summary>
        public static readonly Color TeamRed = new Color(0.85f, 0.12f, 0.1f), TeamBlue = new Color(0.15f, 0.4f, 0.9f);

        /// <summary>Whether a kill bursts the head into pieces (gore on); at Off the head pops in a puff, a fall just vanishes.</summary>
        public static bool Bursts(int level, DeathCause cause) => level > Off && cause == DeathCause.Killed;

        /// <summary>Whether a kill pops the head in a cartoon puff of its blood colour instead of pieces: a kill with gore off.</summary>
        public static bool Puffs(int level, DeathCause cause) => level <= Off && cause == DeathCause.Killed;

        /// <summary>The human body never bursts or bleeds, at any gore level (it ragdolls: <see cref="Ragdolls"/>).</summary>
        public static bool BodyBursts(int level) => false;

        /// <summary>Whether a death leaves a ragdoll: every kill, at every level (a fall or a rule removes the body).</summary>
        public static bool Ragdolls(DeathCause cause) => cause == DeathCause.Killed;

        /// <summary>The blood colour of a head by id (the <see cref="SdfHeads.MonsterHeads"/> table), a neutral orange for an unknown one.</summary>
        public static Color BloodOf(string headId) =>
            SdfHeads.MonsterHeads.ById(headId) is { } head ? head.Blood : new Color(1f, 0.5f, 0f, 1f);

        /// <summary>Seconds a ragdoll simulates at most before it lies still (and sooner, once it has settled).</summary>
        public const float RagdollSeconds = 3f;

        /// <summary>Speed (metres per second) under which every body of a ragdoll counts as settled, for <see cref="RagdollSettleSeconds"/>.</summary>
        public const float RagdollSettleSpeed = 0.15f;

        public const float RagdollSettleSeconds = 0.4f;

        /// <summary>The push of the hit on the ragdoll, metres per second at the chest: the weapon's <see cref="Strength"/> times this.</summary>
        public const float RagdollPush = 3.2f;

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
        /// a random vector in the unit ball, <paramref name="speed01"/> a random 0 to 1 for the speed within the range. With
        /// <paramref name="fling"/> (ultra gore) every piece really flies: faster and higher. A headshot pops the pieces higher.
        /// </summary>
        public static Vector3 Launch(GibKind kind, Vector3 outward, Vector3 away, bool headshot, float strength, Vector3 jitter, float speed01,
            bool fling = false)
        {
            outward.y = 0;
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.zero;
            away.y = 0;
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.zero;
            var (up, push, low, high) = headshot ? (1.7f, 1.9f, 5.5f, 8f) : (1.0f, 1.6f, 3.5f, 6f);
            if (fling)
                (up, low, high) = (up + 0.4f, low * 1.6f, high * 1.6f);
            var direction = outward + away * push + Vector3.up * up + jitter * 0.35f;
            if (direction.sqrMagnitude < 1e-6f)
                direction = Vector3.up;
            return direction.normalized * (Mathf.Lerp(low, high, Mathf.Clamp01(speed01)) * strength);
        }

        /// <summary>Degrees per second of spin a piece leaves with, at most.</summary>
        public static float Spin(GibKind kind) => 700;

        /// <summary>
        /// A colour of the goop models turned to the <paramref name="blood"/> colour: the blood's own hue and saturation (so charcoal stays
        /// charcoal and ice stays pale), the goo's brightness variation kept as shading (its brightest the blood itself, its darkest half of it). <paramref name="darken"/> scales the result (the flamethrower's charred pieces use less).
        /// </summary>
        public static Color Tint(Color goop, Color blood, float darken = 1)
        {
            Color.RGBToHSV(goop, out _, out _, out var v);
            var shade = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(v));
            return new Color(Mathf.Clamp01(blood.r * shade * darken), Mathf.Clamp01(blood.g * shade * darken), Mathf.Clamp01(blood.b * shade * darken), goop.a);
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
