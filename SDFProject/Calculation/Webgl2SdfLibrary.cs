using System.Text.RegularExpressions;

namespace SDFProject.Calculation;

public static partial class Webgl2SdfLibrary
{
    public const string SdfInfinity = """
        const float SDF_INFINITY = 1e20;
        """;

    public const string SdfPi = """
        const float SDF_PI = 3.14159265358979;
        """;

    public const string Dot2 = """
        float dot2(vec2 v) { return dot(v, v); }
        float dot2(vec3 v) { return dot(v, v); }
        """;

    public const string Ndot = """
        float ndot(vec2 a, vec2 b) { return a.x * b.x - a.y * b.y; }
        """;

    public const string MaxComponent = """
        float maxComponent(vec3 v) { return max(v.x, max(v.y, v.z)); }
        """;

    public const string MinComponent = """
        float minComponent(vec3 v) { return min(v.x, min(v.y, v.z)); }
        """;

    public const string OpCbrt = """
        float opCbrt(float x) { return sign(x) * pow(abs(x), 1.0 / 3.0); }
        """;

    public const string OpRoundAwayFromZero = """
        float opRoundAwayFromZero(float x) { return sign(x) * floor(abs(x) + 0.5); }
        """;

    public const string OpRotate2D = """
        vec2 opRotate2D(vec2 v, float c, float s) { return vec2(c * v.x + s * v.y, -s * v.x + c * v.y); }
        """;

    public const string OpSmoothMin = """
        float opSmoothMin(float a, float b, float k)
        {
            if (k <= 0.0) return min(a, b);
            k *= 4.0;
            float h = max(k - abs(a - b), 0.0);
            return min(a, b) - h * h * 0.25 / k;
        }
        """;

    public const string OpXor = """
        float opXor(float a, float b) { return max(min(a, b), -max(a, b)); }
        """;

    public const string OpInverseRotateXYZ = """
        vec3 opInverseRotateXYZ(vec3 p, vec3 r)
        {
            float c = cos(r.z), s = sin(r.z);
            p = vec3(c * p.x + s * p.y, -s * p.x + c * p.y, p.z);
            c = cos(r.y); s = sin(r.y);
            p = vec3(c * p.x - s * p.z, p.y, s * p.x + c * p.z);
            c = cos(r.x); s = sin(r.x);
            return vec3(p.x, c * p.y + s * p.z, -s * p.y + c * p.z);
        }
        """;

    public const string OpRepeatAxis = """
        float opRepeatAxis(float x, float s) { return s == 0.0 ? x : x - s * opRoundAwayFromZero(x / s); }
        """;

    public const string OpRepeat = """
        vec3 opRepeat(vec3 p, vec3 s) { return vec3(opRepeatAxis(p.x, s.x), opRepeatAxis(p.y, s.y), opRepeatAxis(p.z, s.z)); }
        """;

    public const string OpRepeatLimited = """
        vec3 opRepeatLimited(vec3 p, float s, vec3 l)
        {
            if (s == 0.0) return p;
            vec3 id = vec3(opRoundAwayFromZero(p.x / s), opRoundAwayFromZero(p.y / s), opRoundAwayFromZero(p.z / s));
            return p - s * clamp(id, -l, l);
        }
        """;

    public const string OpTwist = """
        vec3 opTwist(vec3 p, float k)
        {
            vec2 xz = opRotate2D(p.xz, cos(k * p.y), sin(k * p.y));
            return vec3(xz.x, p.y, xz.y);
        }
        """;

    public const string OpRadialWave = """
        vec3 opRadialWave(vec3 p, float amount, float count)
        {
            float r = length(p.xz);
            return r < 1e-5 ? p : vec3(p.x, p.y - amount * r * sin(count * atan(p.z, p.x)), p.z);
        }
        """;

    public const string OpCheapBend = """
        vec3 opCheapBend(vec3 p, float k)
        {
            vec2 xy = opRotate2D(p.xy, cos(k * p.x), sin(k * p.x));
            return vec3(xy, p.z);
        }
        """;

    public const string SdfHash = """
        float sdfHash(vec3 c)
        {
            uvec3 q = uvec3(ivec3(c) + 32768) * uvec3(1597334673u, 3812015801u, 2798796415u);
            uint n = (q.x ^ q.y ^ q.z) * 1597334673u;
            return float(n >> 8u) * (1.0 / 16777216.0);
        }
        """;

    public const string SdfValueNoise = """
        float sdfValueNoise(vec3 p)
        {
            vec3 i = floor(p);
            vec3 f = p - i;
            vec3 u = f * f * (3.0 - 2.0 * f);
            float x00 = mix(sdfHash(i), sdfHash(i + vec3(1.0, 0.0, 0.0)), u.x);
            float x10 = mix(sdfHash(i + vec3(0.0, 1.0, 0.0)), sdfHash(i + vec3(1.0, 1.0, 0.0)), u.x);
            float x01 = mix(sdfHash(i + vec3(0.0, 0.0, 1.0)), sdfHash(i + vec3(1.0, 0.0, 1.0)), u.x);
            float x11 = mix(sdfHash(i + vec3(0.0, 1.0, 1.0)), sdfHash(i + vec3(1.0, 1.0, 1.0)), u.x);
            return mix(mix(x00, x10, u.y), mix(x01, x11, u.y), u.z);
        }
        """;

