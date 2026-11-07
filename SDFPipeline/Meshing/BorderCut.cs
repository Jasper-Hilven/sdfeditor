using System.Collections.Concurrent;
using System.Numerics;

namespace SDFPipeline.Meshing;

/// <summary>
/// A mesh cut along its material borders: every face lies in one region, and the border runs along edges.
/// </summary>
/// <param name="Mesh">The cut mesh, its vertices shared: the first <paramref name="OriginalCount"/> are the uncut mesh's, then the border points.</param>
/// <param name="FaceRegions">The region of each face.</param>
/// <param name="OriginalCount">How many of the vertices are the uncut mesh's; each is in faces of its own region only.</param>
/// <param name="Parents">For each border point, the uncut mesh's vertices it lies between: the two ends of its edge, or the three
/// corners of the face whose regions meet at it.</param>
public sealed record CutMesh(TriangleMesh Mesh, int[] FaceRegions, int OriginalCount, int[][] Parents)
{
    public bool IsBorderPoint(int vertex) => vertex >= OriginalCount;
}

/// <summary>
/// Cuts the faces that straddle a material border, so no face has corners of two materials. With a material per vertex, such a face
/// blends from one colour to the other across its whole width, and the border shows the shape of the triangles. Here the border is found
/// in the SDF instead: a point where it crosses each edge whose ends are of different regions, and, in a face whose three corners are of
/// three regions, the point inside it where the three meet. Each face is split along the segments between them (marching triangles on
/// the region field), so every face is of one region, the border is a line of edges through those points, and no edge crosses it.
/// The points stay on the mesh's own edges and faces, so the surface does not move.
/// </summary>
public static class BorderCut
{
    private const int BisectionSteps = 8;

    /// <summary>How near an end of its edge a border point may come, as a share of the edge: nearer, its faces would be slivers.</summary>
    private const float MinEdgeShare = 0.05f;

    /// <summary>How finely a three-region face is searched for the point where they meet: this many steps along each side.</summary>
    private const int JunctionSteps = 6;

    /// <summary>How near a side of its face the meeting point of three regions may come, as a barycentric weight.</summary>
    private const float MinJunctionWeight = 0.1f;

    /// <param name="vertexRegions">The region of each vertex.</param>
    /// <param name="regionAt">The region at a point of the mesh (the caller projects it onto the surface if it needs to).</param>
    public static CutMesh Cut(TriangleMesh mesh, int[] vertexRegions, Func<Vector3, int> regionAt)
    {
        var positions = mesh.Positions;
        var triangles = mesh.Triangles;

        // Where the border crosses each edge between two regions, found once per edge so the faces either side share the point
        var crossed = new HashSet<(int, int)>();
        for (var t = 0; t < triangles.Length; t += 3)
            for (var c = 0; c < 3; c++)
            {
                var (a, b) = (triangles[t + c], triangles[t + (c + 1) % 3]);
                if (vertexRegions[a] != vertexRegions[b])
                    crossed.Add(a < b ? (a, b) : (b, a));
            }
        var edges = crossed.ToArray();
        var crossings = new Vector3[edges.Length];
        Parallel.For(0, edges.Length, e =>
        {
            var (a, b) = edges[e];
            crossings[e] = Crossing(positions[a], positions[b], vertexRegions[a], regionAt);
        });

        var output = new List<Vector3>(positions);
        var parents = new List<int[]>(edges.Length);
        var pointOfEdge = new Dictionary<(int, int), int>(edges.Length);
        for (var e = 0; e < edges.Length; e++)
        {
            pointOfEdge[edges[e]] = output.Count;
            output.Add(crossings[e]);
            parents.Add([edges[e].Item1, edges[e].Item2]);
        }
        int Point(int a, int b) => pointOfEdge[a < b ? (a, b) : (b, a)];

        // The meeting points of three regions, one per face whose corners are of three regions
        var threeWay = new List<int>();
        for (var t = 0; t < triangles.Length; t += 3)
        {
            var (ra, rb, rc) = (vertexRegions[triangles[t]], vertexRegions[triangles[t + 1]], vertexRegions[triangles[t + 2]]);
            if (ra != rb && rb != rc && rc != ra)
                threeWay.Add(t);
        }
        var junctions = new ConcurrentDictionary<int, Vector3>();
        Parallel.ForEach(threeWay, t =>
        {
            var (a, b, c) = (triangles[t], triangles[t + 1], triangles[t + 2]);
            junctions[t] = Junction(positions[a], positions[b], positions[c], vertexRegions[a], vertexRegions[b], vertexRegions[c],
                output[Point(a, b)], output[Point(b, c)], output[Point(c, a)], regionAt);
        });

        var cut = new List<int>(triangles.Length + 6 * edges.Length);
        var faceRegions = new List<int>(triangles.Length / 3 + 2 * edges.Length);
        void Face(int a, int b, int c, int region)
        {
            cut.Add(a);
            cut.Add(b);
            cut.Add(c);
            faceRegions.Add(region);
        }

        for (var t = 0; t < triangles.Length; t += 3)
        {
            var (a, b, c) = (triangles[t], triangles[t + 1], triangles[t + 2]);
            var (ra, rb, rc) = (vertexRegions[a], vertexRegions[b], vertexRegions[c]);
            if (ra == rb && rb == rc)
            {
                Face(a, b, c, ra);
                continue;
            }
            if (ra != rb && rb != rc && rc != ra)
            {
                // Each corner keeps the quad between it, its two border points and the meeting point
                var (ab, bc, ca) = (Point(a, b), Point(b, c), Point(c, a));
                var j = output.Count;
                output.Add(junctions[t]);
                parents.Add([a, b, c]);
                Face(a, ab, j, ra);
                Face(a, j, ca, ra);
                Face(b, bc, j, rb);
                Face(b, j, ab, rb);
                Face(c, ca, j, rc);
                Face(c, j, bc, rc);
                continue;
            }

            // Two regions: turn the face so that c is the corner on its own
            if (rb == rc) (a, b, c) = (b, c, a);
            else if (ra == rc) (a, b, c) = (c, a, b);
            var (p, q) = (Point(b, c), Point(c, a));
            Face(p, c, q, vertexRegions[c]);
            // The quad a, b, p, q split along its shorter diagonal
            if (Vector3.DistanceSquared(output[a], output[p]) <= Vector3.DistanceSquared(output[b], output[q]))
            {
                Face(a, b, p, vertexRegions[a]);
                Face(a, p, q, vertexRegions[a]);
            }
            else
            {
                Face(a, b, q, vertexRegions[a]);
                Face(b, p, q, vertexRegions[a]);
            }
        }

        return new CutMesh(new TriangleMesh(output.ToArray(), cut.ToArray()), faceRegions.ToArray(), positions.Length, parents.ToArray());
    }

