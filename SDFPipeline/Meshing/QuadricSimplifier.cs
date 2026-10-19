using System.Numerics;

namespace SDFPipeline.Meshing;

/// <summary>
/// Simplifies by edge collapses in order of quadric error (Garland and Heckbert 1997): each vertex carries planes, and a merged vertex
/// goes where it is nearest all of them. The planes are the surface's exact tangent planes from contouring, not the mesh's own faces,
/// so the error is measured against the SDF: where contouring cut a corner off a crease that runs just outside a cell, merging the
/// vertex back onto the crease costs nothing. Flat areas shrink to a few big triangles while sharp edges and corners stay exactly where
/// they are, since moving off a corner is costly in all three of its planes.
/// </summary>
public static class QuadricSimplifier
{
    /// <summary>A collapse may turn no face by more than this (cosine): beyond it the surface would fold over.</summary>
    private const float MinNormalAgreement = 0.2f;

    /// <summary>
    /// The same on coarse levels, which must turn faces further to fold a curved surface into a few faces; the SDF's gradient still keeps
    /// every face from coming to face inwards.
    /// </summary>
    private const float CoarseMinNormalAgreement = 0;

    /// <summary>
    /// A collapse may not make a face thinner than this (see Quality) unless it was already: slivers shade badly, since the per-vertex
    /// data from the SDF is spread across them.
    /// </summary>
    private const float MinQuality = 0.2f;

    /// <summary>A collapse costing less than this fraction of the squared error budget (a tenth of it in distance) counts as free: it moves nothing noticeably off the surface.</summary>
    private const double FreeCollapse = 0.01;

    /// <summary>
    /// How far a merged vertex may be from the SDF's surface, in cells, unless one it replaces was further; on coarse levels the error
    /// limit, if that is more.
    /// </summary>
    private const float MaxVertexDistanceInCells = 0.25f;

    /// <summary>A face this close (cosine) to the outward normals of its corners' surface needs no check against the SDF.</summary>
    private const float VouchedAgreement = 0.97f;

    /// <summary>Quadric directions weaker than this fraction of the strongest leave the merged vertex at the edge midpoint along them.</summary>
    private const double PlacementCutoff = 1e-5;

    /// <summary>A refused collapse can become possible once the faces around it change, so the queue is filled again, up to this often.</summary>
    private const int MaxPasses = 8;

    /// <param name="sdf">The surface: no collapse may leave a face facing against its gradient, so simplification cannot fold the mesh.</param>
    /// <param name="maxError">
    /// How far, in metres, a merged vertex may be from the tangent planes it stands for, on average; and from those of creases and
    /// corners on their own, so a vertex that also stands for a large flat area cannot wander off a crease.
    /// </param>
    /// <param name="cellSize">The grid's cell size: each tangent plane stands for about a cell's area of surface.</param>
    /// <param name="creaseAngleDegrees">Cells whose tangent planes differ by more than this hold a crease or corner.</param>
    /// <param name="regions">Optional region of each vertex, such as its material; vertices of different regions are never merged.</param>
    public static TriangleMesh Simplify(ContouredMesh mesh, Func<Vector3, float> sdf, float maxError, float cellSize, float creaseAngleDegrees,
        int[]? regions = null) =>
        new Simplification(mesh, sdf, cellSize, creaseAngleDegrees, regions).Run(maxError, coarse: false);

    /// <summary>
    /// The mesh at each error limit in turn, for levels of detail: the first exactly as <see cref="Simplify"/> gives it, each further one
    /// simplified on from the one before, so the levels cost little more than the first and every level keeps what the one before kept.
    /// Each level is made when it is enumerated.
    /// <para>
    /// The further levels are coarse, so they trade the first level's care for the shape at a distance. Each face must stay within the
    /// error limit of the SDF's surface, checked on the face rather than trusted to the quadrics: they average the error over all the
    /// planes a vertex stands for, which grows lenient once it stands for a large curved area, and would let a thin rim fold flat. In
    /// return a merged vertex may move off the surface by up to the limit (a round shape keeps its volume when its vertices sit a little
    /// outside it), faces may be thin (curved shapes are best followed by long faces along the direction they bend least, as along a stem)
    /// and may turn further, and a vertex next to a material border may merge into the border, which stays where it is.
    /// </para>
    /// </summary>
    /// <param name="maxErrors">The error limit of each level, in metres, growing.</param>
    public static IEnumerable<TriangleMesh> SimplifyInSteps(ContouredMesh mesh, Func<Vector3, float> sdf, IReadOnlyList<float> maxErrors,
        float cellSize, float creaseAngleDegrees, int[]? regions = null)
    {
        var simplification = new Simplification(mesh, sdf, cellSize, creaseAngleDegrees, regions);
        for (var level = 0; level < maxErrors.Count; level++)
            yield return simplification.Run(maxErrors[level], coarse: level > 0);
    }