    public const string OpDistort = """
        float opDistort(vec3 p, float amount, float frequency)
        {
            vec3 q = p * frequency;
            float n = 0.65 * sdfValueNoise(q) + 0.35 * sdfValueNoise(q * 2.03 + 17.1);
            return amount * (2.0 * n - 1.0);
        }
        """;

    public const string OpExtrusion = """
        float opExtrusion(vec3 p, float d, float h)
        {
            vec2 w = vec2(d, abs(p.z) - h);
            return min(max(w.x, w.y), 0.0) + length(max(w, 0.0));
        }
        """;

    public const string SdSphere = """
        float sdSphere(vec3 p, float r) { return length(p) - r; }
        """;

    public const string SdBox = """
        float sdBox(vec3 p, vec3 b)
        {
            vec3 q = abs(p) - b;
            return length(max(q, 0.0)) + min(maxComponent(q), 0.0);
        }
        """;

    public const string SdRoundBox = """
        float sdRoundBox(vec3 p, vec3 b, float r)
        {
            vec3 q = abs(p) - b + vec3(r);
            return length(max(q, 0.0)) + min(maxComponent(q), 0.0) - r;
        }
        """;

    public const string SdBoxFrame = """
        float sdBoxFrame(vec3 p, vec3 b, float e)
        {
            p = abs(p) - b;
            vec3 q = abs(p + vec3(e)) - vec3(e);
            return min(min(
                    length(max(vec3(p.x, q.y, q.z), 0.0)) + min(max(p.x, max(q.y, q.z)), 0.0),
                    length(max(vec3(q.x, p.y, q.z), 0.0)) + min(max(q.x, max(p.y, q.z)), 0.0)),
                length(max(vec3(q.x, q.y, p.z), 0.0)) + min(max(q.x, max(q.y, p.z)), 0.0));
        }
        """;

    public const string SdTorus = """
        float sdTorus(vec3 p, float majorRadius, float minorRadius)
        {
            vec2 q = vec2(length(p.xz) - majorRadius, p.y);
            return length(q) - minorRadius;
        }
        """;

    public const string SdCappedTorus = """
        float sdCappedTorus(vec3 p, vec2 sc, float ra, float rb)
        {
            p.x = abs(p.x);
            float k = sc.y * p.x > sc.x * p.y ? dot(p.xy, sc) : length(p.xy);
            return sqrt(dot(p, p) + ra * ra - 2.0 * ra * k) - rb;
        }
        """;

    public const string SdLink = """
        float sdLink(vec3 p, float le, float r1, float r2)
        {
            vec3 q = vec3(p.x, max(abs(p.y) - le, 0.0), p.z);
            return length(vec2(length(q.xy) - r1, q.z)) - r2;
        }
        """;

    public const string SdInfiniteCylinder = """
        float sdInfiniteCylinder(vec3 p, vec2 center, float radius) { return length(p.xz - center) - radius; }
        """;

    public const string SdCone = """
        float sdCone(vec3 p, vec2 c, float h)
        {
            vec2 q = h * vec2(c.x / c.y, -1.0);
            vec2 w = vec2(length(p.xz), p.y);
            vec2 a = w - q * clamp(dot(w, q) / dot(q, q), 0.0, 1.0);
            vec2 b = w - q * vec2(clamp(w.x / q.x, 0.0, 1.0), 1.0);
            float k = sign(q.y);
            float d = min(dot(a, a), dot(b, b));
            float s = max(k * (w.x * q.y - w.y * q.x), k * (w.y - q.y));
            return sqrt(d) * sign(s);
        }
        """;

    public const string SdInfiniteCone = """
        float sdInfiniteCone(vec3 p, vec2 c)
        {
            vec2 q = vec2(length(p.xz), -p.y);
            float d = length(q - c * max(dot(q, c), 0.0));
            return d * (q.x * c.y - q.y * c.x < 0.0 ? -1.0 : 1.0);
        }
        """;

    public const string SdPlane = """
        float sdPlane(vec3 p, vec3 n, float h) { return dot(p, normalize(n)) + h; }
        """;

    public const string SdHexPrism = """
        float sdHexPrism(vec3 p, vec2 h)
        {
            const vec3 k = vec3(-0.8660254, 0.5, 0.57735);
            p = abs(p);
            vec2 pxy = p.xy - 2.0 * min(dot(k.xy, p.xy), 0.0) * k.xy;
            vec2 d = vec2(
                length(pxy - vec2(clamp(pxy.x, -k.z * h.x, k.z * h.x), h.x)) * sign(pxy.y - h.x),
                p.z - h.y);
            return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
        }
        """;

