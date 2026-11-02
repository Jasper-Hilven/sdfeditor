using System.Numerics;
using SDFPipeline.ProgramFiles;

namespace Kk.Rigging.Tests;

public class RigModelTests
{
    [Fact]
    public void The_worm_has_a_root_and_three_bones_in_hierarchy_order()
    {
        var model = TestSupport.Load("worm.sdf");
        Assert.False(model.HasErrors, string.Join("\n", model.Issues));
        var bones = model.Skeleton.Bones;
        Assert.Equal(["root", "a", "b", "c"], bones.Select(b => b.Name));
        Assert.Equal([-1, 0, 1, 2], bones.Select(b => b.Parent));
        Assert.Equal(new Vector3(0, 0.3f, -0.2f), bones[2].Head);
        Assert.True(model.HitColliders);
    }

    [Fact]
    public void Falloff_defaults_to_the_smoothness_of_the_smooth_union_that_joins_the_bone()
    {
        var bones = TestSupport.Load("worm.sdf").Skeleton.Bones;
        Assert.Equal(0f, bones[1].Falloff);   // a: nothing joins it to root
        Assert.Equal(0.1f, bones[2].Falloff, 4);   // 2 x the smoothness
        Assert.Equal(0.1f, bones[3].Falloff, 4);
    }

    [Fact]
    public void A_bone_frame_has_Y_from_head_to_tail_and_X_the_projection_of_the_models_X()
    {
        var worm = TestSupport.Load("worm.sdf").Skeleton.Bones[2];   // runs along +Z
        var frame = M3.FromQuaternion(worm.Frame);
        AssertNear(new D3(0, 0, 1), frame.Column(1));
        AssertNear(new D3(1, 0, 0), frame.Column(0));
        AssertNear(new D3(0, -1, 0), frame.Column(2));   // Z = X cross Y
        Assert.Equal(1.0, frame.Determinant, 5);
    }

    [Fact]
    public void Mirrored_bones_get_suffixes_and_the_mirrored_position()
    {
        var model = TestSupport.Load("crawler.sdf");
        Assert.False(model.HasErrors, string.Join("\n", model.Issues));
        Assert.Equal(["root", "body", "head", "leg_front_L", "leg_front_R"], model.Skeleton.Bones.Select(b => b.Name));
        var (left, right) = (model.Skeleton.Bones[3], model.Skeleton.Bones[4]);
        Assert.Equal(new Vector3(0.35f, 0.3f, 0.3f), left.Head);
        Assert.Equal(new Vector3(-0.35f, 0.3f, 0.3f), right.Head);
        Assert.Equal(1, left.Parent);
        Assert.Equal(0.06f, right.Falloff, 4);
        // R's frame follows the same rule as any bone: X is the projection of the model's +X
        var frameR = M3.FromQuaternion(right.Frame);
        Assert.True(frameR.Column(0).X > 0);
    }

    [Fact]
    public void Sockets_belong_to_the_nearest_bone_and_are_in_model_space()
    {
        var model = TestSupport.Load("crawler.sdf");
        var mouth = Assert.Single(model.Skeleton.Sockets);
        Assert.Equal("mouth", mouth.Name);
        Assert.Equal(model.Skeleton.IndexOf("head"), mouth.Bone);
        Assert.Equal(new Vector3(0, 0.45f, 0.85f), mouth.Position);
        Assert.Equal(new Vector3(0, 0, 1), mouth.Dir);
    }

    [Fact]
    public void Bone_attributes_are_read_into_the_bone_frame()
    {
        var model = TestSupport.Load("crawler.sdf");
        var head = model.Skeleton.Find("head")!;
        Assert.Equal(new Vector3(1, 0, 0), head.Hinge);
        Assert.Equal((-0.5f, 0.8f), head.Limit);
        var drill = TestSupport.Load("machine.sdf").Skeleton.Find("drill")!;
        Assert.NotNull(drill.SpinAxis);
        Assert.Equal(0f, TestSupport.Load("machine.sdf").Skeleton.Find("turret")!.Falloff);
    }

    [Fact]
    public void A_file_without_bones_gives_exactly_its_format_1_program()
    {
        var text = "format 2\n\nSmoothUnion smoothness=0.1\n    Sphere radius=0.5\n    Translate translation=(0.4, 0, 0)\n        Sphere radius=0.3\n";
        var model = RigModel.Read(text);
        Assert.False(model.HasBones);
        Assert.Equal(text.Replace("format 2", "format 1"), model.RestProgram());
    }

    [Fact]
    public void Stripping_bones_leaves_the_same_shape_as_the_original_format_1_model()
    {
        var rest = TestSupport.Distance(TestSupport.Load("worm.sdf").RestProgram());
        var plain = TestSupport.Distance("""
            format 1
            SmoothUnion smoothness=0.05
                Capsule a=(0, 0.3, -0.6) b=(0, 0.3, -0.2) radius=0.15
                SmoothUnion smoothness=0.05
                    Capsule a=(0, 0.3, -0.2) b=(0, 0.3, 0.2) radius=0.15
                    Capsule a=(0, 0.3, 0.2) b=(0, 0.3, 0.6) radius=0.15
            """);
        foreach (var p in TestSupport.Points(300, new(-0.6f, -0.1f, -1), new(0.6f, 0.7f, 1)))
            Assert.Equal(plain(p), rest(p), 4);
    }

