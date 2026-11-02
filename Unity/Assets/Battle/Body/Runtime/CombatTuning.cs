using System;
using UnityEngine;

namespace SdfBody
{
    /// <summary>
    /// The combat tuning asset (<c>Assets/Battle/Body/Animator/Resources/SoldierCombat.asset</c>): the fades of the upper body layer,
    /// the automatic-fire rule and the reload stretch. Defaults in code; a
    /// test checks the asset against them. The controller builder reads the fades; <see cref="SoldierAnimator"/> reads the rest.
    /// </summary>
    [CreateAssetMenu(menuName = "Sdf/Body Combat", fileName = "SoldierCombat")]
    public sealed class CombatTuning : ScriptableObject
    {
        /// <summary>Resources path.</summary>
        public const string ResourcePath = "SoldierCombat";

        [Header("Upper body (layer UpperBody)")]
        [Tooltip("Seconds of the cross-fade into a single shot (it restarts on every shot).")]
        public float FireFadeSeconds = 0.03f;
        [Tooltip("Seconds of the fade from the end of a shot back to the aim (the locomotion's arms).")]
        public float FireReturnSeconds = 0.12f;
        [Tooltip("A shot this soon after the one before makes the fire automatic: the burst clip loops (12 shots/s = 0.083 s).")]
        public float BurstInterval = 0.16f;
        [Tooltip("No shot for this long ends the automatic fire.")]
        public float BurstHoldSeconds = 0.22f;
        [Tooltip("Seconds of the cross-fade into and out of the burst loop.")]
        public float BurstFadeSeconds = 0.08f;
        [Tooltip("Seconds of the cross-fade into a reload and back out of it.")]
        public float ReloadFadeSeconds = 0.15f;
        [Tooltip("The reload clip's speed is kept in [ReloadMinSpeed, ReloadMaxSpeed] whatever the weapon's reload time.")]
        public float ReloadMinSpeed = 0.25f;
        public float ReloadMaxSpeed = 4f;
        [Tooltip("Length of soldier_reload at speed 1 (set by the controller builder from the clip).")]
        public float ReloadClipSeconds = 3.3f;
        [Tooltip("Seconds of the cross-fade into a flinch and back out of it.")]
        public float HitFadeSeconds = 0.05f;
        public float HitReturnSeconds = 0.12f;

        /// <summary>The committed asset, or the code defaults when it is missing.</summary>
        public static CombatTuning LoadDefault()
        {
            var t = Resources.Load<CombatTuning>(ResourcePath);
            return t != null ? t : CreateInstance<CombatTuning>();
        }
    }
}
