using System.Numerics;
using SDFPipeline.Meshing;

namespace SDFPipeline.Levels;

/// <summary>
/// How the ground is cut into square chunks: <see cref="CountX"/> × <see cref="CountZ"/> tiles of <see cref="ChunkSize"/> metres,
/// centred on the origin, enough to cover the whole island. Chunk (i, j) starts at <see cref="Origin"/> + (i, j) × size.
/// </summary>
public sealed record GroundLayout(Vector2 Origin, float ChunkSize, int CountX, int CountZ)
{
    public static GroundLayout Around(LevelTerrain terrain, float chunkSize)
    {
        var extent = terrain.HalfExtent * 2;
        var countX = Math.Max(1, (int)MathF.Ceiling(extent.X / chunkSize - 1e-4f));
        var countZ = Math.Max(1, (int)MathF.Ceiling(extent.Y / chunkSize - 1e-4f));
        return new GroundLayout(new Vector2(-countX * chunkSize / 2, -countZ * chunkSize / 2), chunkSize, countX, countZ);
    }

    public Vector2 Min(int i, int j) => new(X(i, 1), Z(j, 1));

    public Vector2 Max(int i, int j) => new(X(i + 1, 1), Z(j + 1, 1));

    /// <summary>
    /// The x of grid line <paramref name="line"/> when each chunk has <paramref name="cells"/> cells across, counted over the whole level:
    /// neighbouring chunks work out their shared border from the same line number, so it comes out exactly the same in both.
    /// </summary>
    public float X(int line, int cells) => Origin.X + line * ChunkSize / cells;

    /// <inheritdoc cref="X"/>
    public float Z(int line, int cells) => Origin.Y + line * ChunkSize / cells;
}

/// <summary>One level of detail of the ground: a regular grid of this many cells along a chunk's side, simplified to this error.</summary>
/// <param name="MaxError">How far, in metres, simplification may move the surface off the grid's.</param>
public sealed record GroundDetail(int Cells, float MaxError);

/// <summary>
/// One chunk of ground at one level of detail, in metres with +Y up: triangles counter-clockwise seen from above, a normal per vertex
/// straight from the height function (so neighbouring chunks shade alike across their border).
/// </summary>
/// <param name="SkirtTriangles">How many of the triangles, at the end, are the skirts hanging from the chunk's borders.</param>
/// <param name="MaxError">The furthest the surface is above or below the height function, measured on the faces, in metres.</param>
public sealed record GroundMesh(Vector3[] Positions, Vector3[] Normals, int[] Triangles, int SkirtTriangles, float CellSize, float MaxError)
{
    public int TriangleCount => Triangles.Length / 3;
}

/// <summary>
/// Meshes the ground of a level chunk by chunk: a regular grid over the chunk, the triangles wholly beyond the island's edge left out
/// and the grid points beyond it moved onto it, so the island ends in a smooth curve; then simplified with <see cref="QuadricSimplifier"/>,
/// which flattens open grass to a few large triangles while hills keep theirs.
/// <para>
/// Chunks must meet without cracks. Their border vertices are grid points worked out from the whole level's grid lines (see
/// <see cref="GroundLayout.X"/>) and are locked during simplification, so two neighbours at the same level of detail share every
/// border vertex exactly. Between levels the borders differ (a coarse border skips every other fine vertex), so each chunk has a skirt:
/// a strip hanging <see cref="SkirtDepth"/> down from each of its borders, facing out, that hides the sliver of sky a crack would show.
/// The island's own edge is open, for the rocky underside to meet.
/// </para>
/// </summary>
public static class GroundMesher
{
    /// <summary>How far the skirts hang below the chunk's borders, in metres.</summary>
    public const float SkirtDepth = 0.5f;

