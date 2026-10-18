using SDFPipeline.ProgramFiles;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

namespace SDFPipeline.Tests.ProgramFiles;

public class ExampleExportTests
{
    [Theory]
    [InlineData("Snowman", "snowman.sdf")]
    [InlineData("Rotten heavy mushroom", "rotten-heavy-mushroom.sdf")]
    [InlineData("Coffee mug!", "coffee-mug.sdf")]
    public void FileName_IsLowercaseWithDashes(string exampleName, string fileName) =>
        Assert.Equal(fileName, ExampleExport.FileName(exampleName));

    [Fact]
    public void ExportAll_WritesEveryExampleAsAReadableFile()
    {
        var directory = Directory.CreateTempSubdirectory("sdf-examples-").FullName;
        try
        {
            var paths = ExampleExport.ExportAll(directory);

            Assert.Equal(SDFExamples.All.Count, paths.Distinct().Count());
            Assert.All(paths, path =>
            {
                Assert.StartsWith("# ", File.ReadAllText(path));
                SdfProgramFile.Load(path);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
