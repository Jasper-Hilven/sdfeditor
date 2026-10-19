using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using SDFPipeline.Meshing;
using SDFProject.ProgramDefinition;
using static SDFPipeline.Tests.Meshing.MeshTestPrograms;

namespace SDFPipeline.Tests.Meshing;

public class LevelOfDetailTests
{
    private static readonly Dictionary<string, string> Parts = new()
    {
        ["eye"] = "format 1\nUnion\n    Material material=white\n        Sphere radius=0.1\n    Material material=pupil\n        Translate translation=(0, 0, 0.09)\n            Sphere radius=0.03",
        ["hand"] = "format 1\nEllipsoid radii=(0.06, 0.08, 0.05)",
    };

    /// <summary>A body with a painted band, so a material border runs round it, mirrored eyes with pupils, and hands.</summary>
    private const string Scout = """
        Union
            Union
                Material material=flesh
                    VerticalCapsule height=0.6 radius=0.2
                Material material=band
                    Intersection
                        VerticalCapsule height=0.6 radius=0.2
                        Translate translation=(0, 0.3, 0)
                            Box size=(0.3, 0.05, 0.3)
            SymmetryX
                Translate translation=(0.1, 0.55, 0.17)
                    Part part=eye
            SymmetryX
                Translate translation=(0.3, 0.3, 0)
                    Part part=hand
        """;

    /// <summary>The chanterelle scout's thin wavy rim, the hardest place not to fold when simplifying hard.</summary>
    private const string Rim = """
        RadialWave amount=0.08 count=7
            Distort amount=0.006 frequency=3
                Revolution offset=0
                    Translate2D offset=(0.12, 1.3)
                        Rotate2D angle=-1.0667
                            UnevenCapsule2D bottomRadius=0.07 topRadius=0.018 height=0.66
        """;

    private static SDFProgram ReadScout() => Read(Scout, name => Parts.GetValueOrDefault(name));

    private static readonly MeshingOptions Options = new() { Resolution = 64, LodCount = 4 };

    [Fact]
    public void EachMesh_GetsItsLevels_EachMuchCheaperThanTheOneBefore()
    {
        var model = ModelMesher.Mesh(ReadScout(), Options);

        foreach (var mesh in model.Meshes)
        {
            Assert.Equal(Options.LodCount - 1, mesh.Lods.Count);
            var levels = new[] { mesh }.Concat(mesh.Lods).ToArray();
            for (var level = 1; level < levels.Length; level++)
            {
                Assert.Equal(mesh.Name, levels[level].Name);
                Assert.True(levels[level].MaxError > levels[level - 1].MaxError);
                Assert.True(levels[level].TriangleCount <= levels[level - 1].TriangleCount * 0.6f,
                    $"{mesh.Name} LOD{level} has {levels[level].TriangleCount} triangles, LOD{level - 1} {levels[level - 1].TriangleCount}");
            }
        }
        // The body, a capsule, ends as a few dozen triangles
        Assert.InRange(model.Meshes[0].Lods[^1].TriangleCount, 20, 150);
    }

    [Fact]
    public void Levels_AreClosed_Unfolded_OnTheSurface_AndShaded()
    {
        var program = ReadScout();
        var model = ModelMesher.Mesh(program, Options);

        foreach (var mesh in model.Meshes)
        {
            var sdf = Distance(PartSplitter.Split(program).Single(s => s.Name == mesh.Name).Program);
            foreach (var lod in mesh.Lods)
            {
                AssertLevel(lod, sdf);
                Assert.All(lod.Normals, n => Assert.Equal(1, n.Length(), 3));
                Assert.All(lod.Thickness, t => Assert.InRange(t, 0, Options.MaxThickness));
                Assert.All(lod.Occlusion, o => Assert.InRange(o, 0, 1));
                // Every vertex has its material from the SDF, and the thick body and parts are thick all over, as LOD0 is
                Assert.All(lod.Materials, m => Assert.InRange(m.First, 0, model.Materials.Count - 1));
                Assert.All(lod.Thickness, t => Assert.True(t > 0.02f, $"{mesh.Name}: a vertex of a coarse level has thickness {t}"));
            }
        }

        // The pupil and the band are kept on every level, however coarse
        var eye = model.Meshes.Single(m => m.Name == "eye");
        var pupil = model.Materials.Select(m => m.Name).ToList().IndexOf("pupil");
        Assert.All(eye.Lods, lod => Assert.Contains(GlbWriter.Regions(lod), region => region.Material == pupil));
        var band = model.Materials.Select(m => m.Name).ToList().IndexOf("band");
        Assert.All(model.Meshes[0].Lods, lod => Assert.Contains(GlbWriter.Regions(lod), region => region.Material == band));
    }