    /// <summary>A plane quadric: the weighted sum of squared distances to planes, and the total weight for the average.</summary>
    private struct Quadric
    {
        public double A2, AB, AC, AD, B2, BC, BD, C2, CD, D2, Weight;

        public static Quadric Plane(Vector3 normal, Vector3 point, double weight)
        {
            double a = normal.X, b = normal.Y, c = normal.Z, d = -Vector3.Dot(normal, point);
            return new Quadric
            {
                A2 = weight * a * a, AB = weight * a * b, AC = weight * a * c, AD = weight * a * d,
                B2 = weight * b * b, BC = weight * b * c, BD = weight * b * d,
                C2 = weight * c * c, CD = weight * c * d, D2 = weight * d * d, Weight = weight,
            };
        }

        public static Quadric operator +(Quadric p, Quadric q) => new()
        {
            A2 = p.A2 + q.A2, AB = p.AB + q.AB, AC = p.AC + q.AC, AD = p.AD + q.AD,
            B2 = p.B2 + q.B2, BC = p.BC + q.BC, BD = p.BD + q.BD,
            C2 = p.C2 + q.C2, CD = p.CD + q.CD, D2 = p.D2 + q.D2, Weight = p.Weight + q.Weight,
        };

        public static Quadric operator *(Quadric q, double s) => new()
        {
            A2 = q.A2 * s, AB = q.AB * s, AC = q.AC * s, AD = q.AD * s, B2 = q.B2 * s, BC = q.BC * s, BD = q.BD * s,
            C2 = q.C2 * s, CD = q.CD * s, D2 = q.D2 * s, Weight = q.Weight * s,
        };

        /// <summary>The weighted average squared distance from the planes.</summary>
        public double Error(Vector3 v)
        {
            double x = v.X, y = v.Y, z = v.Z;
            var sum = A2 * x * x + 2 * AB * x * y + 2 * AC * x * z + 2 * AD * x
                      + B2 * y * y + 2 * BC * y * z + 2 * BD * y
                      + C2 * z * z + 2 * CD * z + D2;
            return Weight > 0 ? Math.Max(sum, 0) / Weight : 0;
        }

        /// <summary>The same planes with a total weight of 1, so quadrics that stand for very different areas count alike.</summary>
        public Quadric Averaged() => Weight > 0 ? this * (1 / Weight) : default;

        public Vector3 Optimum(Vector3 bias)
        {
            var (x, y, z) = SymmetricSolver.Solve([A2, AB, AC, B2, BC, C2], (-AD, -BD, -CD), (bias.X, bias.Y, bias.Z), PlacementCutoff);
            return new Vector3((float)x, (float)y, (float)z);
        }
    }

    private sealed class Simplification
    {
        private readonly Vector3[] positions;
        private readonly int[] triangles;
        private readonly bool[] removedTriangle;
        private readonly List<int>[] trianglesOf;
        private readonly Quadric[] quadrics;
        /// <summary>The planes of creases and corners alone, so they count in full however much smooth surface a vertex also stands for.</summary>
        private readonly Quadric[] creaseQuadrics;
        /// <summary>Which way the surface faces at each vertex, from the SDF: faces contouring folded over may turn back to it.</summary>
        private readonly Vector3[] outward;
        private readonly int[]? regions;
        private readonly bool[] regionBorder;
        private readonly int[] version;
        private readonly bool[] removedVertex;

        private readonly Func<Vector3, float> sdf;
        private readonly float gradientStep;
        private readonly float cellVertexDistance;

        /// <summary>Whether this run makes a coarse level of detail (see <see cref="SimplifyInSteps"/>), and how far it may then move the surface.</summary>
        private bool coarse;
        private float maxVertexDistance;
        private float maxFaceDistance;