    /// <summary>
    /// The shaded mesh with each vertex whose faces are of several regions (the border points) split, one copy per region, so each
    /// side of the border can take its own material: the colour then changes sharply along the border's edges.
    /// </summary>
    /// <returns>The split mesh (its <see cref="ShadedMesh.Source"/> still the cut mesh's vertices) and the region of each vertex.</returns>
    public static (ShadedMesh Mesh, int[] Regions) SplitByRegion(ShadedMesh shaded, int[] faceRegions)
    {
        var positions = new List<Vector3>(shaded.Positions);
        var normals = new List<Vector3>(shaded.Normals);
        var source = new List<int>(shaded.Source);
        var regions = Enumerable.Repeat(-1, shaded.Positions.Length).ToList();
        var copies = new Dictionary<(int Vertex, int Region), int>();
        var triangles = (int[])shaded.Triangles.Clone();
        for (var t = 0; t < triangles.Length; t++)
        {
            var v = triangles[t];
            var region = faceRegions[t / 3];
            if (regions[v] < 0)
                regions[v] = region;
            else if (regions[v] != region)
            {
                if (!copies.TryGetValue((v, region), out var copy))
                {
                    copy = positions.Count;
                    copies[(v, region)] = copy;
                    positions.Add(shaded.Positions[v]);
                    normals.Add(shaded.Normals[v]);
                    source.Add(shaded.Source[v]);
                    regions.Add(region);
                }
                triangles[t] = copy;
            }
        }
        return (new ShadedMesh(positions.ToArray(), normals.ToArray(), triangles, source.ToArray()), regions.ToArray());
    }

    /// <summary>Where on the edge from <paramref name="from"/> the region first stops being <paramref name="region"/>, kept off its ends.</summary>
    private static Vector3 Crossing(Vector3 from, Vector3 to, int region, Func<Vector3, int> regionAt)
    {
        float inside = 0, outside = 1;
        for (var i = 0; i < BisectionSteps; i++)
        {
            var middle = (inside + outside) * 0.5f;
            if (regionAt(Vector3.Lerp(from, to, middle)) == region)
                inside = middle;
            else
                outside = middle;
        }
        return Vector3.Lerp(from, to, Math.Clamp((inside + outside) * 0.5f, MinEdgeShare, 1 - MinEdgeShare));
    }

    /// <summary>
    /// The point of the face where its corners' three regions meet: the middle of the smallest piece of a grid over the face whose
    /// corners are of all three, nearest the middle of the face's border points. Without one (the regions meet just outside the face),
    /// the middle of the border points.
    /// </summary>
    private static Vector3 Junction(Vector3 a, Vector3 b, Vector3 c, int ra, int rb, int rc, Vector3 ab, Vector3 bc, Vector3 ca,
        Func<Vector3, int> regionAt)
    {
        const int n = JunctionSteps;
        var middle = (ab + bc + ca) / 3;
        var regionOf = new Dictionary<(int, int), int>();
        int At(int i, int j)
        {
            if (!regionOf.TryGetValue((i, j), out var region))
                regionOf[(i, j)] = region = regionAt(Lerp(a, b, c, Weights(i, j)));
            return region;
        }

        Vector3? best = null;
        var bestDistance = float.PositiveInfinity;
        void Try(params (int i, int j)[] corners)
        {
            var seen = corners.Select(k => At(k.i, k.j)).ToArray();
            if (!seen.Contains(ra) || !seen.Contains(rb) || !seen.Contains(rc))
                return;
            var w = corners.Select(k => Weights(k.i, k.j)).Aggregate(Vector3.Zero, (s, x) => s + x) / corners.Length;
            var point = Lerp(a, b, c, Clamped(w));
            var distance = Vector3.DistanceSquared(point, middle);
            if (distance < bestDistance)
                (best, bestDistance) = (point, distance);
        }

        // The grid's small triangles, (i, j) counting steps from a towards b and towards c
        for (var i = 0; i < n; i++)
            for (var j = 0; i + j < n; j++)
            {
                Try((i, j), (i + 1, j), (i, j + 1));
                if (i + j + 2 <= n)
                    Try((i + 1, j), (i + 1, j + 1), (i, j + 1));
            }
        return best ?? middle;

        static Vector3 Weights(int i, int j) => new(1 - (i + j) / (float)n, i / (float)n, j / (float)n);
    }

    private static Vector3 Clamped(Vector3 w)
    {
        w = Vector3.Max(w, new Vector3(MinJunctionWeight));
        return w / (w.X + w.Y + w.Z);
    }

    private static Vector3 Lerp(Vector3 a, Vector3 b, Vector3 c, Vector3 w) => a * w.X + b * w.Y + c * w.Z;
}
