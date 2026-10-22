using System.Globalization;
using System.Text.RegularExpressions;
using SDFPipeline.ProgramFiles;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 31.3: 42's room is placed by content/scenes/home.txt (built into Home.unity by HomeBuilder, in the editor only). The home
/// models belong to that file alone: no battle level places one, and the file places nothing but home models, the scout characters and
/// the scouts' hands, with every model inside the room.
/// </summary>
public class HomePlacementTests
{
    private static string Content()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    private static string[] HomeModels() =>
        Directory.GetFiles(Path.Combine(Content(), "models", "home"), "*.sdf").Select(Path.GetFileNameWithoutExtension).OfType<string>().ToArray();

    /// <summary>The non-comment lines of the placement file, with their numbers.</summary>
    private static IEnumerable<(int Line, string Text)> PlacementLines() =>
        File.ReadAllLines(Path.Combine(Content(), "scenes", "home.txt"))
            .Select((text, index) => (Line: index + 1, Text: text.Trim()))
            .Where(l => l.Text.Length > 0 && l.Text[0] != '#');

    private static float[] Vec(string text, string key)
    {
        var match = Regex.Match(text, key + @"=\(([^)]*)\)");
        Assert.True(match.Success, $"{key}=(x, y, z) missing in: {text}");
        return match.Groups[1].Value.Split(',').Select(n => float.Parse(n, CultureInfo.InvariantCulture)).ToArray();
    }

    [Fact]
    public void NoLevelPlacesAHomeModel()
    {
        var home = HomeModels();
        Assert.NotEmpty(home);
        var levels = Directory.GetFiles(Path.Combine(Content(), "levels"), "*.level");
        Assert.NotEmpty(levels);
        foreach (var level in levels)
        {
            var lines = File.ReadAllLines(level);
            for (var i = 0; i < lines.Length; i++)
                foreach (var word in Regex.Split(Regex.Replace(lines[i], "#.*", ""), @"[\s=(),""]+").Where(w => w.Length > 0))
                    Assert.False(home.Contains(word), $"{Path.GetFileName(level)} line {i + 1} names the home model '{word}'");
        }
    }

    [Fact]
    public void HomePlacementReferencesOnlyHomeModelsAndScouts()
    {
        var home = HomeModels();
        var models = new List<string>();
        var actors = new List<string>();
        foreach (var (line, text) in PlacementLines())
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (words[0])
            {
                case "model":
                    Assert.True(home.Contains(words[1]), $"home.txt line {line}: '{words[1]}' is not a model in content/models/home");
                    models.Add(words[1]);
                    break;
                case "actor":
                    // The scouts and their hands (amanita-scout, chanterelle-scout-hand0 ...), nothing else
                    Assert.Matches(@"^(amanita|chanterelle)-scout(-hand[01])?$", words[1]);
                    actors.Add(Regex.Match(text, "name=\"([^\"]*)\"").Groups[1].Value);
                    break;
                case "format":
                case "light":
                case "camera":
                case "ambient":
                    break;
                default:
                    Assert.Fail($"home.txt line {line}: unknown command '{words[0]}'");
                    break;
            }
        }

        // The whole room is placed; Prime's crown is not (it is put on the chanterelle scout by the capture)
        foreach (var model in home.Where(m => m != "prime-crown"))
            Assert.Contains(model, models);
        Assert.DoesNotContain("prime-crown", models);

        // The actors 31.4 looks for, by name: 42, Mushroom 7 and four hands
        Assert.Contains("Mushroom 42", actors);
        Assert.Contains("Mushroom 7", actors);
        Assert.Equal(4, actors.Count(a => a.StartsWith("Chanterelle Hand ")));
        Assert.Equal(actors.Count, actors.Distinct().Count());
    }

    [Fact]
    public void HomeModelsStandInsideTheRoom()
    {
        // The shell: floor x -2..2, z -1.5..1.5, walls 2.2 m high (window parts and the backdrop reach a little behind the back wall)
        foreach (var (_, text) in PlacementLines().Where(l => l.Text.StartsWith("model ")))
        {
            var at = Vec(text, "at");
            Assert.InRange(at[0], -2, 2);
            Assert.InRange(at[1], 0, 2.2f);
            Assert.InRange(at[2], -1.8f, 1.5f);
        }
    }

    [Fact]
    public void HomeStageIsNotInTheBuild()
    {
        // HomeBuilder saves Home.unity but never adds it to the build settings' scene list
        var root = Path.GetDirectoryName(Content())!;
        var builder = File.ReadAllText(Path.Combine(root, "Unity", "Assets", "Story", "Editor", "HomeBuilder.cs"));
        Assert.DoesNotContain("EditorBuildSettings", builder);
        var settings = Path.Combine(root, "Unity", "ProjectSettings", "EditorBuildSettings.asset");
        if (File.Exists(settings))
            Assert.DoesNotContain("Home.unity", File.ReadAllText(settings));
    }
}
