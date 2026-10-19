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

    public const string PartsFolderName = "parts";

    /// <summary>
    /// Parts are the program files in content/parts/, named after their files. A kit of parts may have a folder of its own
    /// (content/parts/castle/castle_tower.sdf is the part castle_tower): a file directly in content/parts/ comes first, then the
    /// subfolders in name order.
    /// </summary>
    public static PartSource PartsIn(string contentFolder) => name =>
    {
        if (!FileSyntax.IsIdentifier(name))
            return null;
        var folder = Path.Combine(contentFolder, PartsFolderName);
        var path = Path.Combine(folder, name + SdfProgramFile.Extension);
        if (!File.Exists(path))
            path = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, name + SdfProgramFile.Extension, SearchOption.AllDirectories).Order(StringComparer.Ordinal).FirstOrDefault() ?? path
                : path;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    };

    public static IReadOnlyList<string> All(string directory) =>
        Directory.EnumerateFiles(directory, "*" + SdfProgramFile.Extension, SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>The names of the materials a program refers to, each once.</summary>
    public static IReadOnlyList<string> MaterialNames(SDFProject.ProgramDefinition.SDFProgram program) =>
        program.pointers.SDFEvaluablePointers.Values.OfType<SDFProject.ProgramDefinition.Material>().Select(m => m.material).Distinct().Order().ToArray();
}
