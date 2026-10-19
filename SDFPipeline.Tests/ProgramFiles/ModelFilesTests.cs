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

    [Fact]
    public void PartsIn_FindsAPartInContentPartsOrInAKitFolderBelowIt()
    {
        var root = Directory.CreateTempSubdirectory("sdf-models-");
        try
        {
            var parts = Directory.CreateDirectory(Path.Combine(root.FullName, ModelFiles.PartsFolderName, "castle"));
            File.WriteAllText(Path.Combine(parts.Parent!.FullName, "eye.sdf"), "eye");
            File.WriteAllText(Path.Combine(parts.FullName, "castle_tower.sdf"), "tower");
            var source = ModelFiles.PartsIn(root.FullName);

            Assert.Equal("eye", source("eye"));
            Assert.Equal("tower", source("castle_tower"));
            Assert.Null(source("castle"));
            Assert.Null(source("castle/castle_tower"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
