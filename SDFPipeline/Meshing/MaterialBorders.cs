using System.Numerics;

namespace SDFPipeline.Meshing;

/// <summary>
/// Lines the vertices along material borders up with the SDF's own border. Contouring places vertices by the shape alone, so where a
/// material changes on a smooth surface (the rings of an eye painted on its sphere) the vertices on either side sit anywhere up to a
/// cell from it, and the border, which runs between them, comes out as a staircase. Here each vertex next to another region moves along
/// the surface to a fixed distance inside its own region, measured from the border found between it and its neighbours. The two rows
/// of vertices on either side then run parallel to the true border, and simplification (which keeps borders) has straight rows to keep.
/// </summary>
public static class MaterialBorders
{
    /// <summary>How far inside its own region a border vertex goes, in cells: the colour changes across twice this.</summary>
    private const float InsetInCells = 0.2f;

    /// <summary>A vertex moves no further than this, in cells, so it stays near the cell contouring put it in.</summary>
    private const float MaxMoveInCells = 0.75f;

    /// <summary>A move may turn no face by more than this (cosine), so the mesh cannot fold.</summary>
    private const float MinNormalAgreement = 0.5f;

    private const int BisectionSteps = 12;

    /// <param name="regions">The region of each vertex, as <paramref name="regionAt"/> gives it at the vertex.</param>
    /// <param name="regionAt">The region at any point near the surface, such as its dominant material.</param>
    public static ContouredMesh Straightened(ContouredMesh contoured, int[] regions, Func<Vector3, int> regionAt, Func<Vector3, float> sdf,
        float cellSize)
    {
        var mesh = contoured.Mesh;
        var positions = mesh.Positions;
        var neighbours = Neighbours(mesh);
        var moved = (Vector3[])positions.Clone();
        var step = cellSize * 1e-3f;

        Parallel.For(0, positions.Length, u =>
        {
            var others = neighbours[u].Where(v => regions[v] != regions[u]).ToArray();
            if (others.Length == 0)
                return;
            var normal = Gradient(sdf, positions[u], step);
            if (normal.LengthSquared() <= 0)
                return;
            normal = Vector3.Normalize(normal);

            // The border between this vertex and each neighbour of another region, and the direction across it along the surface
            var across = Vector3.Zero;
            var border = Vector3.Zero;
            foreach (var v in others)
            {
                var offset = positions[v] - positions[u];
                var along = offset - normal * Vector3.Dot(offset, normal);
                if (along.LengthSquared() > 0)
                    across += Vector3.Normalize(along);
                border += Crossing(positions[u], positions[v], regions[u], regionAt);
            }
            if (across.LengthSquared() <= 0)
                return;
            across = Vector3.Normalize(across);
            border /= others.Length;

            var shift = Vector3.Dot(border - positions[u], across) - InsetInCells * cellSize;
            shift = Math.Clamp(shift, -MaxMoveInCells * cellSize, MaxMoveInCells * cellSize);
            var target = OntoSurface(positions[u] + across * shift, sdf, step);
            if (regionAt(target) == regions[u])
                moved[u] = target;
        });

        // A move that would turn a face too far is undone; undoing one can only make its neighbours' faces better
        for (var pass = 0; pass < 3; pass++)
        {
            var undone = 0;
            for (var t = 0; t < mesh.Triangles.Length; t += 3)
            {
                var (a, b, c) = (mesh.Triangles[t], mesh.Triangles[t + 1], mesh.Triangles[t + 2]);
                if (moved[a] == positions[a] && moved[b] == positions[b] && moved[c] == positions[c])
                    continue;
                var before = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                var after = Vector3.Cross(moved[b] - moved[a], moved[c] - moved[a]);
                if (Vector3.Dot(before, after) >= MinNormalAgreement * before.Length() * after.Length() && after.LengthSquared() > 0)
                    continue;
                foreach (var corner in (ReadOnlySpan<int>)[a, b, c])
                    if (moved[corner] != positions[corner])
                    {
                        moved[corner] = positions[corner];
                        undone++;
                    }
            }
            if (undone == 0)
                break;
        }

        return contoured with { Mesh = mesh with { Positions = moved } };
    }

    /// <summary>Where on the segment from <paramref name="from"/> the region first stops being <paramref name="region"/>.</summary>
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
        return Vector3.Lerp(from, to, (inside + outside) * 0.5f);
    }

    private static Vector3 OntoSurface(Vector3 p, Func<Vector3, float> sdf, float step)
    {
        for (var i = 0; i < 3; i++)
        {
            var gradient = Gradient(sdf, p, step);
            if (gradient.LengthSquared() <= 0)
                break;
            p -= Vector3.Normalize(gradient) * sdf(p);
        }
        return p;
    }

    private static Vector3 Gradient(Func<Vector3, float> sdf, Vector3 p, float h) => new(
        sdf(p + new Vector3(h, 0, 0)) - sdf(p - new Vector3(h, 0, 0)),
        sdf(p + new Vector3(0, h, 0)) - sdf(p - new Vector3(0, h, 0)),
        sdf(p + new Vector3(0, 0, h)) - sdf(p - new Vector3(0, 0, h)));

    private static HashSet<int>[] Neighbours(TriangleMesh mesh)
    {
        var neighbours = new HashSet<int>[mesh.Positions.Length];
        for (var v = 0; v < neighbours.Length; v++) neighbours[v] = [];
        for (var t = 0; t < mesh.Triangles.Length; t += 3)
            for (var c = 0; c < 3; c++)
            {
                var (a, b) = (mesh.Triangles[t + c], mesh.Triangles[t + (c + 1) % 3]);
                if (a == b) continue;
                neighbours[a].Add(b);
                neighbours[b].Add(a);
            }
        return neighbours;
    }
}
