using System.Reflection;
using SDFPipeline.Materials;
using SDFProject.ProgramDefinition;

namespace IDE;

/// <summary>
/// The repository's materials/ and content/ folders, embedded at build because the browser cannot read the disk. Change a file, rebuild
/// (dotnet watch does it for you) and refresh.
/// </summary>
public static class BundledContent
{
    private const string MaterialPrefix = "IDE.materials.";
    private const string ModelPrefix = "IDE.content.";

    /// <summary>A model file; <see cref="Path"/> is relative to content/, like models/chanterelle-scout.sdf.</summary>
    public sealed record Model(string Name, string Path, string Text);

    private static readonly List<string> problems = [];

    public static readonly MaterialLibrary Materials = LoadMaterials();

    public static readonly IReadOnlyList<Model> Models = Resources(ModelPrefix)
        .Select(r => new Model(System.IO.Path.GetFileNameWithoutExtension(r.path), r.path, r.text))
        .ToArray();

    /// <summary>The text of a part, content/parts/{name}.sdf, for models that use it.</summary>
    public static string? Part(string name) => Models.FirstOrDefault(m => m.Path == $"parts/{name}.sdf")?.Text;

    /// <summary>Material files that could not be read, so the IDE can say so instead of failing to start.</summary>
    public static IReadOnlyList<string> Problems => problems;

    private static MaterialLibrary LoadMaterials()
    {
        var materials = new Dictionary<string, MaterialDefinition>();
        foreach (var (path, text) in Resources(MaterialPrefix))
        {
            try
            {
                materials[MaterialFile.NameOf(path)] = MaterialFile.Read(text);
            }
            catch (Exception e)
            {
                problems.Add($"materials/{path}: {e.Message}");
            }
        }
        return MaterialLibrary.Of(materials);
    }

    private static IEnumerable<(string path, string text)> Resources(string prefix)
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            yield return (name[prefix.Length..], reader.ReadToEnd());
        }
    }
}
