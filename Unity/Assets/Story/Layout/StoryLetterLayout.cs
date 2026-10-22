using System;
using System.Collections.Generic;

namespace SdfStory.Layout
{
    /// <summary>
    /// The lettering's layout (ticket 31.6), pure: fitting text, keeping balloons inside the safe area and apart, the tail's shape, and the
    /// outlines of the comic devices (jagged boxes, bursts, the irregular border, speed and motion lines). Everything is in a frame's
    /// pixels, y down; the capture turns these into pixels and TextMesh Pro text.
    /// </summary>
    public static class StoryLetterLayout
    {
        /// <summary>The ink border's thickness at the edge of a frame.</summary>
        public const float BorderWidth = 8;
        /// <summary>Lettering keeps this far from the inside of the border (the GUI's edge margin).</summary>
        public const float EdgeMargin = 5;

        /// <summary>The area balloons, captions and lettering must stay in: inside the border and the 5 px margin.</summary>
        public static Box SafeArea(Box frame) => frame.Inset(BorderWidth + EdgeMargin);

        // ------------------------------------------------------------------------------------------------------------ text

        /// <summary>The size of the text set in a column of the given width at a font size.</summary>
        public delegate Pt Measure(float fontSize, float width);

        /// <summary>The largest font size in [min, max] whose text, wrapped to <paramref name="maxWidth"/>, is not taller than <paramref name="maxHeight"/>.</summary>
        public static float FitSize(Measure measure, float maxWidth, float maxHeight, float min, float max)
        {
            if (measure(max, maxWidth).Y <= maxHeight)
                return max;
            var low = min;
            var high = max;
            // The height only grows with the size: bisect to half a pixel
            while (high - low > 0.5f)
            {
                var middle = (low + high) * 0.5f;
                if (measure(middle, maxWidth).Y <= maxHeight)
                    low = middle;
                else
                    high = middle;
            }
            return low;
        }

        /// <summary>
        /// The column width for a balloon's text: the narrowest, in steps of a twentieth of <paramref name="maxWidth"/>, that sets the text
        /// no taller than <paramref name="aspect"/> times its width (balloons read wide), or the widest allowed.
        /// </summary>
        public static float ColumnWidth(Measure measure, float fontSize, float minWidth, float maxWidth, float aspect)
        {
            var step = Math.Max(1, maxWidth / 20);
            for (var width = minWidth; width < maxWidth; width += step)
                if (measure(fontSize, width).Y <= width * aspect)
                    return width;
            return maxWidth;
        }

        // ------------------------------------------------------------------------------------------------------------ placement

        /// <summary>
        /// A box moved to lie inside the safe area and apart from the boxes already placed: pushed up or down (whichever is shorter), then
        /// back inside. When there is no room it stays inside the safe area and may overlap.
        /// </summary>
        public static Box Place(Box wanted, Box safe, IList<Box> placed)
        {
            var box = ClampInto(wanted, safe);
            for (var pass = 0; pass < 4; pass++)
            {
                var moved = false;
                foreach (var other in placed)
                {
                    if (!box.Overlaps(other.Grow(8)))
                        continue;
                    var up = new Box(box.X, other.Y - 8 - box.H, box.W, box.H);
                    var down = new Box(box.X, other.Bottom + 8, box.W, box.H);
                    var upFits = up.IsInside(safe);
                    var downFits = down.IsInside(safe);
                    if (upFits && (!downFits || Math.Abs(up.Y - box.Y) <= Math.Abs(down.Y - box.Y)))
                        box = up;
                    else if (downFits)
                        box = down;
                    else
                        box = ClampInto(Math.Abs(up.Y - box.Y) <= Math.Abs(down.Y - box.Y) ? up : down, safe);
                    moved = true;
                }
                if (!moved)
                    break;
            }
            return box;
        }

        /// <summary>A box shifted (and, when too big, centred) to lie inside <paramref name="area"/>.</summary>
        public static Box ClampInto(Box box, Box area)
        {
            var x = box.W >= area.W ? area.X + (area.W - box.W) * 0.5f : Math.Max(area.X, Math.Min(box.X, area.Right - box.W));
            var y = box.H >= area.H ? area.Y + (area.H - box.H) * 0.5f : Math.Max(area.Y, Math.Min(box.Y, area.Bottom - box.H));
            return new Box(x, y, box.W, box.H);
        }

        // ------------------------------------------------------------------------------------------------------------ balloon shapes

