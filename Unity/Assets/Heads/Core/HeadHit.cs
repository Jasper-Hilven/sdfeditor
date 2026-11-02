using System;
using System.Collections.Generic;
using SdfRig;
using UnityEngine;

namespace SdfHeads
{
    /// <summary>
    /// Where a shot counts as a headshot on a worn head: an upright ellipsoid (<see cref="RadiusAcross"/> over the width and depth,
    /// <see cref="RadiusUp"/> up and down) round <see cref="Centre"/>, which is in the head's own frame (the origin is the soldier's head
    /// socket, ticket 43).
    /// </summary>
    public readonly struct HeadShape
    {
        public HeadShape(Vector3 centre, float radiusAcross, float radiusUp)
        {
            Centre = centre;
            RadiusAcross = radiusAcross;
            RadiusUp = radiusUp;
        }

        public Vector3 Centre { get; }
        public float RadiusAcross { get; }
        public float RadiusUp { get; }

        public override string ToString() => $"centre {Centre}, radii {RadiusAcross:0.000} across, {RadiusUp:0.000} up";
    }

    /// <summary>
    /// The head's hit shape and the fairness rule (ticket 45): each head's hit shape follows its own bounds, but headshots must stay fair,
    /// so no head is easier to hit than another. The hit radii are held within <see cref="Tolerance"/> (15 %) of one reference size, whatever
    /// the head's own size: a big shaggy head hits as large as a small one, within that band. Pure geometry, tested without the models.
    /// </summary>
    public static class HeadHit
    {
        /// <summary>How far a head's hit radii may be from the reference, as a share of it: the same size across heads within 15 %.</summary>
        public const float Tolerance = 0.15f;

        /// <summary>The reference half-width (across and deep) and half-height of a head's hit shape, metres: a 0.40 by 0.44 m head.</summary>
        public const float ReferenceAcross = 0.20f, ReferenceUp = 0.22f;

        /// <summary>The share of a head's bounds the hit shape starts from: tufts, icicles, horns and stems on the bounds' edges do not count.</summary>
        public const float BoundsShare = 0.85f;

        /// <summary>The band a hit radius lies in: [reference * (1 - tolerance), reference * (1 + tolerance)].</summary>
        public static Vector2 Band(float reference) => new Vector2(reference * (1 - Tolerance), reference * (1 + Tolerance));

        /// <summary>A value held to the band round the reference.</summary>
        public static float Held(float value, float reference)
        {
            var band = Band(reference);
            return Mathf.Clamp(value, band.x, band.y);
        }

        /// <summary>The hit shape of a head whose bounds (in the head's frame) are <paramref name="centre"/> and <paramref name="size"/>.</summary>
        public static HeadShape Fit(Vector3 centre, Vector3 size)
        {
            var across = Mathf.Max(size.x, size.z) * 0.5f * BoundsShare;
            var up = size.y * 0.5f * BoundsShare;
            return new HeadShape(centre, Held(across, ReferenceAcross), Held(up, ReferenceUp));
        }

        public static HeadShape Fit(Bounds bounds) => Fit(bounds.center, bounds.size);

        /// <summary>Whether a shape's radii are inside the fair band (small float slack).</summary>
        public static bool IsFair(HeadShape shape)
        {
            const float slack = 1e-5f;
            var across = Band(ReferenceAcross);
            var up = Band(ReferenceUp);
            return shape.RadiusAcross >= across.x - slack && shape.RadiusAcross <= across.y + slack
                && shape.RadiusUp >= up.x - slack && shape.RadiusUp <= up.y + slack;
        }

        /// <summary>
        /// The ellipsoid's volume relative to the reference's: what "how easy to hit" comes to over a whole head. The fair band bounds it to
        /// (1 - tolerance)^3 .. (1 + tolerance)^3, about 0.61 to 1.52; <see cref="AreFair"/> asks for the smaller spread of its cross-sections.
        /// </summary>
        public static float RelativeVolume(HeadShape shape) =>
            shape.RadiusAcross * shape.RadiusAcross * shape.RadiusUp / (ReferenceAcross * ReferenceAcross * ReferenceUp);

        /// <summary>
        /// Whether every shape is fair and no two heads differ by more than the band allows (the biggest radius over the smallest of the
        /// same kind at most (1 + tolerance) / (1 - tolerance)).
        /// </summary>
        public static bool AreFair(IEnumerable<HeadShape> shapes)
        {
            var any = false;
            float minAcross = float.MaxValue, maxAcross = 0, minUp = float.MaxValue, maxUp = 0;
            foreach (var shape in shapes)
            {
                if (!IsFair(shape))
                    return false;
                any = true;
                minAcross = Mathf.Min(minAcross, shape.RadiusAcross);
                maxAcross = Mathf.Max(maxAcross, shape.RadiusAcross);
                minUp = Mathf.Min(minUp, shape.RadiusUp);
                maxUp = Mathf.Max(maxUp, shape.RadiusUp);
            }
            var spread = (1 + Tolerance) / (1 - Tolerance) + 1e-4f;
            return !any || (maxAcross <= minAcross * spread && maxUp <= minUp * spread);
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
