using UnityEngine;

namespace SdfBody
{
    /// <summary>The combat rules of the body (from BattleRoyale's ticket 06), pure (no scene): the reload stretch, the hit blend, the automatic-fire rule. The directional deaths are not ported: a death is the ragdoll of ticket 51.</summary>
    public static class CombatRules
    {
        /// <summary>The mirrored hit baked from <c>soldier_hit</c>.</summary>
        public const string MirroredHitClip = "soldier_hit_mirror";

        /// <summary>The <c>Reload</c> state's speed so a clip of <paramref name="clipSeconds"/> lasts <paramref name="reloadSeconds"/>, clamped.</summary>
        public static float ReloadSpeed(float clipSeconds, float reloadSeconds, float min, float max)
        {
            if (!(clipSeconds > 0f) || !(reloadSeconds > 0f) || float.IsInfinity(reloadSeconds)) return 1f;
            return Mathf.Clamp(clipSeconds / reloadSeconds, min, max);
        }

        /// <summary>The <c>Hit</c> tree's position from where the hit came (body frame): its x, clamped to [−1, 1].</summary>
        public static float HitBlend(Vector2 from) => float.IsNaN(from.x) ? 0f : Mathf.Clamp(from.x, -1f, 1f);
    }

    /// <summary>
    /// The automatic-fire rule: a shot within <c>burstInterval</c> of the one before makes the fire automatic (the upper body loops the
    /// burst clip instead of restarting the single shot); no shot for <c>holdSeconds</c> ends it. Pure, so the tests drive the clock.
    /// </summary>
    public struct FireCadence
    {
        float lastShot;
        bool any;

        /// <summary>True while the fire is automatic.</summary>
        public bool Automatic { get; private set; }

        /// <summary>A shot at <paramref name="now"/>; returns <see cref="Automatic"/>.</summary>
        public bool Shot(float now, float burstInterval)
        {
            Automatic = any && now - lastShot <= burstInterval;
            lastShot = now;
            any = true;
            return Automatic;
        }

        /// <summary>Ends the automatic fire once no shot came for <paramref name="holdSeconds"/>; true when it just ended.</summary>
        public bool Tick(float now, float holdSeconds)
        {
            if (!Automatic || now - lastShot <= holdSeconds) return false;
            Automatic = false;
            return true;
        }

        /// <summary>Forget everything (a reload, a death, a revive).</summary>
        public void Reset()
        {
            Automatic = false;
            any = false;
            lastShot = 0f;
        }
    }
}