        /// <summary>A rounded, slightly squarish ellipse round the box (a superellipse), the balloon's body.</summary>
        public static Pt[] BalloonBody(Box box, int sides = 72)
        {
            var points = new Pt[sides];
            var centre = box.Center;
            for (var i = 0; i < sides; i++)
            {
                var angle = i * 2 * Math.PI / sides;
                var cos = Math.Cos(angle);
                var sin = Math.Sin(angle);
                // |x|^2.6 + |y|^2.6 = 1
                var power = 2.6;
                var x = Math.Sign(cos) * Math.Pow(Math.Abs(cos), 2 / power);
                var y = Math.Sign(sin) * Math.Pow(Math.Abs(sin), 2 / power);
                points[i] = new Pt(centre.X + (float)x * box.W * 0.5f, centre.Y + (float)y * box.H * 0.5f);
            }
            return points;
        }

        /// <summary>The body box that holds a text block with room round it: the ellipse is wider and taller than the block's corners need.</summary>
        public static Box BodyAround(Pt centre, float contentWidth, float contentHeight) =>
            Box.Around(centre, contentWidth * 1.22f + 36, contentHeight * 1.22f + 30);

        /// <summary>
        /// The tail: a triangle from inside the body to a tip towards the target. The tip stops <paramref name="gap"/> short of the target and
        /// is at most <paramref name="maxLength"/> outside the body; null when the target is inside the body (nothing to point at).
        /// </summary>
        public static Pt[] Tail(Box body, Pt target, float gap, float maxLength)
        {
            var centre = body.Center;
            var toTarget = target - centre;
            var direction = toTarget.Normalized();
            // Where the ray from the centre leaves the (elliptical) body
            var rx = body.W * 0.5f;
            var ry = body.H * 0.5f;
            var scale = 1 / (float)Math.Sqrt(direction.X * direction.X / (rx * rx) + direction.Y * direction.Y / (ry * ry));
            var edge = centre + direction * scale;
            var outside = toTarget.Length - scale;
            if (outside <= gap + 6)
                return null;
            var length = Math.Min(outside - gap, maxLength);
            var tip = edge + direction * length;
            var side = direction.Perpendicular;
            var width = Math.Min(Math.Min(rx, ry) * 0.32f, 34f);
            // The base lies well inside the body, so that the joint with the outline is hidden by the fill
            var baseCentre = edge - direction * Math.Min(rx, ry) * 0.35f;
            return new[] { baseCentre + side * width, tip, baseCentre - side * width };
        }

        /// <summary>A rectangle with a jagged (zigzag) edge: Prime's box in the sky. The teeth point out by up to <paramref name="depth"/>.</summary>
        public static Pt[] JaggedBox(Box box, float tooth, float depth, int seed)
        {
            var random = new StoryRandom(seed);
            var points = new List<Pt>();
            void Edge(Pt from, Pt to)
            {
                var length = Pt.Distance(from, to);
                var teeth = Math.Max(2, (int)(length / tooth));
                var along = (to - from).Normalized();
                var normal = along.Perpendicular;
                for (var i = 0; i < teeth; i++)
                {
                    var start = Pt.Lerp(from, to, i / (float)teeth);
                    var middle = Pt.Lerp(from, to, (i + 0.5f) / (float)teeth);
                    // The path walks clockwise on the screen, so the normal points inwards: the notches go in, the points out
                    points.Add(start + normal * (depth * random.Range(0.1f, 0.4f)));
                    points.Add(middle + normal * (-depth * random.Range(0.7f, 1f)));
                }
            }
            Edge(new Pt(box.X, box.Y), new Pt(box.Right, box.Y));
            Edge(new Pt(box.Right, box.Y), new Pt(box.Right, box.Bottom));
            Edge(new Pt(box.Right, box.Bottom), new Pt(box.X, box.Bottom));
            Edge(new Pt(box.X, box.Bottom), new Pt(box.X, box.Y));
            return points.ToArray();
        }

        // ------------------------------------------------------------------------------------------------------------ devices

        /// <summary>A star-shaped impact burst round a centre, with irregular spikes; <paramref name="rx"/> and <paramref name="ry"/> are the outer reach.</summary>
        public static Pt[] Burst(Pt centre, float rx, float ry, int spikes, int seed)
        {
            var random = new StoryRandom(seed);
            var points = new Pt[spikes * 2];
            var phase = random.Range(0, (float)Math.PI);
            for (var i = 0; i < spikes * 2; i++)
            {
                var angle = phase + i * Math.PI / spikes;
                var outer = i % 2 == 0;
                var k = outer ? random.Range(0.9f, 1.08f) : random.Range(0.58f, 0.74f);
                points[i] = new Pt(centre.X + (float)Math.Cos(angle) * rx * k, centre.Y + (float)Math.Sin(angle) * ry * k);
            }
            return points;
        }

