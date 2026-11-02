using System.Numerics;
using Kk.Rigging.Skeletons;

namespace Kk.Rigging.Tests.Skeletons;

public class SkeletonFileTests
{
    private const string Header = "skeleton synty\nspace unity\nup +y\nforward +z\nscale 1\n";

    private const string Example = """
        # a small skeleton
        skeleton synty
        space unity
        up +y
        forward +z
        scale 1

        bone Root root - pos=(0, 0, 0) rot=(0, 0, 0, 1)
        bone Hips hips Root pos=(0, 1.02, 0.01) rot=(0.46113685, -0.53605306, -0.46113706, 0.53605276)   # trailing comment
        bone Spine_01 spine_01 Hips pos=(0, 0.1, 0) rot=(0, 0, 0, 1)
        bone UpperLeg_L upper_leg_L Hips pos=(-0.09, -0.05, 0) rot=(0, 0, 1, 0)
        bone UpperLeg_R upper_leg_R Hips pos=(0.09, -0.05, 0) rot=(0, 0, 1, 0) scale=(1, 1, 1)
        bone Finger_011 finger_01_R UpperLeg_R pos=(0.01, 0.02, 0.03) rot=(0, 0, 0, 1)
        """;

    private static int ErrorLine(string text)
    {
        var e = Assert.Throws<SkeletonFormatException>(() => SkeletonFile.Parse(text));
        Assert.StartsWith($"line {e.Line}: ", e.Message);
        return e.Line;
    }

    private static string Bones(params string[] lines) => Header + string.Join("\n", lines);

    private const string RootLine = "bone Root root - pos=(0, 0, 0) rot=(0, 0, 0, 1)";

    [Fact]
    public void Parses_names_aliases_parents_and_values()
    {
        var file = SkeletonFile.Parse(Example);
        Assert.Equal("synty", file.Name);
        Assert.Equal(6, file.Joints.Count);
        Assert.Equal(["Root", "Hips", "Spine_01", "UpperLeg_L", "UpperLeg_R", "Finger_011"], file.Joints.Select(j => j.Name));
        Assert.Equal(["root", "hips", "spine_01", "upper_leg_L", "upper_leg_R", "finger_01_R"], file.Joints.Select(j => j.Alias));
        Assert.Equal([-1, 0, 1, 1, 1, 4], file.Joints.Select(j => j.Parent));
        Assert.Equal(new Vector3(0, 1.02f, 0.01f), file.Joints[1].Position);
        Assert.Equal(1f, file.Joints[1].Rotation.Length(), 1e-6f);
        Assert.Equal(4, file.IndexOf("UpperLeg_R"));
        Assert.Equal(-1, file.IndexOf("upper_leg_R"));
        Assert.Equal(5, file.IndexOfAlias("finger_01_R"));
        Assert.Equal("Finger_011", file.Find("finger_01_R")!.Name);
        Assert.Equal("Hips", file.Find("Hips")!.Name);
        Assert.Null(file.Find("nothing"));
    }

    [Fact]
    public void Write_then_parse_gives_identical_joints()
    {
        var file = SkeletonFile.Parse(Example);
        var again = SkeletonFile.Parse(file.Write());
        Assert.Equal(file.Name, again.Name);
        Assert.Equal(file.Joints.Count, again.Joints.Count);
        for (var i = 0; i < file.Joints.Count; i++)
        {
            var a = file.Joints[i];
            var b = again.Joints[i];
            Assert.Equal((a.Index, a.Name, a.Alias, a.Parent), (b.Index, b.Name, b.Alias, b.Parent));
            Assert.True(a.Position == b.Position && a.Rotation == b.Rotation);
        }
        Assert.Equal(file.Write(), again.Write());
    }

    [Fact]
    public void Accepts_windows_line_endings()
    {
        Assert.Equal(6, SkeletonFile.Parse(Example.Replace("\n", "\r\n")).Joints.Count);
    }

    [Fact]
    public void Reports_bone_errors_with_their_line()
    {
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone A a Missing pos=(0,0,0) rot=(0,0,0,1)")));
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone A a B pos=(0,0,0) rot=(0,0,0,1)", "bone B b Root pos=(0,0,0) rot=(0,0,0,1)")));
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone Root a Root pos=(0,0,0) rot=(0,0,0,1)")));
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone A root Root pos=(0,0,0) rot=(0,0,0,1)")));
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone A Bad-Alias Root pos=(0,0,0) rot=(0,0,0,1)")));
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone A a_X Root pos=(0,0,0) rot=(0,0,0,1)")));
        Assert.Equal(6, ErrorLine(Bones("bone Hips hips - pos=(0,0,0) rot=(0,0,0,1)")));
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone Other other - pos=(0,0,0) rot=(0,0,0,1)")));
        Assert.Equal(6, ErrorLine(Bones("bone Root root Root pos=(0,0,0) rot=(0,0,0,1)")));
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone A a Root pos=(0,0,0)")));
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone A a Root rot=(0,0,0,1)")));
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone A a Root pos=(0,0,0) rot=(0,0,0,1) wobble=(1)")));
    }

