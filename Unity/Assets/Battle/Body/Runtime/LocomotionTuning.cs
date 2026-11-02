using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBody
{
    /// <summary>The soldier animation track's one tuning asset.</summary>
    [CreateAssetMenu(menuName = "Sdf/Body Animation", fileName = "SoldierAnimation")]
    public sealed class LocomotionTuning : ScriptableObject
    {
        /// <summary>Resources path (Assets/Battle/Body/Animator/Resources/SoldierAnimation.asset).</summary>
        public const string ResourcePath = "SoldierAnimation";

        [Tooltip("The soldier's animator controller, set by the builder.")]
        public RuntimeAnimatorController Controller;
        [Tooltip("Metres per second at MoveY = 1: the game's walk speed (FirstPersonController.walkSpeed), so the walk-speed player is on the run ring.")]
        public float RunSpeed = 4.4f;
        public float WalkRadius = 0.5f;
        public float RunRadius = 1f;
        [Tooltip("The tree's largest radius: Frenzy runs at 1.6 times the walk speed, the flag carrier at 0.9.")]
        public float SprintRadius = 1.65f;
        public float MoveDampSeconds = 0.1f;
        public float SprintDampSeconds = 0.15f;
        [Tooltip("Seconds the weapon stance (rifle, pistol, heavy) takes to blend.")]
        public float StanceDampSeconds = 0.12f;
        [Tooltip("The locomotion state's playback multiplier is kept in [MinPlayback, MaxPlayback].")]
        public float MinPlayback = 0.5f;
        public float MaxPlayback = 2.5f;
        [Tooltip("Moving faster than this (m/s) ends an emote and blocks one.")]
        public float EmoteCancelSpeed = 0.5f;
        public float EmoteFadeSeconds = 0.2f;
        public ClipSpeed[] ClipSpeeds = DefaultClipSpeeds();

        /// <summary>A clip's natural ground speed at playback 1.</summary>
        [Serializable]
        public struct ClipSpeed
        {
            public string Clip;
            public float MetresPerSecond;
            public ClipSpeed(string c, float m) { Clip = c; MetresPerSecond = m; }
        }

        public float WalkSpeed => RunSpeed * WalkRadius;
        public float MaxTreeRadius => SprintRadius;

        /// <summary>
        /// The clips' natural ground speeds (m/s at playback 1), measured on the soldier by <c>LocomotionClipSpeeds</c> (the planted
        /// foot's median slide over 120 samples per cycle; 2026-11-02). The stand-ins (sprint, heavy) carry their stand-in clip's number;
        /// rerun the measure when a Mixamo clip replaces one (the tests compare these with a fresh measure).
        /// </summary>
        public static ClipSpeed[] DefaultClipSpeeds() => new[]
        {
            new ClipSpeed("soldier_loco_walk_f", 1.796f), new ClipSpeed("soldier_loco_walk_b", 1.029f),
            new ClipSpeed("soldier_loco_strafe_l", 1.301f), new ClipSpeed("soldier_loco_strafe_r", 0.700f),
            new ClipSpeed("soldier_loco_walk_fl", 1.797f), new ClipSpeed("soldier_loco_walk_fr", 1.777f),
            new ClipSpeed("soldier_loco_walk_bl", 1.793f), new ClipSpeed("soldier_loco_walk_br", 1.800f),
            new ClipSpeed("soldier_loco_run_f", 2.995f), new ClipSpeed("soldier_loco_run_b", 2.526f),
            new ClipSpeed("soldier_loco_run_l", 4.264f), new ClipSpeed("soldier_loco_run_r", 4.478f),
            new ClipSpeed("soldier_loco_run_fl", 1.797f), new ClipSpeed("soldier_loco_run_fr", 1.777f),
            new ClipSpeed("soldier_loco_run_bl", 1.793f), new ClipSpeed("soldier_loco_run_br", 1.800f),
            new ClipSpeed("soldier_loco_sprint_f", 2.995f),
            new ClipSpeed("body_pistol_run_f", 2.932f), new ClipSpeed("body_pistol_run_b", 3.178f),
            new ClipSpeed("body_pistol_run_l", 2.096f), new ClipSpeed("body_pistol_run_r", 2.351f),
            new ClipSpeed("body_heavy_run_f", 2.995f),
            new ClipSpeed("body_pistol_walk_f", 2.483f), new ClipSpeed("body_pistol_walk_b", 1.369f),
        };

        /// <summary>Natural ground speed of a clip by name; unknown gives 0.</summary>
        public float SpeedOf(string clip)
        {
            if (ClipSpeeds != null)
                foreach (var c in ClipSpeeds)
                    if (c.Clip == clip) return c.MetresPerSecond;
            return 0f;
        }

        /// <summary>The tree radius of a ring (the centre is 0).</summary>
        public float RadiusOf(LocomotionRing ring) =>
            ring == LocomotionRing.Walk ? WalkRadius : ring == LocomotionRing.Run ? RunRadius : ring == LocomotionRing.Sprint ? SprintRadius : 0f;

        /// <summary>
        /// The blend tree child's time scale: the clip played so its feet cover its ring's speed (radius times <see cref="RunSpeed"/>),
        /// clamped to [<see cref="MinPlayback"/>, <see cref="MaxPlayback"/>]; 1 for the centre and for a clip with no measured speed.
        /// </summary>
        public float TimeScaleOf(LocomotionClip clip)
        {
            if (clip.Ring == LocomotionRing.Centre) return 1f;
            float natural = SpeedOf(clip.Name);
            if (natural <= 1e-4f) return 1f;
            return Mathf.Clamp(RadiusOf(clip.Ring) * RunSpeed / natural, MinPlayback, MaxPlayback);
        }

        /// <summary>The clip's ground speed in the tree: its natural speed times its time scale (m/s at state speed 1).</summary>
        public float TreeSpeedOf(LocomotionClip clip) => SpeedOf(clip.Name) * TimeScaleOf(clip);

        /// <summary>Angles and tree speeds (<see cref="TreeSpeedOf"/>) of one ring of a clip set.</summary>
        public LocomotionSpeedRing Ring(IEnumerable<LocomotionClip> set, LocomotionRing ring, float radius)
        {
            var angles = new List<float>();
            var speeds = new List<float>();
            foreach (var c in LocomotionClips.InRing(set, ring))
            {
                angles.Add(c.AngleDegrees);
                speeds.Add(TreeSpeedOf(c));
            }
            return new LocomotionSpeedRing { Radius = radius, Angles = angles.ToArray(), Speeds = speeds.ToArray() };
        }

        public LocomotionSpeedRing WalkRing(LocomotionClips.Stance stance = LocomotionClips.Stance.Rifle) => Ring(LocomotionClips.Of(stance), LocomotionRing.Walk, WalkRadius);
        public LocomotionSpeedRing RunRing(LocomotionClips.Stance stance = LocomotionClips.Stance.Rifle) => Ring(LocomotionClips.Of(stance), LocomotionRing.Run, RunRadius);
        public LocomotionSpeedRing SprintRing(LocomotionClips.Stance stance = LocomotionClips.Stance.Rifle) => Ring(LocomotionClips.Of(stance), LocomotionRing.Sprint, SprintRadius);

        /// <summary>The asset from Resources, else a fresh instance with the code defaults.</summary>
        public static LocomotionTuning LoadDefault()
        {
            var t = Resources.Load<LocomotionTuning>(ResourcePath);
            return t != null ? t : CreateInstance<LocomotionTuning>();
        }
    }
}
