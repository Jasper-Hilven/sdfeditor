using System.Collections.Generic;
using UnityEngine;

namespace SdfHeads
{
    /// <summary>
    /// The tight fit of a head's mesh (ticket 45): the upright ellipsoid with the mesh's centre of volume, the mesh's spread along each axis and
    /// the mesh's volume. It is the head's hit shape (<see cref="HeadShape"/>) and the size every head is normalized by (<see cref="HeadSize"/>).
    /// Spikes, tufts, icicles, hats and stems hold little volume, so they hardly move it.
    /// </summary>
    public readonly struct HeadFit
    {
        public HeadFit(Vector3 centre, Vector3 radii, float volume)
        {
            Centre = centre;
            Radii = radii;
            Volume = volume;
        }

        /// <summary>The mesh's centre of volume, in the head's frame (the origin is the soldier's head socket).</summary>
        public Vector3 Centre { get; }

        /// <summary>Half the ellipsoid's size along x, y and z.</summary>
        public Vector3 Radii { get; }

        /// <summary>The mesh's enclosed volume in cubic metres, which the ellipsoid has too.</summary>
        public float Volume { get; }

        public HeadShape Shape => new HeadShape(Centre, Radii);

        public override string ToString() => $"centre {Centre:F3}, radii {Radii:F3}, volume {Volume:0.0000} m3";
    }

    /// <summary>
    /// Adds the triangles of a closed surface (any winding, as long as it is the same all over, in any number of pieces) and gives its
    /// <see cref="HeadFit"/>: the volume and the first and second moments come from the divergence theorem, a signed tetrahedron to the origin
    /// per triangle. The radii are those of the ellipsoid with the same second moments (sqrt(5 variance) along an axis), brought to the
    /// mesh's volume.
    /// </summary>
    public sealed class HeadFitter
    {
        double volume;
        double cx, cy, cz;
        double sxx, syy, szz;

        /// <summary>Adds one triangle.</summary>
        public void Add(Vector3 a, Vector3 b, Vector3 c)
        {
            double v = (a.x * (b.y * c.z - b.z * c.y) + a.y * (b.z * c.x - b.x * c.z) + a.z * (b.x * c.y - b.y * c.x)) / 6.0;
            volume += v;
            cx += v * (a.x + b.x + c.x) / 4.0;
            cy += v * (a.y + b.y + c.y) / 4.0;
            cz += v * (a.z + b.z + c.z) / 4.0;
            sxx += v / 10.0 * (a.x * a.x + b.x * b.x + c.x * c.x + a.x * b.x + a.x * c.x + b.x * c.x);
            syy += v / 10.0 * (a.y * a.y + b.y * b.y + c.y * c.y + a.y * b.y + a.y * c.y + b.y * c.y);
            szz += v / 10.0 * (a.z * a.z + b.z * b.z + c.z * c.z + a.z * b.z + a.z * c.z + b.z * c.z);
        }

        /// <summary>Adds an indexed mesh, its vertices carried through <paramref name="toHeadFrame"/>.</summary>
        public void Add(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> triangles, Matrix4x4 toHeadFrame)
        {
            for (var i = 0; i + 2 < triangles.Count; i += 3)
                Add(toHeadFrame.MultiplyPoint3x4(vertices[triangles[i]]), toHeadFrame.MultiplyPoint3x4(vertices[triangles[i + 1]]),
                    toHeadFrame.MultiplyPoint3x4(vertices[triangles[i + 2]]));
        }

        /// <summary>The fit; a zero fit for nothing or a flat surface.</summary>
        public HeadFit Result()
        {
            var sign = volume < 0 ? -1.0 : 1.0;
            var v = volume * sign;
            if (v < 1e-9)
                return default;
            double mx = cx * sign / v, my = cy * sign / v, mz = cz * sign / v;
            double varX = sxx * sign / v - mx * mx, varY = syy * sign / v - my * my, varZ = szz * sign / v - mz * mz;
            double rx = System.Math.Sqrt(System.Math.Max(0, 5 * varX)), ry = System.Math.Sqrt(System.Math.Max(0, 5 * varY)), rz = System.Math.Sqrt(System.Math.Max(0, 5 * varZ));
            var ellipsoid = 4.0 / 3.0 * System.Math.PI * rx * ry * rz;
            var k = ellipsoid > 0 ? System.Math.Pow(v / ellipsoid, 1.0 / 3.0) : 0;
            return new HeadFit(new Vector3((float)mx, (float)my, (float)mz), new Vector3((float)(rx * k), (float)(ry * k), (float)(rz * k)), (float)v);
        }
    }

    /// <summary>
    /// The one size every head is normalized to (the user's decision on ticket 45's phase 1): 1.2 times the mean of the phase-1 Yeti's and
    /// Jack-o'-Lantern's tight fits. Each head carries one uniform scale in its model so its fit is within <see cref="Tolerance"/> (10 %) of
    /// this in volume and in each axis's radius. Pure data and arithmetic, tested without the models.
    /// </summary>
    public static class HeadSize
    {
        /// <summary>Phase 1's fits, before any scaling (radii x, y, z in metres, volume): the Yeti, then the pumpkin.</summary>
        public static readonly Vector3 Phase1YetiRadii = new Vector3(0.2701f, 0.2519f, 0.2882f), Phase1PumpkinRadii = new Vector3(0.2130f, 0.2164f, 0.2030f);
        public const float Phase1YetiVolume = 0.08214f, Phase1PumpkinVolume = 0.03918f;

        /// <summary>The user's "20 % bigger", applied to the common size.</summary>
        public const float Bigger = 1.2f;

        /// <summary>How far a head's volume and each radius may be from the target, as a share of it.</summary>
        public const float Tolerance = 0.10f;

        /// <summary>1.2 times the mean of the two phase-1 radii, per axis.</summary>
        public static Vector3 TargetRadii => Bigger * 0.5f * (Phase1YetiRadii + Phase1PumpkinRadii);

        /// <summary>1.2 cubed times the mean of the two phase-1 volumes (a uniform 1.2 scale multiplies a volume by 1.2 cubed).</summary>
        public static float TargetVolume => Bigger * Bigger * Bigger * 0.5f * (Phase1YetiVolume + Phase1PumpkinVolume);

        /// <summary>The uniform scale that brings a fit's volume to the target.</summary>
        public static float ScaleFor(HeadFit fit) => Mathf.Pow(TargetVolume / fit.Volume, 1f / 3f);

        /// <summary>Whether the volume and each radius are within the tolerance of the target.</summary>
        public static bool IsNormalized(HeadFit fit) => Deviation(fit) <= Tolerance + 1e-4f;

        /// <summary>The largest share a head's volume or radius is off the target by.</summary>
        public static float Deviation(HeadFit fit)
        {
            var radii = TargetRadii;
            return Mathf.Max(Mathf.Abs(fit.Volume / TargetVolume - 1),
                Mathf.Abs(fit.Radii.x / radii.x - 1), Mathf.Abs(fit.Radii.y / radii.y - 1), Mathf.Abs(fit.Radii.z / radii.z - 1));
        }
    }
}