        public Simplification(ContouredMesh contoured, Func<Vector3, float> sdf, float cellSize, float creaseAngleDegrees, int[]? regions)
        {
            this.sdf = sdf;
            gradientStep = cellSize * 1e-3f;
            cellVertexDistance = cellSize * MaxVertexDistanceInCells;
            var mesh = contoured.Mesh;
            positions = (Vector3[])mesh.Positions.Clone();
            this.regions = regions;
            var vertexCount = positions.Length;

            // Neighbouring cells can put their vertices on the same spot (their shared corner): one vertex there keeps the surface closed.
            // Faces that then lose a corner go; faces that are merely flat stay, keeping the surface closed until a collapse removes them
            var welded = new int[vertexCount];
            var first = new Dictionary<Vector3, int>();
            for (var v = 0; v < vertexCount; v++)
                welded[v] = first.TryAdd(positions[v], v) ? v : first[positions[v]];
            var kept = new List<int>();
            for (var t = 0; t < mesh.TriangleCount; t++)
            {
                var (a, b, c) = (welded[mesh.Triangles[3 * t]], welded[mesh.Triangles[3 * t + 1]], welded[mesh.Triangles[3 * t + 2]]);
                if (a != b && b != c && a != c)
                    kept.AddRange([a, b, c]);
            }
            triangles = kept.ToArray();
            var triangleCount = triangles.Length / 3;
            removedTriangle = new bool[triangleCount];

            trianglesOf = new List<int>[vertexCount];
            for (var v = 0; v < vertexCount; v++) trianglesOf[v] = [];
            for (var t = 0; t < triangleCount; t++)
                for (var c = 0; c < 3; c++)
                    trianglesOf[triangles[3 * t + c]].Add(t);

            quadrics = new Quadric[vertexCount];
            creaseQuadrics = new Quadric[vertexCount];
            outward = new Vector3[vertexCount];
            var area = (double)cellSize * cellSize;
            var creaseCosine = MathF.Cos(creaseAngleDegrees * MathF.PI / 180);
            for (var v = 0; v < vertexCount; v++)
            {
                var planes = contoured.Planes[v];
                var crease = IsCrease(planes, creaseCosine);
                foreach (var plane in planes)
                {
                    var quadric = Quadric.Plane(plane.Normal, plane.Point, area / planes.Length);
                    quadrics[welded[v]] += quadric;
                    if (crease)
                        creaseQuadrics[welded[v]] += quadric;
                    outward[welded[v]] += plane.Normal;
                }
            }

            // The rare vertex whose cell had no usable normal falls back on the planes of its faces
            for (var t = 0; t < triangleCount; t++)
            {
                var (a, b, c) = Corners(t);
                var normal = FaceNormal(a, b, c);
                if (normal.LengthSquared() <= 0)
                    continue;
                var plane = Quadric.Plane(Vector3.Normalize(normal), positions[a], normal.Length() * 0.5);
                foreach (var corner in (ReadOnlySpan<int>)[a, b, c])
                {
                    if (contoured.Planes[corner].Length > 0 || welded[corner] != corner)
                        continue;
                    quadrics[corner] += plane;
                    outward[corner] += normal;
                }
            }

            regionBorder = new bool[vertexCount];
            if (regions is not null)
            {
                // A border vertex also gets a plane across the border, through the vertex and facing the other region, counted in full
                // like a crease: merging along a straight border costs nothing, but cutting across a curved one, which would turn a
                // round pupil into a triangle, costs as much as leaving the surface (Garland and Heckbert's boundary planes)
                var across = new Vector3[vertexCount];
                foreach (var (u, v) in Edges())
                {
                    if (regions[u] == regions[v])
                        continue;
                    regionBorder[u] = regionBorder[v] = true;
                    across[u] += AlongSurface(positions[v] - positions[u], outward[u]);
                    across[v] += AlongSurface(positions[u] - positions[v], outward[v]);
                }
                for (var v = 0; v < vertexCount; v++)
                    if (regionBorder[v] && across[v].LengthSquared() > 0)
                        creaseQuadrics[v] += Quadric.Plane(Vector3.Normalize(across[v]), positions[v], area);
            }

            version = new int[vertexCount];
            removedVertex = new bool[vertexCount];
        }