    [Theory]
    [InlineData(64)]
    [InlineData(96)]
    public void ThinRim_StaysClosedAndUnfolded_OnEveryLevel(int resolution)
    {
        var program = Read(Rim);
        var model = ModelMesher.Mesh(program, new MeshingOptions { Resolution = resolution, SizeExponent = 0, LodCount = 4 });
        var mesh = Assert.Single(model.Meshes);

        Assert.Equal(3, mesh.Lods.Count);
        Assert.All(mesh.Lods, lod => AssertLevel(lod, Distance(program)));
        Assert.True(mesh.Lods[^1].TriangleCount < mesh.TriangleCount / 10);
    }

    [Fact]
    public void OneLevel_LeavesTheMeshAndTheFileAsTheyWere()
    {
        var program = ReadScout();
        var one = ModelMesher.Mesh(program, Options with { LodCount = 1 });
        var three = ModelMesher.Mesh(program, Options with { LodCount = 3 });

        Assert.All(one.Meshes, mesh => Assert.Empty(mesh.Lods));
        Assert.Equal(one.Materials.Select(m => m.Name), three.Materials.Select(m => m.Name));
        foreach (var (a, b) in one.Meshes.Zip(three.Meshes))
        {
            Assert.Equal(a.Positions, b.Positions);
            Assert.Equal(a.Normals, b.Normals);
            Assert.Equal(a.Triangles, b.Triangles);
            Assert.Equal(a.Materials, b.Materials);
            Assert.Equal(a.Thickness, b.Thickness);
            Assert.Equal(a.Occlusion, b.Occlusion);
        }

        // LOD0 is written first, so its meshes, accessors and bytes are the same with or without levels after it
        var (oneJson, oneBinary) = Chunks(GlbWriter.Write(one, "scout"));
        var (threeJson, threeBinary) = Chunks(GlbWriter.Write(three, "scout"));
        using (oneJson)
        using (threeJson)
        {
            var oneRoot = oneJson.RootElement;
            var threeRoot = threeJson.RootElement;
            var oneMeshes = oneRoot.GetProperty("meshes").EnumerateArray().ToArray();
            var threeMeshes = threeRoot.GetProperty("meshes").EnumerateArray().ToArray();
            for (var i = 0; i < oneMeshes.Length; i++)
                Assert.Equal(oneMeshes[i].GetRawText(), threeMeshes[i].GetRawText());
            Assert.Equal(oneBinary, threeBinary[..oneBinary.Length]);
            Assert.Equal(oneRoot.GetProperty("scenes").GetRawText(), threeRoot.GetProperty("scenes").GetRawText());

            Assert.DoesNotContain(GlbWriter.LodExtension, oneRoot.GetRawText());
            Assert.False(oneRoot.GetProperty("extras").GetProperty("sdf").TryGetProperty("lods", out _));
            Assert.Equal(oneMeshes.Length, oneRoot.GetProperty("nodes").GetArrayLength());
        }
    }

