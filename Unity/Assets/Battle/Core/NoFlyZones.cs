using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// One no-fly box (ticket 29.6, the level file's <c>nofly at= size= yaw=</c>): a rectangle on the floor plan, from the floor to the lid.
    /// <see cref="Center"/> (x, z), <see cref="Half"/> the half width (x) and half depth (z) before the turn, <see cref="Yaw"/> degrees
    /// about y with the placed models' sense (yaw 90 turns the box's +z to world +x).
    /// </summary>
    public readonly struct NoFlyBox
    {
        public readonly Vector2 Center;
        public readonly Vector2 Half;
        public readonly float Yaw;

        public NoFlyBox(Vector2 center, Vector2 half, float yaw)
        {
            Center = center;
            Half = half;
            Yaw = yaw;
        }

        /// <summary>A world point (x, z) in the box's own frame, its centre the origin.</summary>
        public Vector2 ToLocal(Vector2 world)
        {
            var d = world - Center;
            var r = Yaw * Mathf.Deg2Rad;
            var c = Mathf.Cos(r);
            var s = Mathf.Sin(r);
            return new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
        }

        /// <summary>A point in the box's own frame in the world.</summary>
        public Vector2 ToWorld(Vector2 local)
        {
            var r = Yaw * Mathf.Deg2Rad;
            var c = Mathf.Cos(r);
            var s = Mathf.Sin(r);
            return Center + new Vector2(local.x * c + local.y * s, -local.x * s + local.y * c);
        }
    }

    /// <summary>
    /// The places a helicopter does not fly (ticket 29.6): a room's things that stand in the flight band and up to or past the flight
    /// ceiling (the bathroom's tub, giant duck, pyramids and tall bottles), as boxes on the floor plan. Pure rules for
    /// <see cref="HelicopterPilot"/>: whether a point is in one (<see cref="Blocked"/>, with a margin the pilot keeps), the path round them
    /// between two points (<see cref="Route"/>: straight if nothing is in the way, else along the boxes' corners, the shortest way), and
    /// <see cref="PushOut"/> for a machine that was carried in anyway.
    /// </summary>
    public sealed class NoFlyZones
    {
        private readonly List<NoFlyBox> boxes = new List<NoFlyBox>();

        /// <summary>How far a flight stays off a box's side, in metres.</summary>
        public float Margin = 4;

        /// <summary>How far past the margin the corners of a route are, so a leg hugging a box is not on the edge of the margin.</summary>
        public float CornerSlack = 0.75f;

        /// <summary>The corners (x, z) of the area a route may pass through; a corner of a box outside it is no way round (the tub's far corners are past the wall).</summary>
        public Vector2 AreaMin = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        public Vector2 AreaMax = new Vector2(float.PositiveInfinity, float.PositiveInfinity);

        public IReadOnlyList<NoFlyBox> Boxes => boxes;

        public int Count => boxes.Count;

        public void Add(NoFlyBox box) => boxes.Add(box);

        public void Add(Vector2 center, Vector2 size, float yaw) => boxes.Add(new NoFlyBox(center, size * 0.5f, yaw));

        /// <summary>Whether a point (x, z) is within <paramref name="margin"/> of a box (inside it for 0).</summary>
        public bool Blocked(Vector2 point, float margin)
        {
            foreach (var box in boxes)
            {
                var local = box.ToLocal(point);
                if (Mathf.Abs(local.x) < box.Half.x + margin && Mathf.Abs(local.y) < box.Half.y + margin)
                    return true;
            }
            return false;
        }

        /// <summary>Whether a point (x, z) is inside a box, no margin.</summary>
        public bool Contains(Vector2 point) => Blocked(point, 0);

        /// <summary>
        /// Whether the segment from <paramref name="a"/> to <paramref name="b"/> crosses the inside of a box grown by <paramref name="margin"/>
        /// (touching its edge is not crossing it).
        /// </summary>
        public bool Crosses(Vector2 a, Vector2 b, float margin)
        {
            foreach (var box in boxes)
            {
                var half = box.Half + Vector2.one * (margin - 0.01f);
                if (SegmentHitsRectangle(box.ToLocal(a), box.ToLocal(b), half))
                    return true;
            }
            return false;
        }

        /// <summary>The slab test of a segment against the rectangle -half..half.</summary>
        private static bool SegmentHitsRectangle(Vector2 a, Vector2 b, Vector2 half)
        {
            var d = b - a;
            var t0 = 0f;
            var t1 = 1f;
            for (var axis = 0; axis < 2; axis++)
            {
                var p = axis == 0 ? a.x : a.y;
                var q = axis == 0 ? d.x : d.y;
                var h = axis == 0 ? half.x : half.y;
                if (Mathf.Abs(q) < 1e-6f)
                {
                    if (Mathf.Abs(p) >= h)
                        return false;
                    continue;
                }
                var u = (-h - p) / q;
                var v = (h - p) / q;
                if (u > v)
                {
                    var swap = u;
                    u = v;
                    v = swap;
                }
                t0 = Mathf.Max(t0, u);
                t1 = Mathf.Min(t1, v);
                if (t0 >= t1)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// A point carried into a box grown by <paramref name="margin"/> brought out through its nearest side (to that side plus
        /// <paramref name="margin"/>); a point outside is returned as it is.
        /// </summary>
        public Vector2 PushOut(Vector2 point, float margin)
        {
            // A few passes: leaving one box may land in the margin of the next
            for (var pass = 0; pass < 4; pass++)
            {
                var moved = false;
                foreach (var box in boxes)
                {
                    var local = box.ToLocal(point);
                    var hx = box.Half.x + margin;
                    var hy = box.Half.y + margin;
                    if (Mathf.Abs(local.x) >= hx || Mathf.Abs(local.y) >= hy)
                        continue;
                    var toX = hx - Mathf.Abs(local.x);
                    var toY = hy - Mathf.Abs(local.y);
                    if (toX < toY)
                        local.x = (local.x >= 0 ? 1 : -1) * hx;
                    else
                        local.y = (local.y >= 0 ? 1 : -1) * hy;
                    point = box.ToWorld(local);
                    moved = true;
                }
                if (!moved)
                    break;
            }
            return point;
        }

        /// <summary>
        /// The points to fly through to get from <paramref name="from"/> to <paramref name="to"/> keeping <see cref="Margin"/> off every box:
        /// just <paramref name="to"/> when the straight line is clear, else corners of the boxes grown by the margin (and the slack) on the
        /// shortest way, then <paramref name="to"/>. A <paramref name="from"/> inside the margin first leaves through the nearest side, and a
        /// <paramref name="to"/> inside it is brought out to the margin's edge (the last point is that, never a point in the margin).
        /// </summary>
        public List<Vector2> Route(Vector2 from, Vector2 to)
        {
            var route = new List<Vector2>();
            if (boxes.Count == 0)
            {
                route.Add(to);
                return route;
            }
            if (Blocked(to, Margin))
                to = PushOut(to, Margin + CornerSlack);
            if (Blocked(from, Margin))
            {
                from = PushOut(from, Margin + CornerSlack);
                route.Add(from);
            }
            if (!Crosses(from, to, Margin))
            {
                route.Add(to);
                return route;
            }

            // The nodes: from, to and each corner of each grown box that no other box covers
            var nodes = new List<Vector2> { from, to };
            foreach (var box in boxes)
            {
                var hx = box.Half.x + Margin + CornerSlack;
                var hy = box.Half.y + Margin + CornerSlack;
                for (var i = 0; i < 4; i++)
                {
                    var corner = box.ToWorld(new Vector2(i % 2 == 0 ? -hx : hx, i < 2 ? -hy : hy));
                    var inArea = corner.x >= AreaMin.x && corner.x <= AreaMax.x && corner.y >= AreaMin.y && corner.y <= AreaMax.y;
                    if (inArea && !Blocked(corner, Margin))
                        nodes.Add(corner);
                }
            }

            // Dijkstra over the nodes that see each other
            var n = nodes.Count;
            var cost = new float[n];
            var before = new int[n];
            var done = new bool[n];
            for (var i = 0; i < n; i++)
            {
                cost[i] = float.PositiveInfinity;
                before[i] = -1;
            }
            cost[0] = 0;
            for (var step = 0; step < n; step++)
            {
                var at = -1;
                for (var i = 0; i < n; i++)
                    if (!done[i] && (at < 0 || cost[i] < cost[at]))
                        at = i;
                if (at < 0 || float.IsInfinity(cost[at]))
                    break;
                done[at] = true;
                if (at == 1)
                    break;
                for (var i = 0; i < n; i++)
                {
                    if (done[i] || Crosses(nodes[at], nodes[i], Margin))
                        continue;
                    var c = cost[at] + Vector2.Distance(nodes[at], nodes[i]);
                    if (c < cost[i])
                    {
                        cost[i] = c;
                        before[i] = at;
                    }
                }
            }
            if (before[1] < 0)
            {
                // Nothing sees the goal (boxes close enough to wall it in): the straight line, the pilot is pushed out of whatever it meets
                route.Add(to);
                return route;
            }
            var way = new List<Vector2>();
            for (var i = 1; i > 0; i = before[i])
                way.Add(nodes[i]);
            way.Reverse();
            route.AddRange(way);
            return route;
        }
    }
}
