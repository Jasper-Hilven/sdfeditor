using System.Collections.Concurrent;
using System.Numerics;
using SDFProject.MeshExport;

namespace SDFPipeline.Meshing;

/// <summary>A triangle mesh with shared vertices: triangle i is Triangles[3i..3i+2], counter-clockwise seen from outside.</summary>
public sealed record TriangleMesh(Vector3[] Positions, int[] Triangles)
{
    public static readonly TriangleMesh Empty = new([], []);

    public int TriangleCount => Triangles.Length / 3;

    /// <summary>
    /// For a simplified mesh, the vertex of the contoured mesh each vertex is (the one that stayed when others merged into it), so it
    /// keeps that vertex's region; null for any other mesh.
    /// </summary>
    public int[]? Sources { get; init; }
}

/// <summary>A point on the surface and the outward normal there, from the SDF: the tangent plane the mesh should keep close to.</summary>
public readonly record struct SurfacePlane(Vector3 Point, Vector3 Normal);

/// <summary>A contoured mesh, and for each vertex the exact tangent planes of the surface in its cell (the Hermite data it was placed by).</summary>
public sealed record ContouredMesh(TriangleMesh Mesh, SurfacePlane[][] Planes);

/// <summary>
/// The grid every mesh of a model is contoured on: cubic cells of <see cref="CellSize"/> metres, corner (i, j, k) at
/// <see cref="Origin"/> + (i, j, k) × <see cref="CellSize"/>, with <see cref="Cells"/> cells along each axis.
/// </summary>
public sealed record ContouringGrid(Vector3 Origin, float CellSize, (int X, int Y, int Z) Cells)
{
    /// <summary>Empty cells around the model, so its surface never touches the edge of the grid.</summary>
    public const int Margin = 2;

    /// <summary>A grid of <paramref name="resolution"/> cells along the longest side of <paramref name="bounds"/>, plus the margin.</summary>
    public static ContouringGrid Around(SdfBounds bounds, int resolution)
    {
        var cellSize = MathF.Max(bounds.LongestSide, 1e-4f) / resolution;
        int Count(float size) => (int)MathF.Ceiling(size / cellSize) + 2 * Margin;
        var cells = (Count(bounds.Size.X), Count(bounds.Size.Y), Count(bounds.Size.Z));
        var gridSize = new Vector3(cells.Item1, cells.Item2, cells.Item3) * cellSize;
        var origin = (bounds.Min + bounds.Max) * 0.5f - gridSize * 0.5f;
        return new ContouringGrid(origin, cellSize, cells);
    }

    public Vector3 Corner(int i, int j, int k) => Origin + new Vector3(i, j, k) * CellSize;

    public Vector3 Max => Corner(Cells.X, Cells.Y, Cells.Z);
}

/// <summary>
/// Manifold dual contouring (Ju et al. 2002; Schaefer, Ju and Warren 2007): a vertex for each separate piece of surface in a cell,
/// placed by a QEF over that piece's edge crossings and the SDF's normals there, and a quad around every grid edge the surface crosses.
/// Unlike marching cubes it puts vertices on sharp edges and corners, so boxes stay boxes; unlike plain dual contouring, the two sides
/// of a shell thinner than a cell get a vertex each instead of being folded into one. Only cells near the surface are evaluated: a cell
/// whose centre is further from the surface than its half-diagonal cannot contain any of it, because the SDFs are distance bounds.
/// </summary>
public static class DualContouring
{
    /// <summary>Cells per side of the blocks that are contoured in parallel.</summary>
    private const int BlockSize = 8;

    /// <summary>
    /// How much a distance may overestimate the true one before a cell is wrongly skipped: distortions like RadialWave and Distort are
    /// not exact distances, so cells are only skipped when they are clearly far away.
    /// </summary>
    private const float SkipSlack = 2f;

    /// <summary>QEF directions with less than this fraction of the strongest one's weight are not trusted: the vertex stays at the mass point along them.</summary>
    private const double QefCutoff = 0.05;

    /// <summary>
    /// The steps of the finite differences for normals, in cells: large enough that float rounding does not tilt the normal, and made
    /// smaller only where the samples straddle a sharp edge, so normals next to it are not blurred across it.
    /// </summary>
    private const float NormalStepInCells = 0.05f, SmallestNormalStepInCells = 1e-4f;

    /// <summary>A second difference above this fraction of the step means the samples straddle a sharp edge.</summary>
    private const float KinkThreshold = 0.05f;

