using System;
using System.Collections.Generic;

namespace SdfStory.Layout
{
    /// <summary>A point in a frame's pixels, x right and y down from the frame's top left.</summary>
    public readonly struct Pt
    {
        public readonly float X, Y;
        public Pt(float x, float y) { X = x; Y = y; }
        public static Pt operator +(Pt a, Pt b) => new Pt(a.X + b.X, a.Y + b.Y);
        public static Pt operator -(Pt a, Pt b) => new Pt(a.X - b.X, a.Y - b.Y);
        public static Pt operator *(Pt a, float f) => new Pt(a.X * f, a.Y * f);
        public float Length => (float)Math.Sqrt(X * X + Y * Y);
        public static float Distance(Pt a, Pt b) => (a - b).Length;
        public static Pt Lerp(Pt a, Pt b, float t) => new Pt(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        /// <summary>The unit vector, or (1, 0) for a zero one.</summary>
        public Pt Normalized()
        {
            var length = Length;
            return length < 1e-6f ? new Pt(1, 0) : new Pt(X / length, Y / length);
        }

        /// <summary>The vector turned a quarter (clockwise on the screen).</summary>
        public Pt Perpendicular => new Pt(-Y, X);
        public override string ToString() => $"({X:0.##}, {Y:0.##})";
    }

    /// <summary>A rectangle in a frame's pixels.</summary>
    public readonly struct Box
    {
        public readonly float X, Y, W, H;
        public Box(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
        public float Right => X + W;
        public float Bottom => Y + H;
        public Pt Center => new Pt(X + W * 0.5f, Y + H * 0.5f);
        public static Box Around(Pt center, float w, float h) => new Box(center.X - w * 0.5f, center.Y - h * 0.5f, w, h);
        public Box Inset(float amount) => new Box(X + amount, Y + amount, Math.Max(0, W - 2 * amount), Math.Max(0, H - 2 * amount));
        public Box Grow(float amount) => new Box(X - amount, Y - amount, W + 2 * amount, H + 2 * amount);
        public bool Contains(Pt p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
        public bool Overlaps(Box o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

        /// <summary>Whether this rectangle lies inside <paramref name="outer"/> (a small tolerance for rounding).</summary>
        public bool IsInside(Box outer) => X >= outer.X - 0.01f && Y >= outer.Y - 0.01f && Right <= outer.Right + 0.01f && Bottom <= outer.Bottom + 0.01f;

        public override string ToString() => $"[{X:0.#}, {Y:0.#}, {W:0.#} x {H:0.#}]";
    }

    /// <summary>A small deterministic random source (xorshift), so that a panel renders the same every time.</summary>
    public sealed class StoryRandom
    {
        private uint state;

        public StoryRandom(int seed)
        {
            state = (uint)seed * 2654435761u + 0x9E3779B9u;
            if (state == 0)
                state = 1;
            Next();
            Next();
        }

        private uint Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        /// <summary>0 up to (not including) 1.</summary>
        public float Value() => (Next() & 0xFFFFFF) / (float)0x1000000;

        public float Range(float min, float max) => min + (max - min) * Value();
    }

    /// <summary>
    /// Antialiased coverage of filled shapes on a pixel grid (a plain scanline fill: four sub-rows per pixel, exact in x), for the comic's
    /// balloons, bursts, lines and borders. Shapes are combined with the larger coverage, so overlapping strokes do not add up.
    /// </summary>
    public sealed class CoverageMask
    {
        public int Width { get; }
        public int Height { get; }
        /// <summary>0..1 per pixel, row by row from the top.</summary>
        public float[] Cover { get; }

        private const int SubRows = 4;

        public CoverageMask(int width, int height)
        {
            Width = width;
            Height = height;
            Cover = new float[width * height];
        }

        public void Clear() => Array.Clear(Cover, 0, Cover.Length);

        public float At(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? 0 : Cover[y * Width + x];

        /// <summary>The sum of the coverage: the covered area in pixels.</summary>
        public float Area()
        {
            double sum = 0;
            foreach (var value in Cover)
                sum += value;
            return (float)sum;
        }

        /// <summary>Fills the area inside the contours (even-odd: a second contour inside the first makes a hole).</summary>
        public void Fill(params IList<Pt>[] contours)
        {
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var contour in contours)
                foreach (var p in contour)
                {
                    minY = Math.Min(minY, p.Y);
                    maxY = Math.Max(maxY, p.Y);
                }
            if (minY > maxY)
                return;
            var firstRow = Math.Max(0, (int)Math.Floor(minY));
            var lastRow = Math.Min(Height - 1, (int)Math.Ceiling(maxY));
            var row = new float[Width];
            var crossings = new List<float>();
            for (var y = firstRow; y <= lastRow; y++)
            {
                Array.Clear(row, 0, row.Length);
                var any = false;
                for (var s = 0; s < SubRows; s++)
                {
                    var sampleY = y + (s + 0.5f) / SubRows;
                    crossings.Clear();
                    foreach (var contour in contours)
                    {
                        var count = contour.Count;
                        for (var i = 0; i < count; i++)
                        {
                            var a = contour[i];
                            var b = contour[(i + 1) % count];
                            if ((a.Y <= sampleY) == (b.Y <= sampleY))
                                continue;
                            crossings.Add(a.X + (sampleY - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                        }
                    }
                    crossings.Sort();
                    for (var i = 0; i + 1 < crossings.Count; i += 2)
                    {
                        var left = Math.Max(0, crossings[i]);
                        var right = Math.Min(Width, crossings[i + 1]);
                        if (right <= left)
                            continue;
                        any = true;
                        var x0 = (int)left;
                        var x1 = Math.Min(Width - 1, (int)right);
                        for (var x = x0; x <= x1; x++)
                        {
                            var covered = Math.Min(x + 1, right) - Math.Max(x, left);
                            if (covered > 0)
                                row[x] += covered / SubRows;
                        }
                    }
                }
                if (!any)
                    continue;
                var offset = y * Width;
                for (var x = 0; x < Width; x++)
                    if (row[x] > Cover[offset + x])
                        Cover[offset + x] = Math.Min(1, row[x]);
            }
        }

        /// <summary>A line of the given width through the points, with round joints.</summary>
        public void Stroke(IList<Pt> points, float width, bool closed)
        {
            var half = width * 0.5f;
            var count = points.Count;
            var last = closed ? count : count - 1;
            for (var i = 0; i < last; i++)
                Segment(points[i], points[(i + 1) % count], half, half);
            for (var i = 0; i < count; i++)
                if (closed || (i > 0 && i < count - 1))
                    Fill(Disc(points[i], half, 12));
        }

        /// <summary>A segment that is <paramref name="halfA"/> wide at a and <paramref name="halfB"/> at b (each side of the line).</summary>
        public void Segment(Pt a, Pt b, float halfA, float halfB)
        {
            var normal = (b - a).Normalized().Perpendicular;
            Fill(new[] { a + normal * halfA, b + normal * halfB, b - normal * halfB, a - normal * halfA });
        }

        public static Pt[] Disc(Pt center, float radius, int sides)
        {
            var points = new Pt[sides];
            for (var i = 0; i < sides; i++)
            {
                var angle = i * 2 * Math.PI / sides;
                points[i] = new Pt(center.X + (float)Math.Cos(angle) * radius, center.Y + (float)Math.Sin(angle) * radius);
            }
            return points;
        }
    }
}
