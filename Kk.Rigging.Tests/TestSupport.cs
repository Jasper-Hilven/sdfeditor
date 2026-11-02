using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;

namespace Kk.Rigging.Tests;

internal static class TestSupport
{
    public static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SDFProject.sln")))
                return directory.FullName;
        throw new InvalidOperationException("SDFProject.sln was not found above the test output");
    }

    public static string ModelPath(string file) => Path.Combine(Repository(), "Kk.Rigging.Tests", "Models", file);

    public static RigModel Load(string file) => RigModel.Load(ModelPath(file));

    /// <summary>The signed distance function of a format 1 program text.</summary>
    public static Func<Vector3, float> Distance(string format1, PartSource? parts = null)
    {
        var program = SdfProgramFile.Read(format1, parts);
        return new SdfCalculatorCpu().Prepare(program).Distance;
    }

    public static PartSource Parts() => ModelFiles.PartsIn(Path.Combine(Repository(), "content"));

    public static IEnumerable<Vector3> Points(int count, Vector3 min, Vector3 max, int seed = 7)
    {
        var random = new Random(seed);
        for (var i = 0; i < count; i++)
            yield return min + new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle()) * (max - min);
    }
}
