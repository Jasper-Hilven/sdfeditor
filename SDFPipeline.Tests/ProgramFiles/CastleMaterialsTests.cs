using System.Text.RegularExpressions;
using SDFPipeline.Materials;
using SDFPipeline.ProgramFiles;

namespace SDFPipeline.Tests.ProgramFiles;

public class CastleMaterialsTests
{
    [Fact]
    public void Castle0_UsesNoStonesPattern_InAnyOfItsParts()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var patterns = MaterialFiles.LoadPatterns(Path.Combine(content, "..", "materials"));
        var partsFolder = Path.Combine(content, ModelFiles.PartsFolderName);

        var parts = new SortedSet<string>();
        var materials = new SortedSet<string>();
        var pending = new Stack<string>();
        pending.Push(Path.Combine(content, "models", "buildings", "castle0.sdf"));
        while (pending.Count > 0)
        {
            var text = File.ReadAllText(pending.Pop());
            foreach (Match m in Regex.Matches(text, @"^\s*Material\s+material=(\S+)", RegexOptions.Multiline))
                materials.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"^\s*Part\s+part=(\S+)", RegexOptions.Multiline))
                if (parts.Add(m.Groups[1].Value))
                    pending.Push(Directory.EnumerateFiles(partsFolder, m.Groups[1].Value + ".sdf", SearchOption.AllDirectories).Single());
        }

        // The walls are grown of the towers' stem flesh: no brick or stone material, and no glowing windows
        Assert.Contains("castle_wall_bay", parts);
        Assert.Contains("castle_stem", materials);
        Assert.DoesNotContain("castle_wall", materials);
        Assert.DoesNotContain("castle_window_frame", parts);
        foreach (var material in materials)
            Assert.False(patterns.TryGetValue(material, out var p) && p.Kind == MaterialPatternKind.Stones, $"{material} has a stones pattern");
    }
}