    public static ContouredMesh Contour(Func<Vector3, float> sdf, ContouringGrid grid)
    {
        var clipped = Clip(sdf, grid);
        var blocks = SurfaceBlocks(clipped, grid);
        var results = new ConcurrentBag<BlockResult>();
        Parallel.ForEach(blocks, block => results.Add(new BlockContourer(clipped, grid, block).Run()));
        return Assemble(results, grid, clipped);
    }

    /// <summary>
    /// The SDF cut off half a cell inside the grid, so a shape that runs out of it (a ground plane) is closed off rather than left open.
    /// NaN, as at the exact centre of an ellipsoid, is looked at again a hair away.
    /// </summary>
    private static Func<Vector3, float> Clip(Func<Vector3, float> sdf, ContouringGrid grid)
    {
        var half = grid.CellSize * 0.5f;
        var center = (grid.Origin + grid.Max) * 0.5f;
        var extent = (grid.Max - grid.Origin) * 0.5f - new Vector3(half);
        var jitter = new Vector3(0.31f, 0.57f, 0.73f) * (grid.CellSize * 1e-3f);
        return p =>
        {
            var d = sdf(p);
            if (float.IsNaN(d))
                d = sdf(p + jitter);
            if (float.IsNaN(d))
                d = float.PositiveInfinity;
            var q = Vector3.Abs(p - center) - extent;
            var box = Vector3.Max(q, Vector3.Zero).Length() + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, q.Z)), 0f);
            return MathF.Max(d, box);
        };
    }

    private readonly record struct Block(int I, int J, int K);

    /// <summary>The blocks the surface may pass through, found top-down in an octree of blocks.</summary>
    private static List<Block> SurfaceBlocks(Func<Vector3, float> sdf, ContouringGrid grid)
    {
        var size = BlockSize;
        var longest = Math.Max(grid.Cells.X, Math.Max(grid.Cells.Y, grid.Cells.Z));
        while (size < longest) size *= 2;

        var nodes = new List<(int i, int j, int k)> { (0, 0, 0) };
        while (true)
        {
            var nodeSize = size;
            var near = new ConcurrentBag<(int i, int j, int k)>();
            Parallel.ForEach(nodes, node =>
            {
                if (node.i >= grid.Cells.X || node.j >= grid.Cells.Y || node.k >= grid.Cells.Z)
                    return;
                if (MayContainSurface(sdf, grid, node.i, node.j, node.k, nodeSize))
                    near.Add(node);
            });

            if (size == BlockSize)
                return near.Select(n => new Block(n.i, n.j, n.k)).ToList();

            size /= 2;
            nodes = near.SelectMany(n => Children(n, size)).ToList();
        }
    }

    private static IEnumerable<(int i, int j, int k)> Children((int i, int j, int k) node, int childSize)
    {
        for (var c = 0; c < 8; c++)
            yield return (node.i + ((c & 1) == 0 ? 0 : childSize), node.j + ((c & 2) == 0 ? 0 : childSize), node.k + ((c & 4) == 0 ? 0 : childSize));
    }

    /// <summary>Whether the cube of <paramref name="size"/> cells at corner (i, j, k) may hold surface; NaN might, infinity is empty.</summary>
    private static bool MayContainSurface(Func<Vector3, float> sdf, ContouringGrid grid, int i, int j, int k, int size)
    {
        var halfSize = size * grid.CellSize * 0.5f;
        var d = sdf(grid.Corner(i, j, k) + new Vector3(halfSize));
        return float.IsNaN(d) || float.IsFinite(d) && MathF.Abs(d) <= halfSize * MathF.Sqrt(3f) * SkipSlack;
    }

    /// <summary>A grid edge the surface crosses, named by its lower corner and axis; it becomes a quad.</summary>
    private readonly record struct CrossedEdge(int I, int J, int K, int Axis, bool InsideAtLowerCorner);

    /// <summary>The vertices of one cell, and for each of its 12 edges the vertex of the piece of surface crossing it (−1 if none).</summary>
    private sealed record CellVertices(long Cell, int[] VertexOfEdge, Vector3[] Positions, Vector3[] MassPoints, SurfacePlane[][] Planes);

    private sealed record BlockResult(List<CellVertices> Cells, List<CrossedEdge> Edges);

    /// <summary>Contours one block: its corner values and edge crossings are cached, as neighbouring cells share them.</summary>
    private sealed class BlockContourer(Func<Vector3, float> sdf, ContouringGrid grid, Block block)
    {
        private const int Corners = BlockSize + 1;
        private readonly float[] values = new float[Corners * Corners * Corners];
        private readonly bool[] known = new bool[Corners * Corners * Corners];
        private readonly Dictionary<int, (Vector3 point, Vector3 normal, bool valid)> crossings = [];
        private readonly BlockResult result = new([], []);

        public BlockResult Run()
        {
            Visit(0, 0, 0, BlockSize);
            return result;
        }

        private void Visit(int i, int j, int k, int size)
        {
            var (gi, gj, gk) = (block.I + i, block.J + j, block.K + k);
            if (gi >= grid.Cells.X || gj >= grid.Cells.Y || gk >= grid.Cells.Z)
                return;
            if (size < BlockSize && !MayContainSurface(sdf, grid, gi, gj, gk, size))
                return;
            if (size > 1)
            {
                var half = size / 2;
                for (var c = 0; c < 8; c++)
                    Visit(i + ((c & 1) == 0 ? 0 : half), j + ((c & 2) == 0 ? 0 : half), k + ((c & 4) == 0 ? 0 : half), half);
                return;
            }
            ContourCell(i, j, k);
        }

        private static int CornerIndex(int i, int j, int k) => i + Corners * (j + Corners * k);

        private float Value(int i, int j, int k)
        {
            var index = CornerIndex(i, j, k);
            if (!known[index])
            {
                values[index] = sdf(grid.Corner(block.I + i, block.J + j, block.K + k));
                known[index] = true;
            }
            return values[index];
        }

        private static readonly (int a, int b)[] CellEdges =
        [
            (0, 1), (2, 3), (4, 5), (6, 7), // along x
            (0, 2), (1, 3), (4, 6), (5, 7), // along y
            (0, 4), (1, 5), (2, 6), (3, 7), // along z
        ];

        /// <summary>The corners of each cube face in order around it.</summary>
        private static readonly int[][] FaceCorners =
        [
            [0, 2, 6, 4], [1, 3, 7, 5], // x = 0, x = 1
            [0, 1, 5, 4], [2, 3, 7, 6], // y = 0, y = 1
            [0, 1, 3, 2], [4, 5, 7, 6], // z = 0, z = 1
        ];

        private static readonly int[,] EdgeOfCorners = EdgeTable();

        private static int[,] EdgeTable()
        {
            var table = new int[8, 8];
            for (var e = 0; e < 12; e++)
            {
                var (a, b) = CellEdges[e];
                table[a, b] = table[b, a] = e;
            }
            return table;
        }

        private void ContourCell(int i, int j, int k)
        {
            Span<float> corner = stackalloc float[8];
            var inside = 0;
            for (var c = 0; c < 8; c++)
            {
                corner[c] = Value(i + (c & 1), j + ((c >> 1) & 1), k + ((c >> 2) & 1));
                if (corner[c] < 0) inside++;
            }
            if (inside is 0 or 8)
                return;

            var (gi, gj, gk) = (block.I + i, block.J + j, block.K + k);
            var pieceOfEdge = Pieces(corner, gi, gj, gk);
            var pieceCount = pieceOfEdge.Max() + 1;
            var positions = new Vector3[pieceCount];
            var massPoints = new Vector3[pieceCount];
            var planes = new SurfacePlane[pieceCount][];
            var cellMin = grid.Corner(gi, gj, gk);
            for (var piece = 0; piece < pieceCount; piece++)
                (positions[piece], massPoints[piece], planes[piece]) = PlacePiece(i, j, k, corner, pieceOfEdge, piece, cellMin);
            result.Cells.Add(new CellVertices(CellKey(grid, gi, gj, gk), pieceOfEdge, positions, massPoints, planes));

            // The three edges from the cell's lowest corner are this cell's to turn into quads, so each edge is turned once
            for (var axis = 0; axis < 3; axis++)
            {
                var other = corner[1 << axis];
                if (corner[0] < 0 != other < 0)
                    result.Edges.Add(new CrossedEdge(gi, gj, gk, axis, corner[0] < 0));
            }
        }

        /// <summary>
        /// Which piece of surface crosses each edge (−1 for none), pieces numbered from 0. On each face, the crossings the surface joins
        /// are joined: two crossings always are; with four, the face centre decides which corners the surface keeps together. Both cells
        /// that share a face sample the same centre, so they agree, which keeps the mesh a closed sheet.
        /// </summary>
        private int[] Pieces(ReadOnlySpan<float> corner, int gi, int gj, int gk)
        {
            Span<int> parent = stackalloc int[12];
            for (var e = 0; e < 12; e++) parent[e] = e;
            int Find(Span<int> p, int e) { while (p[e] != e) e = p[e] = p[p[e]]; return e; }

            Span<int> crossed = stackalloc int[4];
            for (var face = 0; face < 6; face++)
            {
                var corners = FaceCorners[face];
                var count = 0;
                for (var c = 0; c < 4; c++)
                {
                    var (a, b) = (corners[c], corners[(c + 1) % 4]);
                    if (corner[a] < 0 != corner[b] < 0)
                        crossed[count++] = EdgeOfCorners[a, b];
                }
                if (count == 2)
                {
                    parent[Find(parent, crossed[0])] = Find(parent, crossed[1]);
                    continue;
                }
                if (count != 4)
                    continue;

                // Four crossings: each corner on the other side from the centre is cut off by the two edges next to it
                var centreInside = FaceCentreValue(face, gi, gj, gk) < 0;
                for (var c = 0; c < 4; c++)
                {
                    if (corner[corners[c]] < 0 == centreInside)
                        continue;
                    var before = EdgeOfCorners[corners[(c + 3) % 4], corners[c]];
                    var after = EdgeOfCorners[corners[c], corners[(c + 1) % 4]];
                    parent[Find(parent, before)] = Find(parent, after);
                }
            }

            var pieceOfEdge = new int[12];
            Span<int> pieceOfRoot = stackalloc int[12];
            pieceOfRoot.Fill(-1);
            var pieces = 0;
            for (var e = 0; e < 12; e++)
            {
                var (a, b) = CellEdges[e];
                if (corner[a] < 0 == corner[b] < 0)
                {
                    pieceOfEdge[e] = -1;
                    continue;
                }
                var root = Find(parent, e);
                if (pieceOfRoot[root] < 0) pieceOfRoot[root] = pieces++;
                pieceOfEdge[e] = pieceOfRoot[root];
            }
            return pieceOfEdge;
        }

        /// <summary>The SDF at a face centre, computed from the face's grid position alone so both cells beside it get the same value.</summary>
        private float FaceCentreValue(int face, int gi, int gj, int gk)
        {
            var side = face % 2;
            var centre = (face / 2) switch
            {
                0 => new Vector3(gi + side, gj + 0.5f, gk + 0.5f),
                1 => new Vector3(gi + 0.5f, gj + side, gk + 0.5f),
                _ => new Vector3(gi + 0.5f, gj + 0.5f, gk + side),
            };
            return sdf(grid.Origin + centre * grid.CellSize);
        }

        /// <summary>The vertex of one piece of surface: the QEF over its crossings, the mass point of its crossings, and its tangent planes.</summary>
        private (Vector3 position, Vector3 massPoint, SurfacePlane[] planes) PlacePiece(int i, int j, int k, ReadOnlySpan<float> corner,
            int[] pieceOfEdge, int piece, Vector3 cellMin)
        {
            var planes = new List<SurfacePlane>(6);
            var massPoint = Vector3.Zero;
            var crossingCount = 0;
            for (var e = 0; e < 12; e++)
            {
                if (pieceOfEdge[e] != piece)
                    continue;
                var (a, b) = CellEdges[e];
                var (point, normal, valid) = Crossing(i + (a & 1), j + ((a >> 1) & 1), k + ((a >> 2) & 1), e / 4, corner[a], corner[b]);
                massPoint += point;
                crossingCount++;
                if (valid)
                    planes.Add(new SurfacePlane(point, normal));
            }
            massPoint /= crossingCount;

            // The QEF Σ (n · (x − p))², relative to the mass point for good conditioning
            var ata = new double[6];
            double bx = 0, by = 0, bz = 0, btb = 0;
            foreach (var plane in planes)
            {
                var (nx, ny, nz) = ((double)plane.Normal.X, (double)plane.Normal.Y, (double)plane.Normal.Z);
                var offset = Vector3.Dot(plane.Normal, plane.Point - massPoint);
                ata[0] += nx * nx; ata[1] += nx * ny; ata[2] += nx * nz;
                ata[3] += ny * ny; ata[4] += ny * nz; ata[5] += nz * nz;
                bx += nx * offset; by += ny * offset; bz += nz * offset;
                btb += (double)offset * offset;
            }
            var position = PlaceVertex(ata, (bx, by, bz), btb, massPoint, cellMin, cellMin + new Vector3(grid.CellSize));
            return (position, massPoint, planes.ToArray());
        }

        /// <summary>
        /// The point of the cell nearest all the planes: the QEF's minimum if it is in the cell, otherwise the best point on the cell's
        /// faces, edges or corners (each choice of coordinates held at the cell's sides is tried). Clamping instead would drag the vertex
        /// off the surface wherever planes meet in the next cell.
        /// </summary>
        private static Vector3 PlaceVertex(double[] ata, (double x, double y, double z) atb, double btb, Vector3 massPoint, Vector3 cellMin,
            Vector3 cellMax)
        {
            var low = cellMin - massPoint;
            var high = cellMax - massPoint;
            var slack = (cellMax.X - cellMin.X) * 1e-4;
            var best = Vector3.Clamp(massPoint, cellMin, cellMax);
            var bestError = double.PositiveInfinity;
            Span<double> fixedAt = stackalloc double[3];
            Span<double> y = stackalloc double[3];
            for (var choice = 0; choice < 27; choice++)
            {
                // Each coordinate is free (0), held at the low side (1) or held at the high side (2)
                var held = (choice % 3, choice / 3 % 3, choice / 9);
                Span<int> holds = [held.Item1, held.Item2, held.Item3];
                for (var axis = 0; axis < 3; axis++)
                    fixedAt[axis] = holds[axis] switch { 1 => Component(low, axis), 2 => Component(high, axis), _ => 0 };

                var m = (double[])ata.Clone();
                double[] r = [atb.x, atb.y, atb.z];
                for (var row = 0; row < 3; row++)
                    for (var column = 0; column < 3; column++)
                        if (holds[column] != 0) r[row] -= Entry(ata, row, column) * fixedAt[column];
                for (var axis = 0; axis < 3; axis++)
                {
                    if (holds[axis] == 0) continue;
                    r[axis] = 0;
                    for (var other = 0; other < 3; other++)
                        SetEntry(m, axis, other, 0);
                }

                var (sx, sy, sz) = SymmetricSolver.Solve(m, (r[0], r[1], r[2]), (0, 0, 0), QefCutoff);
                y[0] = holds[0] == 0 ? sx : fixedAt[0];
                y[1] = holds[1] == 0 ? sy : fixedAt[1];
                y[2] = holds[2] == 0 ? sz : fixedAt[2];

                var inside = true;
                for (var axis = 0; axis < 3; axis++)
                    inside &= y[axis] >= Component(low, axis) - slack && y[axis] <= Component(high, axis) + slack;
                if (!inside)
                    continue;

                // Σ (n · y − offset)² = yᵀ AᵀA y − 2 yᵀ Aᵀb + bᵀb
                var error = btb;
                for (var row = 0; row < 3; row++)
                {
                    error -= 2 * y[row] * (row switch { 0 => atb.x, 1 => atb.y, _ => atb.z });
                    for (var column = 0; column < 3; column++)
                        error += y[row] * Entry(ata, row, column) * y[column];
                }
                if (error < bestError - 1e-12 * Math.Max(btb, 1e-30))
                {
                    bestError = error;
                    best = Vector3.Clamp(massPoint + new Vector3((float)y[0], (float)y[1], (float)y[2]), cellMin, cellMax);
                }
            }
            return best;
        }

        private static float Component(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

        private static readonly int[,] PackedIndex = { { 0, 1, 2 }, { 1, 3, 4 }, { 2, 4, 5 } };

        private static double Entry(double[] packed, int row, int column) => packed[PackedIndex[row, column]];

        private static void SetEntry(double[] packed, int row, int column, double value) => packed[PackedIndex[row, column]] = value;

        /// <summary>Where the surface crosses the edge from corner (i, j, k) along the axis, and the normal there.</summary>
        private (Vector3 point, Vector3 normal, bool valid) Crossing(int i, int j, int k, int axis, float da, float db)
        {
            var key = CornerIndex(i, j, k) * 3 + axis;
            if (crossings.TryGetValue(key, out var known))
                return known;

            var a = grid.Corner(block.I + i, block.J + j, block.K + k);
            var direction = axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ } * grid.CellSize;
            var t = FindRoot(sdf, a, direction, da, db, grid.CellSize);
            var point = a + direction * t;
            var normal = Normal(sdf, point, grid.CellSize);
            var length = normal.Length();
            var crossing = length > 1e-6f && float.IsFinite(length) ? (point, normal / length, true) : (point, Vector3.Zero, false);
            crossings[key] = crossing;
            return crossing;
        }
    }

    /// <summary>Where along a → a + direction the SDF is zero, by regula falsi (Illinois) between the two signs.</summary>
    internal static float FindRoot(Func<Vector3, float> sdf, Vector3 a, Vector3 direction, float da, float db, float cellSize)
    {
        if (!float.IsFinite(da) || !float.IsFinite(db))
            return Bisect(sdf, a, direction, da);

        float t0 = 0, t1 = 1, d0 = da, d1 = db;
        var tolerance = cellSize * 1e-5f;
        var side = 0;
        var t = 0.5f;
        for (var iteration = 0; iteration < 12; iteration++)
        {
            t = Math.Clamp((t0 * d1 - t1 * d0) / (d1 - d0), t0, t1);
            if (!float.IsFinite(t)) t = (t0 + t1) * 0.5f;
            var d = sdf(a + direction * t);
            if (MathF.Abs(d) < tolerance || !float.IsFinite(d))
                break;
            if (d < 0 == d0 < 0)
            {
                (t0, d0) = (t, d);
                if (side == -1) d1 *= 0.5f;
                side = -1;
            }
            else
            {
                (t1, d1) = (t, d);
                if (side == 1) d0 *= 0.5f;
                side = 1;
            }
        }
        return t;
    }

    private static float Bisect(Func<Vector3, float> sdf, Vector3 a, Vector3 direction, float da)
    {
        float t0 = 0, t1 = 1;
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var t = (t0 + t1) * 0.5f;
            if (sdf(a + direction * t) < 0 == da < 0) t0 = t;
            else t1 = t;
        }
        return (t0 + t1) * 0.5f;
    }

    /// <summary>
    /// The SDF's gradient at a surface point, by central differences; its direction is the outward normal. Where the second difference
    /// shows a kink between the samples (a sharp edge within the step), the step shrinks until the samples are all on one side of it.
    /// A point right on a sharp edge has no one normal: it gets none (zero), and its plane is left out rather than tilted.
    /// </summary>
    internal static Vector3 Normal(Func<Vector3, float> sdf, Vector3 p, float cellSize)
    {
        var center = sdf(p);
        var step = cellSize * NormalStepInCells;
        Span<float> plus = stackalloc float[3];
        Span<float> minus = stackalloc float[3];
        while (true)
        {
            var kinked = false;
            for (var axis = 0; axis < 3; axis++)
            {
                var offset = axis switch { 0 => new Vector3(step, 0, 0), 1 => new Vector3(0, step, 0), _ => new Vector3(0, 0, step) };
                plus[axis] = sdf(p + offset);
                minus[axis] = sdf(p - offset);
                kinked |= MathF.Abs(plus[axis] + minus[axis] - 2 * center) > KinkThreshold * step;
            }
            if (!kinked)
                return new Vector3(plus[0] - minus[0], plus[1] - minus[1], plus[2] - minus[2]) / (2 * step);
            if (step <= cellSize * SmallestNormalStepInCells)
                return Vector3.Zero;
            step *= 0.125f;
        }
    }

    /// <summary>The SDF's gradient by central differences; its direction is the outward normal on the surface.</summary>
    internal static Vector3 Gradient(Func<Vector3, float> sdf, Vector3 p, float step) =>
        new Vector3(
            sdf(p + new Vector3(step, 0, 0)) - sdf(p - new Vector3(step, 0, 0)),
            sdf(p + new Vector3(0, step, 0)) - sdf(p - new Vector3(0, step, 0)),
            sdf(p + new Vector3(0, 0, step)) - sdf(p - new Vector3(0, 0, step))) / (2 * step);

    private static long CellKey(ContouringGrid grid, int i, int j, int k) => i + (long)grid.Cells.X * (j + (long)grid.Cells.Y * k);

    /// <summary>The four cells around a grid edge, counter-clockwise around its axis seen from the positive end, and the edge's number in each.</summary>
    private static readonly (int di, int dj, int dk, int edge)[][] AroundEdge =
    [
        [(0, -1, -1, 3), (0, 0, -1, 2), (0, 0, 0, 0), (0, -1, 0, 1)],
        [(-1, 0, -1, 7), (-1, 0, 0, 5), (0, 0, 0, 4), (0, 0, -1, 6)],
        [(-1, -1, 0, 11), (0, -1, 0, 10), (0, 0, 0, 8), (-1, 0, 0, 9)],
    ];

    /// <summary>
    /// Numbers the vertices and turns each crossed edge into a quad of the pieces of surface around it, split along the diagonal that
    /// keeps both triangles facing the way the SDF does (the shorter one when both do). Then no vertex may fold its faces over.
    /// </summary>
    private static ContouredMesh Assemble(IEnumerable<BlockResult> results, ContouringGrid grid, Func<Vector3, float> sdf)
    {
        var blocks = results.ToList();
        var firstVertexOfCell = new Dictionary<long, (int first, int[] vertexOfEdge)>();
        var positions = new List<Vector3>();
        var massPoints = new List<Vector3>();
        var planes = new List<SurfacePlane[]>();
        foreach (var cell in blocks.SelectMany(b => b.Cells).OrderBy(c => c.Cell))
        {
            firstVertexOfCell[cell.Cell] = (positions.Count, cell.VertexOfEdge);
            positions.AddRange(cell.Positions);
            massPoints.AddRange(cell.MassPoints);
            planes.AddRange(cell.Planes);
        }

        var gradientStep = grid.CellSize * 1e-3f;
        bool FacesOut(Vector3 a, Vector3 b, Vector3 c) =>
            Vector3.Cross(b - a, c - a) is var normal && normal.LengthSquared() > 0 && Vector3.Dot(normal, Gradient(sdf, (a + b + c) / 3, gradientStep)) > 0;

        var edges = blocks.SelectMany(b => b.Edges).OrderBy(e => (CellKey(grid, e.I, e.J, e.K), e.Axis)).ToArray();
        var quads = new int[edges.Length][];
        var foldedQuad = new bool[edges.Length];
        Parallel.For(0, edges.Length, q =>
        {
            var edge = edges[q];
            var quad = new int[4];
            for (var c = 0; c < 4; c++)
            {
                var (di, dj, dk, local) = AroundEdge[edge.Axis][c];
                var (ci, cj, ck) = (edge.I + di, edge.J + dj, edge.K + dk);
                if (ci < 0 || cj < 0 || ck < 0 || !firstVertexOfCell.TryGetValue(CellKey(grid, ci, cj, ck), out var cell) || cell.vertexOfEdge[local] < 0)
                    return;
                quad[c] = cell.first + cell.vertexOfEdge[local];
            }

            // Inside at the lower corner means the surface faces along the axis, which is the order above
            if (!edge.InsideAtLowerCorner)
                (quad[1], quad[3]) = (quad[3], quad[1]);

            var (p0, p1, p2, p3) = (positions[quad[0]], positions[quad[1]], positions[quad[2]], positions[quad[3]]);
            int[] first = [quad[0], quad[1], quad[2], quad[0], quad[2], quad[3]];
            int[] second = [quad[0], quad[1], quad[3], quad[1], quad[2], quad[3]];
            var (preferred, other) = Vector3.DistanceSquared(p0, p2) <= Vector3.DistanceSquared(p1, p3) ? (first, second) : (second, first);
            if (FacesOutAll(preferred))
                quads[q] = preferred;
            else if (FacesOutAll(other))
                quads[q] = other;
            else
                (quads[q], foldedQuad[q]) = (preferred, true);
        });

        bool FacesOutAll(int[] pair) =>
            FacesOut(positions[pair[0]], positions[pair[1]], positions[pair[2]]) && FacesOut(positions[pair[3]], positions[pair[4]], positions[pair[5]]);

        var triangles = new List<int>();
        var suspects = new List<int>();
        for (var q = 0; q < quads.Length; q++)
        {
            if (quads[q] is null)
                continue;
            if (foldedQuad[q])
                suspects.AddRange([triangles.Count / 3, triangles.Count / 3 + 1]);
            triangles.AddRange(quads[q]);
        }
        var vertices = positions.ToArray();
        Unfold(vertices, triangles.ToArray(), massPoints, suspects, FacesOut);
        return new ContouredMesh(new TriangleMesh(vertices, triangles.ToArray()), planes.ToArray());
    }

    /// <summary>How many times the vertices of folded faces are moved towards their mass points.</summary>
    private const int UnfoldRounds = 4;

    /// <summary>
    /// The fold guard: a vertex whose faces face against the SDF (or have become flat), as where the QEF puts a vertex on the far side
    /// of a thin lip, moves towards the mass point of its crossings, which lies between them, as far as it takes to face them all out;
    /// failing that, towards the middle of its neighbours.
    /// </summary>
    /// <param name="suspectFaces">The faces that may be folded; faces around moved vertices are looked at again.</param>
    private static void Unfold(Vector3[] positions, int[] triangles, List<Vector3> massPoints, IEnumerable<int> suspectFaces,
        Func<Vector3, Vector3, Vector3, bool> facesOut)
    {
        var trianglesOf = new List<int>[positions.Length];
        for (var v = 0; v < positions.Length; v++) trianglesOf[v] = [];
        for (var t = 0; t < triangles.Length / 3; t++)
            for (var c = 0; c < 3; c++)
                trianglesOf[triangles[3 * t + c]].Add(t);

        bool AllFaceOut(int vertex, Vector3 at) => trianglesOf[vertex].All(t =>
        {
            var (a, b, c) = (triangles[3 * t], triangles[3 * t + 1], triangles[3 * t + 2]);
            return facesOut(a == vertex ? at : positions[a], b == vertex ? at : positions[b], c == vertex ? at : positions[c]);
        });

        var suspects = new HashSet<int>(suspectFaces);
        for (var round = 0; round < UnfoldRounds && suspects.Count > 0; round++)
        {
            var suspectArray = suspects.ToArray();
            var isFolded = new bool[suspectArray.Length];
            Parallel.For(0, suspectArray.Length, s =>
            {
                var t = suspectArray[s];
                isFolded[s] = !facesOut(positions[triangles[3 * t]], positions[triangles[3 * t + 1]], positions[triangles[3 * t + 2]]);
            });
            var folded = new SortedSet<int>();
            for (var s = 0; s < suspectArray.Length; s++)
                if (isFolded[s])
                    folded.UnionWith(triangles.AsSpan(3 * suspectArray[s], 3).ToArray());

            // As if one vertex at a time in order, so neighbours see each other's moves: a vertex's move depends only on the corners of
            // its faces, so vertices that share no face are moved at the same time, in waves, each after every earlier vertex it shares
            // a face with. The result is the same as moving them one by one. Only the faces of moved vertices need looking at again
            suspects.Clear();
            foreach (var wave in Waves(folded, trianglesOf, triangles))
            {
                var moves = new (bool moved, Vector3 to)[wave.Length];
                Parallel.For(0, wave.Length, w => moves[w] = Unfolded(wave[w]));
                for (var w = 0; w < wave.Length; w++)
                {
                    if (!moves[w].moved)
                        continue;
                    positions[wave[w]] = moves[w].to;
                    suspects.UnionWith(trianglesOf[wave[w]]);
                }
            }
        }

        // Where the vertex should go so that all its faces face out, if it needs to and can
        (bool moved, Vector3 to) Unfolded(int vertex)
        {
            if (AllFaceOut(vertex, positions[vertex]))
                return (false, default);
            var ring = Vector3.Zero;
            var ringCount = 0;
            foreach (var t in trianglesOf[vertex])
                for (var c = 0; c < 3; c++)
                    if (triangles[3 * t + c] != vertex) (ring, ringCount) = (ring + positions[triangles[3 * t + c]], ringCount + 1);
            ring = ringCount > 0 ? ring / ringCount : positions[vertex];

            foreach (var (towards, t) in (ReadOnlySpan<(Vector3, float)>)
                     [(massPoints[vertex], 0.25f), (massPoints[vertex], 0.5f), (massPoints[vertex], 0.75f), (massPoints[vertex], 1f), (ring, 0.5f), (ring, 1f)])
            {
                var candidate = Vector3.Lerp(positions[vertex], towards, t);
                if (AllFaceOut(vertex, candidate))
                    return (true, candidate);
            }
            return (false, default);
        }
    }

    /// <summary>
    /// The vertices in waves: each vertex in the wave after the latest one holding an earlier vertex (in their order) it shares a face
    /// with, so no two in a wave share a face and each comes after every earlier vertex whose move it would see.
    /// </summary>
    private static List<int[]> Waves(SortedSet<int> vertices, List<int>[] trianglesOf, int[] triangles)
    {
        var waveOf = new Dictionary<int, int>();
        var waves = new List<List<int>>();
        foreach (var vertex in vertices)
        {
            var wave = 0;
            foreach (var t in trianglesOf[vertex])
                for (var c = 0; c < 3; c++)
                    if (waveOf.TryGetValue(triangles[3 * t + c], out var earlier))
                        wave = Math.Max(wave, earlier + 1);
            waveOf[vertex] = wave;
            if (wave == waves.Count)
                waves.Add([]);
            waves[wave].Add(vertex);
        }
        return waves.Select(w => w.ToArray()).ToList();
    }
}
