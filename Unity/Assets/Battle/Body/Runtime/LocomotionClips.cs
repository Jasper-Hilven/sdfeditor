using System.Collections.Generic;
using UnityEngine;

namespace SdfBody
{
    /// <summary>Which ring of the locomotion blend tree a clip sits on.</summary>
    public enum LocomotionRing { Centre, Walk, Run, Sprint }

    /// <summary>One clip of a locomotion blend tree: its name, unit direction and ring.</summary>
    public readonly struct LocomotionClip
    {
        public readonly string Name;
        /// <summary>Unit vector in the tree frame (x right, y forward); zero for the centre clip.</summary>
        public readonly Vector2 Direction;
        public readonly LocomotionRing Ring;
        /// <summary>The clip played while 03's clip map has no slot of <see cref="Name"/> (05's own stand-in); null for none.</summary>
        public readonly string StandIn;

        public LocomotionClip(string name, Vector2 direction, LocomotionRing ring, string standIn = null)
        {
            Name = name;
            Direction = direction;
            Ring = ring;
            StandIn = standIn;
        }

        /// <summary>Angle of the direction in degrees, atan2(y, x).</summary>
        public float AngleDegrees => Mathf.Atan2(Direction.y, Direction.x) * Mathf.Rad2Deg;

        /// <summary>Direction times the radius of this clip's ring; zero for the centre.</summary>
        public Vector2 Position(float walkRadius, float runRadius, float sprintRadius)
        {
            switch (Ring)
            {
                case LocomotionRing.Walk: return Direction * walkRadius;
                case LocomotionRing.Run: return Direction * runRadius;
                case LocomotionRing.Sprint: return Direction * sprintRadius;
                default: return Vector2.zero;
            }
        }
    }

    /// <summary>
    /// The single table of clip positions that the editor builder and the runtime both read.
    /// The directions are those of the medievalminiguns source blend tree (commit f198d418,
    /// Assets/Animations/CharacterAnimationController.controller, whose MovementX was forward and
    /// MovementY left, walk ring at 1 and run ring at 2), turned into this game's frame
    /// (x right, y forward) and halved. The source's diagonal walks sat on the corners of a square
    /// (radius 1.41 of its ring); here they are on the ring (radius WalkRadius), so a diagonal walk
    /// at walk speed plays the diagonal clip fully.
    /// <para>
    /// Added here (not in the source): four diagonal runs on the run ring, <c>soldier_loco_run_fl/fr/bl/br</c>. The source has none, so
    /// a diagonal run blended the straight runs, whose gaits are out of phase, and the planted foot slid 30 to 75 % of the body's speed
    /// (measured by the play mode test). Until those names exist in 03's clip map (request 03-diagonal-runs), they play the walk
    /// diagonals, sped up by their time scale.
    /// </para>
    /// </summary>
    public static class LocomotionClips
    {
        const float S = 0.70710678f;

        static LocomotionClip C(string n, float x, float y, LocomotionRing r, string standIn = null) => new LocomotionClip(n, new Vector2(x, y), r, standIn);

        /// <summary>The standing (rifle) tree, 18 clips.</summary>
        public static readonly LocomotionClip[] Stand =
        {
            C("soldier_loco_aim_idle", 0, 0, LocomotionRing.Centre),
            C("soldier_loco_walk_f", 0, 1, LocomotionRing.Walk),
            C("soldier_loco_walk_b", 0, -1, LocomotionRing.Walk),
            C("soldier_loco_strafe_l", -1, 0, LocomotionRing.Walk),
            C("soldier_loco_strafe_r", 1, 0, LocomotionRing.Walk),
            C("soldier_loco_walk_fl", -S, S, LocomotionRing.Walk),
            C("soldier_loco_walk_fr", S, S, LocomotionRing.Walk),
            C("soldier_loco_walk_bl", -S, -S, LocomotionRing.Walk),
            C("soldier_loco_walk_br", S, -S, LocomotionRing.Walk),
            C("soldier_loco_run_f", 0, 1, LocomotionRing.Run),
            C("soldier_loco_run_b", 0, -1, LocomotionRing.Run),
            C("soldier_loco_run_l", -1, 0, LocomotionRing.Run),
            C("soldier_loco_run_r", 1, 0, LocomotionRing.Run),
            C("soldier_loco_run_fl", -S, S, LocomotionRing.Run, "soldier_loco_walk_fl"),
            C("soldier_loco_run_fr", S, S, LocomotionRing.Run, "soldier_loco_walk_fr"),
            C("soldier_loco_run_bl", -S, -S, LocomotionRing.Run, "soldier_loco_walk_bl"),
            C("soldier_loco_run_br", S, -S, LocomotionRing.Run, "soldier_loco_walk_br"),
            C("soldier_loco_sprint_f", 0, 1, LocomotionRing.Sprint),
        };

