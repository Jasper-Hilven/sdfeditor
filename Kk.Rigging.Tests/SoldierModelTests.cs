using System.Numerics;
using SDFPipeline.Meshing;

namespace Kk.Rigging.Tests;

/// <summary>The headless soldier of ticket 43 (content/models/characters/soldier/soldier.sdf) and the file detection of RigFiles.</summary>
public class SoldierModelTests
{
    private static string SoldierPath => Path.Combine(TestSupport.Repository(), "content", "models", "characters", "soldier", "soldier.sdf");

    [Fact]
    public void The_soldier_is_a_format_2_file_and_a_boxy_model_is_not()
    {
        Assert.True(RigFiles.IsRigged(SoldierPath));
        Assert.False(RigFiles.IsRigged(Path.Combine(TestSupport.Repository(), "content", "models", "characters", "amanita-scout.sdf")));
        Assert.True(RigFiles.IsFormat2Line("format 2"));
        Assert.False(RigFiles.IsFormat2Line("format 1"));
        Assert.False(RigFiles.IsFormat2Line("format 20"));
    }

    [Fact]
    public void The_soldier_loads_without_issues_on_the_synty_skeleton_with_every_bone()
    {
        var model = RigFiles.Load(SoldierPath);
        Assert.False(model.HasErrors);
        Assert.Equal(50, model.Skeleton.Bones.Count); // the root and the 49 bones of the skeleton
        Assert.True(model.HitColliders);
    }

    [Theory]
    [InlineData("grip", "Hand_R")]
    [InlineData("support", "Hand_L")]
    [InlineData("back", "Spine_03")]
    [InlineData("head_top", "Head")]
    [InlineData("chest", "Spine_03")]
    public void The_soldier_has_its_sockets_on_the_right_bones(string socket, string bone)
    {
        var model = RigFiles.Load(SoldierPath);
        var found = Assert.Single(model.Skeleton.Sockets, s => s.Name == socket);
        Assert.Equal(bone, model.Skeleton.Bones[found.Bone].Name);
    }

    [Fact]
    public void The_head_socket_is_over_the_neck_and_the_grip_in_the_right_hand()
    {
        var model = RigFiles.Load(SoldierPath);
        var head = model.Skeleton.Sockets.Single(s => s.Name == "head_top");
        var grip = model.Skeleton.Sockets.Single(s => s.Name == "grip");
        Assert.InRange(head.Position.Y, 1.7f, 1.9f);
        Assert.True(grip.Position.X < -0.5f); // the right hand is on -X in format 2 (+X is the left)
    }

    [Fact]
    public void The_meshed_soldier_has_no_head_a_head_capsule_and_stays_in_budget()
    {
        var model = RigFiles.Load(SoldierPath);
        var materials = SDFPipeline.Materials.MaterialFiles.LoadLibrary(Path.Combine(TestSupport.Repository(), "materials"));
        var mesh = RigMesher.Mesh(model, TestSupport.Parts(), new MeshingOptions { Materials = materials });
        var positions = mesh.Body.Positions;
        Assert.InRange(mesh.Body.TriangleCount, 1000, 6500);
        // No skull, hair or helmet: nothing of the mesh is above the neck's top and the collar
        Assert.True(positions.Max(p => p.Y) < 1.62f, $"top {positions.Max(p => p.Y)}");
        Assert.InRange(positions.Min(p => p.Y), -0.02f, 0.02f); // soles on the ground
        var head = Assert.Single(mesh.Hits, h => model.Skeleton.Bones[h.Bone].Name == "Head");
        Assert.Equal(0.13f, head.Radius, 3);
        Assert.Equal(18, mesh.Hits.Count);
    }

    [Fact]
    public void The_skin_materials_exist_for_every_role_in_red_and_blue_and_olive()
    {
        var folder = Path.Combine(TestSupport.Repository(), "materials", "characters", "soldier");
        foreach (var role in new[] { "fatigues", "vest", "boots", "gloves", "pack" })
        {
            Assert.True(File.Exists(Path.Combine(folder, "olive", $"soldier_{role}.sdfmat")), role);
            foreach (var skin in new[] { "red", "blue" })
                Assert.True(File.Exists(Path.Combine(folder, skin, $"soldier_{role}_{skin}.sdfmat")), role + skin);
        }
    }
}
