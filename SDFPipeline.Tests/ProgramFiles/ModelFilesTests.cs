using SDFPipeline.ProgramFiles;

namespace SDFPipeline.Tests.ProgramFiles;

public class ModelFilesTests
{
    [Fact]
    public void FindContentFolder_LooksUpFromTheStartFolder()
    {
        var root = Directory.CreateTempSubdirectory("sdf-models-");
        try
        {
            var content = Directory.CreateDirectory(Path.Combine(root.FullName, ModelFiles.ContentFolderName));
            var deeper = Directory.CreateDirectory(Path.Combine(root.FullName, "a", "b"));

            Assert.Equal(content.FullName, ModelFiles.FindContentFolder(deeper.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void All_FindsProgramFilesInSubfoldersOnly()
    {
        var root = Directory.CreateTempSubdirectory("sdf-models-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "models"));
            File.WriteAllText(Path.Combine(root.FullName, "b.sdf"), "");
            File.WriteAllText(Path.Combine(root.FullName, "models", "a.sdf"), "");
            File.WriteAllText(Path.Combine(root.FullName, "models", "a.png"), "");

            Assert.Equal(
                [Path.Combine(root.FullName, "b.sdf"), Path.Combine(root.FullName, "models", "a.sdf")],
                ModelFiles.All(root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
