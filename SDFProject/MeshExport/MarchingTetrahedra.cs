using System.Numerics;

namespace SDFProject.MeshExport;

public static class MarchingTetrahedra
{
    private static readonly int[][] Tetrahedra =
    [
        [0, 1, 3, 7],
        [0, 1, 5, 7],
        [0, 2, 3, 7],
        [0, 2, 6, 7],
        [0, 4, 5, 7],
        [0, 4, 6, 7],
    ];

    private sealed class Grid(Vector3 origin, float cellSize, int nx, int ny, int nz)
    {
        public readonly Vector3 Origin = origin;
        public readonly float CellSize = cellSize;
        public readonly int Nx = nx, Ny = ny, Nz = nz;
        public readonly float[] Values = new float[(nx + 1) * (ny + 1) * (nz + 1)];

        public int PointCount => Values.Length;

        public int Index(int i, int j, int k) => i + (Nx + 1) * (j + (Ny + 1) * k);

        public Vector3 Position(int index)
        {
            var i = index % (Nx + 1);
            var rest = index / (Nx + 1);
            return Origin + CellSize * new Vector3(i, rest % (Ny + 1), rest / (Ny + 1));
        }
    }

    private sealed class MeshBuilder(Grid grid)
    {
        private readonly Dictionary<long, int> _edgeVertices = new();
        private readonly List<Vector3> _vertices = new();
        private readonly List<int> _indices = new();

        public int EdgeVertex(int a, int b)
        {
            if (a > b) (a, b) = (b, a);
            var key = (long)a * grid.PointCount + b;
            if (_edgeVertices.TryGetValue(key, out var existing)) return existing;

            var va = grid.Values[a];
            var vb = grid.Values[b];
            var t = Math.Clamp(va / (va - vb), 0f, 1f);
            var vertex = _vertices.Count;
            _vertices.Add(Vector3.Lerp(grid.Position(a), grid.Position(b), t));
            _edgeVertices[key] = vertex;
            return vertex;
        }

        public void AddTriangle(int a, int b, int c)
        {
            _indices.Add(a);
            _indices.Add(b);
            _indices.Add(c);
        }

        public SdfMesh Build() => _indices.Count == 0 ? SdfMesh.Empty : new SdfMesh(_vertices.ToArray(), _indices.ToArray());
    }

    public static SdfMesh Mesh(Func<Vector3, float> sdf, SdfBounds bounds, int resolution)
    {
        if (resolution < 1) throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Resolution must be at least 1");

        var cellSize = bounds.LongestSide / resolution;
        if (!(cellSize > 0f) || !float.IsFinite(cellSize)) return SdfMesh.Empty;

        int Cells(float size) => Math.Max(1, (int)MathF.Ceiling(size / cellSize - 1e-4f)) + 2;
        var grid = new Grid(bounds.Min - new Vector3(cellSize), cellSize, Cells(bounds.Size.X), Cells(bounds.Size.Y), Cells(bounds.Size.Z));

        Sample(grid, sdf);
        return Polygonize(grid);
    }

    private static void Sample(Grid grid, Func<Vector3, float> sdf)
    {
        void SampleSlice(int k)
        {
            for (var j = 0; j <= grid.Ny; j++)
            for (var i = 0; i <= grid.Nx; i++)
            {
                var index = grid.Index(i, j, k);
                var boundary = i == 0 || j == 0 || k == 0 || i == grid.Nx || j == grid.Ny || k == grid.Nz;
                var value = Sanitize(sdf(grid.Position(index)));
                grid.Values[index] = boundary ? MathF.Max(value, grid.CellSize) : value;
            }
        }

        if (OperatingSystem.IsBrowser())
            for (var k = 0; k <= grid.Nz; k++) SampleSlice(k);
        else
            Parallel.For(0, grid.Nz + 1, SampleSlice);
    }

    private static float Sanitize(float value) => float.IsNaN(value) ? 1e30f : Math.Clamp(value, -1e30f, 1e30f);