    [Fact]
    public void Glb_NamesEachLevel_AndListsItOnItsPartsNode()
    {
        var model = ModelMesher.Mesh(ReadScout(), Options with { LodCount = 3 });
        var (json, binary) = Chunks(GlbWriter.Write(model, "scout"));
        using (json)
        {
            var root = json.RootElement;
            var meshes = root.GetProperty("meshes").EnumerateArray().ToArray();
            var nodes = root.GetProperty("nodes").EnumerateArray().ToArray();
            var accessors = root.GetProperty("accessors").EnumerateArray().ToArray();

            Assert.Equal(["body", "eye", "hand", "body_LOD1", "body_LOD2", "eye_LOD1", "eye_LOD2", "hand_LOD1", "hand_LOD2"],
                meshes.Select(m => m.GetProperty("name").GetString()));
            Assert.Equal(meshes.Select(m => m.GetProperty("name").GetString()), nodes.Select(n => n.GetProperty("name").GetString()));
            // Only LOD0 is in the scene
            Assert.Equal([0, 1, 2], root.GetProperty("scenes")[0].GetProperty("nodes").EnumerateArray().Select(n => n.GetInt32()));
            Assert.Contains(GlbWriter.LodExtension, root.GetProperty("extensionsUsed").EnumerateArray().Select(e => e.GetString()));
            Assert.Equal(3, root.GetProperty("extras").GetProperty("sdf").GetProperty("lods").GetProperty("count").GetInt32());
            Assert.True(binary.Length >= root.GetProperty("buffers")[0].GetProperty("byteLength").GetInt32());

            for (var part = 0; part < model.Meshes.Count; part++)
            {
                var ids = nodes[part].GetProperty("extensions").GetProperty(GlbWriter.LodExtension).GetProperty("ids")
                    .EnumerateArray().Select(id => id.GetInt32()).ToArray();
                Assert.Equal(2, ids.Length);
                for (var level = 1; level <= ids.Length; level++)
                {
                    var node = nodes[ids[level - 1]];
                    var mesh = meshes[node.GetProperty("mesh").GetInt32()];
                    var lod = model.Meshes[part].Lods[level - 1];
                    Assert.Equal(GlbWriter.LodName(model.Meshes[part].Name, level), node.GetProperty("name").GetString());
                    Assert.Equal(model.Meshes[part].Name, mesh.GetProperty("extras").GetProperty("part").GetString());
                    Assert.Equal(level, mesh.GetProperty("extras").GetProperty("lod").GetInt32());
                    Assert.Equal(lod.MaxError, mesh.GetProperty("extras").GetProperty("maxError").GetSingle());

                    // The level's own vertices and triangles, split into its material regions like LOD0
                    var primitives = mesh.GetProperty("primitives").EnumerateArray().ToArray();
                    var position = accessors[primitives[0].GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                    Assert.Equal(lod.Positions.Length, position.GetProperty("count").GetInt32());
                    Assert.Equal(lod.Triangles.Length, primitives.Sum(p => accessors[p.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32()));
                }
            }
        }
    }

    /// <summary>
    /// A level is closed and faces outwards, no face faces against the surface, and its vertices and faces are within its error limit
    /// of the surface.
    /// </summary>
    private static void AssertLevel(PartMesh lod, Func<Vector3, float> sdf)
    {
        var welded = Welded(lod);
        AssertClosedAndOriented(welded);
        Assert.True(SignedVolume(welded) > 0);
        var limit = lod.MaxError * 1.01f;
        Assert.All(lod.Positions, p => Assert.InRange(MathF.Abs(sdf(p)), 0, limit));
        for (var t = 0; t < welded.TriangleCount; t++)
        {
            var (a, b, c) = (welded.Positions[welded.Triangles[3 * t]], welded.Positions[welded.Triangles[3 * t + 1]], welded.Positions[welded.Triangles[3 * t + 2]]);
            var normal = Vector3.Cross(b - a, c - a);
            var centre = (a + b + c) / 3;
            Assert.True(normal.LengthSquared() > 0, $"{lod.Name}: triangle {t} is flat");
            Assert.InRange(MathF.Abs(sdf(centre)), 0, limit);
            Assert.True(Vector3.Dot(normal, Gradient(sdf, centre, 1e-4f)) > 0, $"{lod.Name}: triangle {t} at {centre} faces against the surface");
        }
    }

    private static (JsonDocument Json, byte[] Binary) Chunks(byte[] glb)
    {
        var jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        var binaryLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(20 + jsonLength));
        return (JsonDocument.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength)), glb.AsSpan(28 + jsonLength, binaryLength).ToArray());
    }

    private static Vector3 Gradient(Func<Vector3, float> sdf, Vector3 p, float h) =>
        new(sdf(p + new Vector3(h, 0, 0)) - sdf(p - new Vector3(h, 0, 0)),
            sdf(p + new Vector3(0, h, 0)) - sdf(p - new Vector3(0, h, 0)),
            sdf(p + new Vector3(0, 0, h)) - sdf(p - new Vector3(0, 0, h)));
}