        /// <summary>A rectangle's path with a slight wobble: the panel's border, inset by <paramref name="inset"/> and swaying by up to <paramref name="wobble"/>.</summary>
        public static Pt[] WobblyRect(Box box, float inset, float wobble, int seed, float step = 90)
        {
            var random = new StoryRandom(seed);
            var points = new List<Pt>();
            var rect = box.Inset(inset);
            void Edge(Pt from, Pt to, Pt inward)
            {
                var count = Math.Max(2, (int)(Pt.Distance(from, to) / step));
                for (var i = 0; i < count; i++)
                    points.Add(Pt.Lerp(from, to, i / (float)count) + inward * random.Range(-wobble, wobble));
            }
            Edge(new Pt(rect.X, rect.Y), new Pt(rect.Right, rect.Y), new Pt(0, 1));
            Edge(new Pt(rect.Right, rect.Y), new Pt(rect.Right, rect.Bottom), new Pt(-1, 0));
            Edge(new Pt(rect.Right, rect.Bottom), new Pt(rect.X, rect.Bottom), new Pt(0, -1));
            Edge(new Pt(rect.X, rect.Bottom), new Pt(rect.X, rect.Y), new Pt(1, 0));
            return points.ToArray();
        }

        /// <summary>One wedge of a speed line: a tapered line from <see cref="From"/> (the point) to <see cref="To"/> (the wide end).</summary>
        public readonly struct Streak
        {
            public readonly Pt From, To;
            public readonly float Width;
            public Streak(Pt from, Pt to, float width) { From = from; To = to; Width = width; }
        }

        /// <summary>
        /// Radial speed lines round a centre: spokes that start at a random distance from it (the clear middle is a fraction
        /// <paramref name="clear"/> of the way to the nearest frame edge) and run out past the frame.
        /// </summary>
        public static List<Streak> RadialLines(Box frame, Pt centre, float density, float clear, int seed)
        {
            var random = new StoryRandom(seed);
            var lines = new List<Streak>();
            var count = (int)(110 * density);
            var reach = Math.Max(Math.Max(Pt.Distance(centre, new Pt(frame.X, frame.Y)), Pt.Distance(centre, new Pt(frame.Right, frame.Y))),
                Math.Max(Pt.Distance(centre, new Pt(frame.X, frame.Bottom)), Pt.Distance(centre, new Pt(frame.Right, frame.Bottom))));
            var nearest = Math.Min(Math.Min(centre.X - frame.X, frame.Right - centre.X), Math.Min(centre.Y - frame.Y, frame.Bottom - centre.Y));
            var innermost = Math.Max(40, Math.Abs(nearest) * clear);
            for (var i = 0; i < count; i++)
            {
                var angle = (i + random.Range(-0.4f, 0.4f)) * 2 * Math.PI / count;
                var direction = new Pt((float)Math.Cos(angle), (float)Math.Sin(angle));
                var start = innermost + random.Range(0f, 1f) * (reach - innermost) * 0.55f;
                lines.Add(new Streak(centre + direction * start, centre + direction * (reach + 20), random.Range(2.5f, 9f)));
            }
            return lines;
        }

        /// <summary>
        /// Parallel speed lines in the direction <paramref name="angleDegrees"/> (0 along +x, counter-clockwise on the screen): short tapered
        /// streaks scattered over the frame, each as long as 6 to 22 percent of its width.
        /// </summary>
        public static List<Streak> ParallelLines(Box frame, float angleDegrees, float density, int seed)
        {
            var random = new StoryRandom(seed);
            var lines = new List<Streak>();
            var radians = angleDegrees * Math.PI / 180;
            // Screen y is down, so a counter-clockwise angle has a negative y
            var direction = new Pt((float)Math.Cos(radians), -(float)Math.Sin(radians));
            var count = (int)(46 * density);
            for (var i = 0; i < count; i++)
            {
                var head = new Pt(random.Range(frame.X, frame.Right), random.Range(frame.Y, frame.Bottom));
                var length = frame.W * random.Range(0.06f, 0.22f);
                lines.Add(new Streak(head, head - direction * length, random.Range(2f, 6f)));
            }
            return lines;
        }

        /// <summary>
        /// Motion lines trailing behind a moving thing: three streaks side by side from <paramref name="at"/> back against the direction of
        /// travel (<paramref name="angleDegrees"/>, 0 = to the right, counter-clockwise), tapering to a point.
        /// </summary>
        public static List<Streak> MotionLines(Pt at, float angleDegrees, float length, int seed)
        {
            var random = new StoryRandom(seed);
            var radians = angleDegrees * Math.PI / 180;
            var direction = new Pt((float)Math.Cos(radians), -(float)Math.Sin(radians));
            var side = direction.Perpendicular;
            var lines = new List<Streak>();
            for (var i = -1; i <= 1; i++)
            {
                var offset = side * (i * 26f + random.Range(-4f, 4f));
                var trail = length * (i == 0 ? 1f : random.Range(0.6f, 0.85f));
                // The wide end is at the thing, the point trails away
                var head = at + offset - direction * random.Range(0f, 12f);
                lines.Add(new Streak(head - direction * trail, head, i == 0 ? 8 : 6));
            }
            return lines;
        }
    }
}