    [Fact]
    public void Round_trip_read_write_read_gives_the_same_model()
    {
        foreach (var file in new[] { "worm.sdf", "crawler.sdf", "machine.sdf" })
        {
            var first = TestSupport.Load(file);
            var text = first.Write();
            var second = RigModel.Read(text, TestSupport.Parts());
            Assert.Equal(text, second.Write());
            Assert.Equal(first.RestProgram(), second.RestProgram());
            Assert.Equal(first.Skeleton.Bones.Select(b => (b.Name, b.Parent, b.Head, b.Tail, b.Falloff)), second.Skeleton.Bones.Select(b => (b.Name, b.Parent, b.Head, b.Tail, b.Falloff)));
            Assert.Equal(first.Skeleton.Sockets.Select(s => (s.Name, s.Bone, s.Position)), second.Skeleton.Sockets.Select(s => (s.Name, s.Bone, s.Position)));
        }
    }

    [Fact]
    public void Format_1_files_read_as_models_without_bones()
    {
        var model = RigModel.Read("format 1\nSphere radius=1\n");
        Assert.False(model.HasBones);
        Assert.Single(model.Skeleton.Bones);
    }

    [Theory]
    [InlineData("duplicate", "Union\n    Bone name=a head=(0, 0, 0)\n        Sphere radius=1\n    Bone name=a head=(0, 1, 0)\n        Sphere radius=1\n", "two bones named 'a'")]
    [InlineData("root", "Bone name=root head=(0, 0, 0)\n    Sphere radius=1\n", "reserved")]
    [InlineData("space", "Distort amount=0.1 frequency=2\n    Bone name=a head=(0, 0, 0)\n        Sphere radius=1\n", "depends on the space")]
    [InlineData("scale", "Scale scale=(1, 2, 1)\n    Bone name=a head=(0, 0, 0)\n        Sphere radius=1\n", "non-uniform Scale")]
    [InlineData("suffix", "SymmetryX\n    Bone name=arm_L head=(1, 0, 0)\n        Sphere radius=1\n", "mirror suffix")]
    [InlineData("clash", "Union\n    SymmetryX\n        Bone name=arm head=(1, 0, 0)\n            Sphere radius=1\n    Bone name=arm_L head=(2, 0, 0)\n        Sphere radius=1\n", "two bones named 'arm_L'")]
    [InlineData("badname", "Bone name=Arm head=(0, 0, 0)\n    Sphere radius=1\n", "lowercase")]
    [InlineData("nested", "SymmetryX\n    SymmetryXZ\n        Bone name=a head=(1, 0, 1)\n            Sphere radius=1\n", "inside another symmetry")]
    [InlineData("tailhead", "Bone name=a head=(0, 0, 0) tail=(0, 0, 0)\n    Sphere radius=1\n", "tail at its head")]
    [InlineData("spinhinge", "Bone name=a head=(0, 0, 0) spin=y hinge=x\n    Sphere radius=1\n", "not both")]
    [InlineData("var", "Translate translation=$t\n    Bone name=a head=(0, 0, 0)\n        Sphere radius=1\n", "plain numbers")]
    public void Validation_reports_errors(string _, string tree, string expected)
    {
        var model = RigModel.Read("format 2\n" + (tree.Contains("$t") ? "t = (1, 0, 0)\n" : "") + tree);
        Assert.Contains(model.Issues, i => i.IsError && i.Message.Contains(expected));
    }

    [Theory]
    [InlineData("format 2\nBone head=(0, 0, 0)\n    Sphere radius=1\n", "needs a name")]
    [InlineData("format 2\nBone name=a\n    Sphere radius=1\n", "needs a head")]
    [InlineData("format 2\nBone name=a head=(0, 0, 0) colour=red\n    Sphere radius=1\n", "no attribute 'colour'")]
    [InlineData("format 3\nSphere radius=1\n", "only formats 1 and 2")]
    [InlineData("format 2\nv = 1\nSphere radius=$v\nSphere radius=2\n", "already has its shape")]
    [InlineData("format 2\nf = \n    Bone name=a head=(0, 0, 0)\n        Sphere radius=1\nSphere radius=1\n", "must be in the model's shape tree")]
    public void Syntax_problems_throw_with_the_line(string text, string expected)
    {
        var e = Assert.Throws<RigFormatException>(() => RigModel.Read(text));
        Assert.Contains(expected, e.Problem);
        Assert.True(e.Line >= 1);
    }

    private static void AssertNear(D3 expected, D3 actual, double tolerance = 1e-5)
    {
        Assert.True((expected - actual).Length < tolerance, $"expected {expected}, got {actual}");
    }
}
