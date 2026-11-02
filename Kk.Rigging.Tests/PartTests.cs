using System.Numerics;
using SDFPipeline.ProgramFiles;

namespace Kk.Rigging.Tests;

public class PartTests
{
    private static readonly Dictionary<string, string> Parts = new()
    {
        ["ball"] = "format 1\nr = 0.2\nSphere radius=$r\n",
        ["twice"] = "format 1\nUnion\n    Part part=ball\n    Translate translation=(0.5, 0, 0)\n        Part part=ball\n",
        ["hand"] = "format 2\nUnion\n    Capsule a=(0, 0, 0) b=(0, 0, 0.1) radius=0.02\n    Bone name=thumb head=(-0.02, 0, 0.03) tail=(-0.05, 0, 0.05)\n        Capsule a=(-0.02, 0, 0.03) b=(-0.05, 0, 0.05) radius=0.01\n        Socket name=tip at=(-0.05, 0, 0.05)\n",
        ["badhand"] = "format 2\nv = 1\nBone name=thumb head=(0, 0, 0)\n    Sphere radius=$v\n",
        ["arm"] = "format 2\nUnion\n    Capsule a=(0, 0, 0) b=(0, -0.3, 0) radius=0.04\n    Translate translation=(0, -0.3, 0)\n        Part part=hand as=palm\n",
    };

    private static PartSource Source => name => Parts.GetValueOrDefault(name);

    [Fact]
    public void A_part_without_bones_stays_a_Part_line_even_with_variables_and_nested_parts()
    {
        var model = RigModel.Read("format 2\nUnion\n    Part part=twice\n    Bone name=a head=(0, 1, 0)\n        Sphere radius=0.1\n", Source);
        Assert.False(model.HasErrors, string.Join("\n", model.Issues));
        Assert.Contains("Part part=twice", model.RestProgram());
        Assert.DoesNotContain("Sphere radius=$r", model.RestProgram());
        Assert.NotNull(SdfProgramFile.Read(model.RestProgram(), Source));
    }

    [Fact]
    public void A_part_with_bones_is_read_inline_and_its_bones_are_prefixed_with_the_use()
    {
        var model = RigModel.Read("format 2\nBone name=body head=(0, 1, 0)\n    SymmetryX\n        Translate translation=(0.3, 1, 0)\n            Part part=hand\n", Source);
        Assert.False(model.HasErrors, string.Join("\n", model.Issues));
        Assert.Equal(["root", "body", "hand_thumb_L", "hand_thumb_R"], model.Skeleton.Bones.Select(b => b.Name));
        Assert.Equal(["hand_tip_L", "hand_tip_R"], model.Skeleton.Sockets.Select(s => s.Name));
        Assert.Equal(new Vector3(0.28f, 1f, 0.03f), model.Skeleton.Find("hand_thumb_L")!.Head);   // the part's bone in the model's space
        Assert.Equal(new Vector3(-0.28f, 1f, 0.03f), model.Skeleton.Find("hand_thumb_R")!.Head);
        Assert.DoesNotContain("Part part=hand", model.RestProgram());
    }

    [Fact]
    public void As_names_a_use_and_parts_nest()
    {
        var model = RigModel.Read("format 2\nUnion\n    Part part=arm as=left\n    Translate translation=(1, 0, 0)\n        Part part=arm as=right\n", Source);
        Assert.False(model.HasErrors, string.Join("\n", model.Issues));
        Assert.Equal(["root", "left_palm_thumb", "right_palm_thumb"], model.Skeleton.Bones.Select(b => b.Name));
    }

    [Fact]
    public void Using_a_part_with_bones_twice_without_as_is_a_name_clash()
    {
        var model = RigModel.Read("format 2\nUnion\n    Part part=hand\n    Translate translation=(1, 0, 0)\n        Part part=hand\n", Source);
        Assert.Contains(model.Issues, i => i.IsError && i.Message.Contains("two bones named 'hand_thumb'"));
    }

    [Fact]
    public void A_part_with_bones_may_not_declare_variables_and_a_missing_part_is_reported()
    {
        var e = Assert.Throws<RigFormatException>(() => RigModel.Read("format 2\nPart part=badhand\n", Source));
        Assert.Contains("declares variables", e.Problem);
        e = Assert.Throws<RigFormatException>(() => RigModel.Read("format 2\nPart part=nothing\n", Source));
        Assert.Contains("no part 'nothing'", e.Problem);
    }

    [Fact]
    public void The_round_trip_keeps_the_Part_line()
    {
        var text = "format 2\nBone name=body head=(0, 1, 0)\n    Part part=hand as=palm\n";
        var first = RigModel.Read(text, Source);
        Assert.Contains("Part part=hand", first.Write());
        Assert.Equal(first.Skeleton.Bones.Select(b => b.Name), RigModel.Read(first.Write(), Source).Skeleton.Bones.Select(b => b.Name));
    }
}
