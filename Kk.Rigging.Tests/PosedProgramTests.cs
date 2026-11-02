using System.Numerics;

namespace Kk.Rigging.Tests;

public class PosedProgramTests
{
    /// <summary>The rigid motion a pose gives a bone's own geometry (and its descendants'): rotation about the head by F * R * F^T.</summary>
    private static Func<Vector3, Vector3> Motion(RigBone bone, Quaternion frameRotation)
    {
        var f = M3.FromQuaternion(bone.Frame);
        var r = f * M3.FromQuaternion(frameRotation) * f.Transposed();
        var head = D3.From(bone.Head);
        return p => (head + r * (D3.From(p) - head)).ToVector3();
    }

    /// <summary>The crawler with a nearly sharp union, so rigid-motion checks near a leg are not disturbed by the blend with the body.</summary>
    private static RigModel SharpCrawler() =>
        RigModel.Read(File.ReadAllText(TestSupport.ModelPath("crawler.sdf")).Replace("smoothness=0.12", "smoothness=0.01"));

    [Fact]
    public void Posing_with_nothing_posed_is_the_rest_program()
    {
        var model = SharpCrawler();
        var rest = TestSupport.Distance(model.RestProgram());
        var posed = TestSupport.Distance(model.PosedProgram(new Pose()));
        foreach (var p in TestSupport.Points(200, new(-1, -0.1f, -1), new(1, 1, 1.2f)))
            Assert.Equal(rest(p), posed(p), 5);
    }

    [Fact]
    public void Bending_the_worms_middle_bone_moves_the_rest_of_the_worm_exactly()
    {
        var model = TestSupport.Load("worm.sdf");
        var b = model.Skeleton.Find("b")!;
        var pose = new Pose().Rotate("b", 1.0f, 0, 0);
        var posed = TestSupport.Distance(model.PosedProgram(pose));
        var move = Motion(b, BonePose.FromEuler(1.0f, 0, 0));

        // Near the surface at the far end of the worm (bone c's tail, away from the blends) the posed shape is the rest shape, moved
        var rest0 = TestSupport.Distance(model.RestProgram());
        var counted = 0;
        foreach (var q in TestSupport.Points(5000, new(-0.3f, 0.0f, 0.45f), new(0.3f, 0.6f, 0.9f)))
        {
            if (MathF.Abs(rest0(q)) > 0.05f)
                continue;
            Assert.InRange(posed(move(q)) - rest0(q), -0.003f, 0.003f);
            counted++;
        }
        Assert.True(counted > 50);

        // and the fixed bone does not move at all
        var rest = TestSupport.Distance(model.RestProgram());
        foreach (var p in TestSupport.Points(100, new(-0.3f, 0.1f, -0.8f), new(0.3f, 0.5f, -0.7f)))
            Assert.InRange(posed(p) - rest(p), -0.006f, 0.006f);   // only the blend with the moved neighbour differs
    }

    [Fact]
    public void Bending_goes_the_way_the_frames_X_axis_turns()
    {
        // Rotating about +X by a positive angle takes +Z towards -Y (right-handed): the worm's tail sinks
        var model = TestSupport.Load("worm.sdf");
        var posed = TestSupport.Distance(model.PosedProgram(new Pose().Rotate("b", 1.2f, 0, 0)));
        Assert.True(posed(new Vector3(0, 0.3f, 0.55f)) > 0.05f);       // where the tail was
        Assert.True(posed(new Vector3(0, -0.26f, 0.02f)) < 0);       // where it went
    }

    [Theory]
    [InlineData("leg_front_L")]
    [InlineData("leg_front_R")]
    public void Each_mirrored_leg_poses_apart(string leg)
    {
        var model = SharpCrawler();
        var bone = model.Skeleton.Find(leg)!;
        var other = model.Skeleton.Find(leg == "leg_front_L" ? "leg_front_R" : "leg_front_L")!;
        var rotation = BonePose.FromEuler(0.6f, 0, 0);
        var posed = TestSupport.Distance(model.PosedProgram(new Pose().Set(leg, new BonePose(rotation, Vector3.Zero))));
        var rest = TestSupport.Distance(model.RestProgram());
        var bodyOnly = TestSupport.Distance(model.BoneProgram(1)!);
        var move = Motion(bone, rotation);

        // Near the moved leg's far end the posed shape is the rest leg, moved (a rigid motion keeps distances)
        var checkedPoints = 0;
        foreach (var q in TestSupport.Points(20000, bone.Tail - new Vector3(0.2f), bone.Tail + new Vector3(0.2f)))
        {
            var p = move(q);
            if (MathF.Abs(rest(q)) > 0.06f || bodyOnly(q) < 0.25f || bodyOnly(p) < 0.25f)
                continue;
            Assert.InRange(posed(p) - rest(q), -0.002f, 0.002f);
            checkedPoints++;
        }
        Assert.True(checkedPoints > 30, $"only {checkedPoints} points were far enough from the body");

        // the other leg stays where it was
        foreach (var p in TestSupport.Points(300, other.Tail - new Vector3(0.3f), other.Tail + new Vector3(0.3f)))
            Assert.InRange(posed(p) - rest(p), -0.006f, 0.006f);   // only the blend with the moved neighbour differs
    }

