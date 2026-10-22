using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SdfBattle
{
    /// <summary>What a death is, for the lines that tell of it: the weapon, a headshot, a machine, a fall, a revenge or a streak.</summary>
    public enum DeathKind
    {
        Pistol,
        DualPistols,
        Minigun,
        Sniper,
        ElectroGun,
        Flamethrower,
        PoisonGun,
        BlobGun,
        /// <summary>The killing shot hit the cap.</summary>
        Headshot,
        Tank,
        Helicopter,
        Turret,
        /// <summary>A machine that is none of the three, or its cannon by name.</summary>
        Cannon,
        /// <summary>A hazard of the level, with no team and no player behind it (ticket 27.10's toy train).</summary>
        Hazard,
        /// <summary>Burnt by a hot patch of the level (ticket 28.10's hob): no weapon, team or player, named by the patch ("the hob").</summary>
        Heat,
        /// <summary>Off the island, or out of the world.</summary>
        Fall,
        /// <summary>The killer got back at the one who last killed them.</summary>
        Revenge,
        /// <summary>The killer's kills in a row without dying reached a round number (3, 5, 10, 15, ...).</summary>
        Streak,
        /// <summary>Any kill: the fallback, and now and then mixed in with a weapon's lines.</summary>
        Generic,
    }

    /// <summary>Where a line is shown.</summary>
    public enum DeathText
    {
        /// <summary>The kill feed: "{killer} sautéed {icon} {victim}"; the text before {icon} goes left of the weapon icon, the rest right.</summary>
        Feed,
        /// <summary>The death screen's (and the elimination pop-up's) big word: "SAUTÉED!".</summary>
        Title,
        /// <summary>The death screen's two short lines under it: "by {killer}'s\nflamethrower".</summary>
        Detail,
    }

    /// <summary>One line, and the gore level it needs (0 for any, so also with gore off).</summary>
    public readonly struct DeathLine
    {
        public DeathLine(string text, int gore = GoreRules.Off)
        {
            Text = text;
            Gore = gore;
        }

        public string Text { get; }

        /// <summary>The lowest gore level (<see cref="GoreRules"/>) this line may show at.</summary>
        public int Gore { get; }

        public override string ToString() => Text;
    }

    /// <summary>
    /// The lines the HUD tells deaths with (the kill feed, the death screen, the elimination pop-up), many per kind of death, picked at
    /// random without repeating the last few of each pool, and only those the gore level allows. Placeholders: {killer}, {victim},
    /// {weapon} (its name, <see cref="WeaponName"/>), {streak} (the killer's kills in a row) and, in the feed, {icon} (where the weapon
    /// icon sits). Plain C#, so EditMode tests can check every line.
    /// </summary>
    public sealed class DeathLines
    {
        /// <summary>Picks of a pool that are not repeated (fewer when the pool has fewer lines to choose from).</summary>
        public const int Recent = 4;
        /// <summary>The longest title, in characters: the death screen's box fits "SPLATTERED!" at its size.</summary>
        public const int MaxTitle = 12;
        /// <summary>The longest feed phrase, without the names: the feed entry has room for about 38 characters with both names.</summary>
        public const int MaxFeedPhrase = 20;
        /// <summary>Characters a feed entry fits, the names included; a line that would not fit gives way to a shorter one.</summary>
        public const int FeedRoom = 38;
        /// <summary>The longest line of a detail, with a ten-letter name for {killer}: the box between the avatar and the icon is narrow.</summary>
        public const int MaxDetailLine = 22;

        /// <summary>The feed's line when no other fits: the names around the weapon's icon.</summary>
        public const string PlainFeed = "{killer} {icon} {victim}";
        public const string PlainFallFeed = "{victim} fell";
        /// <summary>The feed's line for a burn when no other fits ("Red 2 burned on the hob").</summary>
        public const string PlainHeatFeed = "{victim} burned on {killer}";

        /// <summary>The placeholders a line may hold.</summary>
        public static readonly string[] Placeholders = { "{killer}", "{victim}", "{weapon}", "{streak}", "{icon}" };

        /// <summary>The picker the HUD shares.</summary>
        public static readonly DeathLines Shared = new DeathLines();

        private readonly Random random;
        private readonly Dictionary<(DeathKind, DeathText), List<int>> recent = new Dictionary<(DeathKind, DeathText), List<int>>();

        public DeathLines(int? seed = null) => random = seed is { } s ? new Random(s) : new Random();

        /// <summary>The chance a weapon's or a headshot's kill takes a generic line instead, for more variety.</summary>
        public float GenericShare { get; set; } = 0.2f;

        public string Feed(DeathKind kind, int gore, int namesLength = 0) => Pick(kind, DeathText.Feed, gore, FeedRoom - namesLength);

        public string Title(DeathKind kind, int gore) => Pick(kind, DeathText.Title, gore);

        public string Detail(DeathKind kind, int gore) => Pick(kind, DeathText.Detail, gore);

        /// <summary>
        /// A line for <paramref name="kind"/> at <paramref name="gore"/>, none of the pool's last <see cref="Recent"/>; for the feed only
        /// those whose phrase fits in <paramref name="room"/> characters, else <see cref="PlainFeed"/>.
        /// </summary>
        public string Pick(DeathKind kind, DeathText text, int gore, int room = int.MaxValue)
        {
            if (IsWeapon(kind) && GenericShare > 0 && random.NextDouble() < GenericShare)
                kind = DeathKind.Generic;
            var lines = Lines(kind, text);
            var candidates = new List<int>();
            for (var i = 0; i < lines.Count; i++)
                if (lines[i].Gore <= gore && (text != DeathText.Feed || PhraseLength(lines[i].Text) <= room))
                    candidates.Add(i);
            if (candidates.Count == 0)
                return text == DeathText.Feed ? kind == DeathKind.Fall ? PlainFallFeed : kind == DeathKind.Heat ? PlainHeatFeed : PlainFeed : lines.Count > 0 ? lines[0].Text : "";

            if (!recent.TryGetValue((kind, text), out var last))
                recent[(kind, text)] = last = new List<int>();
            // Leave out the last picks, as many as leaves one to choose
            var skip = Math.Min(Recent, candidates.Count - 1);
            var fresh = new List<int>(candidates.Count);
            foreach (var i in candidates)
            {
                var at = last.LastIndexOf(i);
                if (at < 0 || at < last.Count - skip)
                    fresh.Add(i);
            }
            var pick = fresh[random.Next(fresh.Count)];
            last.Add(pick);
            if (last.Count > Recent)
                last.RemoveAt(0);
            return lines[pick].Text;
        }

        /// <summary>Forgets the recent picks.</summary>
        public void Clear() => recent.Clear();

        /// <summary>A weapon's kind (a headshot's too): those mix in generic lines.</summary>
        public static bool IsWeapon(DeathKind kind) => kind <= DeathKind.Headshot;

        /// <summary>The lines of one pool, gory or not.</summary>
        public static IReadOnlyList<DeathLine> Lines(DeathKind kind, DeathText text) =>
            Library.TryGetValue((kind, text), out var lines) ? lines : Array.Empty<DeathLine>();

        /// <summary>
        /// What kind of death this is: a fall, a machine's (by its name: "Blue tank"), a revenge or a streak's (from
        /// <see cref="KillMemory"/>), a headshot, else the weapon's.
        /// </summary>
        public static DeathKind KindOf(Death death, bool revenge = false, int streak = 0)
        {
            if (death.Cause == DeathCause.Fell)
                return DeathKind.Fall;
            if (death.Heat)
                return DeathKind.Heat;
            if (death.Killer == null)
            {
                var name = death.KillerName?.ToLowerInvariant() ?? "";
                // A hazard has a name and no team ("Toy train"); a machine has both
                if (death.KillerTeam == null && name.Length > 0)
                    return DeathKind.Hazard;
                return name.EndsWith("tank") ? DeathKind.Tank
                    : name.EndsWith("helicopter") ? DeathKind.Helicopter
                    : name.EndsWith("turret") ? DeathKind.Turret
                    : name.Length > 0 ? DeathKind.Cannon
                    : DeathKind.Generic;
            }
            if (revenge)
                return DeathKind.Revenge;
            if (IsStreak(streak))
                return DeathKind.Streak;
            if (death.Headshot)
                return DeathKind.Headshot;
            return KindOfWeapon(death.WeaponId);
        }

        /// <summary>The streaks the feed calls out: 3, 5, then every 5 from 10.</summary>
        public static bool IsStreak(int streak) => streak == 3 || streak == 5 || (streak >= 10 && streak % 5 == 0);

        /// <summary>The kind of a weapon's kill, by its id; generic for an id that is no weapon.</summary>
        public static DeathKind KindOfWeapon(string id) => id switch
        {
            WeaponIds.Pistol => DeathKind.Pistol,
            WeaponIds.DualPistols => DeathKind.DualPistols,
            WeaponIds.Minigun => DeathKind.Minigun,
            WeaponIds.Sniper => DeathKind.Sniper,
            WeaponIds.ElectroGun => DeathKind.ElectroGun,
            WeaponIds.Flamethrower => DeathKind.Flamethrower,
            WeaponIds.PoisonGun => DeathKind.PoisonGun,
            WeaponIds.BlobGun => DeathKind.BlobGun,
            "cannon" => DeathKind.Cannon,
            _ => DeathKind.Generic,
        };

        /// <summary>A weapon's name as a line writes it ("poison gun").</summary>
        public static string WeaponName(string id) => id switch
        {
            WeaponIds.DualPistols => "dual pistols",
            WeaponIds.PoisonGun => "poison gun",
            WeaponIds.BlobGun => "blob gun",
            null or "" => "something",
            _ => id,
        };

        /// <summary>Fills a line's placeholders; a missing name leaves its placeholder out.</summary>
        public static string Format(string line, string killer = null, string victim = null, string weapon = null, int streak = 0)
        {
            var text = new StringBuilder(line ?? "");
            text.Replace("{killer}", killer ?? "");
            text.Replace("{victim}", victim ?? "");
            text.Replace("{weapon}", weapon ?? "");
            text.Replace("{streak}", streak.ToString());
            return text.ToString();
        }

        /// <summary>A formatted feed line split at its {icon}: the text left of the weapon icon and right of it, trimmed.</summary>
        public static (string before, string after) SplitAtIcon(string line)
        {
            line ??= "";
            var at = line.IndexOf("{icon}", StringComparison.Ordinal);
            return at < 0 ? (line.Trim(), "") : (line.Substring(0, at).Trim(), line.Substring(at + "{icon}".Length).Trim());
        }

        /// <summary>The characters of a feed line without its names and icon (a streak counts as two digits).</summary>
        public static int PhraseLength(string line)
        {
            var text = Format(line, streak: 10).Replace("{icon}", " ");
            return Regex.Replace(text, @"\s+", " ").Trim().Length;
        }

        /// <summary>The placeholders in a line that are none of <see cref="Placeholders"/>.</summary>
        public static IEnumerable<string> UnknownPlaceholders(string line)
        {
            foreach (System.Text.RegularExpressions.Match match in Regex.Matches(line ?? "", @"\{[^}]*\}|[{}]"))
                if (Array.IndexOf(Placeholders, match.Value) < 0)
                    yield return match.Value;
        }

        // ---------------------------------------------------------------- The lines

        private static DeathLine L(string text, int gore = GoreRules.Off) => new DeathLine(text, gore);

        private const int Gory = GoreRules.Splatty, Ultra = GoreRules.UltraGore;

        private static readonly Dictionary<(DeathKind, DeathText), DeathLine[]> Library = new Dictionary<(DeathKind, DeathText), DeathLine[]>
        {
            // Pistol
            [(DeathKind.Pistol, DeathText.Feed)] = new[]
            {
                L("{killer} plinked {icon} {victim}"),
                L("{killer} {icon} tapped {victim}'s cap"),
                L("{victim} got pew-pewed {icon} by {killer}"),
                L("{killer} {icon} popped {victim}"),
                L("{killer} {icon} button-mushroomed {victim}"),
                L("{killer} picked off {icon} {victim}"),
                L("{victim} got peppered {icon} by {killer}"),
                L("{killer} {icon} sent {victim} home"),
                L("{killer} {icon} poked {victim} over"),
                L("{killer} {icon} punched a hole in {victim}", Gory),
            },
            [(DeathKind.Pistol, DeathText.Title)] = new[]
            {
                L("PLINKED!"), L("POPPED!"), L("PICKED OFF!"), L("PEW PEW!"), L("TAPPED!"), L("PEPPERED!"), L("HOLE-PUNCHED", Gory),
            },
            [(DeathKind.Pistol, DeathText.Detail)] = new[]
            {
                L("by {killer}\nand a tiny pistol"),
                L("{killer} plinked\nyou fair and square"),
                L("by {killer}'s\npea shooter"),
                L("{killer} only\nneeded a pistol"),
                L("by {killer}\nwith the pistol"),
                L("{killer} put\nholes in your cap", Gory),
            },

            // Dual pistols
            [(DeathKind.DualPistols, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} double-tapped {victim}"),
                L("{killer} went two-fisted {icon} on {victim}"),
                L("{killer} {icon} doubled down on {victim}"),
                L("{killer} {icon} plinked {victim} twice"),
                L("{victim} got paired up {icon} by {killer}"),
                L("{killer} {icon} gave {victim} two"),
                L("{killer} {icon} double-picked {victim}"),
                L("{killer} {icon} went left-right on {victim}"),
                L("{killer} {icon} perforated {victim}", Gory),
            },
            [(DeathKind.DualPistols, DeathText.Title)] = new[]
            {
                L("DOUBLE TAP!"), L("TWO-FISTED!"), L("PAIRED UP!"), L("TWICE OVER!"), L("DOUBLE PEW!"), L("PERFORATED!", Gory),
            },
            [(DeathKind.DualPistols, DeathText.Detail)] = new[]
            {
                L("by {killer}\nand both pistols"),
                L("{killer} brought\na spare pistol"),
                L("by {killer}\ntimes two"),
                L("{killer} had\na pistol per hand"),
                L("{killer} doubled\nup on you"),
            },

            // Minigun
            [(DeathKind.Minigun, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} shredded {victim}"),
                L("{killer} {icon} mulched {victim}"),
                L("{killer} brrrted {icon} {victim}"),
                L("{victim} got minced {icon} by {killer}"),
                L("{killer} {icon} hosed down {victim}"),
                L("{killer} {icon} diced {victim}"),
                L("{killer} {icon} chopped {victim} fine"),
                L("{killer} spun up {icon} on {victim}"),
                L("{victim} {icon} met {killer}'s minigun"),
                L("{killer} {icon} turned {victim} to mulch"),
                L("{killer} {icon} made spore paste of {victim}", Gory),
            },
            [(DeathKind.Minigun, DeathText.Title)] = new[]
            {
                L("SHREDDED!"), L("MULCHED!"), L("BRRRRT!"), L("MINCED!"), L("DICED!"), L("SPUN OUT!"), L("SPORE PASTE!", Gory),
            },
            [(DeathKind.Minigun, DeathText.Detail)] = new[]
            {
                L("by {killer}\nand a lot of minigun"),
                L("{killer} held\nthe trigger down"),
                L("{killer}'s minigun\ndid not stop"),
                L("by {killer}\nand all 250 spores"),
                L("{killer} spun up.\nYou spun out."),
                L("{killer} made\nmulch of you"),
                L("{killer} turned\nyou into a smoothie", Ultra),
            },

            // Sniper
            [(DeathKind.Sniper, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} picked {victim}"),
                L("{victim} got picked {icon} by {killer}"),
                L("{killer} {icon} spotted {victim} first"),
                L("{killer} {icon} reached out to {victim}"),
                L("{victim} {icon} never saw {killer}"),
                L("{killer} {icon} plucked {victim}"),
                L("{killer} {icon} harvested {victim}"),
                L("{killer} {icon} called dibs on {victim}"),
                L("{killer} {icon} foraged {victim}"),
                L("{killer} {icon} had {victim} in season"),
            },
            [(DeathKind.Sniper, DeathText.Title)] = new[]
            {
                L("PICKED!"), L("HARVESTED!"), L("FORAGED!"), L("PLUCKED!"), L("SCOPED!"), L("IN SEASON!"),
            },
            [(DeathKind.Sniper, DeathText.Detail)] = new[]
            {
                L("by {killer}\nfrom very far away"),
                L("{killer} saw you\nbefore you saw them"),
                L("{killer}'s sniper\nsaid you were ripe"),
                L("picked by {killer}\nfrom across the map"),
                L("by {killer}\nwith the sniper"),
                L("{killer} was\nfar too patient"),
            },

            // Electro-gun
            [(DeathKind.ElectroGun, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} zapped {victim}"),
                L("{killer} {icon} frizzled {victim}'s gills"),
                L("{victim} got sparked {icon} by {killer}"),
                L("{killer} {icon} jolted {victim}"),
                L("{killer} {icon} rewired {victim}"),
                L("{killer} {icon} gave {victim} a buzz"),
                L("{killer} {icon} shorted out {victim}"),
                L("{killer} {icon} lit up {victim}"),
                L("{victim} {icon} conducted {killer}'s bolt"),
                L("{killer} {icon} charged {victim} up"),
            },
            [(DeathKind.ElectroGun, DeathText.Title)] = new[]
            {
                L("ZAPPED!"), L("SPARKED!"), L("JOLTED!"), L("SHORTED OUT"), L("BZZZT!"), L("FRIZZLED!"),
            },
            [(DeathKind.ElectroGun, DeathText.Detail)] = new[]
            {
                L("by {killer}'s\nelectro-gun"),
                L("{killer} put volts\nin your gills"),
                L("you conducted\n{killer}'s bolt"),
                L("{killer} zapped\nyour mycelium"),
                L("gills: frizzy.\nby: {killer}"),
                L("{killer} gave\nyou a big buzz"),
            },

            // Flamethrower
            [(DeathKind.Flamethrower, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} sautéed {victim}"),
                L("{killer} {icon} toasted {victim}"),
                L("{victim} got roasted {icon} by {killer}"),
                L("{killer} {icon} grilled {victim}"),
                L("{killer} {icon} caramelised {victim}"),
                L("{killer} {icon} flambéed {victim}"),
                L("{killer} {icon} browned {victim} nicely"),
                L("{killer} {icon} served {victim} warm"),
                L("{killer} {icon} fried up {victim}"),
                L("{killer} {icon} charred {victim} crispy"),
            },
            [(DeathKind.Flamethrower, DeathText.Title)] = new[]
            {
                L("SAUTÉED!"), L("TOASTED!"), L("ROASTED!"), L("FLAMBÉED!"), L("WELL DONE!"), L("CRISPY!"), L("GRILLED!"),
            },
            [(DeathKind.Flamethrower, DeathText.Detail)] = new[]
            {
                L("by {killer}'s\nflamethrower"),
                L("{killer} cooked you\nin garlic butter"),
                L("{killer} likes\ntheir caps crispy"),
                L("served hot by\n{killer}"),
                L("{killer} turned\nthe heat up"),
                L("{killer} says:\nchef's kiss"),
            },

            // Poison-gun
            [(DeathKind.PoisonGun, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} gassed {victim}"),
                L("{victim} got spored out {icon} by {killer}"),
                L("{killer} {icon} wilted {victim}"),
                L("{victim} {icon} sniffed {killer}'s cloud"),
                L("{killer} {icon} poisoned {victim}"),
                L("{killer} {icon} made {victim} queasy"),
                L("{killer} {icon} clouded {victim} over"),
                L("{killer} {icon} gave {victim} the blight"),
                L("{killer} {icon} soured {victim}"),
                L("{killer} {icon} fogged out {victim}"),
            },
            [(DeathKind.PoisonGun, DeathText.Title)] = new[]
            {
                L("SPORED OUT"), L("WILTED!"), L("BLIGHTED!"), L("GASSED!"), L("QUEASY!"), L("TOXIC!"),
            },
            [(DeathKind.PoisonGun, DeathText.Detail)] = new[]
            {
                L("you breathed in\n{killer}'s cloud"),
                L("by {killer}'s\npoison gun"),
                L("{killer}'s cloud\nwas not fog"),
                L("{killer} said:\nhold your breath"),
                L("{killer} gave you\nthe bad spores"),
                L("{killer} left you\nwilting"),
            },

            // Blob-gun
            [(DeathKind.BlobGun, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} blobbed {victim}"),
                L("{victim} got gooped {icon} by {killer}"),
                L("{killer} {icon} glued {victim} down"),
                L("{killer} {icon} jellied {victim}"),
                L("{killer} {icon} slimed {victim}"),
                L("{victim} {icon} stuck to {killer}'s blob"),
                L("{killer} {icon} squelched {victim}"),
                L("{killer} {icon} gummed up {victim}"),
                L("{killer} {icon} splatted {victim}"),
            },
            [(DeathKind.BlobGun, DeathText.Title)] = new[]
            {
                L("BLOBBED!"), L("GOOPED!"), L("SQUELCH!"), L("STUCK!"), L("JELLIED!"), L("SLIMED!"),
            },
            [(DeathKind.BlobGun, DeathText.Detail)] = new[]
            {
                L("by {killer}'s\nblob gun"),
                L("{killer} gooped you\nand it stuck"),
                L("stuck in place by\n{killer}"),
                L("{killer}'s blob\nwas not a friend"),
                L("thanks to {killer}\nyou are jelly"),
            },

            // Headshot
            [(DeathKind.Headshot, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} popped {victim}'s cap"),
                L("{killer} {icon} capped {victim}"),
                L("{victim} lost their cap {icon} to {killer}"),
                L("{killer} {icon} decapped {victim}"),
                L("{killer} {icon} knocked {victim}'s cap off"),
                L("{killer} {icon} tipped {victim}'s cap"),
                L("{killer} {icon} topped {victim}"),
                L("{killer} {icon} went cap-first on {victim}"),
                L("{killer} {icon} split {victim}'s cap", Gory),
                L("{killer} {icon} burst {victim}'s cap open", Ultra),
            },
            [(DeathKind.Headshot, DeathText.Title)] = new[]
            {
                L("CAP POPPED!"), L("DECAPPED!"), L("CAPPED!"), L("TOP OFF!"), L("HATS OFF!"), L("CAP TIPPED!"), L("CAP SPLIT!", Gory),
            },
            [(DeathKind.Headshot, DeathText.Detail)] = new[]
            {
                L("{killer} popped\nyour cap"),
                L("right in the cap,\nby {killer}"),
                L("{killer} knocked\nyour cap clean off"),
                L("by {killer}\nwith a cap shot"),
                L("{killer} aimed\nfor the top"),
                L("{killer} split\nyour cap in two", Gory),
            },

            // Tank
            [(DeathKind.Tank, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} flattened {victim}"),
                L("{killer} rolled over {icon} {victim}"),
                L("{victim} got shelled {icon} by {killer}"),
                L("{killer} {icon} boomed {victim}"),
                L("{killer} {icon} squashed {victim}"),
                L("{killer} {icon} pancaked {victim}"),
                L("{killer} {icon} cannoned {victim}"),
                L("{victim} {icon} lost to the {killer}"),
                L("{killer} {icon} tracked down {victim}"),
            },
            [(DeathKind.Tank, DeathText.Title)] = new[]
            {
                L("FLATTENED!"), L("PANCAKED!"), L("SQUASHED!"), L("SHELLED!"), L("KA-BOOM!"), L("TREAD ON!"),
            },
            [(DeathKind.Tank, DeathText.Detail)] = new[]
            {
                L("by the {killer}"),
                L("the {killer}\nrolled right over"),
                L("the {killer}\ndid not brake"),
                L("the {killer}\nwins this one"),
                L("the {killer}\nhad a bigger gun"),
            },

            // Helicopter
            [(DeathKind.Helicopter, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} rained on {victim}"),
                L("{killer} {icon} buzzed {victim}"),
                L("{victim} got airmailed {icon} by {killer}"),
                L("{killer} {icon} dropped in on {victim}"),
                L("{killer} {icon} swooped {victim}"),
                L("{killer} {icon} chopped {victim}"),
                L("{killer} {icon} spotted {victim} from up high"),
                L("{victim} looked up late {icon} at {killer}"),
            },
            [(DeathKind.Helicopter, DeathText.Title)] = new[]
            {
                L("AIRMAILED!"), L("SWOOPED!"), L("CHOPPED!"), L("FROM ABOVE!"), L("BUZZED!"), L("LOOK UP!"),
            },
            [(DeathKind.Helicopter, DeathText.Detail)] = new[]
            {
                L("by the {killer}"),
                L("the {killer}\nsaw you from above"),
                L("the {killer}\nhad the high ground"),
                L("the {killer}\nbuzzed your cap"),
                L("look up next time:\nthe {killer}"),
            },

            // Turret
            [(DeathKind.Turret, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} denied {victim}"),
                L("{victim} walked into {icon} {killer}"),
                L("{killer} {icon} stopped {victim}"),
                L("{victim} got turreted {icon} by {killer}"),
                L("{killer} {icon} held the line on {victim}"),
                L("{killer} {icon} said no to {victim}"),
                L("{killer} {icon} tracked {victim}"),
                L("{victim} {icon} tested {killer}"),
            },
            [(DeathKind.Turret, DeathText.Title)] = new[]
            {
                L("TURRETED!"), L("DENIED!"), L("HALT!"), L("GUARDED!"), L("NO ENTRY!"), L("TRACKED!"),
            },
            [(DeathKind.Turret, DeathText.Detail)] = new[]
            {
                L("by the {killer}"),
                L("the {killer}\nwas watching"),
                L("the {killer}\nnever blinks"),
                L("the {killer}\nheld its ground"),
                L("you walked into\nthe {killer}"),
            },

            // Cannon (a machine by any other name)
            [(DeathKind.Cannon, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} cannoned {victim}"),
                L("{killer} {icon} blasted {victim}"),
                L("{victim} got boomed {icon} by {killer}"),
                L("{killer} {icon} kabloomed {victim}"),
                L("{killer} {icon} shelled {victim}"),
                L("{killer} {icon} launched {victim}"),
                L("{killer} {icon} burst {victim} wide open", Ultra),
            },
            [(DeathKind.Cannon, DeathText.Title)] = new[]
            {
                L("KABLOOM!"), L("BLASTED!"), L("BOOM!"), L("CANNONED!"), L("LAUNCHED!"),
            },
            [(DeathKind.Cannon, DeathText.Detail)] = new[]
            {
                L("by the {killer}"),
                L("the {killer}\nand its cannon"),
                L("the {killer}\nwas not a friend"),
                L("the {killer}\nfired first"),
            },

            // A hazard of the level (the toy train): no weapon, so the icon sits between the words without one
            [(DeathKind.Hazard, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} ran over {victim}"),
                L("{killer} {icon} chugged over {victim}"),
                L("{victim} {icon} lost to the {killer}"),
                L("{killer} {icon} steamrolled {victim}"),
                L("{killer} {icon} derailed {victim}"),
                L("{killer} {icon} tooted at {victim}"),
                L("{victim} {icon} missed the {killer}"),
                L("{killer} {icon} flattened {victim}"),
            },
            [(DeathKind.Hazard, DeathText.Title)] = new[]
            {
                L("RAN OVER!"), L("ALL ABOARD!"), L("CHOO-CHOO!"), L("DERAILED!"), L("TRACKED!"), L("FLATTENED!"),
            },
            [(DeathKind.Hazard, DeathText.Detail)] = new[]
            {
                L("by {killer}"),
                L("{killer}\nran you over"),
                L("{killer}\nhad right of way"),
                L("{killer}\ndid not brake"),
                L("you stood on\nthe track"),
            },

            // A hot patch of the level (the kitchen's hob): the killer is its name, "the hob", so no line starts with it; the icon sits between the words without one
            [(DeathKind.Heat, DeathText.Feed)] = new[]
            {
                L("{victim} {icon} sizzled on {killer}"),
                L("{victim} {icon} fried on {killer}"),
                L("{victim} {icon} stood on {killer}"),
                L("{victim} {icon} got seared by {killer}"),
                L("{victim} {icon} toasted on {killer}"),
                L("{victim} {icon} cooked on {killer}"),
                L("{victim} {icon} grilled on {killer}"),
                L("{victim} {icon} sautéed on {killer}"),
            },
            [(DeathKind.Heat, DeathText.Title)] = new[]
            {
                L("SIZZLED!"), L("SEARED!"), L("TOASTED!"), L("GRILLED!"), L("COOKED!"), L("FRIED!"), L("HOT STUFF!"),
            },
            [(DeathKind.Heat, DeathText.Detail)] = new[]
            {
                L("by {killer}"),
                L("you stood on\n{killer}"),
                L("{killer}\nwas too hot"),
                L("{killer}\nwarned you"),
                L("too hot\nto handle"),
                L("you cooked\non {killer}"),
                L("should have\nwatched your feet"),
            },

            // A fall
            [(DeathKind.Fall, DeathText.Feed)] = new[]
            {
                L("{victim} found the edge"),
                L("{victim} fell off the island"),
                L("{victim} went for a long walk"),
                L("{victim} composted themselves"),
                L("{victim} tested gravity"),
                L("{victim} took the scenic drop"),
                L("{victim} left the island"),
                L("{victim} returned to the soil"),
                L("{victim} dropped off the map"),
                L("{victim} took a tumble"),
                L("{victim} stepped into the sky"),
            },
            [(DeathKind.Fall, DeathText.Title)] = new[]
            {
                L("COMPOSTED"), L("TUMBLED!"), L("WHOOPS!"), L("OVERBOARD!"), L("GRAVITY!"), L("SO LONG!"), L("DROPPED!"),
            },
            [(DeathKind.Fall, DeathText.Detail)] = new[]
            {
                L("the island\nhad an edge"),
                L("gravity: 1\nyou: 0"),
                L("the ground was\nmuch further down"),
                L("back to the soil\nwith you"),
                L("next time,\nmind the edge"),
                L("you composted\nyourself"),
                L("the island got you"),
            },

            // Revenge
            [(DeathKind.Revenge, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} got even with {victim}"),
                L("{killer} {icon} paid back {victim}"),
                L("{killer} {icon} settled up with {victim}"),
                L("{killer} {icon} took revenge on {victim}"),
                L("{killer} {icon} hit {victim} back"),
                L("{killer} {icon} remembered {victim}"),
                L("{killer} {icon} returned {victim}'s gift"),
                L("{killer} {icon} answered {victim}"),
            },
            [(DeathKind.Revenge, DeathText.Title)] = new[]
            {
                L("REVENGE!"), L("PAYBACK!"), L("EVEN NOW!"), L("SETTLED!"), L("RETURNED!"),
            },
            [(DeathKind.Revenge, DeathText.Detail)] = new[]
            {
                L("{killer} got even\nwith the {weapon}"),
                L("{killer} remembered\nyou"),
                L("payback from\n{killer}"),
                L("{killer} had\na score to settle"),
            },

            // A streak
            [(DeathKind.Streak, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} made {victim} #{streak}"),
                L("{killer} {icon} picked {victim}, {streak} in a row"),
                L("{killer} is on a roll {icon} {victim}"),
                L("{killer} {icon} piled {victim} on"),
                L("{killer} {icon} keeps going: {victim}"),
                L("{killer} blooms {icon} {victim} ({streak})"),
                L("{killer} {icon} {victim}: {streak} straight"),
                L("{killer} can't stop {icon} {victim}"),
            },
            [(DeathKind.Streak, DeathText.Title)] = new[]
            {
                L("ON A ROLL!"), L("UNSTOPPABLE"), L("RAMPAGE!"), L("IN BLOOM!"), L("SPORE STORM!"),
            },
            [(DeathKind.Streak, DeathText.Detail)] = new[]
            {
                L("{killer} is on\na {streak}-kill streak"),
                L("{killer} has\n{streak} in a row now"),
                L("you are number {streak}\nfor {killer}"),
                L("{killer} is\nin full bloom"),
            },

            // Anything
            [(DeathKind.Generic, DeathText.Feed)] = new[]
            {
                L("{killer} {icon} composted {victim}"),
                L("{killer} {icon} picked {victim}"),
                L("{killer} {icon} uprooted {victim}"),
                L("{victim} got weeded {icon} by {killer}"),
                L("{killer} {icon} pruned {victim}"),
                L("{killer} {icon} sent {victim} to the soil"),
                L("{killer} {icon} outgrew {victim}"),
                L("{killer} {icon} out-funged {victim}"),
                L("{killer} {icon} eliminated {victim}"),
                L("{killer} {icon} trimmed {victim}"),
                L("{killer} {icon} splattered {victim}", Gory),
                L("{killer} {icon} spattered {victim} everywhere", Ultra),
            },
            [(DeathKind.Generic, DeathText.Title)] = new[]
            {
                L("COMPOSTED"), L("ELIMINATED"), L("UPROOTED!"), L("WEEDED OUT!"), L("PRUNED!"), L("PICKED!"), L("MUSHED!"),
                L("SPLATTERED!", Gory), L("GOOPSPLOSION", Ultra),
            },
            [(DeathKind.Generic, DeathText.Detail)] = new[]
            {
                L("by {killer}\nwith the {weapon}"),
                L("{killer} and the\n{weapon} say hi"),
                L("{killer} picked\nyou fair and square"),
                L("back to the soil,\ncourtesy of {killer}"),
                L("{killer} out-funged\nyou this time"),
                L("{killer} splattered\nyou all over", Gory),
            },
        };
    }

    /// <summary>
    /// The deaths so far, for the kill feed's revenges and streaks: who last killed whom, and each player's kills since they last died.
    /// </summary>
    public sealed class KillMemory
    {
        private readonly Dictionary<PlayerState, int> streaks = new Dictionary<PlayerState, int>();
        private readonly Dictionary<PlayerState, PlayerState> killedBy = new Dictionary<PlayerState, PlayerState>();

        /// <summary>The player's kills since they last died.</summary>
        public int StreakOf(PlayerState player) => player != null && streaks.TryGetValue(player, out var s) ? s : 0;

        /// <summary>Remembers a death; returns its kind (<see cref="DeathLines.KindOf"/>) and the killer's streak with it.</summary>
        public DeathKind Record(Death death, out int streak)
        {
            var victim = death.Victim;
            var killer = death.Killer;
            streaks.Remove(victim);
            streak = 0;
            var revenge = false;
            if (death.CountsAsKill)
            {
                streak = StreakOf(killer) + 1;
                streaks[killer] = streak;
                // Getting back at your last killer, once
                revenge = killedBy.TryGetValue(killer, out var last) && last == victim;
                if (revenge)
                    killedBy.Remove(killer);
                killedBy[victim] = killer;
            }
            return DeathLines.KindOf(death, revenge, streak);
        }

        /// <summary>A new round: no streaks, nothing to avenge.</summary>
        public void Clear()
        {
            streaks.Clear();
            killedBy.Clear();
        }
    }
}
