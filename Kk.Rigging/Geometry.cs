using System.Numerics;

namespace Kk.Rigging;

/// <summary>A 3D vector in double precision; the rigging maths composes many transforms, so it avoids float drift.</summary>
internal readonly record struct D3(double X, double Y, double Z)
{
    public static readonly D3 Zero = new(0, 0, 0);
    public static readonly D3 UnitX = new(1, 0, 0);
    public static readonly D3 UnitY = new(0, 1, 0);
    public static readonly D3 UnitZ = new(0, 0, 1);

    public static D3 operator +(D3 a, D3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static D3 operator -(D3 a, D3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static D3 operator -(D3 a) => new(-a.X, -a.Y, -a.Z);
    public static D3 operator *(D3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static D3 operator *(double s, D3 a) => a * s;
    public static D3 operator /(D3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double Dot(D3 o) => X * o.X + Y * o.Y + Z * o.Z;
    public D3 Cross(D3 o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
    public double Length => Math.Sqrt(Dot(this));
    public D3 Normalized() => Length is var l && l > 1e-12 ? this / l : this;
    public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);
    public static D3 From(Vector3 v) => new(v.X, v.Y, v.Z);
}

/// <summary>A 3x3 matrix acting on column vectors (m * v), double precision.</summary>
internal readonly struct M3
{
    public readonly double A, B, C, D, E, F, G, H, I; // rows: (A B C) (D E F) (G H I)

    public M3(double a, double b, double c, double d, double e, double f, double g, double h, double i)
        => (A, B, C, D, E, F, G, H, I) = (a, b, c, d, e, f, g, h, i);

    public static readonly M3 Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public static M3 FromColumns(D3 x, D3 y, D3 z) => new(x.X, y.X, z.X, x.Y, y.Y, z.Y, x.Z, y.Z, z.Z);
    public static M3 Diagonal(double x, double y, double z) => new(x, 0, 0, 0, y, 0, 0, 0, z);
    public static M3 Scale(double s) => Diagonal(s, s, s);

    public D3 Column(int index) => index switch { 0 => new(A, D, G), 1 => new(B, E, H), _ => new(C, F, I) };

    public static D3 operator *(M3 m, D3 v) => new(m.A * v.X + m.B * v.Y + m.C * v.Z, m.D * v.X + m.E * v.Y + m.F * v.Z, m.G * v.X + m.H * v.Y + m.I * v.Z);

    public static M3 operator *(M3 l, M3 r) => FromColumns(l * r.Column(0), l * r.Column(1), l * r.Column(2));

    public M3 Transposed() => new(A, D, G, B, E, H, C, F, I);

    public double Determinant => A * (E * I - F * H) - B * (D * I - F * G) + C * (D * H - E * G);

    public M3 Inverse()
    {
        var det = Determinant;
        if (Math.Abs(det) < 1e-18)
            throw new InvalidOperationException("The matrix cannot be inverted");
        var k = 1 / det;
        return new M3(
            (E * I - F * H) * k, (C * H - B * I) * k, (B * F - C * E) * k,
            (F * G - D * I) * k, (A * I - C * G) * k, (C * D - A * F) * k,
            (D * H - E * G) * k, (B * G - A * H) * k, (A * E - B * D) * k);
    }

    /// <summary>The rotation by <paramref name="angle"/> radians about <paramref name="axis"/> (right-handed).</summary>
    public static M3 Rotation(D3 axis, double angle)
    {
        var u = axis.Normalized();
        double c = Math.Cos(angle), s = Math.Sin(angle), t = 1 - c;
        return new M3(
            t * u.X * u.X + c, t * u.X * u.Y - s * u.Z, t * u.X * u.Z + s * u.Y,
            t * u.X * u.Y + s * u.Z, t * u.Y * u.Y + c, t * u.Y * u.Z - s * u.X,
            t * u.X * u.Z - s * u.Y, t * u.Y * u.Z + s * u.X, t * u.Z * u.Z + c);
    }

    /// <summary>The rotation of format 1's <c>Rotate rotation=(x, y, z)</c>: about X, then Y, then Z (matrix Rz * Ry * Rx).</summary>
    public static M3 EulerXyz(double x, double y, double z) =>
        Rotation(D3.UnitZ, z) * Rotation(D3.UnitY, y) * Rotation(D3.UnitX, x);

    /// <summary>The (x, y, z) with <see cref="EulerXyz"/> equal to this rotation (this must be a proper rotation).</summary>
    public D3 ToEulerXyz()
    {
        var sy = -G;
        if (Math.Abs(sy) < 1 - 1e-9)
            return new D3(Math.Atan2(H, I), Math.Asin(sy), Math.Atan2(D, A));
        // Gimbal lock: Y is a quarter turn, X and Z turn about the same axis; put it all in X
        return new D3(Math.Atan2(-F, E), Math.Sign(sy) * Math.PI / 2, 0);
    }

    public static M3 FromQuaternion(Quaternion q)
    {
        var n = Quaternion.Normalize(q);
        double x = n.X, y = n.Y, z = n.Z, w = n.W;
        return new M3(
            1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
            2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
            2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y));
    }

    public Quaternion ToQuaternion()
    {
        var trace = A + E + I;
        double x, y, z, w;
        if (trace > 0)
        {
            var s = Math.Sqrt(trace + 1) * 2;
            w = s / 4; x = (H - F) / s; y = (C - G) / s; z = (D - B) / s;
        }
        else if (A > E && A > I)
        {
            var s = Math.Sqrt(1 + A - E - I) * 2;
            w = (H - F) / s; x = s / 4; y = (B + D) / s; z = (C + G) / s;
        }
        else if (E > I)
        {
            var s = Math.Sqrt(1 + E - A - I) * 2;
            w = (C - G) / s; x = (B + D) / s; y = s / 4; z = (F + H) / s;
        }
        else
        {
            var s = Math.Sqrt(1 + I - A - E) * 2;
            w = (D - B) / s; x = (C + G) / s; y = (F + H) / s; z = s / 4;
        }
        return Quaternion.Normalize(new Quaternion((float)x, (float)y, (float)z, (float)w));
    }
}

/// <summary>An affine map p -> Linear * p + Offset.</summary>
internal readonly record struct Aff(M3 Linear, D3 Offset)
{
    public static readonly Aff Identity = new(M3.Identity, D3.Zero);

    public D3 Apply(D3 p) => Linear * p + Offset;

    /// <summary>The map that applies <paramref name="inner"/> first, then this one.</summary>
    public Aff After(Aff inner) => new(Linear * inner.Linear, Linear * inner.Offset + Offset);

    public Aff Inverse()
    {
        var inverse = Linear.Inverse();
        return new Aff(inverse, -(inverse * Offset));
    }
}
