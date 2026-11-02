using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBody
{
    /// <summary>A ring of clips with their angles and natural ground speeds.</summary>
    public struct LocomotionSpeedRing
    {
        /// <summary>Radius of the ring in the blend tree.</summary>
        public float Radius;
        /// <summary>Clip angles in degrees, atan2(y, x).</summary>
        public float[] Angles;
        /// <summary>Natural ground speed in m/s at playback 1, per clip.</summary>
        public float[] Speeds;
    }

    /// <summary>Pure maths for the locomotion blend tree and playback speed.</summary>
    public static class LocomotionMath
    {
        static bool Bad(float f) => float.IsNaN(f) || float.IsInfinity(f);

        /// <summary>Body-frame velocity divided by the run speed, magnitude capped at maxRadius.</summary>
        public static Vector2 TreePosition(Vector2 localVelocity, float runSpeed, float maxRadius)
        {
            if (Bad(localVelocity.x) || Bad(localVelocity.y) || Bad(runSpeed) || runSpeed <= 0f) return Vector2.zero;
            var p = localVelocity / runSpeed;
            return Vector2.ClampMagnitude(p, Mathf.Max(0f, maxRadius));
        }

        /// <summary>World velocity in the body frame (x right, y forward, flattened on XZ).</summary>
        public static Vector2 BodyVelocity(Vector3 worldVelocity, Quaternion bodyRotation)
        {
            var fwd = bodyRotation * Vector3.forward; fwd.y = 0f;
            var right = bodyRotation * Vector3.right; right.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.forward;
            right = right.sqrMagnitude > 1e-8f ? right.normalized : Vector3.right;
            return new Vector2(Vector3.Dot(worldVelocity, right), Vector3.Dot(worldVelocity, fwd));
        }

        /// <summary>Fraction of the gap closed in dt with time constant tau: 1 - exp(-dt/tau).</summary>
        public static float DampFraction(float tau, float dt)
        {
            if (float.IsNaN(dt) || dt <= 0f) return 0f;
            if (float.IsNaN(tau) || tau <= 0f) return 1f;
            return 1f - Mathf.Exp(-dt / tau);
        }

        /// <summary>Moves a value toward its target with an exponential damp.</summary>
        public static float Damp(float current, float target, float tau, float dt)
            => current + (target - current) * DampFraction(tau, dt);

        /// <summary>Moves a vector toward its target with an exponential damp.</summary>
        public static Vector2 Damp(Vector2 current, Vector2 target, float tau, float dt)
            => current + (target - current) * DampFraction(tau, dt);

        /// <summary>Linear interpolation over (angle, value) pairs with wrap-around at 360 degrees.</summary>
        public static float AngularLerp(float angleDeg, IReadOnlyList<float> anglesDeg, IReadOnlyList<float> values)
        {
            int n = Math.Min(anglesDeg?.Count ?? 0, values?.Count ?? 0);
            if (n == 0) return 0f;
            if (n == 1) return values[0];
            float a = Wrap(angleDeg);
            // Find the nearest entry below (counter-clockwise gap smallest) and above.
            int lo = -1, hi = -1;
            float bestLo = float.MaxValue, bestHi = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float below = Wrap(a - anglesDeg[i]);   // how far entry i is below a
                float above = Wrap(anglesDeg[i] - a);   // how far entry i is above a
                if (below < bestLo) { bestLo = below; lo = i; }
                if (above < bestHi) { bestHi = above; hi = i; }
            }
            if (bestLo <= 1e-5f) return values[lo];
            if (bestHi <= 1e-5f) return values[hi];
            float span = bestLo + bestHi;
            if (span <= 1e-6f) return values[lo];
            return Mathf.Lerp(values[lo], values[hi], bestLo / span);
        }

        static float Wrap(float deg)
        {
            float r = deg % 360f;
            return r < 0f ? r + 360f : r;
        }

        /// <summary>
        /// The tree's ground speed (m/s at state speed 1) at tree position p: the rings' speeds (each clip's natural speed times its
        /// time scale in the tree) by angle; inside the walk ring the idle blends in (shorter strides); between the rings a lerp; past the
        /// run ring the run ring's, moving toward the sprint point's speed in its direction (a cosine cone) up to the sprint radius.
        /// The crouch set passes the same ring as walk and run: beyond it, the walk ring's speeds.
        /// </summary>
        public static float TreeGroundSpeed(Vector2 p, LocomotionSpeedRing walk, LocomotionSpeedRing run, LocomotionSpeedRing sprint = default)
        {
            float m = p.magnitude;
            float a = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            float nW = AngularLerp(a, walk.Angles, walk.Speeds);
            if (m <= walk.Radius) return walk.Radius > 0f ? nW * m / walk.Radius : nW;
            if (run.Radius <= walk.Radius) return nW;
            float nR = AngularLerp(a, run.Angles, run.Speeds);
            if (m <= run.Radius) return Mathf.Lerp(nW, nR, (m - walk.Radius) / (run.Radius - walk.Radius));
            int count = Math.Min(sprint.Angles?.Length ?? 0, sprint.Speeds?.Length ?? 0);
            if (count == 0 || sprint.Radius <= run.Radius) return nR;
            float t = Mathf.Clamp01((m - run.Radius) / (sprint.Radius - run.Radius));
            float best = 0f, nS = nR;
            for (int i = 0; i < count; i++)
            {
                float cone = Mathf.Clamp01(Mathf.Cos((a - sprint.Angles[i]) * Mathf.Deg2Rad));
                if (cone > best) { best = cone; nS = sprint.Speeds[i]; }
            }
            return Mathf.Lerp(nR, nS, t * best);
        }

        /// <summary>
        /// The locomotion state's speed so the feet match the ground: the body's speed (|p| times the run speed) over
        /// <see cref="TreeGroundSpeed"/>. Inside the walk ring the ratio is the ring's (constant by radius), eased in from 1 at rest (the
        /// idle keeps its own pace) to full at half the walk ring. A ground speed of 0 gives 1; the result is clamped to [min, max].
        /// </summary>
        public static float Playback(Vector2 p, float runSpeed, LocomotionSpeedRing walk, LocomotionSpeedRing run, float min, float max)
            => Playback(p, runSpeed, walk, run, default, min, max);

        /// <summary>As <see cref="Playback(Vector2, float, LocomotionSpeedRing, LocomotionSpeedRing, float, float)"/> with the sprint point(s).</summary>
        public static float Playback(Vector2 p, float runSpeed, LocomotionSpeedRing walk, LocomotionSpeedRing run, LocomotionSpeedRing sprint, float min, float max)
        {
            float m = p.magnitude;
            float result;
            if (m <= walk.Radius)
            {
                float a = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
                float nW = AngularLerp(a, walk.Angles, walk.Speeds);
                float ratio = nW <= 1e-4f ? 1f : walk.Radius * runSpeed / nW;
                float w = walk.Radius > 0f ? Mathf.Clamp01(m / (0.5f * walk.Radius)) : 1f;
                result = Mathf.Lerp(1f, ratio, w);
            }
            else
            {
                float natural = TreeGroundSpeed(p, walk, run, sprint);
                result = natural <= 1e-4f ? 1f : m * runSpeed / natural;
            }
            if (Bad(result)) result = 1f;
            return Mathf.Clamp(result, min, max);
        }

        /// <summary>Body-frame direction (x right, y forward, normalised) of a world point seen from a body; zero when degenerate.</summary>
        public static Vector2 DirectionFrom(Vector3 from, Vector3 position, Quaternion rotation)
        {
            var d = BodyVelocity(from - position, rotation);
            if (Bad(d.x) || Bad(d.y) || d.sqrMagnitude < 1e-8f) return Vector2.zero;
            return d.normalized;
        }
    }
}