    /// <returns>The mesh, or null when the chunk lies wholly beyond the island's edge.</returns>
    public static GroundMesh? Mesh(LevelTerrain terrain, GroundLayout layout, int chunkI, int chunkJ, GroundDetail detail)
    {
        var cells = detail.Cells;
        var cellSize = layout.ChunkSize / cells;
        var n = cells + 1;
        int Index(int a, int b) => a + b * n;

        // The grid, its points beyond the edge moved onto it
        var grid = new Vector3[n * n];
        var flat = new Vector2[n * n];
        for (var b = 0; b < n; b++)
        for (var a = 0; a < n; a++)
        {
            var x = layout.X(chunkI * cells + a, cells);
            var z = layout.Z(chunkJ * cells + b, cells);
            flat[Index(a, b)] = new Vector2(x, z);
            var ground = terrain.OnIsland(x, z) ? new Vector2(x, z) : terrain.OntoEdge(x, z);
            grid[Index(a, b)] = new Vector3(ground.X, terrain.Height(ground.X, ground.Y), ground.Y);
        }

        // Two triangles per cell, split along the diagonal whose middle is nearer the ground's height there. A triangle stays when a
        // corner of it is on the island: then every open edge between kept and dropped triangles has both ends beyond the edge, moved
        // onto it, so the island ends on the curve rather than in a cell-sized zigzag
        var onIsland = flat.Select(p => terrain.OnIsland(p.X, p.Y)).ToArray();
        var triangles = new List<int>();
        void AddTriangle(int p, int q, int r)
        {
            if (onIsland[p] || onIsland[q] || onIsland[r])
                triangles.AddRange([p, q, r]);
        }
        for (var b = 0; b < cells; b++)
        for (var a = 0; a < cells; a++)
        {
            int v00 = Index(a, b), v10 = Index(a + 1, b), v01 = Index(a, b + 1), v11 = Index(a + 1, b + 1);
            var centre = (flat[v00] + flat[v11]) / 2;
            var height = terrain.Height(centre.X, centre.Y);
            if (MathF.Abs((grid[v00].Y + grid[v11].Y) / 2 - height) <= MathF.Abs((grid[v01].Y + grid[v10].Y) / 2 - height))
            {
                AddTriangle(v00, v11, v10);
                AddTriangle(v00, v01, v11);
            }
            else
            {
                AddTriangle(v00, v01, v10);
                AddTriangle(v10, v01, v11);
            }
        }
        if (triangles.Count == 0)
            return null;

        // Only the grid points the kept triangles use; the vertices on an open edge (the chunk's border, the island's edge) are locked
        var used = new int[grid.Length];
        Array.Fill(used, -1);
        var positions = new List<Vector3>();
        var corners = triangles.Select(v => used[v] >= 0 ? used[v] : used[v] = Add(positions, grid[v])).ToArray();
        var locked = new bool[positions.Count];
        foreach (var (u, v) in OpenEdges(corners))
            locked[u] = locked[v] = true;

        var mesh = new TriangleMesh(positions.ToArray(), corners);
        var planes = mesh.Positions.Select(p => new[] { new SurfacePlane(p, terrain.Normal(p.X, p.Z)) }).ToArray();
        float Distance(Vector3 p) => p.Y - terrain.Height(p.X, p.Z);
        var simplified = QuadricSimplifier.Simplify(new ContouredMesh(mesh, planes), Distance, detail.MaxError, cellSize,
            MeshingOptions.DefaultCreaseAngleDegrees, locked: locked);

        // A merged vertex goes where its planes meet best, which on curved ground is a little above or below it: put it back on the ground
        simplified = simplified with { Positions = simplified.Positions.Select(p => p with { Y = terrain.Height(p.X, p.Z) }).ToArray() };

        var maxError = MeasureError(terrain, simplified);
        var (withSkirts, skirtTriangles) = AddSkirts(simplified, layout.Min(chunkI, chunkJ), layout.Max(chunkI, chunkJ));
        var normals = withSkirts.Positions.Select(p => terrain.Normal(p.X, p.Z)).ToArray();
        return new GroundMesh(withSkirts.Positions, normals, withSkirts.Triangles, skirtTriangles, cellSize, maxError);
    }

    private static int Add(List<Vector3> positions, Vector3 position)
    {
        positions.Add(position);
        return positions.Count - 1;
    }

    /// <summary>The edges with a face on one side only, in the winding of that face: the face is on their left seen from above.</summary>
    private static List<(int From, int To)> OpenEdges(int[] triangles)
    {
        var directed = new HashSet<(int, int)>();
        for (var t = 0; t < triangles.Length; t += 3)
            for (var c = 0; c < 3; c++)
                directed.Add((triangles[t + c], triangles[t + (c + 1) % 3]));
        var open = new List<(int, int)>();
        for (var t = 0; t < triangles.Length; t += 3)
            for (var c = 0; c < 3; c++)
                if (!directed.Contains((triangles[t + (c + 1) % 3], triangles[t + c])))
                    open.Add((triangles[t + c], triangles[t + (c + 1) % 3]));
        return open;
    }

    /// <summary>
    /// How far the faces are off the height function, at their middles and the middles of their edges, where a flat face over curved
    /// ground is furthest from it.
    /// </summary>
    private static float MeasureError(LevelTerrain terrain, TriangleMesh mesh)
    {
        var max = 0f;
        for (var t = 0; t < mesh.Triangles.Length; t += 3)
        {
            var (a, b, c) = (mesh.Positions[mesh.Triangles[t]], mesh.Positions[mesh.Triangles[t + 1]], mesh.Positions[mesh.Triangles[t + 2]]);
            foreach (var p in (ReadOnlySpan<Vector3>)[(a + b + c) / 3, (a + b) / 2, (b + c) / 2, (c + a) / 2])
                max = MathF.Max(max, MathF.Abs(p.Y - terrain.Height(p.X, p.Z)));
        }
        return max;
    }

    /// <summary>
    /// A strip <see cref="SkirtDepth"/> deep below each open edge that lies on one of the chunk's four borders, facing out of the chunk,
    /// so a neighbour drawn at another level of detail shows the strip rather than the sky through a crack. The open edges on the island's
    /// edge get none.
    /// </summary>
    private static (TriangleMesh Mesh, int SkirtTriangles) AddSkirts(TriangleMesh mesh, Vector2 min, Vector2 max)
    {
        var positions = mesh.Positions.ToList();
        var triangles = mesh.Triangles.ToList();
        var below = new Dictionary<int, int>();
        int Below(int vertex)
        {
            if (!below.TryGetValue(vertex, out var index))
            {
                below[vertex] = index = positions.Count;
                positions.Add(mesh.Positions[vertex] - new Vector3(0, SkirtDepth, 0));
            }
            return index;
        }
        bool OnBorder(Vector3 p, Vector3 q) =>
            p.X == q.X && (p.X == min.X || p.X == max.X) || p.Z == q.Z && (p.Z == min.Y || p.Z == max.Y);

        var skirts = 0;
        foreach (var (from, to) in OpenEdges(mesh.Triangles))
        {
            if (!OnBorder(mesh.Positions[from], mesh.Positions[to]))
                continue;
            // The chunk is on the edge's left seen from above, so these face right, out of it
            triangles.AddRange([from, Below(to), to, from, Below(from), Below(to)]);
            skirts += 2;
        }
        return (new TriangleMesh(positions.ToArray(), triangles.ToArray()), skirts);
    }
}
