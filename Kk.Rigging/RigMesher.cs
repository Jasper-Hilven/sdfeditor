using System.Numerics;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;

namespace Kk.Rigging;

/// <summary>A rigged model meshed once, in the rest pose: one mesh, the skin weights per vertex, and the hit capsules.</summary>
public sealed record RigMesh(MeshedModel Mesh, Skeleton Skeleton, SkinWeights Weights, IReadOnlyList<HitCapsule> Hits, IReadOnlyList<Func<Vector3, float>?> BoneDistances)
{
    public PartMesh Body => Mesh.Meshes[0];
}

/// <summary>
/// Meshes a model with bones: the rest-pose format 1 program goes through the pipeline's meshing and reduction as any model does, and
/// the weights are computed afterwards, per final vertex, so whatever the reducer did to the mesh the weights follow it.
/// </summary>
public static class RigMesher
{
    /// <param name="keepJointDensity">
    /// Keep the contour's full density in the blend zone of every non-rigid joint (a sphere of the bone's falloff round its head,
    /// <see cref="MeshingOptions.KeepDensity"/>), since heavy reduction across a joint makes it bend badly. On by default.
    /// </param>
    public static RigMesh Mesh(RigModel model, PartSource? parts, MeshingOptions? options = null, bool keepJointDensity = true)
    {
        if (model.HasErrors)
            throw new InvalidOperationException("The model has errors:\n" + string.Join("\n", model.Issues.Where(i => i.IsError)));
        var restText = model.RestProgram();
        var program = SdfProgramFile.Read(restText, parts);
        options = (options ?? MeshingOptions.Default) with { LodCount = 1 };
        if (keepJointDensity)
            options = options with
            {
                KeepDensity = options.KeepDensity.Concat(model.Skeleton.Bones.Where(b => b.Parent >= 0 && b.Falloff > 0)
                    .Select(b => new DensityRegion(b.Head, b.Falloff))).ToArray(),
            };
        var meshed = ModelMesher.Mesh(program, options);
        if (meshed.Meshes.Count == 0)
            throw new InvalidOperationException("The model has no surface to mesh");
        var body = Merge(meshed, "body");
        var single = meshed with { Meshes = [body], Settings = ModelSettings.Read(restText) };

        var distances = new Func<Vector3, float>?[model.Skeleton.Bones.Count];
        for (var b = 0; b < distances.Length; b++)
            if (model.BoneProgram(b) is { } text)
                distances[b] = new SdfCalculatorCpu().Prepare(SdfProgramFile.Read(text, parts)).Distance;

        var weights = Skinning.Weights(model.Skeleton, distances, body.Positions);
        var hits = model.HitColliders ? HitFit.Capsules(model.Skeleton, body.Positions, weights) : [];
        return new RigMesh(single, model.Skeleton, weights, hits, distances);
    }

    public static byte[] Glb(RigMesh mesh, string name, IReadOnlyList<BakedClip>? clips = null) =>
        SkinnedGlb.Write(mesh.Mesh, name, mesh.Skeleton, mesh.Weights, mesh.Hits, clips ?? []);

    /// <summary>Puts the body and the parts' meshes (with each use of a part placed) into one mesh in the model's space.</summary>
    internal static PartMesh Merge(MeshedModel meshed, string name)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var materials = new List<VertexMaterial>();
        var thickness = new List<float>();
        var occlusion = new List<float>();
        var triangles = new List<int>();
        foreach (var mesh in meshed.Meshes)
        {
            var placements = mesh.Instances.Count > 0 ? mesh.Instances.Select(m => (Matrix4x4?)m).ToArray() : [null];
            foreach (var placement in placements)
            {
                var offset = positions.Count;
                var flip = placement is { } m && m.GetDeterminant() < 0;
                for (var v = 0; v < mesh.Positions.Length; v++)
                {
                    positions.Add(placement is { } place ? Vector3.Transform(mesh.Positions[v], place) : mesh.Positions[v]);
                    normals.Add(placement is { } placeN ? Vector3.Normalize(Vector3.TransformNormal(mesh.Normals[v], placeN)) : mesh.Normals[v]);
                }
                materials.AddRange(mesh.Materials);
                thickness.AddRange(mesh.Thickness);
                occlusion.AddRange(mesh.Occlusion);
                for (var t = 0; t < mesh.Triangles.Length; t += 3)
                {
                    triangles.Add(offset + mesh.Triangles[t]);
                    triangles.Add(offset + mesh.Triangles[t + (flip ? 2 : 1)]);
                    triangles.Add(offset + mesh.Triangles[t + (flip ? 1 : 2)]);
                }
            }
        }
        var contoured = meshed.Meshes.Sum(m => m.Statistics.ContouredTriangles);
        var statistics = new MeshStatistics(contoured, triangles.Count / 3, positions.Count, TimeSpan.FromTicks(meshed.Meshes.Sum(m => m.Statistics.Time.Ticks)));
        return new PartMesh(name, positions.ToArray(), normals.ToArray(), materials.ToArray(), thickness.ToArray(), occlusion.ToArray(), triangles.ToArray(),
            meshed.Meshes.Max(m => m.MaxError), statistics);
    }
}
