using System;
using System.Collections.Generic;

namespace SdfBody
{
    /// <summary>The folder a clip lives in under <c>Assets/Battle/Body/Animations/</c> (ticket 03).</summary>
    public enum ClipCategory
    {
        Locomotion,
        Combat,
        Emote,
        Misc,
    }

    /// <summary>
    /// One clip the soldiers have: its convention name (<c>soldier_&lt;group&gt;_&lt;name&gt;</c>, lowercase), the file that holds it
    /// (relative to <see cref="SoldierClips.Folder"/>), where it came from (medievalminiguns <c>f198d418</c>, under
    /// <c>Assets/Animations/</c>), whether it loops, what it is for and which tickets use it. An <see cref="IsAlt"/> clip is a duplicate
    /// kept for the history (not for 05/06).
    /// </summary>
    public sealed class ClipEntry
    {
        public string Name { get; }
        public ClipCategory Category { get; }
        public string File { get; }
        public string Source { get; }
        public bool Loop { get; }
        public string Role { get; }
        public string UsedBy { get; }
        public bool IsAlt => Name.EndsWith("_alt", StringComparison.Ordinal);
        public bool IsFbx => File.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        public ClipEntry(string name, ClipCategory category, string file, string source, bool loop, string role, string usedBy)
        {
            Name = name;
            Category = category;
            File = file;
            Source = source;
            Loop = loop;
            Role = role;
            UsedBy = usedBy;
        }
    }

    /// <summary>
    /// A clip a battle royale needs that the medievalminiguns set lacks (decision 26): its convention name (the file name to save the
    /// Mixamo download as, in <see cref="SoldierClips.MixamoFolder"/>), whether it loops, what to search on Mixamo, and the stand-in
    /// that plays until it exists (an existing clip, held at <see cref="HoldAt"/> (normalized time) when that is 0 or more).
    /// </summary>
    public sealed class GapEntry
    {
        public string Name { get; }
        public ClipCategory Category { get; }
        public bool Loop { get; }
        public string Mixamo { get; }
        public bool InPlace { get; }
        public string StandIn { get; }
        public float HoldAt { get; }
        public string UsedBy { get; }
        public bool Optional { get; }

        public GapEntry(string name, ClipCategory category, bool loop, string mixamo, bool inPlace, string standIn, float holdAt, string usedBy, bool optional = false)
        {
            Name = name;
            Category = category;
            Loop = loop;
            Mixamo = mixamo;
            InPlace = inPlace;
            StandIn = standIn;
            HoldAt = holdAt;
            UsedBy = usedBy;
            Optional = optional;
        }
    }

    /// <summary>
    /// The soldiers' clips (ticket 03): every clip copied from medievalminiguns <c>f198d418</c> and every gap with its stand-in. The one
    /// table the import fixer, the clip map builder, the Mixamo drop validator, the tests and <c>docs/animations.md</c> follow.
    /// </summary>
    public static class SoldierClips
    {
        public const string Folder = "Assets/Battle/Body/Animations/";
        public const string MixamoFolder = Folder + "Mixamo/";
        public const string SourceCommit = "f198d418";
        public const string SourceRoot = "medievalminiguns/Assets/Animations/";

        private const ClipCategory L = ClipCategory.Locomotion, C = ClipCategory.Combat, E = ClipCategory.Emote, M = ClipCategory.Misc;
        private const string Loco = "44", Combat = "44";