    public const string SdCapsule = """
        float sdCapsule(vec3 p, vec3 a, vec3 b, float r)
        {
            vec3 pa = p - a, ba = b - a;
            float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
            return length(pa - ba * h) - r;
        }
        """;

    public const string SdVerticalCapsule = """
        float sdVerticalCapsule(vec3 p, float h, float r)
        {
            p.y -= clamp(p.y, 0.0, h);
            return length(p) - r;
        }
        """;

    public const string SdCappedCylinder = """
        float sdCappedCylinder(vec3 p, float r, float h)
        {
            vec2 d = abs(vec2(length(p.xz), p.y)) - vec2(r, h);
            return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
        }
        """;

    public const string SdArbitraryCappedCylinder = """
        float sdArbitraryCappedCylinder(vec3 p, vec3 a, vec3 b, float r)
        {
            vec3 ba = b - a;
            vec3 pa = p - a;
            float baba = dot(ba, ba);
            float paba = dot(pa, ba);
            float x = length(pa * baba - ba * paba) - r * baba;
            float y = abs(paba - baba * 0.5) - baba * 0.5;
            float x2 = x * x;
            float y2 = y * y * baba;
            float d = max(x, y) < 0.0 ? -min(x2, y2) : (x > 0.0 ? x2 : 0.0) + (y > 0.0 ? y2 : 0.0);
            return sign(d) * sqrt(abs(d)) / baba;
        }
        """;

    public const string SdRoundedCylinder = """
        float sdRoundedCylinder(vec3 p, float ra, float rb, float h)
        {
            vec2 d = vec2(length(p.xz) - ra + rb, abs(p.y) - h + rb);
            return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - rb;
        }
        """;

    public const string SdCappedCone = """
        float sdCappedCone(vec3 p, float h, float r1, float r2)
        {
            vec2 q = vec2(length(p.xz), p.y);
            vec2 k1 = vec2(r2, h);
            vec2 k2 = vec2(r2 - r1, 2.0 * h);
            vec2 ca = vec2(q.x - min(q.x, q.y < 0.0 ? r1 : r2), abs(q.y) - h);
            vec2 cb = q - k1 + k2 * clamp(dot(k1 - q, k2) / dot2(k2), 0.0, 1.0);
            float s = cb.x < 0.0 && ca.y < 0.0 ? -1.0 : 1.0;
            return s * sqrt(min(dot2(ca), dot2(cb)));
        }
        """;

    public const string SdArbitraryCappedCone = """
        float sdArbitraryCappedCone(vec3 p, vec3 a, vec3 b, float ra, float rb)
        {
            float rba = rb - ra;
            float baba = dot(b - a, b - a);
            float papa = dot(p - a, p - a);
            float paba = dot(p - a, b - a) / baba;
            float x = sqrt(max(papa - paba * paba * baba, 0.0));
            float cax = max(0.0, x - (paba < 0.5 ? ra : rb));
            float cay = abs(paba - 0.5) - 0.5;
            float k = rba * rba + baba;
            float f = clamp((rba * (x - ra) + paba * baba) / k, 0.0, 1.0);
            float cbx = x - ra - f * rba;
            float cby = paba - f;
            float s = cbx < 0.0 && cay < 0.0 ? -1.0 : 1.0;
            return s * sqrt(min(cax * cax + cay * cay * baba, cbx * cbx + cby * cby * baba));
        }
        """;

    public const string SdSolidAngle = """
        float sdSolidAngle(vec3 p, vec2 c, float ra)
        {
            vec2 q = vec2(length(p.xz), p.y);
            float l = length(q) - ra;
            float m = length(q - c * clamp(dot(q, c), 0.0, ra));
            return max(l, m * sign(c.y * q.x - c.x * q.y));
        }
        """;

    public const string SdCutSphere = """
        float sdCutSphere(vec3 p, float r, float h)
        {
            float w = sqrt(r * r - h * h);
            vec2 q = vec2(length(p.xz), p.y);
            float s = max((h - r) * q.x * q.x + w * w * (h + r - 2.0 * q.y), h * q.x - w * q.y);
            return s < 0.0 ? length(q) - r
                : q.x < w ? h - q.y
                : length(q - vec2(w, h));
        }
        """;

    public const string SdCutHollowSphere = """
        float sdCutHollowSphere(vec3 p, float r, float h, float t)
        {
            float w = sqrt(r * r - h * h);
            vec2 q = vec2(length(p.xz), p.y);
            return (h * q.x < w * q.y ? length(q - vec2(w, h)) : abs(length(q) - r)) - t;
        }
        """;

    public const string SdRoundCone = """
        float sdRoundCone(vec3 p, float r1, float r2, float h)
        {
            float b = (r1 - r2) / h;
            float a = sqrt(1.0 - b * b);
            vec2 q = vec2(length(p.xz), p.y);
            float k = dot(q, vec2(-b, a));
            if (k < 0.0) return length(q) - r1;
            if (k > a * h) return length(q - vec2(0.0, h)) - r2;
            return dot(q, vec2(a, b)) - r1;
        }
        """;

