using System.Numerics;

namespace SDFProject.Calculation;

internal static class SdfMath
{
    public static float Sign(float x) => x > 0 ? 1 : x < 0 ? -1 : 0;
    public static float Clamp(float x, float min, float max) => MathF.Min(MathF.Max(x, min), max);
    public static float Dot2(Vector2 v) => Vector2.Dot(v, v);
    public static float Dot2(Vector3 v) => Vector3.Dot(v, v);
    public static Vector2 Max0(Vector2 v) => Vector2.Max(v, Vector2.Zero);
    public static Vector3 Max0(Vector3 v) => Vector3.Max(v, Vector3.Zero);
    public static float MaxComponent(Vector3 v) => MathF.Max(v.X, MathF.Max(v.Y, v.Z));
    public static float MinComponent(Vector3 v) => MathF.Min(v.X, MathF.Min(v.Y, v.Z));
    public static Vector2 Xy(Vector3 v) => new(v.X, v.Y);
    public static Vector2 Xz(Vector3 v) => new(v.X, v.Z);

    public static float Length(Vector2 v) => v.Length();
    public static float Length(Vector3 v) => v.Length();

    public static Vector2 Rotate(Vector2 v, float c, float s) => new(c * v.X + s * v.Y, -s * v.X + c * v.Y);

    public static float SmoothMin(float a, float b, float k)
    {
        if (k <= 0) return MathF.Min(a, b);
        k *= 4f;
        var h = MathF.Max(k - MathF.Abs(a - b), 0f);
        return MathF.Min(a, b) - h * h * 0.25f / k;
    }

    #region 3D primitives

    public static float Sphere(Vector3 p, float r) => Length(p) - r;

    public static float Box(Vector3 p, Vector3 b)
    {
        var q = Vector3.Abs(p) - b;
        return Length(Max0(q)) + MathF.Min(MaxComponent(q), 0f);
    }

    public static float RoundBox(Vector3 p, Vector3 b, float r)
    {
        var q = Vector3.Abs(p) - b + new Vector3(r);
        return Length(Max0(q)) + MathF.Min(MaxComponent(q), 0f) - r;
    }