        /// <summary>The direction of the offset along the surface with the given outward normal, as a unit vector; zero if it has none.</summary>
        private static Vector3 AlongSurface(Vector3 offset, Vector3 outward)
        {
            var normal = outward.LengthSquared() > 0 ? Vector3.Normalize(outward) : Vector3.Zero;
            var along = offset - normal * Vector3.Dot(offset, normal);
            return along.LengthSquared() > 0 ? Vector3.Normalize(along) : Vector3.Zero;
        }

        private static bool IsCrease(SurfacePlane[] planes, float creaseCosine)
        {
            for (var i = 0; i < planes.Length; i++)
                for (var j = i + 1; j < planes.Length; j++)
                    if (Vector3.Dot(planes[i].Normal, planes[j].Normal) < creaseCosine)
                        return true;
            return false;
        }

        private HashSet<(int, int)> Edges()
        {
            var edges = new HashSet<(int, int)>();
            for (var t = 0; t < removedTriangle.Length; t++)
            {
                if (removedTriangle[t]) continue;
                for (var c = 0; c < 3; c++)
                {
                    var a = triangles[3 * t + c];
                    var b = triangles[3 * t + (c + 1) % 3];
                    edges.Add(a < b ? (a, b) : (b, a));
                }
            }
            return edges;
        }

        private (int a, int b, int c) Corners(int t) => (triangles[3 * t], triangles[3 * t + 1], triangles[3 * t + 2]);

        private Vector3 FaceNormal(int a, int b, int c) => Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);

        private readonly record struct Candidate(int U, int V, int VersionU, int VersionV, Vector3 Target);

        public TriangleMesh Run(float maxError, bool coarse)
        {
            this.coarse = coarse;
            maxVertexDistance = coarse ? MathF.Max(cellVertexDistance, maxError) : cellVertexDistance;
            maxFaceDistance = maxError;
            if (maxError > 0)
                Collapse((double)maxError * maxError);
            return Compact();
        }

        /// <summary>Collapses the cheapest edges first, in passes until a pass changes nothing.</summary>
        private void Collapse(double maxSquaredError)
        {
            for (var pass = 0; pass < MaxPasses; pass++)
            {
                var collapsed = 0;
                var queue = new PriorityQueue<Candidate, double>();
                foreach (var (u, v) in Edges())
                    Push(queue, u, v);

                while (queue.TryDequeue(out var candidate, out var cost))
                {
                    if (cost > maxSquaredError)
                        break;
                    var (u, v) = (candidate.U, candidate.V);
                    if (removedVertex[u] || removedVertex[v] || version[u] != candidate.VersionU || version[v] != candidate.VersionV)
                        continue;
                    // Collapses that cost nothing (on a plane or along a crease line) may pass through slivers on the way to big flat faces;
                    // on coarse levels any collapse may make thin faces
                    var guardQuality = !coarse && cost > maxSquaredError * FreeCollapse;
                    if (!StaysManifold(u, v) || !KeepsFacing(u, v, candidate.Target, guardQuality) || !KeepsFacing(v, u, candidate.Target, guardQuality)
                        || !StaysOnSurface(u, v, candidate.Target)
                        || coarse && (!FacesStayNear(u, v, candidate.Target) || !FacesStayNear(v, u, candidate.Target)))
                        continue;

                    CollapseInto(u, v, candidate.Target);
                    collapsed++;
                    foreach (var neighbour in Neighbours(u))
                        Push(queue, u, neighbour);
                }
                if (collapsed == 0)
                    return;
            }
        }

        private void Push(PriorityQueue<Candidate, double> queue, int u, int v)
        {
            // Region borders stay put: border vertices merge only along the border, and never with another region. On coarse levels a
            // vertex beside the border may merge into it and take its place, or the row beside every border would stay as dense as it is
            if (regions is not null && (regions[u] != regions[v] || regionBorder[u] != regionBorder[v] && !coarse))
                return;

            var quadric = quadrics[u] + quadrics[v];
            var crease = creaseQuadrics[u] + creaseQuadrics[v];
            var target = regionBorder[u] != regionBorder[v]
                ? positions[regionBorder[u] ? u : v]
                : (quadric.Averaged() + crease.Averaged()).Optimum((positions[u] + positions[v]) * 0.5f);
            if (!float.IsFinite(target.X + target.Y + target.Z))
                return;
            queue.Enqueue(new Candidate(u, v, version[u], version[v], target), Math.Max(quadric.Error(target), crease.Error(target)));
        }

