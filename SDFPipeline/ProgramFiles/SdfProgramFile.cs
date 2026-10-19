using SDFProject.ProgramDefinition;

namespace SDFPipeline.ProgramFiles;

/// <summary>The text of the part with this name, or null when there is no such part.</summary>
public delegate string? PartSource(string name);

/// <summary>
/// The text format models are saved in, on disk and in git: the start of the graphics pipeline.
/// <code>
/// format 1
///
/// capRadius = 0.7
/// SmoothUnion smoothness=0.08
///     Color color=(0.9, 0.2, 0.2)
///         Translate translation=(0, 0.6, 0)
///             CutSphere radius=$capRadius cutHeight=0.15
///     CappedCylinder radius=0.16 height=0.4
/// </code>
/// </summary>
public static class SdfProgramFile
{
    public const int FormatVersion = 1;

    public const string Extension = ".sdf";

    public static string Write(SDFProgram program) => SdfProgramFileWriter.Write(program);

    /// <param name="parts">Finds the text of a part by name; without it, a file that uses parts cannot be read.</param>
    /// <exception cref="SdfProgramFileException">The text is not a valid program; the exception names the line.</exception>
    public static SDFProgram Read(string text, PartSource? parts = null) => SdfProgramFileReader.Read(text, parts);

    /// <summary>Loads a model, finding its parts in content/parts/ of the content folder the file is in.</summary>
    public static SDFProgram Load(string path) =>
        Read(File.ReadAllText(path), ModelFiles.FindContentFolder(Path.GetDirectoryName(Path.GetFullPath(path))!) is { } content
            ? ModelFiles.PartsIn(content)
            : null);

    /// <summary>The model's settings, like its collider (<see cref="ModelSettings"/>), which are not part of its shape.</summary>
    public static ModelSettings LoadSettings(string path) => ModelSettings.Load(path);

    /// <summary>
    /// The settings of each of <paramref name="parts"/> whose file has settings lines, read over the model's <paramref name="settings"/>;
    /// the parts are found in content/parts/ of the content folder the model is in.
    /// </summary>
    public static IReadOnlyDictionary<string, ModelSettings> LoadPartSettings(string path, IEnumerable<string> parts, ModelSettings settings)
    {
        var source = ModelFiles.FindContentFolder(Path.GetDirectoryName(Path.GetFullPath(path))!) is { } content ? ModelFiles.PartsIn(content) : null;
        var result = new Dictionary<string, ModelSettings>();
        foreach (var part in parts.Distinct())
            if (source?.Invoke(part) is { } text && ModelSettings.HasSettings(text))
                result[part] = ModelSettings.Read(text, settings);
        return result;
    }

    public static void Save(string path, SDFProgram program) => File.WriteAllText(path, Write(program));
}