    public const string SdArbitraryRoundCone = """
        float sdArbitraryRoundCone(vec3 p, vec3 a, vec3 b, float r1, float r2)
        {
            vec3 ba = b - a;
            float l2 = dot(ba, ba);
            float rr = r1 - r2;
            float a2 = l2 - rr * rr;
            float il2 = 1.0 / l2;

            vec3 pa = p - a;
            float y = dot(pa, ba);
            float z = y - l2;
            float x2 = dot2(pa * l2 - ba * y);
            float y2 = y * y * l2;
            float z2 = z * z * l2;

            float k = sign(rr) * rr * rr * x2;
            if (sign(z) * a2 * z2 > k) return sqrt(x2 + z2) * il2 - r2;
            if (sign(y) * a2 * y2 < k) return sqrt(x2 + y2) * il2 - r1;
            return (sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
        }
        """;

    public const string SdVesicaSegment = """
        float sdVesicaSegment(vec3 p, vec3 a, vec3 b, float w)
        {
            vec3 c = (a + b) * 0.5;
            float l = length(b - a);
            vec3 v = (b - a) / l;
            float y = dot(p - c, v);
            vec2 q = vec2(length(p - c - y * v), abs(y));

            float r = 0.5 * l;
            float d = 0.5 * (r * r - w * w) / w;
            vec3 h = r * q.x < d * (q.y - r) ? vec3(0.0, r, 0.0) : vec3(-d, 0.0, d + w);

            return length(q - h.xy) - h.z;
        }
        """;

    public const string SdRhombus = """
        float sdRhombus(vec3 p, float la, float lb, float h, float ra)
        {
            p = abs(p);
            float f = clamp((la * p.x - lb * p.z + lb * lb) / (la * la + lb * lb), 0.0, 1.0);
            vec2 w = p.xz - vec2(la, lb) * vec2(f, 1.0 - f);
            vec2 q = vec2(length(w) * sign(w.x) - ra, p.y - h);
            return min(max(q.x, q.y), 0.0) + length(max(q, 0.0));
        }
        """;

    public const string SdOctahedron = """
        float sdOctahedron(vec3 p, float s)
        {
            p = abs(p);
            float m = p.x + p.y + p.z - s;
            vec3 q;
            if (3.0 * p.x < m) q = p;
            else if (3.0 * p.y < m) q = p.yzx;
            else if (3.0 * p.z < m) q = p.zxy;
            else return m * 0.57735027;

            float k = clamp(0.5 * (q.z - q.y + s), 0.0, s);
            return length(vec3(q.x, q.y - s + k, q.z - k));
        }
        """;

    public const string SdOctahedronBound = """
        float sdOctahedronBound(vec3 p, float s)
        {
            p = abs(p);
            return (p.x + p.y + p.z - s) * 0.57735027;
        }
        """;

    public const string SdPyramid = """
        float sdPyramid(vec3 p, float h)
        {
            float m2 = h * h + 0.25;

            vec2 xz = abs(p.xz);
            if (xz.y > xz.x) xz = xz.yx;
            xz -= vec2(0.5);
            p = vec3(xz.x, p.y, xz.y);

            vec3 q = vec3(p.z, h * p.y - 0.5 * p.x, h * p.x + 0.5 * p.y);
            float s = max(-q.x, 0.0);
            float t = clamp((q.y - 0.5 * p.z) / (m2 + 0.25), 0.0, 1.0);
            float a = m2 * (q.x + s) * (q.x + s) + q.y * q.y;
            float b = m2 * (q.x + 0.5 * t) * (q.x + 0.5 * t) + (q.y - m2 * t) * (q.y - m2 * t);

            float d2 = min(q.y, -q.x * m2 - q.y * 0.5) > 0.0 ? 0.0 : min(a, b);
            return sqrt((d2 + q.z * q.z) / m2) * sign(max(q.z, -p.y));
        }
        """;

    public const string SdTriangle = """
        float sdTriangle(vec3 p, vec3 a, vec3 b, vec3 c)
        {
            vec3 ba = b - a, pa = p - a;
            vec3 cb = c - b, pb = p - b;
            vec3 ac = a - c, pc = p - c;
            vec3 nor = cross(ba, ac);

            return sqrt(
                sign(dot(cross(ba, nor), pa)) +
                sign(dot(cross(cb, nor), pb)) +
                sign(dot(cross(ac, nor), pc)) < 2.0
                    ? min(min(
                            dot2(ba * clamp(dot(ba, pa) / dot2(ba), 0.0, 1.0) - pa),
                            dot2(cb * clamp(dot(cb, pb) / dot2(cb), 0.0, 1.0) - pb)),
                        dot2(ac * clamp(dot(ac, pc) / dot2(ac), 0.0, 1.0) - pc))
                    : dot(nor, pa) * dot(nor, pa) / dot2(nor));
        }
        """;

