using SDFProject.ProgramDefinition;

namespace SDFPipeline.ProgramFiles;

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

    /// <exception cref="SdfProgramFileException">The text is not a valid program; the exception names the line.</exception>
    public static SDFProgram Read(string text) => SdfProgramFileReader.Read(text);

    public static SDFProgram Load(string path) => Read(File.ReadAllText(path));

    public static void Save(string path, SDFProgram program) => File.WriteAllText(path, Write(program));
}
