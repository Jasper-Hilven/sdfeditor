using System.Numerics;

namespace SDFPipeline.Meshing;

/// <summary>
/// A mesh with a normal per vertex, ready to shade: vertices on creases are split, one copy per side, so the crease shades hard while
/// smooth areas shade smooth. <see cref="Source"/> is the vertex of the unsplit mesh each vertex was copied from.
/// </summary>
public sealed record ShadedMesh(Vector3[] Positions, Vector3[] Normals, int[] Triangles, int[] Source)
{
    public int TriangleCount => Triangles.Length / 3;
}

/// <summary>
/// Normals with hard creases: neighbouring faces at more than the crease angle get separate normals. Whether an edge is a crease is
/// asked of the SDF, not just the mesh, since a coarse mesh over a tight round-off also meets at a steep angle: the SDF's normals just
/// either side of the edge differ by the full angle only across a real crease. Smooth vertices take the SDF's own normal, so shading
/// follows the true surface rather than the facets.
/// </summary>
public static class CreaseNormals
{
    /// <summary>How far either side of an edge the SDF's normals are compared, in cells: far enough to step off a crease, near enough to stay on a round-off.</summary>
    private const float SideStepInCells = 0.05f;

    /// <summary>A smooth vertex keeps the SDF's normal only if it agrees with its faces at least this well (cosine); otherwise, as on a thin sliver of material, the faces know better.</summary>
    private const float MinSdfAgreement = 0.5f;