        private HashSet<int> Neighbours(int vertex)
        {
            var neighbours = new HashSet<int>();
            foreach (var t in trianglesOf[vertex])
            {
                var (a, b, c) = Corners(t);
                neighbours.Add(a);
                neighbours.Add(b);
                neighbours.Add(c);
            }
            neighbours.Remove(vertex);
            return neighbours;
        }

        /// <summary>
        /// Whether the surface stays a closed sheet: no face twice, and no more edges at the merged vertex without exactly two faces than
        /// there were at the two vertices before. On a clean surface that stops it pinching; where contouring left a tangle of edges with
        /// four faces (a cell face with alternating corner signs), it lets collapses untangle it a step at a time.
        /// </summary>
        private bool StaysManifold(int u, int v)
        {
            var before = FacesPerEdge(u, v).Values.Count(count => count != 2) + FacesPerEdge(v, u).Values.Count(count => count != 2);

            var faces = new HashSet<(int, int, int)>();
            var after = new Dictionary<int, int>();
            foreach (var t in trianglesOf[u].Concat(trianglesOf[v]).Distinct())
            {
                var (a, b, c) = Corners(t);
                if ((a == u || b == u || c == u) && (a == v || b == v || c == v))
                    continue;
                (a, b, c) = (a == v ? u : a, b == v ? u : b, c == v ? u : c);
                if (!faces.Add(Sorted(a, b, c)))
                    return false;
                foreach (var corner in (ReadOnlySpan<int>)[a, b, c])
                    if (corner != u) after[corner] = after.GetValueOrDefault(corner) + 1;
            }
            return after.Count >= 3 && after.Values.Count(count => count != 2) <= before;
        }

        /// <summary>How many faces each edge at the vertex has, by the vertex at its other end; the edge to <paramref name="ignored"/> is left out.</summary>
        private Dictionary<int, int> FacesPerEdge(int vertex, int ignored)
        {
            var counts = new Dictionary<int, int>();
            foreach (var t in trianglesOf[vertex])
            {
                var (a, b, c) = Corners(t);
                foreach (var corner in (ReadOnlySpan<int>)[a, b, c])
                    if (corner != vertex) counts[corner] = counts.GetValueOrDefault(corner) + 1;
            }
            counts.Remove(ignored);
            return counts;
        }

        private static (int, int, int) Sorted(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return (a, b, c);
        }