    [Fact]
    public void Reports_quaternion_scale_and_number_errors()
    {
        var q = Assert.Throws<SkeletonFormatException>(() => SkeletonFile.Parse(Bones(RootLine, "bone A a Root pos=(0,0,0) rot=(0,0,0,2)")));
        Assert.Equal(7, q.Line);
        Assert.Contains("unit quaternion", q.Problem);
        var s = Assert.Throws<SkeletonFormatException>(() => SkeletonFile.Parse(Bones(RootLine, "bone A a Root pos=(0,0,0) rot=(0,0,0,1) scale=(1, 2, 1)")));
        Assert.Equal(7, s.Line);
        Assert.Contains("scaled bones are not supported", s.Problem);
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone A a Root pos=(0,x,0) rot=(0,0,0,1)")));
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone A a Root pos=(0,0) rot=(0,0,0,1)")));
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone A a Root pos=(0,0,NaN) rot=(0,0,0,1)")));
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone A a Root pos=(0,0,0,) rot=(0,0,0,1)")));
        Assert.Equal(7, ErrorLine(Bones(RootLine, "bone A a Root pos=(0,0,0) rot=(0,0,0,1")));
        // small deviations are normalised on read
        var ok = SkeletonFile.Parse(Bones(RootLine, "bone A a Root pos=(0,0,0) rot=(0,0,0,1.0005)"));
        Assert.Equal(1f, ok.Joints[1].Rotation.Length(), 1e-6f);
    }

    [Fact]
    public void Reports_header_errors()
    {
        Assert.Equal(5, ErrorLine("space unity\nup +y\nforward +z\nscale 1\n" + RootLine)); // no skeleton line, reported at the first bone
        Assert.Equal(6, ErrorLine(Header.Replace("scale 1\n", "") + "\n" + RootLine));
        Assert.Equal(2, ErrorLine(Header.Replace("space unity", "space blender")));
        Assert.Equal(3, ErrorLine(Header.Replace("up +y", "up +z")));
        Assert.Equal(4, ErrorLine(Header.Replace("forward +z", "forward -z")));
        Assert.Equal(5, ErrorLine(Header.Replace("scale 1", "scale 0.01")));
        Assert.Equal(1, ErrorLine(Header.Replace("synty", "Synty Pack")));
        Assert.Equal(6, ErrorLine(Header + "up +y\n" + RootLine));
        Assert.Equal(7, ErrorLine(Header + RootLine + "\nup +y"));
        Assert.Equal(2, ErrorLine("skeleton a\nfrobnicate 3\n"));
        Assert.Equal(6, ErrorLine(Header)); // no bones
    }

    [Fact]
    public void MirrorRotation_twice_is_identity_and_agrees_with_MirrorPosition()
    {
        var random = new Random(5);
        for (var i = 0; i < 100; i++)
        {
            var q = Quaternion.Normalize(new Quaternion(Next(random), Next(random), Next(random), Next(random)));
            var v = new Vector3(Next(random), Next(random), Next(random));
            Assert.Equal(q, SkeletonFile.MirrorRotation(SkeletonFile.MirrorRotation(q)));
            Assert.Equal(v, SkeletonFile.MirrorPosition(SkeletonFile.MirrorPosition(v)));
            var a = Vector3.Transform(SkeletonFile.MirrorPosition(v), SkeletonFile.MirrorRotation(q));
            var b = SkeletonFile.MirrorPosition(Vector3.Transform(v, q));
            Assert.True((a - b).Length() < 1e-6f, $"{a} vs {b}");
        }
    }