    public const string SdQuad = """
        float sdQuad(vec3 p, vec3 a, vec3 b, vec3 c, vec3 d)
        {
            vec3 ba = b - a, pa = p - a;
            vec3 cb = c - b, pb = p - b;
            vec3 dc = d - c, pc = p - c;
            vec3 ad = a - d, pd = p - d;
            vec3 nor = cross(ba, ad);

            return sqrt(
                sign(dot(cross(ba, nor), pa)) +
                sign(dot(cross(cb, nor), pb)) +
                sign(dot(cross(dc, nor), pc)) +
                sign(dot(cross(ad, nor), pd)) < 3.0
                    ? min(min(min(
                                dot2(ba * clamp(dot(ba, pa) / dot2(ba), 0.0, 1.0) - pa),
                                dot2(cb * clamp(dot(cb, pb) / dot2(cb), 0.0, 1.0) - pb)),
                            dot2(dc * clamp(dot(dc, pc) / dot2(dc), 0.0, 1.0) - pc)),
                        dot2(ad * clamp(dot(ad, pd) / dot2(ad), 0.0, 1.0) - pd))
                    : dot(nor, pa) * dot(nor, pa) / dot2(nor));
        }
        """;

    public const string SdEllipsoid = """
        float sdEllipsoid(vec3 p, vec3 r)
        {
            float k0 = length(p / r);
            float k1 = length(p / (r * r));
            return k0 * (k0 - 1.0) / k1;
        }
        """;

    public const string SdTriPrism = """
        float sdTriPrism(vec3 p, vec2 h)
        {
            vec3 q = abs(p);
            return max(q.z - h.y, max(q.x * 0.866025 + p.y * 0.5, -p.y) - h.x * 0.5);
        }
        """;

    public const string SdCircle = """
        float sdCircle(vec2 p, float r) { return length(p) - r; }
        """;

    public const string SdBox2D = """
        float sdBox2D(vec2 p, vec2 b)
        {
            vec2 d = abs(p) - b;
            return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
        }
        """;

    public const string SdRoundedBox2D = """
        float sdRoundedBox2D(vec2 p, vec2 b, float topRight, float bottomRight, float topLeft, float bottomLeft)
        {
            float rx = p.x > 0.0 ? topRight : topLeft;
            float ry = p.x > 0.0 ? bottomRight : bottomLeft;
            float r = p.y > 0.0 ? rx : ry;
            vec2 q = abs(p) - b + vec2(r);
            return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
        }
        """;

    public const string SdOrientedBox2D = """
        float sdOrientedBox2D(vec2 p, vec2 a, vec2 b, float th)
        {
            float l = length(b - a);
            vec2 d = (b - a) / l;
            vec2 q = p - (a + b) * 0.5;
            q = vec2(d.x * q.x + d.y * q.y, -d.y * q.x + d.x * q.y);
            q = abs(q) - vec2(l, th) * 0.5;
            return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
        }
        """;

    public const string SdSegment2D = """
        float sdSegment2D(vec2 p, vec2 a, vec2 b)
        {
            vec2 pa = p - a, ba = b - a;
            float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
            return length(pa - ba * h);
        }
        """;

    public const string SdRhombus2D = """
        float sdRhombus2D(vec2 p, vec2 b)
        {
            p = abs(p);
            float h = clamp(ndot(b - 2.0 * p, b) / dot(b, b), -1.0, 1.0);
            float d = length(p - 0.5 * b * vec2(1.0 - h, 1.0 + h));
            return d * sign(p.x * b.y + p.y * b.x - b.x * b.y);
        }
        """;

    public const string SdTrapezoid2D = """
        float sdTrapezoid2D(vec2 p, float r1, float r2, float he)
        {
            vec2 k1 = vec2(r2, he);
            vec2 k2 = vec2(r2 - r1, 2.0 * he);
            p.x = abs(p.x);
            vec2 ca = vec2(p.x - min(p.x, p.y < 0.0 ? r1 : r2), abs(p.y) - he);
            vec2 cb = p - k1 + k2 * clamp(dot(k1 - p, k2) / dot2(k2), 0.0, 1.0);
            float s = cb.x < 0.0 && ca.y < 0.0 ? -1.0 : 1.0;
            return s * sqrt(min(dot2(ca), dot2(cb)));
        }
        """;

    public const string SdParallelogram2D = """
        float sdParallelogram2D(vec2 p, float wi, float he, float sk)
        {
            vec2 e = vec2(sk, he);
            p = p.y < 0.0 ? -p : p;
            vec2 w = p - e;
            w.x -= clamp(w.x, -wi, wi);
            vec2 d = vec2(dot(w, w), -w.y);
            float s = p.x * e.y - p.y * e.x;
            p = s < 0.0 ? -p : p;
            vec2 v = p - vec2(wi, 0.0);
            v -= e * clamp(dot(v, e) / dot(e, e), -1.0, 1.0);
            d = min(d, vec2(dot(v, v), wi * he - abs(s)));
            return sqrt(d.x) * sign(-d.y);
        }
        """;

