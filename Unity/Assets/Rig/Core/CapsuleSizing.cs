using UnityEngine;

namespace SdfRig
{
    /// <summary>Plain geometry for the body's hit capsules (ticket 43): how a head capsule is resized for a bigger or smaller head (ticket 45).</summary>
    public static class CapsuleSizing
    {
        /// <summary>
        /// Resizes a trigger capsule that runs along the bone's Y from <paramref name="bottom"/> (kept) so it holds a head of
        /// <paramref name="radius"/> up to <paramref name="top"/>: radius, height (never under two radii) and centre.
        /// </summary>
        public static void FitHead(CapsuleCollider capsule, float bottom, float top, float radius)
        {
            var height = Mathf.Max(top - bottom, 2 * radius);
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = new Vector3(capsule.center.x, bottom + height * 0.5f, capsule.center.z);
        }

        /// <summary>The capsule's lowest and highest local Y.</summary>
        public static Vector2 Extent(CapsuleCollider capsule) =>
            new Vector2(capsule.center.y - capsule.height * 0.5f, capsule.center.y + capsule.height * 0.5f);
    }
}