    private static string R(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static float Next(Random random) => (float)(random.NextDouble() * 2 - 1);

    [Fact]
    public void ModelRest_hand_computed_case()
    {
        // Unity: Hips at (0.1, 1, 0) turned 90 degrees about Y; child 0.5 along its local Z, which then points along +X in Unity
        var h = Math.Sqrt(0.5);
        var file = SkeletonFile.Parse(Bones(
            RootLine,
            $"bone Hips hips Root pos=(0.1, 1, 0) rot=(0, {R(h)}, 0, {R(h)})",
            "bone Child child Hips pos=(0, 0, 0.5) rot=(0, 0, 0, 1)"));
        var world = file.ModelRest();
        Assert.Equal(3, world.Length);
        Assert.Equal(0, world[0].Position.Length, 1e-12);
        // glTF space negates X: Hips at (-0.1, 1, 0); child at Unity (0.6, 1, 0) which is (-0.6, 1, 0)
        Assert.Equal(-0.1, world[1].Position.X, 1e-6);
        Assert.Equal(1, world[1].Position.Y, 1e-9);
        Assert.Equal(0, world[1].Position.Z, 1e-6);
        Assert.Equal(-0.6, world[2].Position.X, 1e-6);
        Assert.Equal(1, world[2].Position.Y, 1e-9);
        Assert.Equal(0, world[2].Position.Z, 1e-6);
        // rotation of the Hips: the Unity quaternion (0, s, 0, s) mirrored is (0, -s, 0, s), a -90 degree turn about Y
        var expected = M3.FromQuaternion(SkeletonFile.MirrorRotation(file.Joints[1].Rotation));
        AssertSame(expected, world[1].Rotation);
        AssertSame(expected, world[2].Rotation);
        var forward = world[1].Rotation * D3.UnitZ;
        Assert.Equal(-1, forward.X, 1e-6);
        Assert.Equal(0, forward.Z, 1e-6);
    }

    private static void AssertSame(M3 a, M3 b)
    {
        for (var c = 0; c < 3; c++)
        {
            Assert.Equal(a.Column(c).X, b.Column(c).X, 1e-9);
            Assert.Equal(a.Column(c).Y, b.Column(c).Y, 1e-9);
            Assert.Equal(a.Column(c).Z, b.Column(c).Z, 1e-9);
        }
    }

    [Fact]
    public void ModelRest_mirror_commutes_with_composition()
    {
        var random = new Random(11);
        var lines = new List<string> { RootLine };
        for (var i = 1; i <= 8; i++)
        {
            var q = Quaternion.Normalize(new Quaternion(Next(random), Next(random), Next(random), Next(random)));
            lines.Add($"bone B{i} b{i} {(i == 1 ? "Root" : "B" + random.Next(1, i))} pos=({R(Next(random))}, {R(Next(random))}, {R(Next(random))}) rot=({R(q.X)}, {R(q.Y)}, {R(q.Z)}, {R(q.W)})");
        }
        var file = SkeletonFile.Parse(Bones(lines.ToArray()));
        var model = file.ModelRest();

        // compose in Unity space, then mirror the result
        var mirror = M3.Diagonal(-1, 1, 1);
        var unity = new (D3 Position, M3 Rotation)[file.Joints.Count];
        foreach (var j in file.Joints)
        {
            var p = D3.From(j.Position);
            var r = M3.FromQuaternion(j.Rotation);
            unity[j.Index] = j.Parent < 0 ? (p, r) : (unity[j.Parent].Position + unity[j.Parent].Rotation * p, unity[j.Parent].Rotation * r);
        }
        for (var i = 0; i < unity.Length; i++)
        {
            var p = mirror * unity[i].Position;
            var r = mirror * unity[i].Rotation * mirror;
            Assert.Equal(p.X, model[i].Position.X, 1e-9);
            Assert.Equal(p.Y, model[i].Position.Y, 1e-9);
            Assert.Equal(p.Z, model[i].Position.Z, 1e-9);
            AssertSame(r, model[i].Rotation);
        }
    }

    [Fact]
    public void FindFile_walks_up_to_the_content_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "skeleton-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var skeletons = Path.Combine(root, "content", "skeletons");
            var deep = Path.Combine(root, "tests", "bin", "Debug");
            Directory.CreateDirectory(skeletons);
            Directory.CreateDirectory(deep);
            var file = Path.Combine(skeletons, "synty.skeleton");
            File.WriteAllText(file, Example);

            Assert.Equal(file, SkeletonFile.FindFile("synty", deep));
            Assert.Equal(file, SkeletonFile.FindFile("synty", root));
            Assert.Equal(file, SkeletonFile.FindFile("synty", Path.Combine(root, "content")));
            Assert.Equal(file, SkeletonFile.FindFile("synty", skeletons));
            Assert.Null(SkeletonFile.FindFile("missing", deep));
            Assert.Equal("synty", SkeletonFile.Load(file).Name);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
