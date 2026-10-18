namespace SDFPipeline.ProgramFiles;

/// <summary>A file that cannot be read, pointing at the line that is wrong.</summary>
public sealed class SdfProgramFileException(int line, string problem) : Exception($"Line {line}: {problem}")
{
    public int Line { get; } = line;

    public string Problem { get; } = problem;
}