    private static SdfMesh Polygonize(Grid grid)
    {
        var builder = new MeshBuilder(grid);
        var corners = new int[8];
        var tet = new int[4];

        for (var k = 0; k < grid.Nz; k++)
        for (var j = 0; j < grid.Ny; j++)
        for (var i = 0; i < grid.Nx; i++)
        {
            var insideCount = 0;
            for (var c = 0; c < 8; c++)
            {
                corners[c] = grid.Index(i + (c & 1), j + ((c >> 1) & 1), k + ((c >> 2) & 1));
                if (grid.Values[corners[c]] < 0f) insideCount++;
            }

            if (insideCount is 0 or 8) continue;

            foreach (var tetrahedron in Tetrahedra)
            {
                for (var v = 0; v < 4; v++) tet[v] = corners[tetrahedron[v]];
                PolygonizeTetrahedron(grid, builder, tet);
            }
        }

        return builder.Build();
    }

    private static void PolygonizeTetrahedron(Grid grid, MeshBuilder builder, int[] tet)
    {
        Span<int> inside = stackalloc int[4];
        Span<int> outside = stackalloc int[4];
        var insideCount = 0;
        var outsideCount = 0;
        foreach (var point in tet)
        {
            if (grid.Values[point] < 0f) inside[insideCount++] = point;
            else outside[outsideCount++] = point;
        }

        Vector3 Mid(int a, int b) => (grid.Position(a) + grid.Position(b)) * 0.5f;
        var outward = Centroid(grid, outside[..outsideCount]) - Centroid(grid, inside[..insideCount]);

        switch (insideCount)
        {
            case 1:
                EmitTriangle(builder, outward,
                    (builder.EdgeVertex(inside[0], outside[0]), Mid(inside[0], outside[0])),
                    (builder.EdgeVertex(inside[0], outside[1]), Mid(inside[0], outside[1])),
                    (builder.EdgeVertex(inside[0], outside[2]), Mid(inside[0], outside[2])));
                break;
            case 3:
                EmitTriangle(builder, outward,
                    (builder.EdgeVertex(outside[0], inside[0]), Mid(outside[0], inside[0])),
                    (builder.EdgeVertex(outside[0], inside[1]), Mid(outside[0], inside[1])),
                    (builder.EdgeVertex(outside[0], inside[2]), Mid(outside[0], inside[2])));
                break;
            case 2:
                EmitQuad(builder, outward,
                    (builder.EdgeVertex(inside[0], outside[0]), Mid(inside[0], outside[0])),
                    (builder.EdgeVertex(inside[0], outside[1]), Mid(inside[0], outside[1])),
                    (builder.EdgeVertex(inside[1], outside[1]), Mid(inside[1], outside[1])),
                    (builder.EdgeVertex(inside[1], outside[0]), Mid(inside[1], outside[0])));
                break;
        }
    }

    private static Vector3 Centroid(Grid grid, ReadOnlySpan<int> points)
    {
        var sum = Vector3.Zero;
        foreach (var point in points) sum += grid.Position(point);
        return sum / points.Length;
    }

    private static void EmitTriangle(MeshBuilder builder, Vector3 outward, (int Index, Vector3 Mid) a, (int Index, Vector3 Mid) b, (int Index, Vector3 Mid) c)
    {
        var normal = Vector3.Cross(b.Mid - a.Mid, c.Mid - a.Mid);
        if (Vector3.Dot(normal, outward) >= 0f) builder.AddTriangle(a.Index, b.Index, c.Index);
        else builder.AddTriangle(a.Index, c.Index, b.Index);
    }

    private static void EmitQuad(MeshBuilder builder, Vector3 outward, (int Index, Vector3 Mid) a, (int Index, Vector3 Mid) b, (int Index, Vector3 Mid) c, (int Index, Vector3 Mid) d)
    {
        var normal = Vector3.Cross(c.Mid - a.Mid, d.Mid - b.Mid);
        if (Vector3.Dot(normal, outward) >= 0f)
        {
            builder.AddTriangle(a.Index, b.Index, c.Index);
            builder.AddTriangle(a.Index, c.Index, d.Index);
        }
        else
        {
            builder.AddTriangle(a.Index, c.Index, b.Index);
            builder.AddTriangle(a.Index, d.Index, c.Index);
        }
    }
}