    public static float BoxFrame(Vector3 p, Vector3 b, float e)
    {
        p = Vector3.Abs(p) - b;
        var q = Vector3.Abs(p + new Vector3(e)) - new Vector3(e);
        return MathF.Min(MathF.Min(
                Length(Max0(new Vector3(p.X, q.Y, q.Z))) + MathF.Min(MathF.Max(p.X, MathF.Max(q.Y, q.Z)), 0f),
                Length(Max0(new Vector3(q.X, p.Y, q.Z))) + MathF.Min(MathF.Max(q.X, MathF.Max(p.Y, q.Z)), 0f)),
            Length(Max0(new Vector3(q.X, q.Y, p.Z))) + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, p.Z)), 0f));
    }

    public static float Torus(Vector3 p, float majorRadius, float minorRadius)
    {
        var q = new Vector2(Length(Xz(p)) - majorRadius, p.Y);
        return Length(q) - minorRadius;
    }

    public static float CappedTorus(Vector3 p, Vector2 sc, float ra, float rb)
    {
        p.X = MathF.Abs(p.X);
        var pxy = Xy(p);
        var k = sc.Y * p.X > sc.X * p.Y ? Vector2.Dot(pxy, sc) : Length(pxy);
        return MathF.Sqrt(Vector3.Dot(p, p) + ra * ra - 2f * ra * k) - rb;
    }

    public static float Link(Vector3 p, float le, float r1, float r2)
    {
        var q = new Vector3(p.X, MathF.Max(MathF.Abs(p.Y) - le, 0f), p.Z);
        return Length(new Vector2(Length(Xy(q)) - r1, q.Z)) - r2;
    }

    public static float InfiniteCylinder(Vector3 p, Vector2 center, float radius) => Length(Xz(p) - center) - radius;

    public static float Cone(Vector3 p, Vector2 c, float h)
    {
        var q = h * new Vector2(c.X / c.Y, -1f);
        var w = new Vector2(Length(Xz(p)), p.Y);
        var a = w - q * Clamp(Vector2.Dot(w, q) / Vector2.Dot(q, q), 0f, 1f);
        var b = w - q * new Vector2(Clamp(w.X / q.X, 0f, 1f), 1f);
        var k = Sign(q.Y);
        var d = MathF.Min(Vector2.Dot(a, a), Vector2.Dot(b, b));
        var s = MathF.Max(k * (w.X * q.Y - w.Y * q.X), k * (w.Y - q.Y));
        return MathF.Sqrt(d) * Sign(s);
    }

    public static float InfiniteCone(Vector3 p, Vector2 c)
    {
        var q = new Vector2(Length(Xz(p)), -p.Y);
        var d = Length(q - c * MathF.Max(Vector2.Dot(q, c), 0f));
        return d * (q.X * c.Y - q.Y * c.X < 0f ? -1f : 1f);
    }

    public static float Plane(Vector3 p, Vector3 n, float h) => Vector3.Dot(p, Vector3.Normalize(n)) + h;

    public static float HexPrism(Vector3 p, Vector2 h)
    {
        var k = new Vector3(-0.8660254f, 0.5f, 0.57735f);
        var kxy = new Vector2(k.X, k.Y);
        p = Vector3.Abs(p);
        var pxy = Xy(p) - 2f * MathF.Min(Vector2.Dot(kxy, Xy(p)), 0f) * kxy;
        var d = new Vector2(
            Length(pxy - new Vector2(Clamp(pxy.X, -k.Z * h.X, k.Z * h.X), h.X)) * Sign(pxy.Y - h.X),
            p.Z - h.Y);
        return MathF.Min(MathF.Max(d.X, d.Y), 0f) + Length(Max0(d));
    }

    public static float Capsule(Vector3 p, Vector3 a, Vector3 b, float r)
    {
        Vector3 pa = p - a, ba = b - a;
        var h = Clamp(Vector3.Dot(pa, ba) / Vector3.Dot(ba, ba), 0f, 1f);
        return Length(pa - ba * h) - r;
    }

    public static float VerticalCapsule(Vector3 p, float h, float r)
    {
        p.Y -= Clamp(p.Y, 0f, h);
        return Length(p) - r;
    }

    public static float CappedCylinder(Vector3 p, float r, float h)
    {
        var d = Vector2.Abs(new Vector2(Length(Xz(p)), p.Y)) - new Vector2(r, h);
        return MathF.Min(MathF.Max(d.X, d.Y), 0f) + Length(Max0(d));
    }

    public static float ArbitraryCappedCylinder(Vector3 p, Vector3 a, Vector3 b, float r)
    {
        var ba = b - a;
        var pa = p - a;
        var baba = Vector3.Dot(ba, ba);
        var paba = Vector3.Dot(pa, ba);
        var x = Length(pa * baba - ba * paba) - r * baba;
        var y = MathF.Abs(paba - baba * 0.5f) - baba * 0.5f;
        var x2 = x * x;
        var y2 = y * y * baba;
        var d = MathF.Max(x, y) < 0f ? -MathF.Min(x2, y2) : (x > 0f ? x2 : 0f) + (y > 0f ? y2 : 0f);
        return Sign(d) * MathF.Sqrt(MathF.Abs(d)) / baba;
    }

    public static float RoundedCylinder(Vector3 p, float ra, float rb, float h)
    {
        var d = new Vector2(Length(Xz(p)) - ra + rb, MathF.Abs(p.Y) - h + rb);
        return MathF.Min(MathF.Max(d.X, d.Y), 0f) + Length(Max0(d)) - rb;
    }

    public static float CappedCone(Vector3 p, float h, float r1, float r2)
    {
        var q = new Vector2(Length(Xz(p)), p.Y);
        var k1 = new Vector2(r2, h);
        var k2 = new Vector2(r2 - r1, 2f * h);
        var ca = new Vector2(q.X - MathF.Min(q.X, q.Y < 0f ? r1 : r2), MathF.Abs(q.Y) - h);
        var cb = q - k1 + k2 * Clamp(Vector2.Dot(k1 - q, k2) / Dot2(k2), 0f, 1f);
        var s = cb.X < 0f && ca.Y < 0f ? -1f : 1f;
        return s * MathF.Sqrt(MathF.Min(Dot2(ca), Dot2(cb)));
    }

    public static float ArbitraryCappedCone(Vector3 p, Vector3 a, Vector3 b, float ra, float rb)
    {
        var rba = rb - ra;
        var baba = Vector3.Dot(b - a, b - a);
        var papa = Vector3.Dot(p - a, p - a);
        var paba = Vector3.Dot(p - a, b - a) / baba;
        var x = MathF.Sqrt(MathF.Max(papa - paba * paba * baba, 0f));
        var cax = MathF.Max(0f, x - (paba < 0.5f ? ra : rb));
        var cay = MathF.Abs(paba - 0.5f) - 0.5f;
        var k = rba * rba + baba;
        var f = Clamp((rba * (x - ra) + paba * baba) / k, 0f, 1f);
        var cbx = x - ra - f * rba;
        var cby = paba - f;
        var s = cbx < 0f && cay < 0f ? -1f : 1f;
        return s * MathF.Sqrt(MathF.Min(cax * cax + cay * cay * baba, cbx * cbx + cby * cby * baba));
    }

    public static float SolidAngle(Vector3 p, Vector2 c, float ra)
    {
        var q = new Vector2(Length(Xz(p)), p.Y);
        var l = Length(q) - ra;
        var m = Length(q - c * Clamp(Vector2.Dot(q, c), 0f, ra));
        return MathF.Max(l, m * Sign(c.Y * q.X - c.X * q.Y));
    }

    public static float CutSphere(Vector3 p, float r, float h)
    {
        var w = MathF.Sqrt(r * r - h * h);
        var q = new Vector2(Length(Xz(p)), p.Y);
        var s = MathF.Max((h - r) * q.X * q.X + w * w * (h + r - 2f * q.Y), h * q.X - w * q.Y);
        return s < 0f ? Length(q) - r
            : q.X < w ? h - q.Y
            : Length(q - new Vector2(w, h));
    }

    public static float CutHollowSphere(Vector3 p, float r, float h, float t)
    {
        var w = MathF.Sqrt(r * r - h * h);
        var q = new Vector2(Length(Xz(p)), p.Y);
        return (h * q.X < w * q.Y ? Length(q - new Vector2(w, h)) : MathF.Abs(Length(q) - r)) - t;
    }

    public static float RoundCone(Vector3 p, float r1, float r2, float h)
    {
        var b = (r1 - r2) / h;
        var a = MathF.Sqrt(1f - b * b);
        var q = new Vector2(Length(Xz(p)), p.Y);
        var k = Vector2.Dot(q, new Vector2(-b, a));
        if (k < 0f) return Length(q) - r1;
        if (k > a * h) return Length(q - new Vector2(0f, h)) - r2;
        return Vector2.Dot(q, new Vector2(a, b)) - r1;
    }

    public static float ArbitraryRoundCone(Vector3 p, Vector3 a, Vector3 b, float r1, float r2)
    {
        var ba = b - a;
        var l2 = Vector3.Dot(ba, ba);
        var rr = r1 - r2;
        var a2 = l2 - rr * rr;
        var il2 = 1f / l2;

        var pa = p - a;
        var y = Vector3.Dot(pa, ba);
        var z = y - l2;
        var x2 = Dot2(pa * l2 - ba * y);
        var y2 = y * y * l2;
        var z2 = z * z * l2;

        var k = Sign(rr) * rr * rr * x2;
        if (Sign(z) * a2 * z2 > k) return MathF.Sqrt(x2 + z2) * il2 - r2;
        if (Sign(y) * a2 * y2 < k) return MathF.Sqrt(x2 + y2) * il2 - r1;
        return (MathF.Sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
    }

    public static float VesicaSegment(Vector3 p, Vector3 a, Vector3 b, float w)
    {
        var c = (a + b) * 0.5f;
        var l = Length(b - a);
        var v = (b - a) / l;
        var y = Vector3.Dot(p - c, v);
        var q = new Vector2(Length(p - c - y * v), MathF.Abs(y));

        var r = 0.5f * l;
        var d = 0.5f * (r * r - w * w) / w;
        var h = r * q.X < d * (q.Y - r) ? new Vector3(0f, r, 0f) : new Vector3(-d, 0f, d + w);

        return Length(q - new Vector2(h.X, h.Y)) - h.Z;
    }

    public static float Rhombus(Vector3 p, float la, float lb, float h, float ra)
    {
        p = Vector3.Abs(p);
        var f = Clamp((la * p.X - lb * p.Z + lb * lb) / (la * la + lb * lb), 0f, 1f);
        var w = Xz(p) - new Vector2(la, lb) * new Vector2(f, 1f - f);
        var q = new Vector2(Length(w) * Sign(w.X) - ra, p.Y - h);
        return MathF.Min(MathF.Max(q.X, q.Y), 0f) + Length(Max0(q));
    }

    public static float Octahedron(Vector3 p, float s)
    {
        p = Vector3.Abs(p);
        var m = p.X + p.Y + p.Z - s;
        Vector3 q;
        if (3f * p.X < m) q = p;
        else if (3f * p.Y < m) q = new Vector3(p.Y, p.Z, p.X);
        else if (3f * p.Z < m) q = new Vector3(p.Z, p.X, p.Y);
        else return m * 0.57735027f;

        var k = Clamp(0.5f * (q.Z - q.Y + s), 0f, s);
        return Length(new Vector3(q.X, q.Y - s + k, q.Z - k));
    }

    public static float OctahedronBound(Vector3 p, float s)
    {
        p = Vector3.Abs(p);
        return (p.X + p.Y + p.Z - s) * 0.57735027f;
    }

    public static float Pyramid(Vector3 p, float h)
    {
        var m2 = h * h + 0.25f;

        var xz = Vector2.Abs(Xz(p));
        if (xz.Y > xz.X) xz = new Vector2(xz.Y, xz.X);
        xz -= new Vector2(0.5f);
        p = new Vector3(xz.X, p.Y, xz.Y);

        var q = new Vector3(p.Z, h * p.Y - 0.5f * p.X, h * p.X + 0.5f * p.Y);
        var s = MathF.Max(-q.X, 0f);
        var t = Clamp((q.Y - 0.5f * p.Z) / (m2 + 0.25f), 0f, 1f);
        var a = m2 * (q.X + s) * (q.X + s) + q.Y * q.Y;
        var b = m2 * (q.X + 0.5f * t) * (q.X + 0.5f * t) + (q.Y - m2 * t) * (q.Y - m2 * t);

        var d2 = MathF.Min(q.Y, -q.X * m2 - q.Y * 0.5f) > 0f ? 0f : MathF.Min(a, b);
        return MathF.Sqrt((d2 + q.Z * q.Z) / m2) * Sign(MathF.Max(q.Z, -p.Y));
    }

    public static float Triangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ba = b - a, pa = p - a;
        Vector3 cb = c - b, pb = p - b;
        Vector3 ac = a - c, pc = p - c;
        var nor = Vector3.Cross(ba, ac);

        return MathF.Sqrt(
            Sign(Vector3.Dot(Vector3.Cross(ba, nor), pa)) +
            Sign(Vector3.Dot(Vector3.Cross(cb, nor), pb)) +
            Sign(Vector3.Dot(Vector3.Cross(ac, nor), pc)) < 2f
                ? MathF.Min(MathF.Min(
                        Dot2(ba * Clamp(Vector3.Dot(ba, pa) / Dot2(ba), 0f, 1f) - pa),
                        Dot2(cb * Clamp(Vector3.Dot(cb, pb) / Dot2(cb), 0f, 1f) - pb)),
                    Dot2(ac * Clamp(Vector3.Dot(ac, pc) / Dot2(ac), 0f, 1f) - pc))
                : Vector3.Dot(nor, pa) * Vector3.Dot(nor, pa) / Dot2(nor));
    }

    public static float Quad(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Vector3 ba = b - a, pa = p - a;
        Vector3 cb = c - b, pb = p - b;
        Vector3 dc = d - c, pc = p - c;
        Vector3 ad = a - d, pd = p - d;
        var nor = Vector3.Cross(ba, ad);

        return MathF.Sqrt(
            Sign(Vector3.Dot(Vector3.Cross(ba, nor), pa)) +
            Sign(Vector3.Dot(Vector3.Cross(cb, nor), pb)) +
            Sign(Vector3.Dot(Vector3.Cross(dc, nor), pc)) +
            Sign(Vector3.Dot(Vector3.Cross(ad, nor), pd)) < 3f
                ? MathF.Min(MathF.Min(MathF.Min(
                            Dot2(ba * Clamp(Vector3.Dot(ba, pa) / Dot2(ba), 0f, 1f) - pa),
                            Dot2(cb * Clamp(Vector3.Dot(cb, pb) / Dot2(cb), 0f, 1f) - pb)),
                        Dot2(dc * Clamp(Vector3.Dot(dc, pc) / Dot2(dc), 0f, 1f) - pc)),
                    Dot2(ad * Clamp(Vector3.Dot(ad, pd) / Dot2(ad), 0f, 1f) - pd))
                : Vector3.Dot(nor, pa) * Vector3.Dot(nor, pa) / Dot2(nor));
    }

    public static float Ellipsoid(Vector3 p, Vector3 r)
    {
        var k0 = Length(p / r);
        var k1 = Length(p / (r * r));
        return k0 * (k0 - 1f) / k1;
    }

    public static float TriPrism(Vector3 p, Vector2 h)
    {
        var q = Vector3.Abs(p);
        return MathF.Max(q.Z - h.Y, MathF.Max(q.X * 0.866025f + p.Y * 0.5f, -p.Y) - h.X * 0.5f);
    }

    #endregion

    #region 2D primitives

    public static float Circle(Vector2 p, float r) => Length(p) - r;

    public static float Box2D(Vector2 p, Vector2 b)
    {
        var d = Vector2.Abs(p) - b;
        return Length(Max0(d)) + MathF.Min(MathF.Max(d.X, d.Y), 0f);
    }

    public static float RoundedBox2D(Vector2 p, Vector2 b, float topRight, float bottomRight, float topLeft, float bottomLeft)
    {
        var rx = p.X > 0f ? topRight : topLeft;
        var ry = p.X > 0f ? bottomRight : bottomLeft;
        var r = p.Y > 0f ? rx : ry;
        var q = Vector2.Abs(p) - b + new Vector2(r);
        return MathF.Min(MathF.Max(q.X, q.Y), 0f) + Length(Max0(q)) - r;
    }

    public static float OrientedBox2D(Vector2 p, Vector2 a, Vector2 b, float th)
    {
        var l = Length(b - a);
        var d = (b - a) / l;
        var q = p - (a + b) * 0.5f;
        q = new Vector2(d.X * q.X + d.Y * q.Y, -d.Y * q.X + d.X * q.Y);
        q = Vector2.Abs(q) - new Vector2(l, th) * 0.5f;
        return Length(Max0(q)) + MathF.Min(MathF.Max(q.X, q.Y), 0f);
    }

    public static float Segment2D(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 pa = p - a, ba = b - a;
        var h = Clamp(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba), 0f, 1f);
        return Length(pa - ba * h);
    }

    public static float Rhombus2D(Vector2 p, Vector2 b)
    {
        static float NDot(Vector2 a, Vector2 b) => a.X * b.X - a.Y * b.Y;
        p = Vector2.Abs(p);
        var h = Clamp(NDot(b - 2f * p, b) / Vector2.Dot(b, b), -1f, 1f);
        var d = Length(p - 0.5f * b * new Vector2(1f - h, 1f + h));
        return d * Sign(p.X * b.Y + p.Y * b.X - b.X * b.Y);
    }

    public static float Trapezoid2D(Vector2 p, float r1, float r2, float he)
    {
        var k1 = new Vector2(r2, he);
        var k2 = new Vector2(r2 - r1, 2f * he);
        p.X = MathF.Abs(p.X);
        var ca = new Vector2(p.X - MathF.Min(p.X, p.Y < 0f ? r1 : r2), MathF.Abs(p.Y) - he);
        var cb = p - k1 + k2 * Clamp(Vector2.Dot(k1 - p, k2) / Dot2(k2), 0f, 1f);
        var s = cb.X < 0f && ca.Y < 0f ? -1f : 1f;
        return s * MathF.Sqrt(MathF.Min(Dot2(ca), Dot2(cb)));
    }

    public static float Parallelogram2D(Vector2 p, float wi, float he, float sk)
    {
        var e = new Vector2(sk, he);
        p = p.Y < 0f ? -p : p;
        var w = p - e;
        w.X -= Clamp(w.X, -wi, wi);
        var d = new Vector2(Vector2.Dot(w, w), -w.Y);
        var s = p.X * e.Y - p.Y * e.X;
        p = s < 0f ? -p : p;
        var v = p - new Vector2(wi, 0f);
        v -= e * Clamp(Vector2.Dot(v, e) / Vector2.Dot(e, e), -1f, 1f);
        d = Vector2.Min(d, new Vector2(Vector2.Dot(v, v), wi * he - MathF.Abs(s)));
        return MathF.Sqrt(d.X) * Sign(-d.Y);
    }

    public static float EquilateralTriangle2D(Vector2 p, float r)
    {
        var k = MathF.Sqrt(3f);
        p.X = MathF.Abs(p.X) - r;
        p.Y += r / k;
        if (p.X + k * p.Y > 0f) p = new Vector2(p.X - k * p.Y, -k * p.X - p.Y) / 2f;
        p.X -= Clamp(p.X, -2f * r, 0f);
        return -Length(p) * Sign(p.Y);
    }

    public static float IsoscelesTriangle2D(Vector2 p, Vector2 q)
    {
        p.X = MathF.Abs(p.X);
        var a = p - q * Clamp(Vector2.Dot(p, q) / Vector2.Dot(q, q), 0f, 1f);
        var b = p - q * new Vector2(Clamp(p.X / q.X, 0f, 1f), 1f);
        var s = -Sign(q.Y);
        var d = Vector2.Min(
            new Vector2(Vector2.Dot(a, a), s * (p.X * q.Y - p.Y * q.X)),
            new Vector2(Vector2.Dot(b, b), s * (p.Y - q.Y)));
        return -MathF.Sqrt(d.X) * Sign(d.Y);
    }

    public static float Triangle2D(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
    {
        Vector2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
        Vector2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
        var pq0 = v0 - e0 * Clamp(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0), 0f, 1f);
        var pq1 = v1 - e1 * Clamp(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1), 0f, 1f);
        var pq2 = v2 - e2 * Clamp(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2), 0f, 1f);
        var s = Sign(e0.X * e2.Y - e0.Y * e2.X);
        var d = Vector2.Min(Vector2.Min(
                new Vector2(Vector2.Dot(pq0, pq0), s * (v0.X * e0.Y - v0.Y * e0.X)),
                new Vector2(Vector2.Dot(pq1, pq1), s * (v1.X * e1.Y - v1.Y * e1.X))),
            new Vector2(Vector2.Dot(pq2, pq2), s * (v2.X * e2.Y - v2.Y * e2.X)));
        return -MathF.Sqrt(d.X) * Sign(d.Y);
    }

    public static float UnevenCapsule2D(Vector2 p, float r1, float r2, float h)
    {
        p.X = MathF.Abs(p.X);
        var b = (r1 - r2) / h;
        var a = MathF.Sqrt(1f - b * b);
        var k = Vector2.Dot(p, new Vector2(-b, a));
        if (k < 0f) return Length(p) - r1;
        if (k > a * h) return Length(p - new Vector2(0f, h)) - r2;
        return Vector2.Dot(p, new Vector2(a, b)) - r1;
    }

    public static float Pentagon2D(Vector2 p, float r)
    {
        var k = new Vector3(0.809016994f, 0.587785252f, 0.726542528f);
        var k1 = new Vector2(-k.X, k.Y);
        var k2 = new Vector2(k.X, k.Y);
        p.X = MathF.Abs(p.X);
        p -= 2f * MathF.Min(Vector2.Dot(k1, p), 0f) * k1;
        p -= 2f * MathF.Min(Vector2.Dot(k2, p), 0f) * k2;
        p -= new Vector2(Clamp(p.X, -r * k.Z, r * k.Z), r);
        return Length(p) * Sign(p.Y);
    }

    public static float Hexagon2D(Vector2 p, float r)
    {
        var k = new Vector3(-0.866025404f, 0.5f, 0.577350269f);
        var kxy = new Vector2(k.X, k.Y);
        p = Vector2.Abs(p);
        p -= 2f * MathF.Min(Vector2.Dot(kxy, p), 0f) * kxy;
        p -= new Vector2(Clamp(p.X, -k.Z * r, k.Z * r), r);
        return Length(p) * Sign(p.Y);
    }

    public static float Octagon2D(Vector2 p, float r)
    {
        var k = new Vector3(-0.9238795325f, 0.3826834323f, 0.4142135623f);
        var k1 = new Vector2(k.X, k.Y);
        var k2 = new Vector2(-k.X, k.Y);
        p = Vector2.Abs(p);
        p -= 2f * MathF.Min(Vector2.Dot(k1, p), 0f) * k1;
        p -= 2f * MathF.Min(Vector2.Dot(k2, p), 0f) * k2;
        p -= new Vector2(Clamp(p.X, -k.Z * r, k.Z * r), r);
        return Length(p) * Sign(p.Y);
    }

    public static float Hexagram2D(Vector2 p, float r)
    {
        var k = new Vector4(-0.5f, 0.8660254038f, 0.5773502692f, 1.7320508076f);
        var kxy = new Vector2(k.X, k.Y);
        var kyx = new Vector2(k.Y, k.X);
        p = Vector2.Abs(p);
        p -= 2f * MathF.Min(Vector2.Dot(kxy, p), 0f) * kxy;
        p -= 2f * MathF.Min(Vector2.Dot(kyx, p), 0f) * kyx;
        p -= new Vector2(Clamp(p.X, r * k.Z, r * k.W), r);
        return Length(p) * Sign(p.Y);
    }

    public static float Star2D(Vector2 p, float r, int n, float m)
    {
        var an = MathF.PI / n;
        var en = MathF.PI / m;
        var acs = new Vector2(MathF.Cos(an), MathF.Sin(an));
        var ecs = new Vector2(MathF.Cos(en), MathF.Sin(en));
        var angle = MathF.Atan2(p.X, p.Y);
        var bn = angle - 2f * an * MathF.Floor(angle / (2f * an)) - an;
        p = Length(p) * new Vector2(MathF.Cos(bn), MathF.Abs(MathF.Sin(bn)));
        p -= r * acs;
        p += ecs * Clamp(-Vector2.Dot(p, ecs), 0f, r * acs.Y / ecs.Y);
        return Length(p) * Sign(p.X);
    }

    public static float Pie2D(Vector2 p, Vector2 c, float r)
    {
        p.X = MathF.Abs(p.X);
        var l = Length(p) - r;
        var m = Length(p - c * Clamp(Vector2.Dot(p, c), 0f, r));
        return MathF.Max(l, m * Sign(c.Y * p.X - c.X * p.Y));
    }

    public static float Arc2D(Vector2 p, Vector2 sc, float ra, float rb)
    {
        p.X = MathF.Abs(p.X);
        return (sc.Y * p.X > sc.X * p.Y ? Length(p - sc * ra) : MathF.Abs(Length(p) - ra)) - rb;
    }

    public static float Ring2D(Vector2 p, Vector2 n, float r, float th)
    {
        p.X = MathF.Abs(p.X);
        p = new Vector2(n.X * p.X - n.Y * p.Y, n.Y * p.X + n.X * p.Y);
        return MathF.Max(MathF.Abs(Length(p) - r) - th * 0.5f,
            Length(new Vector2(p.X, MathF.Max(0f, MathF.Abs(r - p.Y) - th * 0.5f))) * Sign(p.X));
    }

    public static float Moon2D(Vector2 p, float d, float ra, float rb)
    {
        p.Y = MathF.Abs(p.Y);
        var a = (ra * ra - rb * rb + d * d) / (2f * d);
        var b = MathF.Sqrt(MathF.Max(ra * ra - a * a, 0f));
        if (d * (p.X * b - p.Y * a) > d * d * MathF.Max(b - p.Y, 0f))
            return Length(p - new Vector2(a, b));
        return MathF.Max(Length(p) - ra, -(Length(p - new Vector2(d, 0f)) - rb));
    }

    public static float Vesica2D(Vector2 p, float w, float h)
    {
        var d = 0.5f * (w * w - h * h) / h;
        p = Vector2.Abs(p);
        var c = w * p.Y < d * (p.X - w) ? new Vector3(0f, w, 0f) : new Vector3(-d, 0f, d + h);
        return Length(p - new Vector2(c.Y, c.X)) - c.Z;
    }

    public static float Egg2D(Vector2 p, float ra, float rb)
    {
        var k = MathF.Sqrt(3f);
        p.X = MathF.Abs(p.X);
        var r = ra - rb;
        return (p.Y < 0f ? Length(p) - r
            : k * (p.X + r) < p.Y ? Length(new Vector2(p.X, p.Y - k * r))
            : Length(new Vector2(p.X + r, p.Y)) - 2f * r) - rb;
    }

    public static float Heart2D(Vector2 p)
    {
        p.X = MathF.Abs(p.X);
        if (p.Y + p.X > 1f)
            return MathF.Sqrt(Dot2(p - new Vector2(0.25f, 0.75f))) - MathF.Sqrt(2f) / 4f;
        return MathF.Sqrt(MathF.Min(Dot2(p - new Vector2(0f, 1f)),
            Dot2(p - new Vector2(0.5f * MathF.Max(p.X + p.Y, 0f))))) * Sign(p.X - p.Y);
    }

    public static float Cross2D(Vector2 p, Vector2 b, float r)
    {
        p = Vector2.Abs(p);
        if (p.Y > p.X) p = new Vector2(p.Y, p.X);
        var q = p - b;
        var k = MathF.Max(q.Y, q.X);
        var w = k > 0f ? q : new Vector2(b.Y - p.X, -k);
        return Sign(k) * Length(Max0(w)) + r;
    }

    public static float RoundedX2D(Vector2 p, float w, float r)
    {
        p = Vector2.Abs(p);
        return Length(p - new Vector2(MathF.Min(p.X + p.Y, w) * 0.5f)) - r;
    }

    public static float Ellipse2D(Vector2 p, Vector2 ab)
    {
        p = Vector2.Abs(p);
        var q = ab * (p - ab);
        var cs = Vector2.Normalize(q.X < q.Y ? new Vector2(0.01f, 1f) : new Vector2(1f, 0.01f));
        for (var i = 0; i < 5; i++)
        {
            var u = ab * new Vector2(cs.X, cs.Y);
            var v = ab * new Vector2(-cs.Y, cs.X);
            var a = Vector2.Dot(p - u, v);
            var c = Vector2.Dot(p - u, u) + Vector2.Dot(v, v);
            var b = MathF.Sqrt(c * c - a * a);
            cs = new Vector2(cs.X * b - cs.Y * a, cs.Y * b + cs.X * a) / c;
        }
        var d = Length(p - ab * cs);
        return Vector2.Dot(p / ab, p / ab) > 1f ? d : -d;
    }

    public static float Parabola2D(Vector2 pos, float k)
    {
        pos.X = MathF.Abs(pos.X);
        var ik = 1f / k;
        var p = ik * (pos.Y - 0.5f * ik) / 3f;
        var q = 0.25f * ik * ik * pos.X;
        var h = q * q - p * p * p;
        var r = MathF.Sqrt(MathF.Abs(h));
        var x = h > 0f
            ? MathF.Cbrt(q + r) - MathF.Cbrt(MathF.Abs(q - r)) * Sign(r - q)
            : 2f * MathF.Cos(MathF.Atan2(r, q) / 3f) * MathF.Sqrt(p);
        return Length(pos - new Vector2(x, k * x * x)) * Sign(pos.X - x);
    }

    public static float Horseshoe2D(Vector2 p, Vector2 c, float r, Vector2 w)
    {
        p.X = MathF.Abs(p.X);
        var l = Length(p);
        p = new Vector2(-c.X * p.X + c.Y * p.Y, c.Y * p.X + c.X * p.Y);
        p = new Vector2(p.Y > 0f || p.X > 0f ? p.X : l * Sign(-c.X), p.X > 0f ? p.Y : l);
        p = new Vector2(p.X, MathF.Abs(p.Y - r)) - w;
        return Length(Max0(p)) + MathF.Min(0f, MathF.Max(p.X, p.Y));
    }

    #endregion
}
