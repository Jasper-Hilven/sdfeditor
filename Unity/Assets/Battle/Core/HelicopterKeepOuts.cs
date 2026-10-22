using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// A footprint seen from above (x, z, Unity's coordinates) a helicopter keeps out of (ticket 30.7): the box round a tall model's
    /// bounds, baked by the pipeline as <c>battle.helicopterKeepOuts</c> (every placed model over 6 m, in a room with a machines line).
    /// </summary>
    [Serializable]
    public struct KeepOut
    {
        public Vector2 min;
        public Vector2 max;

        public KeepOut(Vector2 min, Vector2 max)
        {
            this.min = min;
            this.max = max;
        }

        /// <summary>The box grown by <paramref name="margin"/> metres all round.</summary>
        public KeepOut Grown(float margin) => new KeepOut(min - new Vector2(margin, margin), max + new Vector2(margin, margin));

        public bool Contains(Vector2 p) => p.x > min.x && p.x < max.x && p.y > min.y && p.y < max.y;
    }

    /// <summary>
    /// The pilot's rules for the keep-outs (ticket 30.7), pure: a flight point may not be in one (or within a margin of it), and a leg
    /// may not cross one. A helicopter already inside a box's margin (it took off beside a tall model) is not stopped by that box on its
    /// leg: it may leave.
    /// </summary>
    public static class KeepOutRules
    {
        /// <summary>Whether <paramref name="p"/> is within <paramref name="margin"/> metres of any of the boxes, or inside.</summary>
        public static bool Near(IReadOnlyList<KeepOut> boxes, Vector2 p, float margin)
        {
            if (boxes == null)
                return false;
            for (var i = 0; i < boxes.Count; i++)
                if (boxes[i].Grown(margin).Contains(p))
                    return true;
            return false;
        }

        /// <summary>Whether the straight leg from <paramref name="start"/> to <paramref name="end"/> comes within <paramref name="margin"/> metres of a box it does not start in the margin of.</summary>
        public static bool Blocks(IReadOnlyList<KeepOut> boxes, Vector2 start, Vector2 end, float margin)
        {
            if (boxes == null)
                return false;
            for (var i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i].Grown(margin);
                if (!box.Contains(start) && Crosses(box, start, end))
                    return true;
            }
            return false;
        }

        /// <summary><paramref name="p"/> moved out of any box it is inside, to the nearest edge; unchanged outside them.</summary>
        public static Vector2 PushOut(IReadOnlyList<KeepOut> boxes, Vector2 p)
        {
            if (boxes == null)
                return p;
            for (var i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (!box.Contains(p))
                    continue;
                var toLeft = p.x - box.min.x;
                var toRight = box.max.x - p.x;
                var toBottom = p.y - box.min.y;
                var toTop = box.max.y - p.y;
                var least = Mathf.Min(Mathf.Min(toLeft, toRight), Mathf.Min(toBottom, toTop));
                if (least == toLeft)
                    p.x = box.min.x;
                else if (least == toRight)
                    p.x = box.max.x;
                else if (least == toBottom)
                    p.y = box.min.y;
                else
                    p.y = box.max.y;
            }
            return p;
        }

        /// <summary>Whether the segment passes through the box (slab test).</summary>
        private static bool Crosses(KeepOut box, Vector2 start, Vector2 end)
        {
            var d = end - start;
            float t0 = 0, t1 = 1;
            return Slab(start.x, d.x, box.min.x, box.max.x, ref t0, ref t1) && Slab(start.y, d.y, box.min.y, box.max.y, ref t0, ref t1);
        }

        private static bool Slab(float start, float delta, float low, float high, ref float t0, ref float t1)
        {
            if (Mathf.Abs(delta) < 1e-6f)
                return start > low && start < high;
            var a = (low - start) / delta;
            var b = (high - start) / delta;
            if (a > b)
                (a, b) = (b, a);
            t0 = Mathf.Max(t0, a);
            t1 = Mathf.Min(t1, b);
            return t0 < t1;
        }
    }
}
