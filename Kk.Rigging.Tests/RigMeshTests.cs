using System.Numerics;
using SDFPipeline.Meshing;

namespace Kk.Rigging.Tests;

public class RigMeshTests
{
    private static readonly MeshingOptions Coarse = new() { Resolution = 64 };

    private static readonly Lazy<(RigModel Model, RigMesh Mesh)> Worm = new(() => Make("worm.sdf"));
    private static readonly Lazy<(RigModel Model, RigMesh Mesh)> Machine = new(() => Make("machine.sdf"));
    private static readonly Lazy<(RigModel Model, RigMesh Mesh)> Crawler = new(() => Make("crawler.sdf"));

    private static (RigModel, RigMesh) Make(string file)
    {
        var model = TestSupport.Load(file);
        return (model, RigMesher.Mesh(model, TestSupport.Parts(), Coarse));
    }

    [Fact]
    public void Every_vertex_has_normalised_weights_for_at_most_four_bones()
    {
        var (model, mesh) = Worm.Value;
        var body = mesh.Body;
        Assert.True(body.Positions.Length > 100);
        Assert.Equal(4 * body.Positions.Length, mesh.Weights.Weights.Length);
        for (var v = 0; v < body.Positions.Length; v++)
        {
            var sum = 0f;
            for (var k = 0; k < 4; k++)
            {
                var w = mesh.Weights.Weights[4 * v + k];
                Assert.InRange(w, 0f, 1f);
                if (k > 0)
                    Assert.True(w <= mesh.Weights.Weights[4 * v + k - 1] + 1e-6f, "heaviest first");
                sum += w;
            }
            Assert.Equal(1f, sum, 4);
        }
    }

    [Fact]
    public void The_worms_ends_belong_to_their_bones_and_the_joints_blend()
    {
        var (model, mesh) = Worm.Value;
        var body = mesh.Body;
        var index = (string name) => model.Skeleton.IndexOf(name);
        int blended = 0, endsA = 0, endsC = 0;
        for (var v = 0; v < body.Positions.Length; v++)
        {
            var z = body.Positions[v].Z;
            var (first, firstWeight) = (mesh.Weights.Joints[4 * v], mesh.Weights.Weights[4 * v]);
            if (z < -0.73f)
            {
                Assert.Equal(index("a"), first);
                Assert.True(firstWeight > 0.99f);
                endsA++;
            }
            if (z > 0.73f)
            {
                Assert.Equal(index("c"), first);
                Assert.True(firstWeight > 0.99f);
                endsC++;
            }
            if (firstWeight < 0.9f)
                blended++;
        }
        Assert.True(endsA >= 1 && endsC >= 1);
        Assert.True(blended > 30, $"only {blended} vertices are blended");
    }

    [Fact]
    public void Rigid_bones_carry_their_geometry_with_weight_one()
    {
        var (model, mesh) = Machine.Value;
        var body = mesh.Body;
        var drill = model.Skeleton.IndexOf("drill");
        var drillVertices = 0;
        for (var v = 0; v < body.Positions.Length; v++)
        {
            Assert.True(mesh.Weights.Weights[4 * v] > 0.999f, $"vertex {v} at {body.Positions[v]} is blended: {mesh.Weights.Weights[4 * v]}");
            if (body.Positions[v].Z > 1.0f)
            {
                Assert.Equal(drill, mesh.Weights.Joints[4 * v]);
                drillVertices++;
            }
        }
        Assert.True(drillVertices >= 3);
    }

