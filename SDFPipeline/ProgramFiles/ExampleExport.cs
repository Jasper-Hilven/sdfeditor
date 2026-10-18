using System.Text;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

namespace SDFPipeline.ProgramFiles;

/// <summary>Writes the IDE's examples as program files, as style references for new models.</summary>
public static class ExampleExport
{
    public static IReadOnlyList<string> ExportAll(string directory)
    {
        Directory.CreateDirectory(directory);
        var initial = new SDFPartialCommandNodeProgram(
            SdfInteractiveNodeProgramConstants.GetInitialProgram(),
            new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home));

        return SDFExamples.All.Select(example =>
        {
            var path = Path.Combine(directory, FileName(example.Name));
            var program = example.Load(initial).Current.interactiveProgram.Program;
            File.WriteAllText(path, $"# {example.Name}: {example.Description}\n{SdfProgramFile.Write(program)}");
            return path;
        }).ToArray();
    }

    /// <summary>"Rotten heavy mushroom" becomes rotten-heavy-mushroom.sdf.</summary>
    public static string FileName(string exampleName)
    {
        var name = new StringBuilder();
        foreach (var c in exampleName.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                name.Append(c);
            else if (name.Length > 0 && name[^1] != '-')
                name.Append('-');
        }
        return name.ToString().TrimEnd('-') + SdfProgramFile.Extension;
    }
}