    public const string SdEquilateralTriangle2D = """
        float sdEquilateralTriangle2D(vec2 p, float r)
        {
            const float k = 1.7320508;
            p.x = abs(p.x) - r;
            p.y += r / k;
            if (p.x + k * p.y > 0.0) p = vec2(p.x - k * p.y, -k * p.x - p.y) / 2.0;
            p.x -= clamp(p.x, -2.0 * r, 0.0);
            return -length(p) * sign(p.y);
        }
        """;

    public const string SdIsoscelesTriangle2D = """
        float sdIsoscelesTriangle2D(vec2 p, vec2 q)
        {
            p.x = abs(p.x);
            vec2 a = p - q * clamp(dot(p, q) / dot(q, q), 0.0, 1.0);
            vec2 b = p - q * vec2(clamp(p.x / q.x, 0.0, 1.0), 1.0);
            float s = -sign(q.y);
            vec2 d = min(
                vec2(dot(a, a), s * (p.x * q.y - p.y * q.x)),
                vec2(dot(b, b), s * (p.y - q.y)));
            return -sqrt(d.x) * sign(d.y);
        }
        """;

    public const string SdTriangle2D = """
        float sdTriangle2D(vec2 p, vec2 p0, vec2 p1, vec2 p2)
        {
            vec2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
            vec2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
            vec2 pq0 = v0 - e0 * clamp(dot(v0, e0) / dot(e0, e0), 0.0, 1.0);
            vec2 pq1 = v1 - e1 * clamp(dot(v1, e1) / dot(e1, e1), 0.0, 1.0);
            vec2 pq2 = v2 - e2 * clamp(dot(v2, e2) / dot(e2, e2), 0.0, 1.0);
            float s = sign(e0.x * e2.y - e0.y * e2.x);
            vec2 d = min(min(
                    vec2(dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                    vec2(dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                vec2(dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
            return -sqrt(d.x) * sign(d.y);
        }
        """;

    public const string SdUnevenCapsule2D = """
        float sdUnevenCapsule2D(vec2 p, float r1, float r2, float h)
        {
            p.x = abs(p.x);
            float b = (r1 - r2) / h;
            float a = sqrt(1.0 - b * b);
            float k = dot(p, vec2(-b, a));
            if (k < 0.0) return length(p) - r1;
            if (k > a * h) return length(p - vec2(0.0, h)) - r2;
            return dot(p, vec2(a, b)) - r1;
        }
        """;

    public const string SdPentagon2D = """
        float sdPentagon2D(vec2 p, float r)
        {
            const vec3 k = vec3(0.809016994, 0.587785252, 0.726542528);
            vec2 k1 = vec2(-k.x, k.y);
            vec2 k2 = vec2(k.x, k.y);
            p.x = abs(p.x);
            p -= 2.0 * min(dot(k1, p), 0.0) * k1;
            p -= 2.0 * min(dot(k2, p), 0.0) * k2;
            p -= vec2(clamp(p.x, -r * k.z, r * k.z), r);
            return length(p) * sign(p.y);
        }
        """;

    public const string SdHexagon2D = """
        float sdHexagon2D(vec2 p, float r)
        {
            const vec3 k = vec3(-0.866025404, 0.5, 0.577350269);
            p = abs(p);
            p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
            p -= vec2(clamp(p.x, -k.z * r, k.z * r), r);
            return length(p) * sign(p.y);
        }
        """;

    public const string SdOctagon2D = """
        float sdOctagon2D(vec2 p, float r)
        {
            const vec3 k = vec3(-0.9238795325, 0.3826834323, 0.4142135623);
            vec2 k1 = vec2(k.x, k.y);
            vec2 k2 = vec2(-k.x, k.y);
            p = abs(p);
            p -= 2.0 * min(dot(k1, p), 0.0) * k1;
            p -= 2.0 * min(dot(k2, p), 0.0) * k2;
            p -= vec2(clamp(p.x, -k.z * r, k.z * r), r);
            return length(p) * sign(p.y);
        }
        """;

    public const string SdHexagram2D = """
        float sdHexagram2D(vec2 p, float r)
        {
            const vec4 k = vec4(-0.5, 0.8660254038, 0.5773502692, 1.7320508076);
            p = abs(p);
            p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
            p -= 2.0 * min(dot(k.yx, p), 0.0) * k.yx;
            p -= vec2(clamp(p.x, r * k.z, r * k.w), r);
            return length(p) * sign(p.y);
        }
        """;

    public const string SdStar2D = """
        float sdStar2D(vec2 p, float r, int n, float m)
        {
            float an = SDF_PI / float(n);
            float en = SDF_PI / m;
            vec2 acs = vec2(cos(an), sin(an));
            vec2 ecs = vec2(cos(en), sin(en));
            float angle = atan(p.x, p.y);
            float bn = angle - 2.0 * an * floor(angle / (2.0 * an)) - an;
            p = length(p) * vec2(cos(bn), abs(sin(bn)));
            p -= r * acs;
            p += ecs * clamp(-dot(p, ecs), 0.0, r * acs.y / ecs.y);
            return length(p) * sign(p.x);
        }
        """;