        public static readonly IReadOnlyList<ClipEntry> All = new[]
        {
            new ClipEntry("soldier_loco_aim_idle", L, "Locomotion/soldier_loco_aim_idle.anim", "movement/rifleaimidle.anim", true, "Rifle aim idle: the blend tree's centre", Loco),
            new ClipEntry("soldier_loco_walk_f", L, "Locomotion/soldier_loco_walk_f.anim", "movement/Walk Forward.anim", true, "Walk forward, rifle up", Loco),
            new ClipEntry("soldier_loco_walk_b", L, "Locomotion/soldier_loco_walk_b.anim", "movement/Walking Backwards.anim", true, "Walk backward", Loco),
            new ClipEntry("soldier_loco_walk_fl", L, "Locomotion/soldier_loco_walk_fl.anim", "movement/Walk Forward Left.anim", true, "Walk forward-left", Loco),
            new ClipEntry("soldier_loco_walk_fr", L, "Locomotion/soldier_loco_walk_fr.anim", "movement/Walk Forward Right.anim", true, "Walk forward-right", Loco),
            new ClipEntry("soldier_loco_walk_bl", L, "Locomotion/soldier_loco_walk_bl.anim", "movement/Walk Backward Left.anim", true, "Walk backward-left", Loco),
            new ClipEntry("soldier_loco_walk_br", L, "Locomotion/soldier_loco_walk_br.anim", "movement/Walk Backward Right.anim", true, "Walk backward-right", Loco),
            new ClipEntry("soldier_loco_strafe_l", L, "Locomotion/soldier_loco_strafe_l.anim", "movement/Strafe Left.anim", true, "Walk strafing left", Loco),
            new ClipEntry("soldier_loco_strafe_r", L, "Locomotion/soldier_loco_strafe_r.anim", "movement/Strafe Right.anim", true, "Walk strafing right", Loco),
            new ClipEntry("soldier_loco_run_f", L, "Locomotion/soldier_loco_run_f.anim", "movement/Rifle Run.anim", true, "Run forward with the rifle; sprint stand-in", Loco),
            new ClipEntry("soldier_loco_run_b", L, "Locomotion/soldier_loco_run_b.anim", "movement/Run Backwards.anim", true, "Run backward, in place", Loco),
            new ClipEntry("soldier_loco_run_l", L, "Locomotion/soldier_loco_run_l.anim", "movement/Run Left.anim", true, "Run left", Loco),
            new ClipEntry("soldier_loco_run_r", L, "Locomotion/soldier_loco_run_r.anim", "movement/Run Right.anim", true, "Run right", Loco),

            new ClipEntry("soldier_fire_rifle", C, "Combat/firing rifle.fbx", "firing rifle.fbx", true, "Firing the rifle, one shot (0.25 s), looped while the trigger is held (upper-body layer)", Combat),
            new ClipEntry("soldier_fire_rifle_burst", C, "Combat/soldier_fire_rifle_burst.anim", "Firing Rifle.anim", true, "Firing the rifle, a longer take of several shots (1.17 s): the cycle for automatic fire", Combat),
            new ClipEntry("soldier_reload", C, "Combat/reloading.fbx", "reloading.fbx", false, "Reload", Combat),
            new ClipEntry("soldier_hit", C, "Combat/hit reaction.fbx", "hit reaction.fbx", false, "Hit reaction", Combat),

            new ClipEntry("soldier_emote_dance_1", E, "Emote/soldier_emote_dance_1.anim", "Silly Dancing.anim", true, "Dance (Silly Dancing); stand-in for the victory", "44"),
            new ClipEntry("soldier_emote_dance_2", E, "Emote/soldier_emote_dance_2.anim", "Chapa Giratoria 2.anim", true, "Spin (Chapa Giratoria, a capoeira spin kick)", "44"),

            new ClipEntry("soldier_jump", M, "Misc/rifle jump.fbx", "rifle jump.fbx", false, "Jump with the rifle; stand-in for the jump, the fall and the landing", "44"),
        };

        /// <summary>The fall pose of <c>soldier_jump</c> (normalized time) that stands in for freefall and the chute.</summary>
        public const float JumpFallPose = 0.45f;

