using System.Diagnostics;
using System.Numerics;
using SDFPipeline.Gpu;

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

    /// <summary>How many threads batched simplification may use (-1: as many as the machine has); the result is the same for any.</summary>
    public static int MaxDegreeOfParallelism { get; set; } = -1;

    private static ParallelOptions Threads => new() { MaxDegreeOfParallelism = MaxDegreeOfParallelism };

    /// <param name="sdf">The surface: no collapse may leave a face facing against its gradient, so simplification cannot fold the mesh.</param>
    /// <param name="maxError">
    /// How far, in metres, a merged vertex may be from the tangent planes it stands for, on average; and from those of creases and
    /// corners on their own, so a vertex that also stands for a large flat area cannot wander off a crease.
    /// </param>
    /// <param name="cellSize">The grid's cell size: each tangent plane stands for about a cell's area of surface.</param>
    /// <param name="creaseAngleDegrees">Cells whose tangent planes differ by more than this hold a crease or corner.</param>
    /// <param name="regions">Optional region of each vertex, such as its material; vertices of different regions are never merged.</param>
    /// <param name="locked">
    /// Optional: the vertices that must stay exactly where they are, such as the border of a terrain chunk, which has to keep meeting its
    /// neighbours. A free vertex may merge into a locked one, which keeps its place; two locked vertices never merge.
    /// </param>
    /// <param name="batched">Simplify in rounds of independent folds (see <see cref="Simplification.CollapseBatched"/>) instead of one collapse at a time.</param>
    /// <param name="attributes">
    /// Optional per-vertex attribute (0 to 1, such as how far a vertex has blended into a second material) that a fold must not change
    /// much, in the batched mode: a fold costs, on top of its geometric error, the difference of the two vertices' attributes times the cell size.
    /// </param>
    public static TriangleMesh Simplify(ContouredMesh mesh, Func<Vector3, float> sdf, float maxError, float cellSize, float creaseAngleDegrees,
        int[]? regions = null, bool[]? locked = null, bool batched = false, float[]? attributes = null) =>
        new Simplification(mesh, sdf, cellSize, creaseAngleDegrees, regions, locked, batched, attributes).Run(maxError, coarse: false);

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
        float cellSize, float creaseAngleDegrees, int[]? regions = null, bool batched = false, float[]? attributes = null, GpuSdf? gpu = null)
    {
        var simplification = new Simplification(mesh, sdf, cellSize, creaseAngleDegrees, regions, locked: null, batched, attributes) { Gpu = gpu };
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
        private readonly bool[]? locked;
        private readonly int[] version;
        private readonly bool[] removedVertex;

        private readonly Func<Vector3, float> sdf;

        /// <summary>The same SDF on the GPU, if there is one: the batched mode's sign-bracket checks of the folds chosen in a round are made there, all at once.</summary>
        public GpuSdf? Gpu { get; init; }

        /// <summary>
        /// The SDF's gradient (the tetrahedron's four samples, see <see cref="GradientOnCpu"/>) at the points the GPU answered for before a
        /// phase of the rounds began; read-only while the threads of that phase run. Vertices never move in half-edge folds, so the centres
        /// of faces made of them are the same from round to round, and the entries stay valid for the whole run.
        /// </summary>
        private Dictionary<Vector3, Vector3> gradientCache = new();

        private const int MaxCachedGradients = 3_000_000;

        /// <summary>
        /// CPU time over all threads spent on gradients the cache did not have, which decides whether asking the GPU first pays: noting
        /// the places takes about as long as the scoring itself, so it only wins where the SDF's gradient is over
        /// <see cref="PrefillWorthShare"/> of the scoring (a big program, like a room), not where the facing tests mostly skip the SDF.
        /// </summary>
        private long gradientTicks;

        private const double PrefillWorthShare = 0.5;

        /// <summary>Set once the scoring or the folds are seen to spend that share on gradients; from then on the places are noted and answered first.</summary>
        private bool prefillForScoring, prefillForFolds;

        /// <summary>Asks the GPU for the gradient at every point of <paramref name="points"/> not already known, in one batch.</summary>
        private void FillGradientCache(IEnumerable<Vector3> points)
        {
            if (Gpu is null)
                return;
            var wanted = points.Where(p => !gradientCache.ContainsKey(p)).Distinct().ToArray();
            if (wanted.Length == 0)
                return;
            if (gradientCache.Count + wanted.Length > MaxCachedGradients)
                gradientCache = new Dictionary<Vector3, Vector3>();
            var gradients = Gpu.TetrahedronGradients(wanted, gradientStep);
            gradientCache.EnsureCapacity(gradientCache.Count + wanted.Length);
            for (var i = 0; i < wanted.Length; i++)
                gradientCache[wanted[i]] = gradients[i];
        }
        private readonly float gradientStep;
        private readonly float cellVertexDistance;

        /// <summary>Whether this run makes a coarse level of detail (see <see cref="SimplifyInSteps"/>), and how far it may then move the surface.</summary>
        private bool coarse;
        private float maxVertexDistance;
        private float maxFaceDistance;

        private readonly bool batched;
        /// <summary>The vertices' positions as contouring left them, which tracked points keep.</summary>
        private readonly Vector3[] originalPositions;
        // Tracked points (batched mode): every vertex folded away stays as a point at its original position, bound to the triangle of the
        // current mesh it now lies on. A fold is allowed only if every point it touches stays within the error limit of the surface.
        private readonly List<Vector3> trackedPosition = new();
        private readonly List<int> trackedRegion = new();
        private readonly List<float> trackedError = new();
        private List<int>?[] trackedOnTriangle = [];
        /// <summary>
        /// For an edge of a material border's chain (by its two vertices, the smaller first), the positions of the border vertices folded
        /// into it: the border may not bend further than the error limit from any of them. The tracked points only prove they are near
        /// the surface, not that the border still runs near them.
        /// </summary>
        private readonly Dictionary<(int, int), List<Vector3>> chainStandsFor = new();

        // Where the time goes (reported with the mesh): wall clock of each phase of the rounds, and the CPU time over all threads of the two
        // costly kinds of work inside them
        private readonly SimplifyTimings timings = new();
        private long bracketTicks, trackingTicks;
        /// <summary>The distance tests the tracked points took, for the report of what tracking costs.</summary>
        private long trackedTests;

        private readonly float[]? attributes;
        private readonly double attributeScale;

        public Simplification(ContouredMesh contoured, Func<Vector3, float> sdf, float cellSize, float creaseAngleDegrees, int[]? regions,
            bool[]? locked, bool batched = false, float[]? attributes = null)
        {
            this.sdf = sdf;
            this.batched = batched;
            this.attributes = attributes;
            attributeScale = cellSize;
            gradientStep = cellSize * 1e-3f;
            cellVertexDistance = cellSize * MaxVertexDistanceInCells;
            var mesh = contoured.Mesh;
            positions = (Vector3[])mesh.Positions.Clone();
            originalPositions = (Vector3[])mesh.Positions.Clone();
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

            if (locked is not null)
            {
                // A welded vertex is locked when any of the vertices it stands for is
                this.locked = new bool[vertexCount];
                for (var v = 0; v < vertexCount; v++)
                    this.locked[welded[v]] |= locked[v];
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

        private readonly record struct Candidate(int U, int V, int VersionU, int VersionV, Vector3 Target, double Cost);

        /// <summary>
        /// The order candidates are taken in: cheapest first, and among those that cost the same, the shortest edge first. On a large
        /// flat face, such as a castle wall, every collapse costs exactly nothing; taken in the order the queue happens to hold them, one
        /// vertex would swallow its neighbours one after the other and grow a fan of thousands of faces, which every further collapse at
        /// it walks (that took minutes). Shortest first spreads them evenly over the face, as merging in rounds would.
        /// </summary>
        private readonly record struct Priority(double Cost, float LengthSquared) : IComparable<Priority>
        {
            public int CompareTo(Priority other) => Cost != other.Cost ? Cost.CompareTo(other.Cost) : LengthSquared.CompareTo(other.LengthSquared);
        }

        private Priority PriorityOf(double cost, int u, int v) => new(cost, Vector3.DistanceSquared(positions[u], positions[v]));

        public TriangleMesh Run(float maxError, bool coarse)
        {
            this.coarse = coarse;
            maxVertexDistance = coarse ? MathF.Max(cellVertexDistance, maxError) : cellVertexDistance;
            maxFaceDistance = maxError;
            if (maxError > 0)
            {
                if (batched)
                    CollapseBatched((double)maxError * maxError);
                else
                    Collapse((double)maxError * maxError);
            }
            return Compact();
        }

        /// <summary>Collapses the cheapest edges first, in passes until a pass changes nothing.</summary>
        private void Collapse(double maxSquaredError)
        {
            for (var pass = 0; pass < MaxPasses; pass++)
            {
                var collapsed = 0;
                var queue = new PriorityQueue<Candidate, Priority>();
                foreach (var (u, v) in Edges())
                    Push(queue, u, v);

                while (queue.TryDequeue(out var candidate, out _))
                {
                    var cost = candidate.Cost;
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


        // ---------------------------------------------------------------- Batched folds

        /// <summary>The most rounds of folds a run makes; a round that folds nothing ends it sooner.</summary>
        private const int MaxRounds = 2000;

        /// <summary>A round folds at most this share of the winners, so the quadrics of what is left stay accurate for the next round.</summary>
        private const float FoldShare = 1f / 3;

        /// <summary>
        /// Simplifies in rounds (after Wu and Kobbelt's multiple-choice decimation, 2002), each of four steps.
        /// <list type="number">
        /// <item>Score every vertex whose surroundings changed: the cost of folding it into its best neighbour, a half-edge collapse, so
        /// the vertex lands on one that exists and none is moved. The cost is the quadric error of the pair at the neighbour, or for a
        /// crease or material border the larger of that and the border's own quadric; with <c>attributes</c>, plus the change in the
        /// attribute.</item>
        /// <item>Pick winners without an order: a vertex wins if its score beats every neighbour still in play, and its neighbours then sit
        /// the round out. Repeating until every vertex has won or sat out gives a set of vertices no two of which touch, found the same
        /// whichever order they are looked at, so the sweeps run in parallel.</item>
        /// <item>Fold the winners under the error limit, cheapest first, no more than a third of them: each fold is checked again against
        /// the surface as it now is, and refused if it would flip a face, make a sliver, pinch the surface or leave a face further
        /// than the limit from the SDF.</item>
        /// <item>Rescore what the folds touched, and go on until no winner is under the limit.</item>
        /// </list>
        /// Materials: a vertex whose neighbours all have its region folds freely, into any of them; one on the border between two regions
        /// folds only into a vertex of the same region that is on that border too, so the border keeps its pair of regions and its
        /// shape (the border's own quadric prices bending it); one where three or more regions meet stays. Locked vertices stay.
        /// </summary>
        private void CollapseBatched(double maxSquaredError)
        {
            var count = positions.Length;
            var best = new int[count];
            var score = new double[count];
            var dirty = new bool[count];
            Array.Fill(dirty, true);
            var status = new byte[count];
            var rejected = new HashSet<(int, int, int, int)>();
            for (var round = 0; round < MaxRounds; round++)
            {
                // 1. Score what changed
                var phase = Stopwatch.StartNew();
                timings.Rounds++;
                var stale = Enumerable.Range(0, count).Where(v => dirty[v] && !removedVertex[v]).ToArray();
                var gradientBefore = Interlocked.Read(ref gradientTicks);
                if (prefillForScoring)
                {
                    // The facing tests of the scoring ask for the SDF's gradient at many places: note them first, answer them on the GPU
                    var queries = new System.Collections.Concurrent.ConcurrentBag<List<Vector3>>();
                    Parallel.ForEach(stale, Threads, () => new List<Vector3>(), (v, _, list) =>
                    {
                        BestFold(v, maxSquaredError, rejected, list);
                        return list;
                    }, queries.Add);
                    FillGradientCache(queries.SelectMany(list => list));
                }
                var scoringBefore = Stopwatch.GetTimestamp();
                Parallel.ForEach(stale, Threads, v => (best[v], score[v]) = BestFold(v, maxSquaredError, rejected));
                if (Gpu is not null && !prefillForScoring && stale.Length >= 500)
                {
                    var wall = Stopwatch.GetTimestamp() - scoringBefore;
                    prefillForScoring = (Interlocked.Read(ref gradientTicks) - gradientBefore) > PrefillWorthShare * wall * (MaxDegreeOfParallelism > 0 ? MaxDegreeOfParallelism : Environment.ProcessorCount);
                }
                timings.Scored += stale.Length;
                timings.Score += phase.Elapsed;
                phase.Restart();
                foreach (var v in stale)
                    dirty[v] = false;

                // 2. Winners: local minima among the vertices in play, sweep by sweep
                var candidates = new List<int>();
                for (var v = 0; v < count; v++)
                {
                    status[v] = removedVertex[v] || best[v] < 0 || score[v] > maxSquaredError ? Out : InPlay;
                    if (status[v] == InPlay)
                        candidates.Add(v);
                }
                if (candidates.Count == 0)
                {
                    timings.Winners += phase.Elapsed;
                    return;
                }
                var winners = new List<int>();
                var undecided = candidates;
                while (undecided.Count > 0)
                {
                    var sweep = new bool[undecided.Count];
                    Parallel.For(0, undecided.Count, Threads, i =>
                    {
                        var u = undecided[i];
                        var wins = true;
                        foreach (var w in RingOf(u))
                            if (status[w] == InPlay && Beats(w, u, score))
                            {
                                wins = false;
                                break;
                            }
                        sweep[i] = wins;
                    });
                    for (var i = 0; i < undecided.Count; i++)
                        if (sweep[i])
                            status[undecided[i]] = Won;
                    foreach (var u in undecided.Where((_, i) => sweep[i]))
                    {
                        winners.Add(u);
                        foreach (var w in RingOf(u))
                            if (status[w] == InPlay)
                                status[w] = SatOut;
                    }
                    undecided = undecided.Where(u => status[u] == InPlay).ToList();
                }

                timings.Winners += phase.Elapsed;
                phase.Restart();

                // 3. Fold the cheapest third of them. The costly checks (the SDF's sign bracket, the tracked points) are made in parallel
                // first: they read only the faces around the vertex, which no other winner's fold changes (winners do not touch, so a
                // winner's fold only rewrites faces that have it as a corner), nor the tracked points on them. The folds are then made one
                // at a time in order, each after a cheap check of the faces as they are by then (no pinching, no flipped face). The
                // outcome does not depend on the threads.
                var order = winners.OrderBy(u => score[u]).ThenBy(u => Vector3.DistanceSquared(positions[u], positions[best[u]])).ThenBy(u => u).ToList();
                var allowed = order.Count <= 3 ? order.Count : (int)Math.Ceiling(order.Count * FoldShare);
                var window = Math.Min(order.Count, allowed + allowed / 4 + 4);
                var checkedAhead = new Prechecked?[window];
                var bracketsOnGpu = Gpu is null ? null : BracketsOnGpu(order, window, best);
                if (prefillForFolds)
                {
                    // The facing re-checks of the folds below, which are made one at a time, find their gradients ready
                    var queries = new List<Vector3>[window];
                    Parallel.For(0, window, Threads, i =>
                    {
                        queries[i] = [];
                        KeepsFacing(order[i], best[order[i]], positions[best[order[i]]], !coarse, queries[i]);
                    });
                    FillGradientCache(queries.SelectMany(list => list));
                }
                Parallel.For(0, window, Threads, i =>
                {
                    var u = order[i];
                    var into = best[u];
                    bool ok;
                    if (bracketsOnGpu is not null)
                        ok = bracketsOnGpu[i];
                    else
                    {
                        var bracketStarted = Stopwatch.GetTimestamp();
                        ok = FacesStayOnTheSurface(u, into, positions[into]);
                        Interlocked.Add(ref bracketTicks, Stopwatch.GetTimestamp() - bracketStarted);
                    }
                    var started = Stopwatch.GetTimestamp();
                    var tracking = ok ? EvaluateFold(u, into, double.PositiveInfinity) : null;
                    Interlocked.Add(ref trackingTicks, Stopwatch.GetTimestamp() - started);
                    checkedAhead[i] = new Prechecked(into, ok, tracking);
                });
                timings.Prechecked += window;
                timings.Precheck += phase.Elapsed;
                phase.Restart();
                var applyGradientBefore = Interlocked.Read(ref gradientTicks);
                var applyStarted = Stopwatch.GetTimestamp();
                var folded = 0;
                var refused = 0;
                var position = -1;
                foreach (var u in order)
                {
                    position++;
                    if (folded >= allowed)
                        break;
                    if (removedVertex[u])
                        continue;
                    int into;
                    bool bracketOk;
                    FoldTracking? tracking;
                    if (position < window)
                    {
                        (into, bracketOk, tracking) = (checkedAhead[position]!.Into, checkedAhead[position]!.BracketOk, checkedAhead[position]!.Tracking);
                        // What an earlier fold of the round may have changed at the target: the surface must still be a sheet, facing out
                        if (!StaysManifold(into, u) || !KeepsFacing(u, into, positions[into], !coarse))
                        {
                            timings.Rechecked++;
                            dirty[u] = true;
                            continue;
                        }
                    }
                    else
                    {
                        timings.Rechecked++;
                        (into, _) = BestFold(u, maxSquaredError, rejected);
                        if (into < 0)
                            continue;
                        var started = Stopwatch.GetTimestamp();
                        bracketOk = FacesStayOnTheSurface(u, into, positions[into]);
                        var between = Stopwatch.GetTimestamp();
                        bracketTicks += between - started;
                        tracking = bracketOk ? EvaluateFold(u, into, double.PositiveInfinity) : null;
                        trackingTicks += Stopwatch.GetTimestamp() - between;
                    }
                    var target = positions[into];
                    if (!bracketOk)
                    {
                        rejected.Add((u, into, version[u], version[into]));
                        dirty[u] = true;
                        refused++;
                        continue;
                    }
                    var touched = RingOf(u).Concat(RingOf(into)).Distinct().ToList();
                    var fan = trianglesOf[u].ToList();
                    tracking ??= EvaluateFold(u, into, double.PositiveInfinity)!;
                    if (regions is not null)
                    {
                        // A border vertex folded along its chain: the chord from the next link to the one after stands for what it did
                        var ringOfU = RingOf(u);
                        var otherRegion = ringOfU.Select(w => regions[w]).FirstOrDefault(r => r != regions[u], -1);
                        if (otherRegion >= 0 && ChainOf(u, ringOfU, otherRegion) is { } links)
                        {
                            var b = into == links.First ? links.Second : links.First;
                            var standing = StandingFor(u, into, b);
                            chainStandsFor.Remove(EdgeKey(u, into));
                            chainStandsFor.Remove(EdgeKey(u, b));
                            if (chainStandsFor.TryGetValue(EdgeKey(into, b), out var existing)) existing.AddRange(standing);
                            else chainStandsFor[EdgeKey(into, b)] = standing;
                        }
                    }
                    var folding = Stopwatch.GetTimestamp();
                    CollapseInto(into, u, target);
                    timings.Fold += TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - folding) / (double)Stopwatch.Frequency);
                    folding = Stopwatch.GetTimestamp();
                    Rebind(u, fan, tracking);
                    timings.Rebind += TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - folding) / (double)Stopwatch.Frequency);
                    folded++;
                    timings.Folds++;
                    foreach (var w in touched)
                    {
                        dirty[w] = true;
                        version[w]++;
                    }
                }
                timings.Apply += phase.Elapsed;
                if (Gpu is not null && !prefillForFolds && folded >= 50)
                    prefillForFolds = Interlocked.Read(ref gradientTicks) - applyGradientBefore > 0.3 * (Stopwatch.GetTimestamp() - applyStarted);
                // 4. Another round, unless nothing could be folded and no refusal changed what the next scores will be
                if (folded == 0 && refused == 0)
                    return;
            }
        }

        private const byte Out = 0, InPlay = 1, Won = 2, SatOut = 3;

        /// <summary>A fold checked ahead against the surface.</summary>
        private sealed record Prechecked(int Into, bool BracketOk, FoldTracking? Tracking);

        /// <summary>Whether <paramref name="a"/> scores better than <paramref name="b"/>: cheaper, and on a tie the one a fixed hash puts first.</summary>
        private static bool Beats(int a, int b, double[] score) =>
            score[a] != score[b] ? score[a] < score[b] : Hash(a) < Hash(b) || Hash(a) == Hash(b) && a < b;

        private static uint Hash(int v)
        {
            var x = (uint)v * 2654435761u;
            x ^= x >> 15;
            x *= 2246822519u;
            x ^= x >> 13;
            return x;
        }

        /// <summary>The vertices that share a face with <paramref name="vertex"/>.</summary>
        private List<int> RingOf(int vertex)
        {
            var ring = new List<int>(8);
            foreach (var t in trianglesOf[vertex])
            {
                var (a, b, c) = Corners(t);
                foreach (var corner in (ReadOnlySpan<int>)[a, b, c])
                    if (corner != vertex && !ring.Contains(corner))
                        ring.Add(corner);
            }
            return ring;
        }

        private static (int, int) EdgeKey(int a, int b) => a < b ? (a, b) : (b, a);

        /// <summary>
        /// The two neighbours of a vertex on a material border that are on the same border, in its own region: the chain it is a link of.
        /// Null where it is not a plain chain (more or fewer links, as at a spot where the border forks).
        /// </summary>
        private (int First, int Second)? ChainOf(int u, List<int> ring, int otherRegion)
        {
            // A link of the chain is a neighbour in u's region that shares a face with u whose third corner is in the other region
            var links = new List<int>(2);
            foreach (var t in trianglesOf[u])
            {
                var (a, b, c) = Corners(t);
                var (first, second) = a == u ? (b, c) : b == u ? (c, a) : (a, b);
                if (regions![first] == regions[u] && regions[second] == otherRegion && !links.Contains(first))
                    links.Add(first);
                if (regions[second] == regions[u] && regions[first] == otherRegion && !links.Contains(second))
                    links.Add(second);
            }
            return links.Count == 2 ? (links[0], links[1]) : null;
        }

        /// <summary>The border vertices that would stand for the chain's new edge when <paramref name="u"/> is folded away.</summary>
        private List<Vector3> StandingFor(int u, int a, int b)
        {
            var points = new List<Vector3> { positions[u] };
            if (chainStandsFor.TryGetValue(EdgeKey(u, a), out var first)) points.AddRange(first);
            if (chainStandsFor.TryGetValue(EdgeKey(u, b), out var second)) points.AddRange(second);
            return points;
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var t = ab.LengthSquared() > 0 ? Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0, 1) : 0;
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>QEM is only a prefilter for the tracked cost: a fold dearer than this many times the limit is not looked at.</summary>
        private const double PrefilterFactor = 16;

        /// <summary>How many folds of a vertex (cheapest by QEM first) are priced with the tracked points.</summary>
        private const int TrackedTries = 3;

        /// <summary>
        /// The neighbour to fold <paramref name="u"/> into and what it costs, or -1 if no fold is allowed or within the limit. The cost is
        /// the worst distance, squared, of any tracked point the fold touches (every vertex folded away so far that lies on the faces
        /// around <paramref name="u"/>, and <paramref name="u"/> itself) from the faces that would replace the fan: so error cannot add up
        /// unseen from fold to fold. QEM only picks which neighbours to try, cheapest first.
        /// </summary>
        private (int Into, double Cost) BestFold(int u, double maxSquaredError, HashSet<(int, int, int, int)> rejected, List<Vector3>? collect = null)
        {
            if (removedVertex[u] || locked is not null && locked[u] || trianglesOf[u].Count == 0)
                return (-1, double.PositiveInfinity);
            var ring = RingOf(u);

            // Materials: which regions meet here
            int otherRegion = -1;
            if (regions is not null)
            {
                foreach (var w in ring)
                {
                    if (regions[w] == regions[u] || regions[w] == otherRegion)
                        continue;
                    if (otherRegion >= 0)
                        return (-1, double.PositiveInfinity); // three regions meet: a corner stays
                    otherRegion = regions[w];
                }
            }

            var chain = regions is not null && otherRegion >= 0 ? ChainOf(u, ring, otherRegion) : null;
            if (regions is not null && otherRegion >= 0 && chain is null)
                return (-1, double.PositiveInfinity);
            var options = new List<(int V, double Cost, float Length)>();
            foreach (var v in ring)
            {
                if (regions is not null)
                {
                    if (regions[v] != regions[u])
                        continue;
                    // On a border, only along it, into the next link of its chain
                    if (otherRegion >= 0)
                    {
                        if (v != chain!.Value.First && v != chain.Value.Second)
                            continue;
                        // ... and only while every border vertex folded into the chain stays within the limit of the new chord
                        var b = v == chain.Value.First ? chain.Value.Second : chain.Value.First;
                        var reach = (float)Math.Sqrt(maxSquaredError);
                        if (StandingFor(u, v, b).Any(point => DistanceToSegment(point, positions[v], positions[b]) > reach))
                            continue;
                    }
                }
                if (rejected.Contains((u, v, version[u], version[v])))
                    continue;
                var merged = quadrics[u] + quadrics[v];
                var mergedCrease = creaseQuadrics[u] + creaseQuadrics[v];
                var cost = Math.Max(merged.Error(positions[v]), mergedCrease.Error(positions[v]));
                if (cost <= maxSquaredError * PrefilterFactor)
                    options.Add((v, cost, Vector3.DistanceSquared(positions[u], positions[v])));
            }
            var best = (Into: -1, Cost: double.PositiveInfinity, Length: float.PositiveInfinity);
            var tried = 0;
            foreach (var (v, _, length) in options.OrderBy(o => o.Cost).ThenBy(o => o.Length).ThenBy(o => o.V))
            {
                if (tried >= TrackedTries + (collect is null ? 0 : 2))
                    break;
                var guardQuality = !coarse;
                if (collect is not null)
                {
                    // Collecting where the gradient is asked: the candidates a scoring would try, and a few more in case the gradient refuses some
                    if (StaysManifold(v, u))
                    {
                        KeepsFacing(u, v, positions[v], guardQuality, collect);
                        tried++;
                    }
                    continue;
                }
                if (!StaysManifold(v, u) || !KeepsFacing(u, v, positions[v], guardQuality))
                    continue;
                tried++;
                var tracking = EvaluateFold(u, v, maxSquaredError);
                if (tracking is null)
                    continue;
                var cost = tracking.WorstSquared;
                if (attributes is not null)
                {
                    var change = (attributes[u] - attributes[v]) * attributeScale;
                    cost += change * change;
                }
                if (cost <= maxSquaredError && (cost < best.Cost || cost == best.Cost && length < best.Length))
                    best = (v, cost, length);
            }
            return (best.Into, best.Into < 0 ? double.PositiveInfinity : best.Cost);
        }

        // ---------------------------------------------------------------- Tracked points

        /// <summary>Where the tracked points a fold touches would land: a triangle of the new fan for each, and its distance.</summary>
        private sealed class FoldTracking
        {
            public readonly List<(int Point, int Triangle, float Distance)> Placed = new();
            public double WorstSquared;
        }

        /// <summary>
        /// Prices folding <paramref name="u"/> into <paramref name="s"/>: every tracked point on the faces around <paramref name="u"/>, and
        /// <paramref name="u"/> itself, is projected on the faces the fan becomes. Null if one would be further than the limit from them
        /// (<paramref name="maxSquaredError"/>), or would land on a face of another material than its own (a face of two materials takes
        /// it only from the side of its own material, a point's weight on its vertices of that material being at least half).
        /// </summary>
        private FoldTracking? EvaluateFold(int u, int s, double maxSquaredError)
        {
            var fan = new List<(int Triangle, Vector3 A, Vector3 B, Vector3 C, int Ia, int Ib, int Ic)>();
            foreach (var t in trianglesOf[u])
            {
                var (a, b, c) = Corners(t);
                if (a == s || b == s || c == s)
                    continue;
                var (ia, ib, ic) = (a == u ? s : a, b == u ? s : b, c == u ? s : c);
                fan.Add((t, positions[ia], positions[ib], positions[ic], ia, ib, ic));
            }
            var result = new FoldTracking();
            var points = new List<(int Index, Vector3 Position, int Region)>();
            foreach (var t in trianglesOf[u])
                if (trackedOnTriangle.Length > t && trackedOnTriangle[t] is { } bucket)
                    foreach (var point in bucket)
                        points.Add((point, trackedPosition[point], regions is null ? 0 : trackedRegion[point]));
            points.Add((-1, positions[u], regions is null ? 0 : regions[u]));
            if (fan.Count == 0)
                return points.Count == 1 ? result : null;

            var limit = double.IsPositiveInfinity(maxSquaredError) ? double.PositiveInfinity : maxSquaredError;
            foreach (var (index, position, region) in points)
            {
                var bestDistance = double.PositiveInfinity;
                var bestTriangle = -1;
                foreach (var (triangle, a, b, c, ia, ib, ic) in fan)
                {
                    trackedTests++;
                    var (closest, weights) = ClosestOnTriangle(position, a, b, c);
                    var distanceSquared = (double)Vector3.DistanceSquared(position, closest);
                    if (distanceSquared >= bestDistance)
                        continue;
                    if (regions is not null)
                    {
                        float own = 0;
                        var any = false;
                        if (regions[ia] == region) { own += weights.X; any = true; }
                        if (regions[ib] == region) { own += weights.Y; any = true; }
                        if (regions[ic] == region) { own += weights.Z; any = true; }
                        var pure = regions[ia] == region && regions[ib] == region && regions[ic] == region;
                        if (!pure && (!any || own < 0.5f))
                            continue;
                    }
                    bestDistance = distanceSquared;
                    bestTriangle = triangle;
                }
                if (bestTriangle < 0 || bestDistance > limit)
                    return null;
                result.WorstSquared = Math.Max(result.WorstSquared, bestDistance);
                result.Placed.Add((index, bestTriangle, (float)Math.Sqrt(bestDistance)));
            }
            return result;
        }

        /// <summary>After <paramref name="removed"/> was folded away: its triangles' points, and itself, bound to the triangles they landed on.</summary>
        private void Rebind(int removed, List<int> oldFan, FoldTracking tracking)
        {
            if (trackedOnTriangle.Length < triangles.Length / 3)
                Array.Resize(ref trackedOnTriangle, triangles.Length / 3);
            foreach (var t in oldFan)
                trackedOnTriangle[t]?.Clear();
            foreach (var (point, triangle, distance) in tracking.Placed)
            {
                var index = point;
                if (index < 0)
                {
                    index = trackedPosition.Count;
                    trackedPosition.Add(originalPositions[removed]);
                    trackedRegion.Add(regions is null ? 0 : regions[removed]);
                    trackedError.Add(0);
                }
                trackedError[index] = distance;
                (trackedOnTriangle[triangle] ??= new List<int>()).Add(index);
            }
        }

        /// <summary>The nearest point of a triangle to <paramref name="p"/> and the barycentric weights of the corners there (Ericson).</summary>
        private static (Vector3 Point, Vector3 Weights) ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a;
            var ac = c - a;
            var ap = p - a;
            var d1 = Vector3.Dot(ab, ap);
            var d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return (a, new Vector3(1, 0, 0));
            var bp = p - b;
            var d3 = Vector3.Dot(ab, bp);
            var d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return (b, new Vector3(0, 1, 0));
            var vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0)
            {
                var v = d1 / (d1 - d3);
                return (a + ab * v, new Vector3(1 - v, v, 0));
            }
            var cp = p - c;
            var d5 = Vector3.Dot(ab, cp);
            var d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return (c, new Vector3(0, 0, 1));
            var vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0)
            {
                var w = d2 / (d2 - d6);
                return (a + ac * w, new Vector3(1 - w, 0, w));
            }
            var va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            {
                var w = (d4 - d3) / (d4 - d3 + (d5 - d6));
                return (b + (c - b) * w, new Vector3(0, 1 - w, w));
            }
            var denominator = 1 / (va + vb + vc);
            var vv = vb * denominator;
            var ww = vc * denominator;
            return (a + ab * vv + ac * ww, new Vector3(1 - vv - ww, vv, ww));
        }

        private void Push(PriorityQueue<Candidate, Priority> queue, int u, int v)
        {
            // Region borders stay put: border vertices merge only along the border, and never with another region. On coarse levels a
            // vertex beside the border may merge into it and take its place, or the row beside every border would stay as dense as it is
            if (regions is not null && (regions[u] != regions[v] || regionBorder[u] != regionBorder[v] && !coarse))
                return;
            // A locked vertex stays: the free one merges into it, which CollapseInto does when the locked one comes first
            if (locked is not null && (locked[u] || locked[v]))
            {
                if (locked[u] && locked[v])
                    return;
                if (locked[v])
                    (u, v) = (v, u);
                var merged = quadrics[u] + quadrics[v];
                var mergedCrease = creaseQuadrics[u] + creaseQuadrics[v];
                var lockedCost = Math.Max(merged.Error(positions[u]), mergedCrease.Error(positions[u]));
                queue.Enqueue(new Candidate(u, v, version[u], version[v], positions[u], lockedCost), PriorityOf(lockedCost, u, v));
                return;
            }

            var quadric = quadrics[u] + quadrics[v];
            var crease = creaseQuadrics[u] + creaseQuadrics[v];
            var target = regionBorder[u] != regionBorder[v]
                ? positions[regionBorder[u] ? u : v]
                : (quadric.Averaged() + crease.Averaged()).Optimum((positions[u] + positions[v]) * 0.5f);
            if (!float.IsFinite(target.X + target.Y + target.Z))
                return;
            var cost = Math.Max(quadric.Error(target), crease.Error(target));
            queue.Enqueue(new Candidate(u, v, version[u], version[v], target, cost), PriorityOf(cost, u, v));
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
        private bool KeepsFacing(int moved, int other, Vector3 target, bool guardQuality, List<Vector3>? collect = null)
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
                if (!vouched)
                {
                    var afterCentre = (pa + pb + pc) / 3;
                    var beforeCentre = (positions[a] + positions[b] + positions[c]) / 3;
                    if (collect is not null)
                    {
                        // Only noting where the gradient will be asked, so the GPU can answer all of it at once
                        collect.Add(afterCentre);
                        collect.Add(beforeCentre);
                    }
                    else if (Vector3.Dot(after, Gradient(afterCentre)) <= 0 && Vector3.Dot(before, Gradient(beforeCentre)) > 0)
                        return false;
                }
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

        /// <summary>
        /// Whether the faces around <paramref name="moved"/> that stay lie on the SDF's surface within the error limit, by the sign of the
        /// SDF, which is right where its size is not (a union is not exact inside, an intersection not outside): at the face's centre and
        /// the middles of its edges the SDF must change sign, or be zero, somewhere along the face's normal within the limit either way. The
        /// segment is sampled at five points rather than two at its ends, so a wall thinner than the limit, which has the same sign at
        /// both ends, still counts as a surface found.
        /// </summary>
        private bool FacesStayOnTheSurface(int moved, int other, Vector3 target)
        {
            foreach (var t in trianglesOf[moved])
            {
                var (a, b, c) = Corners(t);
                if (a == other || b == other || c == other)
                    continue;
                var (pa, pb, pc) = (a == moved ? target : positions[a], b == moved ? target : positions[b], c == moved ? target : positions[c]);
                var cross = Vector3.Cross(pb - pa, pc - pa);
                if (cross.LengthSquared() <= 0)
                    return false;
                var normal = Vector3.Normalize(cross);
                foreach (var p in (ReadOnlySpan<Vector3>)[(pa + pb + pc) / 3, (pa + pb) * 0.5f, (pb + pc) * 0.5f, (pc + pa) * 0.5f])
                    if (!SurfaceWithin(p, normal, maxFaceDistance))
                        return false;
            }
            return true;
        }

        /// <summary>
        /// <see cref="FacesStayOnTheSurface"/> for the first <paramref name="window"/> folds of <paramref name="order"/> (each vertex into
        /// its best neighbour), the SDF samples of all of them taken on the GPU in one batch.
        /// </summary>
        private bool[] BracketsOnGpu(List<int> order, int window, int[] best)
        {
            var started = Stopwatch.GetTimestamp();
            var segmentsOf = new List<(Vector3, Vector3, float)>?[window];
            Parallel.For(0, window, Threads, i =>
            {
                var (moved, other) = (order[i], best[order[i]]);
                var target = positions[other];
                var segments = new List<(Vector3, Vector3, float)>();
                foreach (var t in trianglesOf[moved])
                {
                    var (a, b, c) = Corners(t);
                    if (a == other || b == other || c == other)
                        continue;
                    var (pa, pb, pc) = (a == moved ? target : positions[a], b == moved ? target : positions[b], c == moved ? target : positions[c]);
                    var cross = Vector3.Cross(pb - pa, pc - pa);
                    if (cross.LengthSquared() <= 0)
                    {
                        segmentsOf[i] = null;
                        return;
                    }
                    var normal = Vector3.Normalize(cross);
                    segments.Add(((pa + pb + pc) / 3, normal, maxFaceDistance));
                    segments.Add(((pa + pb) * 0.5f, normal, maxFaceDistance));
                    segments.Add(((pb + pc) * 0.5f, normal, maxFaceDistance));
                    segments.Add(((pc + pa) * 0.5f, normal, maxFaceDistance));
                }
                segmentsOf[i] = segments;
            });

            var offsets = new int[window + 1];
            for (var i = 0; i < window; i++)
                offsets[i + 1] = offsets[i] + (segmentsOf[i]?.Count ?? 0);
            var all = new (Vector3, Vector3, float)[offsets[window]];
            for (var i = 0; i < window; i++)
                segmentsOf[i]?.CopyTo(all, offsets[i]);
            var found = Gpu!.SurfaceWithin(all, gradientStep * 0.1f);

            var ok = new bool[window];
            for (var i = 0; i < window; i++)
            {
                ok[i] = segmentsOf[i] is not null;
                for (var s = offsets[i]; ok[i] && s < offsets[i + 1]; s++)
                    ok[i] = found[s];
            }
            Interlocked.Add(ref bracketTicks, Stopwatch.GetTimestamp() - started);
            return ok;
        }

        /// <summary>Whether the SDF changes sign (or is zero) along the segment of the given length either way from <paramref name="p"/> along <paramref name="normal"/>.</summary>
        private bool SurfaceWithin(Vector3 p, Vector3 normal, float distance)
        {
            var eps = gradientStep * 0.1f;
            var previous = sdf(p - normal * distance);
            if (previous >= -eps && previous <= eps)
                return true;
            for (var i = 1; i <= 4; i++)
            {
                var value = sdf(p + normal * (distance * (i - 2) / 2));
                if (value >= -eps && value <= eps || (value > 0) != (previous > 0))
                    return true;
                previous = value;
            }
            return false;
        }

        /// <summary>The SDF's gradient from four samples on a tetrahedron, which is enough to tell which way a face should face.</summary>
        private Vector3 Gradient(Vector3 p)
        {
            if (gradientCache.TryGetValue(p, out var known))
                return known;
            if (Gpu is null)
                return GradientOnCpu(p);
            var started = Stopwatch.GetTimestamp();
            var gradient = GradientOnCpu(p);
            Interlocked.Add(ref gradientTicks, Stopwatch.GetTimestamp() - started);
            return gradient;
        }

        private Vector3 GradientOnCpu(Vector3 p)
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
            var keptSources = new List<int>();
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
                        keptSources.Add(vertex);
                    }
                    keptTriangles.Add(newIndex[vertex]);
                }
            }
            return new TriangleMesh(kept.ToArray(), keptTriangles.ToArray())
            {
                Sources = keptSources.ToArray(),
                TrackedError = trackedError.Count > 0 ? trackedError.Max() : 0,
                TrackedTests = trackedTests,
                Timings = batched ? timings.With(bracketTicks, trackingTicks) : null,
            };
        }
    }
}