        /// <summary>The stances of the locomotion: the clip set follows the weapon in hand.</summary>
        public enum Stance { Rifle = 0, Pistol = 1, Heavy = 2 }

        /// <summary>The standing tree with the pistol's idle and runs (<c>body_pistol_*</c>; until Mixamo's clips are in, the rifle's play).</summary>
        public static readonly LocomotionClip[] Pistol = Swap(Stand, new[]
        {
            ("soldier_loco_aim_idle", "body_pistol_idle"), ("soldier_loco_run_f", "body_pistol_run_f"), ("soldier_loco_run_b", "body_pistol_run_b"),
            ("soldier_loco_run_l", "body_pistol_run_l"), ("soldier_loco_run_r", "body_pistol_run_r"), ("soldier_loco_sprint_f", "body_pistol_run_f"),
        });

        /// <summary>The standing tree with the heavy weapon's idle and forward run (the sprint too), for the minigun and the flamethrower.</summary>
        public static readonly LocomotionClip[] Heavy = Swap(Stand, new[]
        {
            ("soldier_loco_aim_idle", "body_heavy_idle"), ("soldier_loco_run_f", "body_heavy_run_f"), ("soldier_loco_sprint_f", "body_heavy_run_f"),
        });

        /// <summary>The tree of a stance.</summary>
        public static LocomotionClip[] Of(Stance stance) => stance == Stance.Pistol ? Pistol : stance == Stance.Heavy ? Heavy : Stand;

        /// <summary>Every tree, in <see cref="Stance"/> order.</summary>
        public static readonly LocomotionClip[][] Trees = { Stand, Pistol, Heavy };

        static LocomotionClip[] Swap(LocomotionClip[] set, (string From, string To)[] names)
        {
            var result = new LocomotionClip[set.Length];
            for (var i = 0; i < set.Length; i++)
            {
                var c = set[i];
                foreach (var (from, to) in names)
                    if (c.Name == from)
                    {
                        // The stand-in of the swapped clip is the rifle clip it replaces
                        c = new LocomotionClip(to, c.Direction, c.Ring, from);
                        break;
                    }
                result[i] = c;
            }
            return result;
        }

        /// <summary>The clip that plays for a table entry: the map's slot of its name, else (05's stand-in) the map's slot of <see cref="LocomotionClip.StandIn"/>.</summary>
        public static AnimationClip Resolve(SdfBody.SoldierClipMap map, LocomotionClip clip)
        {
            if (map == null) return null;
            var c = map.Get(clip.Name);
            return c != null || string.IsNullOrEmpty(clip.StandIn) ? c : map.Get(clip.StandIn);
        }

        /// <summary>The table entry of a name (the stand, pistol and heavy sets); false when none.</summary>
        public static bool TryFind(string name, out LocomotionClip clip)
        {
            foreach (var c in Stand) if (c.Name == name) { clip = c; return true; }
            foreach (var c in Pistol) if (c.Name == name) { clip = c; return true; }
            foreach (var c in Heavy) if (c.Name == name) { clip = c; return true; }
            clip = default;
            return false;
        }

        /// <summary>The clips of a set that sit on the given ring, in table order.</summary>
        public static IEnumerable<LocomotionClip> InRing(IEnumerable<LocomotionClip> set, LocomotionRing ring)
        {
            foreach (var c in set)
                if (c.Ring == ring) yield return c;
        }
    }
}
