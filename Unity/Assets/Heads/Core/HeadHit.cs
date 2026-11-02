using SdfRig;
using UnityEngine;

namespace SdfHeads
{
    /// <summary>
    /// Where a shot counts as a headshot on a worn head: an ellipsoid with the radii <see cref="Radii"/> (along the head's x, y and z) round
    /// <see cref="Centre"/>, in the head's own frame (the origin is the soldier's head socket, ticket 43; +Z is where it faces). It is the tight
    /// fit of the head's own mesh (<see cref="HeadFit"/>): the hitbox equals the visible head, with no clamp (the user's decision, ticket 45).
    /// </summary>
    public readonly struct HeadShape
    {
        public HeadShape(Vector3 centre, Vector3 radii)
        {
            Centre = centre;
            Radii = radii;
        }

        /// <summary>A shape round about the vertical axis: <paramref name="across"/> over the width and depth, <paramref name="up"/> high.</summary>
        public HeadShape(Vector3 centre, float across, float up) : this(centre, new Vector3(across, up, across))
        {
        }

        public Vector3 Centre { get; }
        public Vector3 Radii { get; }

        /// <summary>The larger of the half width and the half depth.</summary>
        public float RadiusAcross => Mathf.Max(Radii.x, Radii.z);

        public float RadiusUp => Radii.y;

        public override string ToString() => $"centre {Centre:F3}, radii {Radii:F3}";
    }

    /// <summary>
    /// The head's hit shape (ticket 45): the shot test <c>Combat.Trace</c> uses for a worn head, and the capsule resize. Pure geometry, tested
    /// without the models.
    /// </summary>
    public static class HeadHit
    {
        /// <summary>
        /// Whether a ray (or a ball of radius <paramref name="pad"/> swept along it) hits the shape of a head worn by a player standing at
        /// <paramref name="feet"/> turned <paramref name="yawDegrees"/> about the vertical; the distance along the ray in <paramref name="t"/> and
        /// the surface normal (world) in <paramref name="normal"/>. False when it misses, or starts inside.
        /// </summary>
        public static bool Hit(HeadShape shape, Vector3 feet, float yawDegrees, Vector3 origin, Vector3 direction, float pad, out float t, out Vector3 normal)
        {
            var turn = Quaternion.Euler(0, yawDegrees, 0);
            var back = Quaternion.Inverse(turn);
            var centre = feet + Vector3.up * SoldierBody.HeadTopHeight + turn * shape.Centre;
            var radii = shape.Radii + Vector3.one * pad;
            var scale = new Vector3(1 / radii.x, 1 / radii.y, 1 / radii.z);
            var o = Vector3.Scale(back * (origin - centre), scale);
            var d = Vector3.Scale(back * direction, scale);
            normal = Vector3.up;
            t = 0;
            var a = Vector3.Dot(d, d);
            var b = 2 * Vector3.Dot(o, d);
            var c = Vector3.Dot(o, o) - 1;
            if (c <= 0 || a <= 0)
                return false;
            var disc = b * b - 4 * a * c;
            if (disc < 0)
                return false;
            t = (-b - Mathf.Sqrt(disc)) / (2 * a);
            if (t < 0)
                return false;
            var local = back * (origin + direction * t - centre);
            normal = turn * new Vector3(local.x / (radii.x * radii.x), local.y / (radii.y * radii.y), local.z / (radii.z * radii.z)).normalized;
            return true;
        }

        /// <summary>
        /// Resizes a head hit capsule (ticket 43's trigger capsule on the Head bone, running along its Y) to a shape: the radius across, and
        /// from the shape's lowest to its highest point. <paramref name="headFrame"/> is the head's own frame (the socket, or the soldier root
        /// at rest, which share its axes); the shape's points are carried through it into the capsule's space.
        /// </summary>
        public static void FitCapsule(CapsuleCollider capsule, HeadShape shape, Transform headFrame)
        {
            var low = capsule.transform.InverseTransformPoint(headFrame.TransformPoint(shape.Centre - Vector3.up * shape.RadiusUp));
            var high = capsule.transform.InverseTransformPoint(headFrame.TransformPoint(shape.Centre + Vector3.up * shape.RadiusUp));
            CapsuleSizing.FitHead(capsule, Mathf.Min(low.y, high.y), Mathf.Max(low.y, high.y), shape.RadiusAcross);
        }
    }
}
