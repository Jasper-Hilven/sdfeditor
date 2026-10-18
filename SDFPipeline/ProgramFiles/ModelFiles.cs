namespace SDFPipeline.ProgramFiles;

/// <summary>Where models and materials live: the repository's content and materials folders, side by side.</summary>
public static class ModelFiles
{
    public const string ContentFolderName = "content";

    public const string MaterialsFolderName = "materials";

    /// <summary>The content folder in <paramref name="start"/> or the closest folder above it that has one.</summary>
    public static string? FindContentFolder(string start) => FindFolder(start, ContentFolderName);

    /// <summary>The materials folder in <paramref name="start"/> or the closest folder above it that has one.</summary>
    public static string? FindMaterialsFolder(string start) => FindFolder(start, MaterialsFolderName);

    private static string? FindFolder(string start, string name)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var folder = Path.Combine(directory.FullName, name);
            if (Directory.Exists(folder))
                return folder;
        }
        return null;
    }

    public static IReadOnlyList<string> All(string directory) =>
        Directory.EnumerateFiles(directory, "*" + SdfProgramFile.Extension, SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>The names of the materials a program refers to, each once.</summary>
    public static IReadOnlyList<string> MaterialNames(SDFProject.ProgramDefinition.SDFProgram program) =>
        program.pointers.SDFEvaluablePointers.Values.OfType<SDFProject.ProgramDefinition.Material>().Select(m => m.material).Distinct().Order().ToArray();
}