    [Fact]
    public void A_posed_child_bone_follows_its_parent()
    {
        // Turning the crawler's body carries the head; turning the head too composes
        var model = SharpCrawler();
        var body = model.Skeleton.Find("body")!;
        var head = model.Skeleton.Find("head")!;
        var rotBody = BonePose.FromEuler(0.3f, 0, 0);
        var rotHead = BonePose.FromEuler(0.4f, 0, 0);
        var posed = TestSupport.Distance(model.PosedProgram(new Pose().Set("body", new BonePose(rotBody, Vector3.Zero)).Set("head", new BonePose(rotHead, Vector3.Zero))));
        var rest = TestSupport.Distance(model.RestProgram());
        var bodyOnly = TestSupport.Distance(model.BoneProgram(1)!);
        var bodyMove = Motion(body, rotBody);
        // The head's pose is about its REST head, then the whole thing is carried by the body's motion
        var headMove = Motion(head, rotHead);
        var count = 0;
        foreach (var q in TestSupport.Points(30000, new(-0.25f, 0.4f, 0.7f), new(0.25f, 0.8f, 1.1f)))
        {
            var p = bodyMove(headMove(q));
            if (rest(q) is var d && (Math.Abs(d) > 0.06f || bodyOnly(q) < 0.25f || bodyOnly(headMove(q)) < 0.25f))
                continue;
            Assert.InRange(posed(p) - d, -0.002f, 0.002f);
            count++;
        }
        Assert.True(count > 20, $"{count}");
    }

    [Fact]
    public void A_pose_translation_moves_the_bone_in_its_frame()
    {
        var model = TestSupport.Load("machine.sdf");
        var drill = model.Skeleton.Find("drill")!;   // runs along +Z; frame Y is world Z
        var frame = M3.FromQuaternion(drill.Frame);
        var shift = new Vector3(0, 0.25f, 0);        // along the bone: world +Z
        var posed = TestSupport.Distance(model.PosedProgram(new Pose().Set("drill", new BonePose(Quaternion.Identity, shift))));
        var rest = TestSupport.Distance(model.RestProgram());
        var world = (frame * D3.From(shift)).ToVector3();
        foreach (var q in TestSupport.Points(200, new(-0.15f, -0.15f, 1.0f), new(0.15f, 0.15f, 1.4f)))
            Assert.InRange(posed(q + world) - rest(q), -0.002f, 0.002f);
    }

    [Fact]
    public void Posing_an_unknown_bone_is_an_error_that_lists_the_bones()
    {
        var model = TestSupport.Load("worm.sdf");
        var e = Assert.Throws<ArgumentException>(() => model.PosedProgram(new Pose().Rotate("wing", 1, 0, 0)));
        Assert.Contains("wing", e.Message);
        Assert.Contains("a, b, c", e.Message);
    }

    [Fact]
    public void Euler_conversion_round_trips()
    {
        var random = new Random(3);
        for (var i = 0; i < 200; i++)
        {
            var (x, y, z) = ((random.NextDouble() - 0.5) * 6, (random.NextDouble() - 0.5) * 3, (random.NextDouble() - 0.5) * 6);
            var m = M3.EulerXyz(x, y, z);
            var e = m.ToEulerXyz();
            var back = M3.EulerXyz(e.X, e.Y, e.Z);
            Assert.True((m.Column(0) - back.Column(0)).Length < 1e-9 && (m.Column(1) - back.Column(1)).Length < 1e-9 && (m.Column(2) - back.Column(2)).Length < 1e-9);
        }
    }

    [Fact]
    public void Each_bones_own_program_reads_and_is_the_bones_geometry_only()
    {
        var model = TestSupport.Load("worm.sdf");
        var c = TestSupport.Distance(model.BoneProgram(3)!);
        Assert.Equal(0.1f, c(new Vector3(0, 0.3f + 0.25f, 0.4f)), 4);   // 0.15 + 0.1 above the capsule's axis... = 0.25 -> 0.1 outside
        var a = TestSupport.Distance(model.BoneProgram(1)!);
        Assert.True(a(new Vector3(0, 0.3f, 0.4f)) > 0.4f);               // the capsule of c is not in a's program
        Assert.Null(model.BoneProgram(0));                              // the worm has nothing outside its bones
    }

    private static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
        return (p - (a + ab * t)).Length();
    }
}