    public static ShadedMesh Split(TriangleMesh mesh, Func<Vector3, float> sdf, float cellSize, float creaseAngleDegrees)
    {
        var creaseCosine = MathF.Cos(creaseAngleDegrees * MathF.PI / 180);
        var triangleCount = mesh.TriangleCount;
        var faceNormals = new Vector3[triangleCount];
        for (var t = 0; t < triangleCount; t++)
        {
            var (a, b, c) = Corners(mesh, t);
            faceNormals[t] = Vector3.Normalize(Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]));
        }

        var edgeFaces = new Dictionary<(int, int), List<int>>();
        for (var t = 0; t < triangleCount; t++)
        {
            for (var c = 0; c < 3; c++)
            {
                var (a, b) = (mesh.Triangles[3 * t + c], mesh.Triangles[3 * t + (c + 1) % 3]);
                var key = a < b ? (a, b) : (b, a);
                if (!edgeFaces.TryGetValue(key, out var faces))
                    edgeFaces[key] = faces = new List<int>(2);
                faces.Add(t);
            }
        }

        // An edge is smooth when its two faces meet gently, or when the SDF says the surface is smooth there anyway
        var steep = edgeFaces.Where(e => e.Value.Count == 2 && Vector3.Dot(faceNormals[e.Value[0]], faceNormals[e.Value[1]]) < creaseCosine).ToArray();
        var smoothSteep = new bool[steep.Length];
        Parallel.For(0, steep.Length, i => smoothSteep[i] = !IsSdfCrease(mesh, steep[i].Key, steep[i].Value, sdf, cellSize, creaseCosine));
        var smooth = new HashSet<(int, int)>(edgeFaces
            .Where(e => e.Value.Count == 2 && Vector3.Dot(faceNormals[e.Value[0]], faceNormals[e.Value[1]]) >= creaseCosine)
            .Select(e => e.Key));
        for (var i = 0; i < steep.Length; i++)
            if (smoothSteep[i]) smooth.Add(steep[i].Key);

        var trianglesOf = new List<int>[mesh.Positions.Length];
        for (var v = 0; v < trianglesOf.Length; v++) trianglesOf[v] = [];
        for (var t = 0; t < triangleCount; t++)
            for (var c = 0; c < 3; c++)
                trianglesOf[mesh.Triangles[3 * t + c]].Add(t);

        // Around each vertex, faces joined by smooth edges form one group, which becomes one vertex of the result
        var positions = new List<Vector3>();
        var source = new List<int>();
        var groupOf = new Dictionary<(int vertex, int triangle), int>();
        var groupFaces = new List<List<int>>();
        for (var v = 0; v < mesh.Positions.Length; v++)
        {
            var faces = trianglesOf[v];
            var parent = Enumerable.Range(0, faces.Count).ToArray();
            int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
            for (var i = 0; i < faces.Count; i++)
            for (var j = i + 1; j < faces.Count; j++)
                if (SharedEdge(mesh, faces[i], faces[j], v) is { } edge && smooth.Contains(edge))
                    parent[Find(i)] = Find(j);

            var outputOfRoot = new Dictionary<int, int>();
            for (var i = 0; i < faces.Count; i++)
            {
                var root = Find(i);
                if (!outputOfRoot.TryGetValue(root, out var output))
                {
                    output = positions.Count;
                    outputOfRoot[root] = output;
                    positions.Add(mesh.Positions[v]);
                    source.Add(v);
                    groupFaces.Add([]);
                }
                groupOf[(v, faces[i])] = output;
                groupFaces[output].Add(faces[i]);
            }
        }

        var triangles = new int[mesh.Triangles.Length];
        for (var t = 0; t < triangleCount; t++)
            for (var c = 0; c < 3; c++)
                triangles[3 * t + c] = groupOf[(mesh.Triangles[3 * t + c], t)];

        var normals = new Vector3[positions.Count];
        Parallel.For(0, positions.Count, output =>
        {
            var vertex = source[output];
            var faceNormal = AngleWeightedNormal(mesh, faceNormals, groupFaces[output], vertex);
            var whole = groupFaces[output].Count == trianglesOf[vertex].Count;
            if (whole && DualContouring.Normal(sdf, positions[output], cellSize) is var gradient && gradient.LengthSquared() > 0
                && Vector3.Normalize(gradient) is var sdfNormal && Vector3.Dot(sdfNormal, faceNormal) >= MinSdfAgreement)
                normals[output] = sdfNormal;
            else
                normals[output] = faceNormal;
        });

        return new ShadedMesh(positions.ToArray(), normals, triangles, source.ToArray());
    }

    private static (int a, int b, int c) Corners(TriangleMesh mesh, int t) => (mesh.Triangles[3 * t], mesh.Triangles[3 * t + 1], mesh.Triangles[3 * t + 2]);

    /// <summary>Whether the SDF's normals just either side of the edge, inside each face, differ by more than the crease angle.</summary>
    private static bool IsSdfCrease(TriangleMesh mesh, (int a, int b) edge, List<int> faces, Func<Vector3, float> sdf, float cellSize, float creaseCosine)
    {
        var middle = (mesh.Positions[edge.a] + mesh.Positions[edge.b]) * 0.5f;
        Span<Vector3> sides = stackalloc Vector3[2];
        for (var f = 0; f < 2; f++)
        {
            var (a, b, c) = Corners(mesh, faces[f]);
            var centroid = (mesh.Positions[a] + mesh.Positions[b] + mesh.Positions[c]) / 3;
            var towards = centroid - middle;
            var length = towards.Length();
            if (length <= 0) return true;
            var gradient = DualContouring.Gradient(sdf, middle + towards / length * MathF.Min(length, cellSize * SideStepInCells), cellSize * 1e-3f);
            if (gradient.LengthSquared() <= 0 || !float.IsFinite(gradient.X + gradient.Y + gradient.Z)) return true;
            sides[f] = Vector3.Normalize(gradient);
        }
        return Vector3.Dot(sides[0], sides[1]) < creaseCosine;
    }

    private static (int, int)? SharedEdge(TriangleMesh mesh, int t1, int t2, int vertex)
    {
        var (a, b, c) = Corners(mesh, t1);
        foreach (var other in (ReadOnlySpan<int>)[a, b, c])
        {
            if (other == vertex) continue;
            var (d, e, f) = Corners(mesh, t2);
            if (other == d || other == e || other == f)
                return vertex < other ? (vertex, other) : (other, vertex);
        }
        return null;
    }

    /// <summary>The normals of the faces, each weighted by its angle at the vertex, so a fan of thin faces does not outvote one wide one.</summary>
    private static Vector3 AngleWeightedNormal(TriangleMesh mesh, Vector3[] faceNormals, List<int> faces, int vertex)
    {
        var sum = Vector3.Zero;
        foreach (var t in faces)
        {
            var (a, b, c) = Corners(mesh, t);
            var (p, q, r) = vertex == a ? (a, b, c) : vertex == b ? (b, c, a) : (c, a, b);
            var e1 = Vector3.Normalize(mesh.Positions[q] - mesh.Positions[p]);
            var e2 = Vector3.Normalize(mesh.Positions[r] - mesh.Positions[p]);
            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(e1, e2), -1f, 1f));
            if (float.IsFinite(angle) && float.IsFinite(faceNormals[t].X))
                sum += faceNormals[t] * angle;
        }
        return sum.LengthSquared() > 0 ? Vector3.Normalize(sum) : Vector3.UnitY;
    }
}
