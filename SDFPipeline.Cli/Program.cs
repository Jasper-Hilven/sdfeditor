using System.Diagnostics;
using System.Globalization;
using SDFPipeline.Materials;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;
using SDFPipeline.Rendering;

const string usage = """
    Usage:
      sdf render [--size <pixels>]                            Renders every .sdf in the content folder, each next to its file
                                                              Materials come from the materials folder next to content
      sdf render <directory> [--size <pixels>]                Renders every .sdf in the directory and its subdirectories
      sdf render <model.sdf> [output.png] [--size <pixels>]   Renders side, top, front and perspective views to a PNG
      sdf examples <directory>                                Writes the IDE's examples as .sdf files
    """;

// Each render runs its own headless Chrome; a few at a time keeps the machine responsive.
var parallelRenders = Math.Clamp(Environment.ProcessorCount / 3, 1, 4);

return args switch
{
    ["render", .. var rest] => await Render(rest),
    ["examples", var directory] => Examples(directory),
    _ => Usage(),
};

int Usage(string? problem = null)
{
    Console.Error.WriteLine(problem is null ? usage : $"{problem}\n\n{usage}");
    return 2;
}

int Examples(string directory)
{
    var paths = ExampleExport.ExportAll(directory);
    Console.WriteLine($"Wrote {paths.Count} examples to {directory}");
    return 0;
}

async Task<int> Render(string[] arguments)
{
    var size = RenderOptions.DefaultSize;
    var paths = new List<string>();
    for (var i = 0; i < arguments.Length; i++)
    {
        if (arguments[i] == "--size" && i + 1 < arguments.Length
            && int.TryParse(arguments[++i], NumberStyles.None, CultureInfo.InvariantCulture, out size) && size is >= 64 and <= 4096)
            continue;
        if (arguments[i].StartsWith("--"))
            return Usage($"Unknown or invalid option {arguments[i]}, --size takes 64 to 4096 pixels");
        paths.Add(arguments[i]);
    }

    MaterialLibrary materials;
    try
    {
        materials = ModelFiles.FindMaterialsFolder(Directory.GetCurrentDirectory()) is { } materialsFolder
            ? MaterialFiles.LoadLibrary(materialsFolder)
            : MaterialLibrary.Empty;
    }
    catch (InvalidOperationException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }

    var options = new RenderOptions { Size = size, Materials = materials };
    switch (paths)
    {
        case []:
            var content = ModelFiles.FindContentFolder(Directory.GetCurrentDirectory());
            return content is null
                ? Usage($"No '{ModelFiles.ContentFolderName}' folder was found here or in a folder above; pass a model or directory")
                : await RenderAll(content, options);
        case [var directory] when Directory.Exists(directory):
            return await RenderAll(directory, options);
        case [var input]:
            return await RenderOne(input, Path.ChangeExtension(input, ".png"), options) ? 0 : 1;
        case [var input, var output]:
            return await RenderOne(input, output, options) ? 0 : 1;
        default:
            return Usage();
    }
}

async Task<int> RenderAll(string directory, RenderOptions options)
{
    var models = ModelFiles.All(directory);
    if (models.Count == 0)
    {
        Console.WriteLine($"There are no {SdfProgramFile.Extension} files in {directory}");
        return 0;
    }

    Console.WriteLine($"Rendering {models.Count} models in {Path.GetFullPath(directory)}");
    var stopwatch = Stopwatch.StartNew();
    var failed = 0;
    await Parallel.ForEachAsync(models, new ParallelOptions { MaxDegreeOfParallelism = parallelRenders }, async (model, _) =>
    {
        if (!await RenderOne(model, Path.ChangeExtension(model, ".png"), options))
            Interlocked.Increment(ref failed);
    });

    Console.WriteLine(failed == 0
        ? $"Rendered all {models.Count} models in {stopwatch.Elapsed.TotalSeconds:0.0} s"
        : $"{failed} of {models.Count} models failed, see above ({stopwatch.Elapsed.TotalSeconds:0.0} s)");
    return failed == 0 ? 0 : 1;
}

async Task<bool> RenderOne(string input, string output, RenderOptions options)
{
    try
    {
        var stopwatch = Stopwatch.StartNew();
        var program = SdfProgramFile.Load(input);
        var missing = ModelFiles.MaterialNames(program).Where(name => !options.Materials.Contains(name)).ToArray();
        if (missing.Length > 0)
            Console.Error.WriteLine($"{input}: no material file for {string.Join(", ", missing)}, rendered magenta");
        var rendered = await SdfRenderer.RenderToFileAsync(program, output, options);

        var bounds = rendered.Framing.Bounds is { } b
            ? string.Create(CultureInfo.InvariantCulture, $", size {b.Size.X:0.00} × {b.Size.Y:0.00} × {b.Size.Z:0.00} m")
            : ", nothing finite to frame";
        Console.WriteLine($"Rendered {output} in {stopwatch.Elapsed.TotalSeconds:0.0} s{bounds}");
        return true;
    }
    catch (SdfProgramFileException exception)
    {
        Console.Error.WriteLine($"{input}:{exception.Line}: {exception.Problem}");
        return false;
    }
    catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"{input}: {exception.Message}");
        return false;
    }
}