        /// <summary>
        /// Whether the faces around <paramref name="moved"/> that stay keep facing the same way when it moves to the target. A face may
        /// not become flat, nor turn far, nor face against the SDF's gradient; but one contouring folded against the surface may turn
        /// back to face outwards.
        /// </summary>
        private bool KeepsFacing(int moved, int other, Vector3 target, bool guardQuality)
        {
            foreach (var t in trianglesOf[moved])
            {
                var (a, b, c) = Corners(t);
                if (a == other || b == other || c == other)
                    continue;
                var before = FaceNormal(a, b, c);
                var (pa, pb, pc) = (a == moved ? target : positions[a], b == moved ? target : positions[b], c == moved ? target : positions[c]);
                var after = Vector3.Cross(pb - pa, pc - pa);
                if (after.LengthSquared() <= 0)
                    return false;
                var surface = outward[a] + outward[b] + outward[c];
                var turnsBack = Vector3.Dot(before, surface) <= 0 && Vector3.Dot(after, surface) > 0;
                if (!turnsBack && Vector3.Dot(before, after) < (coarse ? CoarseMinNormalAgreement : MinNormalAgreement) * before.Length() * after.Length())
                    return false;
                // A face facing with the SDF may not come to face against it. Where the surface's own normals vouch for the face that
                // cannot happen; elsewhere, as on a thin lip, ask the SDF. A face contouring left folded may stay so while others untangle
                var vouched = !coarse && Vector3.Dot(after, surface) >= VouchedAgreement * after.Length() * surface.Length();
                if (!vouched && Vector3.Dot(after, Gradient((pa + pb + pc) / 3)) <= 0
                             && Vector3.Dot(before, Gradient((positions[a] + positions[b] + positions[c]) / 3)) > 0)
                    return false;
                if (guardQuality && Quality(pa, pb, pc) is var quality && quality < MinQuality && quality < Quality(positions[a], positions[b], positions[c]))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Whether the merged vertex stays near the SDF's surface: within a fraction of a cell (on coarse levels, the error limit), or no
        /// further off than the worse of the two vertices it replaces. The quadrics average over many planes, which on a lip thinner than a
        /// cell can hide a vertex sliding off it.
        /// </summary>
        private bool StaysOnSurface(int u, int v, Vector3 target)
        {
            var distance = MathF.Abs(sdf(target));
            return distance <= maxVertexDistance
                   || distance <= MathF.Max(MathF.Abs(sdf(positions[u])), MathF.Abs(sdf(positions[v])));
        }

        /// <summary>
        /// Whether the faces around <paramref name="moved"/> that stay keep within the error limit of the SDF's surface when it moves to
        /// the target, at their centres and the middles of their edges, where a face over a curved surface is furthest from it.
        /// </summary>
        private bool FacesStayNear(int moved, int other, Vector3 target)
        {
            foreach (var t in trianglesOf[moved])
            {
                var (a, b, c) = Corners(t);
                if (a == other || b == other || c == other)
                    continue;
                var (pa, pb, pc) = (a == moved ? target : positions[a], b == moved ? target : positions[b], c == moved ? target : positions[c]);
                if (MathF.Abs(sdf((pa + pb + pc) / 3)) > maxFaceDistance || MathF.Abs(sdf((pa + pb) * 0.5f)) > maxFaceDistance
                    || MathF.Abs(sdf((pb + pc) * 0.5f)) > maxFaceDistance || MathF.Abs(sdf((pc + pa) * 0.5f)) > maxFaceDistance)
                    return false;
            }
            return true;
        }

        /// <summary>The SDF's gradient from four samples on a tetrahedron, which is enough to tell which way a face should face.</summary>
        private Vector3 Gradient(Vector3 p)
        {
            var h = gradientStep;
            var (a, b, c, d) = (new Vector3(h, -h, -h), new Vector3(-h, -h, h), new Vector3(-h, h, -h), new Vector3(h, h, h));
            return a * sdf(p + a) + b * sdf(p + b) + c * sdf(p + c) + d * sdf(p + d);
        }

        /// <summary>1 for an equilateral triangle, towards 0 for a sliver.</summary>
        private static float Quality(Vector3 a, Vector3 b, Vector3 c)
        {
            var lengths = Vector3.DistanceSquared(a, b) + Vector3.DistanceSquared(b, c) + Vector3.DistanceSquared(c, a);
            return lengths > 0 ? 2 * MathF.Sqrt(3) * Vector3.Cross(b - a, c - a).Length() / lengths : 0;
        }

        private void CollapseInto(int u, int v, Vector3 target)
        {
            positions[u] = target;
            quadrics[u] += quadrics[v];
            creaseQuadrics[u] += creaseQuadrics[v];
            outward[u] += outward[v];
            regionBorder[u] |= regionBorder[v];
            foreach (var t in trianglesOf[v])
            {
                var (a, b, c) = Corners(t);
                if (a == u || b == u || c == u)
                {
                    removedTriangle[t] = true;
                    foreach (var corner in (ReadOnlySpan<int>)[a, b, c])
                        if (corner != v) trianglesOf[corner].Remove(t);
                    continue;
                }
                for (var corner = 0; corner < 3; corner++)
                    if (triangles[3 * t + corner] == v) triangles[3 * t + corner] = u;
                trianglesOf[u].Add(t);
            }
            trianglesOf[v].Clear();
            removedVertex[v] = true;
            version[u]++;
        }

        private TriangleMesh Compact()
        {
            var newIndex = new int[positions.Length];
            Array.Fill(newIndex, -1);
            var kept = new List<Vector3>();
            var keptTriangles = new List<int>();
            for (var t = 0; t < removedTriangle.Length; t++)
            {
                if (removedTriangle[t]) continue;
                for (var c = 0; c < 3; c++)
                {
                    var vertex = triangles[3 * t + c];
                    if (newIndex[vertex] < 0)
                    {
                        newIndex[vertex] = kept.Count;
                        kept.Add(positions[vertex]);
                    }
                    keptTriangles.Add(newIndex[vertex]);
                }
            }
            return new TriangleMesh(kept.ToArray(), keptTriangles.ToArray());
        }
    }
}