    public const string SdPie2D = """
        float sdPie2D(vec2 p, vec2 c, float r)
        {
            p.x = abs(p.x);
            float l = length(p) - r;
            float m = length(p - c * clamp(dot(p, c), 0.0, r));
            return max(l, m * sign(c.y * p.x - c.x * p.y));
        }
        """;

    public const string SdArc2D = """
        float sdArc2D(vec2 p, vec2 sc, float ra, float rb)
        {
            p.x = abs(p.x);
            return (sc.y * p.x > sc.x * p.y ? length(p - sc * ra) : abs(length(p) - ra)) - rb;
        }
        """;

    public const string SdRing2D = """
        float sdRing2D(vec2 p, vec2 n, float r, float th)
        {
            p.x = abs(p.x);
            p = vec2(n.x * p.x - n.y * p.y, n.y * p.x + n.x * p.y);
            return max(abs(length(p) - r) - th * 0.5,
                length(vec2(p.x, max(0.0, abs(r - p.y) - th * 0.5))) * sign(p.x));
        }
        """;

    public const string SdMoon2D = """
        float sdMoon2D(vec2 p, float d, float ra, float rb)
        {
            p.y = abs(p.y);
            float a = (ra * ra - rb * rb + d * d) / (2.0 * d);
            float b = sqrt(max(ra * ra - a * a, 0.0));
            if (d * (p.x * b - p.y * a) > d * d * max(b - p.y, 0.0))
                return length(p - vec2(a, b));
            return max(length(p) - ra, -(length(p - vec2(d, 0.0)) - rb));
        }
        """;

    public const string SdVesica2D = """
        float sdVesica2D(vec2 p, float w, float h)
        {
            float d = 0.5 * (w * w - h * h) / h;
            p = abs(p);
            vec3 c = w * p.y < d * (p.x - w) ? vec3(0.0, w, 0.0) : vec3(-d, 0.0, d + h);
            return length(p - c.yx) - c.z;
        }
        """;

    public const string SdEgg2D = """
        float sdEgg2D(vec2 p, float ra, float rb)
        {
            const float k = 1.7320508;
            p.x = abs(p.x);
            float r = ra - rb;
            return (p.y < 0.0 ? length(p) - r
                : k * (p.x + r) < p.y ? length(vec2(p.x, p.y - k * r))
                : length(vec2(p.x + r, p.y)) - 2.0 * r) - rb;
        }
        """;

    public const string SdHeart2D = """
        float sdHeart2D(vec2 p)
        {
            p.x = abs(p.x);
            if (p.y + p.x > 1.0)
                return sqrt(dot2(p - vec2(0.25, 0.75))) - sqrt(2.0) / 4.0;
            return sqrt(min(dot2(p - vec2(0.0, 1.0)),
                dot2(p - vec2(0.5 * max(p.x + p.y, 0.0))))) * sign(p.x - p.y);
        }
        """;

    public const string SdCross2D = """
        float sdCross2D(vec2 p, vec2 b, float r)
        {
            p = abs(p);
            if (p.y > p.x) p = p.yx;
            vec2 q = p - b;
            float k = max(q.y, q.x);
            vec2 w = k > 0.0 ? q : vec2(b.y - p.x, -k);
            return sign(k) * length(max(w, 0.0)) + r;
        }
        """;

    public const string SdRoundedX2D = """
        float sdRoundedX2D(vec2 p, float w, float r)
        {
            p = abs(p);
            return length(p - vec2(min(p.x + p.y, w) * 0.5)) - r;
        }
        """;

    public const string SdEllipse2D = """
        float sdEllipse2D(vec2 p, vec2 ab)
        {
            p = abs(p);
            vec2 q = ab * (p - ab);
            vec2 cs = normalize(q.x < q.y ? vec2(0.01, 1.0) : vec2(1.0, 0.01));
            for (int i = 0; i < 5; i++)
            {
                vec2 u = ab * vec2(cs.x, cs.y);
                vec2 v = ab * vec2(-cs.y, cs.x);
                float a = dot(p - u, v);
                float c = dot(p - u, u) + dot(v, v);
                float b = sqrt(c * c - a * a);
                cs = vec2(cs.x * b - cs.y * a, cs.y * b + cs.x * a) / c;
            }
            float d = length(p - ab * cs);
            return dot(p / ab, p / ab) > 1.0 ? d : -d;
        }
        """;

    public const string SdParabola2D = """
        float sdParabola2D(vec2 pos, float k)
        {
            pos.x = abs(pos.x);
            float ik = 1.0 / k;
            float p = ik * (pos.y - 0.5 * ik) / 3.0;
            float q = 0.25 * ik * ik * pos.x;
            float h = q * q - p * p * p;
            float r = sqrt(abs(h));
            float x = h > 0.0
                ? opCbrt(q + r) - opCbrt(abs(q - r)) * sign(r - q)
                : 2.0 * cos(atan(r, q) / 3.0) * sqrt(p);
            return length(pos - vec2(x, k * x * x)) * sign(pos.x - x);
        }
        """;