    [Fact]
    public void The_pipelines_axes_are_the_specs_plus_Z_forward_and_plus_X_left()
    {
        // The mesh is in the program's own coordinates and the GLB is written without conversion: a bone at +X and +Z has vertices there
        var (model, mesh) = Crawler.Value;
        var leftLeg = model.Skeleton.IndexOf("leg_front_L");
        var xs = Enumerable.Range(0, mesh.Body.Positions.Length).Where(v => mesh.Weights.Joints[4 * v] == leftLeg).Select(v => mesh.Body.Positions[v]).ToList();
        Assert.True(xs.Count > 20);
        Assert.True(xs.All(p => p.X > 0.2f), "the left leg is on +X");
        Assert.True(mesh.Body.Positions.Max(p => p.Z) > 0.8f && mesh.Body.Positions.Min(p => p.Z) < -0.4f);   // the head (forward) reaches further than the tail

        var glb = RigMesher.Glb(mesh, "crawler");
        var json = System.Text.Json.JsonDocument.Parse(glb.AsSpan(20, (int)BitConverter.ToUInt32(glb, 12)).ToArray());
        var position = json.RootElement.GetProperty("accessors").EnumerateArray().First(a => a.TryGetProperty("max", out _));
        Assert.Equal(mesh.Body.Positions.Max(p => p.Z), position.GetProperty("max")[2].GetSingle(), 4);
    }

    [Fact]
    public void Hit_capsules_follow_the_bones_with_fitted_radii()
    {
        var (model, mesh) = Crawler.Value;
        Assert.NotEmpty(mesh.Hits);
        var names = mesh.Hits.Select(h => model.Skeleton.Bones[h.Bone].Name).ToList();
        Assert.Contains("body", names);
        Assert.Contains("head", names);
        Assert.Contains("leg_front_L", names);
        Assert.DoesNotContain("root", names);   // no geometry of its own
        var body = mesh.Hits.First(h => model.Skeleton.Bones[h.Bone].Name == "body");
        Assert.InRange(body.Radius, 0.2f, 0.7f);
        var leg = mesh.Hits.First(h => model.Skeleton.Bones[h.Bone].Name == "leg_front_L");
        Assert.InRange(leg.Radius, 0.03f, 0.3f);
        Assert.Equal(Vector3.Distance(model.Skeleton.Find("leg_front_L")!.Head, model.Skeleton.Find("leg_front_L")!.Tail) + 2 * leg.Radius, leg.Height, 4);
    }

    [Fact]
    public void An_explicit_hit_radius_and_hit_none_are_obeyed()
    {
        var model = RigModel.Read("""
            format 2
            collider bones
            Union
                Bone name=a head=(0, 0, 0) tail=(0, 1, 0) hit=0.3
                    Capsule a=(0, 0, 0) b=(0, 1, 0) radius=0.1
                Bone name=b head=(1, 0, 0) tail=(1, 1, 0) hit=none
                    Capsule a=(1, 0, 0) b=(1, 1, 0) radius=0.1
            """);
        var mesh = RigMesher.Mesh(model, null, Coarse);
        var hit = Assert.Single(mesh.Hits);
        Assert.Equal(model.Skeleton.IndexOf("a"), hit.Bone);
        Assert.Equal(0.3f, hit.Radius);
    }

    [Fact]
    public void The_skinned_glb_has_the_skeleton_and_the_capsules()
    {
        var (model, mesh) = Crawler.Value;
        var glb = RigMesher.Glb(mesh, "crawler");
        var text = System.Text.Encoding.UTF8.GetString(glb);
        foreach (var name in new[] { "\"root\"", "\"leg_front_R\"", "\"socket_mouth\"", "\"hit_body\"", "JOINTS_0", "WEIGHTS_0", "_MATERIAL_IDS" })
            Assert.Contains(name, text);
    }

    [Fact]
    public void The_skin_stays_close_to_the_exact_posed_surface()
    {
        var (model, mesh) = Worm.Value;
        var gaps = SkinGap.Measure(model, TestSupport.Parts(), mesh, SkinGap.StandardPoses(model.Skeleton));
        Assert.NotEmpty(gaps);
        foreach (var gap in gaps)
            Assert.True(gap.MaxGap < 0.12f, $"{gap.Bone}: {gap.MaxGap} in {gap.Pose}");
    }
}
