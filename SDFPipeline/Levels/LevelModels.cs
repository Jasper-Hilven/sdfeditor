using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFPipeline.Rendering;

namespace SDFPipeline.Levels;

/// <summary>The models a level can place: the .sdf files in content/models/ and its subfolders, by file name (castle0, amanita-scout).</summary>
public static class LevelModels
{
    /// <summary>
    /// Finds each model by name in the folder and works out its footprint from its shape's bounds, once per name; null for a name that
    /// has no file there.
    /// </summary>
    /// <exception cref="SdfProgramFileException">The model's file cannot be read.</exception>
    public static Func<string, ModelFootprint?> In(string modelsFolder)
    {
        var files = Directory.Exists(modelsFolder)
            ? ModelFiles.All(modelsFolder).GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First())
            : [];
        var footprints = new Dictionary<string, ModelFootprint?>();
        return name =>
        {
            if (footprints.TryGetValue(name, out var known))
                return known;
            ModelFootprint? footprint = null;
            if (files.TryGetValue(name, out var path) && RenderFramer.ModelBounds(SdfProgramFile.Load(path)) is { } bounds)
                footprint = new ModelFootprint(new Vector2(bounds.Min.X, bounds.Min.Z), new Vector2(bounds.Max.X, bounds.Max.Z));
            return footprints[name] = footprint;
        };
    }
}