    public const string SdHorseshoe2D = """
        float sdHorseshoe2D(vec2 p, vec2 c, float r, vec2 w)
        {
            p.x = abs(p.x);
            float l = length(p);
            p = vec2(-c.x * p.x + c.y * p.y, c.y * p.x + c.x * p.y);
            p = vec2(p.y > 0.0 || p.x > 0.0 ? p.x : l * sign(-c.x), p.x > 0.0 ? p.y : l);
            p = vec2(p.x, abs(p.y - r)) - w;
            return length(max(p, 0.0)) + min(0.0, max(p.x, p.y));
        }
        """;

    private static readonly string[] All =
    [
        SdfInfinity,
        SdfPi,
        Dot2,
        Ndot,
        MaxComponent,
        MinComponent,
        OpCbrt,
        OpRoundAwayFromZero,
        OpRotate2D,
        OpSmoothMin,
        OpXor,
        OpInverseRotateXYZ,
        OpRepeatAxis,
        OpRepeat,
        OpRepeatLimited,
        OpTwist,
        OpCheapBend,
        OpRadialWave,
        SdfHash,
        SdfValueNoise,
        OpDistort,
        OpExtrusion,
        SdSphere,
        SdBox,
        SdRoundBox,
        SdBoxFrame,
        SdTorus,
        SdCappedTorus,
        SdLink,
        SdInfiniteCylinder,
        SdCone,
        SdInfiniteCone,
        SdPlane,
        SdHexPrism,
        SdCapsule,
        SdVerticalCapsule,
        SdCappedCylinder,
        SdArbitraryCappedCylinder,
        SdRoundedCylinder,
        SdCappedCone,
        SdArbitraryCappedCone,
        SdSolidAngle,
        SdCutSphere,
        SdCutHollowSphere,
        SdRoundCone,
        SdArbitraryRoundCone,
        SdVesicaSegment,
        SdRhombus,
        SdOctahedron,
        SdOctahedronBound,
        SdPyramid,
        SdTriangle,
        SdQuad,
        SdEllipsoid,
        SdTriPrism,
        SdCircle,
        SdBox2D,
        SdRoundedBox2D,
        SdOrientedBox2D,
        SdSegment2D,
        SdRhombus2D,
        SdTrapezoid2D,
        SdParallelogram2D,
        SdEquilateralTriangle2D,
        SdIsoscelesTriangle2D,
        SdTriangle2D,
        SdUnevenCapsule2D,
        SdPentagon2D,
        SdHexagon2D,
        SdOctagon2D,
        SdHexagram2D,
        SdStar2D,
        SdPie2D,
        SdArc2D,
        SdRing2D,
        SdMoon2D,
        SdVesica2D,
        SdEgg2D,
        SdHeart2D,
        SdCross2D,
        SdRoundedX2D,
        SdEllipse2D,
        SdParabola2D,
        SdHorseshoe2D,
    ];

    public static string Code => string.Join("\n\n", All);

    private sealed record Definition(string Name, string Code, string[] Dependencies);

    private static readonly Definition[] Definitions = CreateDefinitions();

    private static readonly HashSet<string> Names = Definitions.Select(d => d.Name).ToHashSet();

    public static bool Contains(string name) => Names.Contains(name);

    public static string CodeFor(IEnumerable<string> usedNames)
    {
        var byName = Definitions.ToDictionary(d => d.Name);
        var needed = new HashSet<string>();
        var pending = new Stack<string>(usedNames.Where(Names.Contains));
        while (pending.TryPop(out var name))
        {
            if (needed.Add(name))
                foreach (var dependency in byName[name].Dependencies)
                    pending.Push(dependency);
        }

        return string.Join("\n\n", Definitions.Where(d => needed.Contains(d.Name)).Select(d => d.Code));
    }

    private static Definition[] CreateDefinitions()
    {
        var named = All.Select(code => (name: DefinitionName().Match(code) is { Success: true } match
                ? match.Groups[1].Value
                : throw new InvalidOperationException($"Cannot find the GLSL name declared by the library constant '{code}'"),
            code)).ToArray();

        var names = named.Select(d => d.name).ToHashSet();
        return named
            .Select(d => new Definition(d.name, d.code, Identifier().Matches(LineComment().Replace(d.code, ""))
                .Select(m => m.Value)
                .Where(id => id != d.name && names.Contains(id))
                .Distinct()
                .ToArray()))
            .ToArray();
    }

    [GeneratedRegex(@"^(?:const\s+)?\w+\s+(\w+)\s*[(=]", RegexOptions.Multiline)]
    private static partial Regex DefinitionName();

    [GeneratedRegex(@"\b[A-Za-z_]\w*\b")]
    private static partial Regex Identifier();

    [GeneratedRegex("//.*")]
    private static partial Regex LineComment();
}
