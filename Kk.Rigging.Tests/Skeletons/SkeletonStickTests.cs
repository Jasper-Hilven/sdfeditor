using Kk.Rigging.Skeletons;

namespace Kk.Rigging.Tests.Skeletons;

public class SkeletonStickTests
{
    private static SkeletonFile Synty() =>
        SkeletonFile.Load(Path.Combine(TestSupport.Repository(), "content", "skeletons", "synty.skeleton"));

    [Fact]
    public void The_stick_model_reads_with_the_skeletons_bones_and_no_issues()
    {
        var file = Synty();
        var text = SkeletonStick.Write(file, "creature_crust_body");
        var model = RigModel.Read(text, TestSupport.Parts(), _ => Synty());

        Assert.Empty(model.Issues);
        Assert.Equal(file.Joints.Select(j => j.Name), model.Skeleton.Bones.Select(b => b.Name));
        Assert.Equal(file.Joints.Select(j => j.Parent), model.Skeleton.Bones.Select(b => b.Parent));
        Assert.Equal(50, model.Skeleton.Bones.Count);
    }

    [Fact]
    public void Numbers_use_the_invariant_culture_and_no_negative_zero()
    {
        var text = SkeletonStick.Write(Synty(), "creature_crust_body");
        Assert.DoesNotContain("-0,", text);
        Assert.DoesNotContain("-0)", text);
        Assert.DoesNotContain("0,5", text.Replace(", ", "|"));
    }
}
