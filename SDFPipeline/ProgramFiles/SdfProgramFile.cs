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

    /// <summary>Loads a part by name from content/parts/ of the content folder the model at <paramref name="path"/> is in.</summary>
    /// <exception cref="InvalidOperationException">There is no such part.</exception>
    public static SDFProgram LoadPart(string path, string name)
    {
        var content = ModelFiles.FindContentFolder(Path.GetDirectoryName(Path.GetFullPath(path))!)
                      ?? throw new InvalidOperationException($"Part '{name}' cannot be found, because {path} is not in a content folder");
        var parts = ModelFiles.PartsIn(content);
        return Read(parts(name) ?? throw new InvalidOperationException($"There is no part '{name}' in {Path.Combine(content, ModelFiles.PartsFolderName)}"), parts);
    }

    /// <summary>The model's settings, like its collider (<see cref="ModelSettings"/>), which are not part of its shape.</summary>
    public static ModelSettings LoadSettings(string path) => ModelSettings.Load(path);

    public static void Save(string path, SDFProgram program) => File.WriteAllText(path, Write(program));
}
