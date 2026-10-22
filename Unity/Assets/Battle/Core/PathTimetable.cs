using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>Where a car is and how it points: the middle of its underside, the heading about y and the nose-up pitch, degrees.</summary>
    public struct PathPose
    {
        public Vector3 Position;

        /// <summary>The heading about y in degrees as Unity turns, 0 facing +z and 90 facing +x.</summary>
        public float Yaw;

        /// <summary>Degrees the nose is up (a climb), as the car sits on the slope.</summary>
        public float Pitch;

        /// <summary>The rotation to give a model whose forward is +z.</summary>
        public Quaternion Rotation => Quaternion.Euler(-Pitch, Yaw, 0);
    }

    /// <summary>A place where a segment crosses a <see cref="ClosedPath"/>: the arc length on the path and the distance along the segment.</summary>
    public struct PathCrossing
    {
        public float Arc;
        public float Distance;
    }

    /// <summary>
    /// A closed path on the ground (ticket 27.3): corners (x, z) joined by straight legs, the last back to the first, with (arc length, y)
    /// height keys, linear between them and round the loop (0 without keys). Arc length is from the first corner and wraps, so any
    /// distance, negative or beyond one lap, is a place on the path. Plain C#, so the rules are tested without a scene.
    /// </summary>
    public sealed class ClosedPath
    {
        private readonly Vector2[] corners;
        private readonly float[] starts;
        private readonly Vector2[] heights;

        /// <param name="corners">At least three, x and z in metres; the path closes from the last back to the first.</param>
        /// <param name="heights">(arc length, y) keys in increasing arc length, from 0 to less than the length; none for a flat path.</param>
        public ClosedPath(IReadOnlyList<Vector2> corners, IReadOnlyList<Vector2> heights = null)
        {
            if (corners == null || corners.Count < 3)
                throw new ArgumentException("A closed path needs three corners or more", nameof(corners));
            this.corners = new Vector2[corners.Count];
            starts = new float[corners.Count];
            var length = 0f;
            for (var i = 0; i < this.corners.Length; i++)
            {
                this.corners[i] = corners[i];
                starts[i] = length;
                length += Vector2.Distance(corners[i], corners[(i + 1) % corners.Count]);
            }
            if (length <= 0)
                throw new ArgumentException("The path has no length", nameof(corners));
            Length = length;
            this.heights = heights == null ? new Vector2[0] : new List<Vector2>(heights).ToArray();
        }

        /// <summary>The length of one lap in metres.</summary>
        public float Length { get; }

        /// <summary>An arc length brought into [0, Length).</summary>
        public float Wrap(float arc)
        {
            var wrapped = arc % Length;
            return wrapped < 0 ? wrapped + Length : wrapped;
        }

        /// <summary>The point at <paramref name="arc"/> metres along the path, its y the height there.</summary>
        public Vector3 PointAt(float arc)
        {
            arc = Wrap(arc);
            var leg = LegAt(arc);
            var from = corners[leg];
            var to = corners[(leg + 1) % corners.Length];
            var legLength = Vector2.Distance(from, to);
            var t = legLength > 0 ? (arc - starts[leg]) / legLength : 0;
            var flat = Vector2.Lerp(from, to, t);
            return new Vector3(flat.x, HeightAt(arc), flat.y);
        }

        /// <summary>The height of the path at <paramref name="arc"/>: linear between the keys, and from the last round to the first.</summary>
        public float HeightAt(float arc)
        {
            if (heights.Length == 0)
                return 0;
            if (heights.Length == 1)
                return heights[0].y;
            arc = Wrap(arc);
            for (var i = 0; i < heights.Length; i++)
            {
                if (heights[i].x > arc)
                    return Between(i == 0 ? heights.Length - 1 : i - 1, i, arc);
            }
            return Between(heights.Length - 1, 0, arc);
        }

        private float Between(int a, int b, float arc)
        {
            var from = heights[a];
            var to = heights[b];
            var fromArc = from.x;
            var toArc = to.x;
            // Across the seam of the loop, the first key a lap on
            if (toArc <= fromArc)
                toArc += Length;
            if (arc < fromArc)
                arc += Length;
            var span = toArc - fromArc;
            return span > 0 ? Mathf.Lerp(from.y, to.y, (arc - fromArc) / span) : from.y;
        }

        private int LegAt(float arc)
        {
            var leg = Array.BinarySearch(starts, arc);
            if (leg < 0)
                leg = ~leg - 1;
            // Skip legs of no length that share a start
            while (leg + 1 < starts.Length && starts[leg + 1] <= arc)
                leg++;
            return Mathf.Clamp(leg, 0, starts.Length - 1);
        }

        /// <summary>
        /// The arc lengths where the path starts to climb: each height key whose next key (round the loop) is at least
        /// <paramref name="minRise"/> metres higher. The toy train's bridges (ticket 27.10: a whistle puff comes before each).
        /// </summary>
        public List<float> ClimbStarts(float minRise)
        {
            var arcs = new List<float>();
            for (var i = 0; i < heights.Length && heights.Length > 1; i++)
            {
                var next = heights[(i + 1) % heights.Length];
                if (next.y - heights[i].y >= minRise)
                    arcs.Add(heights[i].x);
            }
            return arcs;
        }

        /// <summary>
        /// Where the segment from <paramref name="from"/> to <paramref name="to"/> (x, z) crosses the path, nearest to <paramref name="from"/>
        /// first: the place on the path (arc length) and how far along the segment. A segment that runs along a leg crosses nothing.
        /// </summary>
        public List<PathCrossing> Crossings(Vector2 from, Vector2 to)
        {
            var found = new List<PathCrossing>();
            var along = to - from;
            var length = along.magnitude;
            if (length < 1e-6f)
                return found;
            for (var leg = 0; leg < corners.Length; leg++)
            {
                var a = corners[leg];
                var b = corners[(leg + 1) % corners.Length];
                var legVector = b - a;
                var cross = along.x * legVector.y - along.y * legVector.x;
                if (Mathf.Abs(cross) < 1e-9f)
                    continue;
                var offset = a - from;
                var t = (offset.x * legVector.y - offset.y * legVector.x) / cross;
                var u = (offset.x * along.y - offset.y * along.x) / cross;
                if (t < 0 || t > 1 || u < 0 || u >= 1)
                    continue;
                found.Add(new PathCrossing { Arc = starts[leg] + u * legVector.magnitude, Distance = t * length });
            }
            found.Sort((x, y) => x.Distance.CompareTo(y.Distance));
            return found;
        }

        /// <summary>How far the point <paramref name="p"/> (x, z) is from the path, to its nearest leg; <paramref name="arc"/> is the arc length there.</summary>
        public float DistanceTo(Vector2 p, out float arc)
        {
            var best = float.MaxValue;
            arc = 0;
            for (var leg = 0; leg < corners.Length; leg++)
            {
                var a = corners[leg];
                var b = corners[(leg + 1) % corners.Length];
                var along = b - a;
                var lengthSquared = along.sqrMagnitude;
                var t = lengthSquared > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, along) / lengthSquared) : 0;
                var distance = Vector2.Distance(p, a + along * t);
                if (distance < best)
                {
                    best = distance;
                    arc = starts[leg] + t * Mathf.Sqrt(lengthSquared);
                }
            }
            return best;
        }

        /// <summary>
        /// A rigid car whose rear is at <paramref name="rearArc"/> and front at <paramref name="frontArc"/>: it sits at the middle of the
        /// chord between the two points on the path and points along it, so on a bend it cuts the corner as a real car does.
        /// </summary>
        public PathPose PoseBetween(float rearArc, float frontArc)
        {
            var rear = PointAt(rearArc);
            var front = PointAt(frontArc);
            var along = front - rear;
            var flat = new Vector2(along.x, along.z).magnitude;
            return new PathPose
            {
                Position = (rear + front) / 2,
                Yaw = flat > 1e-6f ? Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg : 0,
                Pitch = Mathf.Atan2(along.y, Mathf.Max(flat, 1e-6f)) * Mathf.Rad2Deg,
            };
        }
    }

    /// <summary>
    /// The timetable of a train on a <see cref="ClosedPath"/> (ticket 27.3): its head is <c>speed * (clock + phase)</c> metres along the
    /// path, a function of the round clock alone, so nothing is saved and a respawn or new round needs nothing. The cars follow the
    /// path's centre line, the first ahead, each its length behind the one before plus the <c>gap</c>.
    /// </summary>
    public sealed class PathTimetable
    {
        private readonly float[] carLengths;
        private readonly float[] frontOffsets;

        /// <param name="carLengths">Each car's length along the path in metres, the first car ahead.</param>
        /// <param name="gap">Metres between a car's rear and the next car's front.</param>
        /// <param name="phase">Seconds the timetable is ahead at clock 0.</param>
        public PathTimetable(ClosedPath path, float speed, float phase, float gap, IReadOnlyList<float> carLengths)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (carLengths == null || carLengths.Count == 0)
                throw new ArgumentException("A train has a car or more", nameof(carLengths));
            Path = path;
            Speed = speed;
            Phase = phase;
            Gap = gap;
            this.carLengths = new List<float>(carLengths).ToArray();
            frontOffsets = new float[this.carLengths.Length];
            var behind = 0f;
            for (var i = 0; i < this.carLengths.Length; i++)
            {
                frontOffsets[i] = behind;
                behind += this.carLengths[i] + gap;
            }
            TrainLength = behind - gap;
        }

        public ClosedPath Path { get; }

        public float Speed { get; }

        public float Phase { get; }

        public float Gap { get; }

        public int CarCount => carLengths.Length;

        /// <summary>The room the cars and gaps take on the path, in metres.</summary>
        public float TrainLength { get; }

        /// <summary>A lap in seconds.</summary>
        public float LapSeconds => Path.Length / Speed;

        public float CarLength(int car) => carLengths[car];

        /// <summary>How far along the path the head (the first car's front) is at <paramref name="clock"/>, not wrapped.</summary>
        public float HeadDistance(float clock) => Speed * (clock + Phase);

        /// <summary>The arc length of a car's front at <paramref name="clock"/>, wrapped to the path.</summary>
        public float FrontArc(int car, float clock) => Path.Wrap(HeadDistance(clock) - frontOffsets[car]);

        /// <summary>The arc length of the middle of a car at <paramref name="clock"/>, wrapped to the path.</summary>
        public float CentreArc(int car, float clock) => Path.Wrap(HeadDistance(clock) - frontOffsets[car] - carLengths[car] / 2);

        /// <summary>Where a car is, and the way it points, at <paramref name="clock"/> seconds of the round.</summary>
        public PathPose PoseOf(int car, float clock)
        {
            var front = HeadDistance(clock) - frontOffsets[car];
            return Path.PoseBetween(front - carLengths[car], front);
        }
    }

    /// <summary>
    /// What it means to stand on a moving platform (ticket 27.3): the platform's step from one pose to the next carries whoever is on it
    /// along, and turns them with it. Pure, so the maths is tested without a scene.
    /// </summary>
    public static class PlatformCarry
    {
        /// <summary>
        /// How far a point at <paramref name="standing"/> on a platform that was at <paramref name="from"/> moves when the platform goes to
        /// <paramref name="to"/>: it keeps its place on the platform, so it moves with the platform's centre and swings round it by the
        /// platform's turn. The height changes by the centre's alone.
        /// </summary>
        public static Vector3 Displacement(PathPose from, PathPose to, Vector3 standing)
        {
            var offset = standing - from.Position;
            // Into the platform's frame at the start, out of it at the end
            var local = RotateAboutY(new Vector3(offset.x, 0, offset.z), -from.Yaw);
            var turned = RotateAboutY(local, to.Yaw);
            var landing = new Vector3(to.Position.x + turned.x, standing.y + (to.Position.y - from.Position.y), to.Position.z + turned.z);
            return landing - standing;
        }

        /// <summary>The turn about y in degrees, -180 to 180, that goes with the step from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static float YawChange(PathPose from, PathPose to) => Mathf.DeltaAngle(from.Yaw, to.Yaw);

        /// <summary>A vector turned <paramref name="degrees"/> about y as Unity turns (+z towards +x).</summary>
        public static Vector3 RotateAboutY(Vector3 v, float degrees)
        {
            var angle = degrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(angle);
            var sin = Mathf.Sin(angle);
            return new Vector3(v.x * cos + v.z * sin, v.y, -v.x * sin + v.z * cos);
        }
    }
}
