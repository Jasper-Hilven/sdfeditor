namespace SDFPipeline.Meshing;

/// <summary>
/// Least squares for the 3 × 3 symmetric systems of the QEF and the quadrics, by eigendecomposition: directions the data says little
/// about (small eigenvalues) are left out, so the solution stays at the point it is biased towards along them rather than flying off.
/// </summary>
internal static class SymmetricSolver
{
    /// <summary>
    /// The x nearest <paramref name="bias"/> that minimises |A x − b|-like energy for symmetric A: x = bias + A⁺ (b − A bias), with
    /// eigenvalues below <paramref name="relativeCutoff"/> × the largest treated as zero.
    /// </summary>
    public static (double x, double y, double z) Solve(double[] a, (double x, double y, double z) b, (double x, double y, double z) bias,
        double relativeCutoff)
    {
        var r = (x: b.x - (a[0] * bias.x + a[1] * bias.y + a[2] * bias.z),
            y: b.y - (a[1] * bias.x + a[3] * bias.y + a[4] * bias.z),
            z: b.z - (a[2] * bias.x + a[4] * bias.y + a[5] * bias.z));

        var (values, vectors) = Eigen(a);
        var largest = Math.Max(Math.Abs(values[0]), Math.Max(Math.Abs(values[1]), Math.Abs(values[2])));
        if (largest <= 0 || !double.IsFinite(largest))
            return bias;

        double dx = 0, dy = 0, dz = 0;
        for (var i = 0; i < 3; i++)
        {
            if (Math.Abs(values[i]) < relativeCutoff * largest)
                continue;
            var (vx, vy, vz) = (vectors[0, i], vectors[1, i], vectors[2, i]);
            var scale = (vx * r.x + vy * r.y + vz * r.z) / values[i];
            dx += scale * vx;
            dy += scale * vy;
            dz += scale * vz;
        }
        return (bias.x + dx, bias.y + dy, bias.z + dz);
    }

    /// <summary>Eigenvalues and eigenvectors (columns) of the symmetric matrix [a0 a1 a2; a1 a3 a4; a2 a4 a5], by Jacobi rotations.</summary>
    public static (double[] values, double[,] vectors) Eigen(double[] a)
    {
        var m = new double[3, 3]
        {
            { a[0], a[1], a[2] },
            { a[1], a[3], a[4] },
            { a[2], a[4], a[5] },
        };
        var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

        for (var sweep = 0; sweep < 16; sweep++)
        {
            var off = m[0, 1] * m[0, 1] + m[0, 2] * m[0, 2] + m[1, 2] * m[1, 2];
            var diagonal = m[0, 0] * m[0, 0] + m[1, 1] * m[1, 1] + m[2, 2] * m[2, 2];
            if (off <= 1e-30 * Math.Max(diagonal, 1e-300))
                break;

            for (var p = 0; p < 2; p++)
            for (var q = p + 1; q < 3; q++)
            {
                if (Math.Abs(m[p, q]) < 1e-300)
                    continue;
                var theta = (m[q, q] - m[p, p]) / (2 * m[p, q]);
                var t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                if (theta == 0) t = 1;
                var c = 1 / Math.Sqrt(t * t + 1);
                var s = t * c;

                for (var k = 0; k < 3; k++)
                {
                    var mkp = m[k, p];
                    var mkq = m[k, q];
                    m[k, p] = c * mkp - s * mkq;
                    m[k, q] = s * mkp + c * mkq;
                }
                for (var k = 0; k < 3; k++)
                {
                    var mpk = m[p, k];
                    var mqk = m[q, k];
                    m[p, k] = c * mpk - s * mqk;
                    m[q, k] = s * mpk + c * mqk;
                }
                for (var k = 0; k < 3; k++)
                {
                    var vkp = v[k, p];
                    var vkq = v[k, q];
                    v[k, p] = c * vkp - s * vkq;
                    v[k, q] = s * vkp + c * vkq;
                }
            }
        }
        return ([m[0, 0], m[1, 1], m[2, 2]], v);
    }
}