        public static readonly IReadOnlyList<GapEntry> Gaps = new[]
        {
            // Needed
            new GapEntry("body_pistol_idle", L, true, "\"Pistol Idle\"", true, "soldier_loco_aim_idle", -1f, "pistol and dual pistols"),
            new GapEntry("body_pistol_run_f", L, true, "\"Pistol Run\"", true, "soldier_loco_run_f", -1f, "pistol locomotion"),
            new GapEntry("body_pistol_run_b", L, true, "\"Pistol Walk Backward\"", true, "soldier_loco_run_b", -1f, "pistol locomotion"),
            new GapEntry("body_pistol_run_l", L, true, "\"Pistol Strafe\" (left)", true, "soldier_loco_run_l", -1f, "pistol locomotion"),
            new GapEntry("body_pistol_run_r", L, true, "\"Pistol Strafe\" (right)", true, "soldier_loco_run_r", -1f, "pistol locomotion"),
            new GapEntry("body_jump_up", M, false, "\"Jump\" (the take-off half)", true, "soldier_jump", -1f, "the jump"),
            // The user's decision (2026-11-02): falling and landing play the idle of the stance (the pistol idle in the pistol stance, else the
            // rifle idle) until Falling Idle and Falling To Landing are downloaded; SoldierControllerBuilder blends it by Stance.
            new GapEntry("body_fall_loop", M, true, "\"Falling Idle\"", true, "soldier_loco_aim_idle", -1f, "in the air"),
            new GapEntry("body_land", M, false, "\"Falling To Landing\"", true, "soldier_loco_aim_idle", -1f, "the landing"),
            // The pistol stance's walk tier (the forward and backward walks; the other walks stay the rifle's)
            new GapEntry("body_pistol_walk_f", L, true, "\"Pistol Walk\"", true, "soldier_loco_walk_f", -1f, "pistol locomotion", optional: true),
            new GapEntry("body_pistol_walk_b", L, true, "\"Pistol Walk Backward\"", true, "soldier_loco_walk_b", -1f, "pistol locomotion", optional: true),
            // Optional
            new GapEntry("body_heavy_idle", L, true, "\"Rifle Idle\" with a heavy weapon", true, "soldier_loco_aim_idle", -1f, "minigun and flamethrower", optional: true),
            new GapEntry("body_heavy_run_f", L, true, "\"Heavy Weapon Run\"", true, "soldier_loco_run_f", -1f, "minigun and flamethrower", optional: true),
            new GapEntry("body_roar", E, false, "\"Mutant Roaring\"", true, "soldier_emote_dance_2", -1f, "using a monster power (49); plays nothing until it exists", optional: true),
            new GapEntry("body_victory", E, false, "\"Victory\" / \"Fist Pump\"", true, "soldier_emote_dance_1", -1f, "the winners on the end card (the dances stand in)", optional: true),
            // A sprint point of the tree for Frenzy; the run stands in
            new GapEntry("soldier_loco_sprint_f", L, true, "(not needed: Rifle Run at a higher speed stands in)", true, "soldier_loco_run_f", -1f, "Frenzy", optional: true),
        };

        private static Dictionary<string, ClipEntry> _byName;
        private static Dictionary<string, GapEntry> _gapByName;

        public static ClipEntry Find(string name)
        {
            _byName ??= Index(All, e => e.Name);
            return name != null && _byName.TryGetValue(name, out var e) ? e : null;
        }

        public static GapEntry FindGap(string name)
        {
            _gapByName ??= Index(Gaps, e => e.Name);
            return name != null && _gapByName.TryGetValue(name, out var e) ? e : null;
        }

        /// <summary>The prefixes whose clips loop when a name is neither in <see cref="All"/> nor <see cref="Gaps"/>.</summary>
        public static readonly IReadOnlyList<string> LoopingPrefixes = new[]
        {
            "soldier_loco_", "body_pistol_", "body_heavy_", "body_fall_", "soldier_emote_dance",
        };

        /// <summary>
        /// Whether a clip of this convention name loops: the catalog's flag for a known name; otherwise a name ending <c>_loop</c> loops, one
        /// ending <c>_once</c> does not, and else <see cref="LoopingPrefixes"/> decide (cycles loop, one-shots do not).
        /// </summary>
        public static bool LoopsByName(string name)
        {
            if (Find(name) is { } e)
                return e.Loop;
            if (FindGap(name) is { } g)
                return g.Loop;
            if (name.EndsWith("_loop", StringComparison.Ordinal))
                return true;
            if (name.EndsWith("_once", StringComparison.Ordinal))
                return false;
            foreach (var p in LoopingPrefixes)
                if (name.StartsWith(p, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>A convention name: <c>soldier_</c> (the catalog) or <c>body_</c> (a Mixamo download) then lowercase letters, digits and underscores.</summary>
        public static bool IsConventionName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            var prefix = name.StartsWith("body_", StringComparison.Ordinal) ? "body_" : name.StartsWith("soldier_", StringComparison.Ordinal) ? "soldier_" : null;
            if (prefix == null || name.Length <= prefix.Length)
                return false;
            foreach (var ch in name)
                if (!(ch is >= 'a' and <= 'z' || ch is >= '0' and <= '9' || ch == '_'))
                    return false;
            return !name.EndsWith("_", StringComparison.Ordinal) && !name.Contains("__");
        }

        private static Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> key)
        {
            var d = new Dictionary<string, T>();
            foreach (var i in items)
                d.Add(key(i), i);
            return d;
        }
    }
}
