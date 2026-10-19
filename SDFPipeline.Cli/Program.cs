using System.Diagnostics;
using System.Globalization;
using SDFPipeline.Materials;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;
using SDFPipeline.Rendering;

const string usage = """
    Usage:
      sdf render [--size <pixels>]                            Renders every .sdf in the content folder, each next to its file
                                                              Materials come from the materials folder next to content
      sdf render <directory> [--size <pixels>]                Renders every .sdf in the directory and its subdirectories
      sdf render <model.sdf> [output.png] [--size <pixels>]   Renders side, top, front and perspective views to a PNG
      Options: --light <x,y,z>   direction towards the sun, e.g. --light 0,1,-2 to light the model from behind
               --style <look>    realistic (default) or toon, the cartoon look of the Unity toon shader
      sdf mesh [--resolution <cells>]                         Meshes every .sdf in the content folder to a .glb next to its file
      sdf mesh <directory> [--resolution <cells>]             Meshes every .sdf in the directory and its subdirectories
      sdf mesh <model.sdf> [output.glb] [--resolution <cells>]
                                                              Meshes the model and each part it uses, with per-vertex material,
                                                              thickness and occlusion for the game engine's shaders
      Options: --resolution <cells>   grid cells along the longest side of a 2 m model, 16 to 1024 (default 128); smaller
                                      models and parts get fewer cells, so fewer triangles
               --reference-size <m>   the size that gets --resolution cells (default 2)
               --into <folder>        write the .glb files into this folder instead of next to each model
               --suffix <text>        add this to each .glb's name, e.g. --suffix _512 for chanterelle-scout_512.glb
      sdf examples <directory>                                Writes the IDE's examples as .sdf files
    """;

// Each render runs its own headless Chrome; a few at a time keeps the machine responsive.
var parallelRenders = Math.Clamp(Environment.ProcessorCount / 3, 1, 4);

return args switch
{
    ["render", .. var rest] => await Render(rest),
    ["mesh", .. var rest] => Mesh(rest),
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

MaterialLibrary? LoadMaterials()
{
    try
    {
        return ModelFiles.FindMaterialsFolder(Directory.GetCurrentDirectory()) is { } materialsFolder
            ? MaterialFiles.LoadLibrary(materialsFolder)
            : MaterialLibrary.Empty;
    }
    catch (InvalidOperationException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return null;
    }
}

int Mesh(string[] arguments)
{
    var resolution = MeshingOptions.DefaultResolution;
    var referenceSize = MeshingOptions.DefaultReferenceSize;
    string? into = null;
    var suffix = "";
    var paths = new List<string>();
    for (var i = 0; i < arguments.Length; i++)
    {
        if (arguments[i] == "--into" && i + 1 < arguments.Length)
        {
            into = arguments[++i];
            continue;
        }
        if (arguments[i] == "--suffix" && i + 1 < arguments.Length)
        {
            suffix = arguments[++i];
            continue;
        }
        if (arguments[i] == "--resolution" && i + 1 < arguments.Length
            && int.TryParse(arguments[++i], NumberStyles.None, CultureInfo.InvariantCulture, out resolution) && resolution is >= 16 and <= 1024)
            continue;
        if (arguments[i] == "--reference-size" && i + 1 < arguments.Length
            && float.TryParse(arguments[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out referenceSize) && referenceSize > 0)
            continue;
        if (arguments[i].StartsWith("--"))
            return Usage($"Unknown or invalid option {arguments[i]}, --resolution takes 16 to 1024 cells, --reference-size a size in metres");
        paths.Add(arguments[i]);
    }

    if (LoadMaterials() is not { } materials)
        return 1;
    var options = new MeshingOptions { Resolution = resolution, ReferenceSize = referenceSize, Materials = materials };
    // Where each model's mesh goes: next to it, or into the --into folder, with the --suffix after its name
    string OutputOf(string model) =>
        Path.Combine(into ?? Path.GetDirectoryName(model) ?? "", Path.GetFileNameWithoutExtension(model) + suffix + GlbWriter.Extension);
    switch (paths)
    {
        case []:
            var content = ModelFiles.FindContentFolder(Directory.GetCurrentDirectory());
            return content is null
                ? Usage($"No '{ModelFiles.ContentFolderName}' folder was found here or in a folder above; pass a model or directory")
                : MeshAll(content, options, OutputOf);
        case [var directory] when Directory.Exists(directory):
            return MeshAll(directory, options, OutputOf);
        case [var input]:
            return MeshOne(input, OutputOf(input), options) ? 0 : 1;
        case [var input, var output]:
            return MeshOne(input, output, options) ? 0 : 1;
        default:
            return Usage();
    }
}

int MeshAll(string directory, MeshingOptions options, Func<string, string> outputOf)
{
    var models = ModelFiles.All(directory);
    if (models.Count == 0)
    {
        Console.WriteLine($"There are no {SdfProgramFile.Extension} files in {directory}");
        return 0;
    }

    // Each model is meshed with all cores, so one at a time
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"Meshing {models.Count} models in {Path.GetFullPath(directory)} at {options.Resolution} cells per {options.ReferenceSize} m"));
    var stopwatch = Stopwatch.StartNew();
    var failed = models.Count(model => !MeshOne(model, outputOf(model), options));
    Console.WriteLine(failed == 0
        ? $"Meshed all {models.Count} models in {stopwatch.Elapsed.TotalSeconds:0.0} s"
        : $"{failed} of {models.Count} models failed, see above ({stopwatch.Elapsed.TotalSeconds:0.0} s)");
    return failed == 0 ? 0 : 1;
}

bool MeshOne(string input, string output, MeshingOptions options)
{
    try
    {
        var stopwatch = Stopwatch.StartNew();
        var program = SdfProgramFile.Load(input);
        var missing = ModelFiles.MaterialNames(program).Where(name => !options.Materials.Contains(name)).ToArray();
        if (missing.Length > 0)
            Console.Error.WriteLine($"{input}: no material file for {string.Join(", ", missing)}, meshed with the magenta stand-in");
        var model = ModelMesher.Mesh(program, options);
        if (model.Meshes.Count == 0)
        {
            Console.Error.WriteLine($"{input}: there is no finite surface to mesh");
            return false;
        }
        GlbWriter.Save(output, model);

        var cells = model.Grid is { } grid ? string.Create(CultureInfo.InvariantCulture, $", {grid.CellSize * 1000:0.#} mm cells") : "";
        Console.WriteLine($"Meshed {output} in {stopwatch.Elapsed.TotalSeconds:0.0} s{cells}");
        var nameWidth = model.Meshes.Max(m => m.Name.Length);
        foreach (var mesh in model.Meshes)
        {
            var statistics = mesh.Statistics;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {mesh.Name.PadRight(nameWidth)}  {statistics.ContouredTriangles,8:N0} → {statistics.SimplifiedTriangles,7:N0} triangles, {statistics.Vertices,7:N0} vertices, {statistics.Time.TotalSeconds:0.00} s"));
        }
        return true;
    }
    catch (SdfProgramFileException exception)
    {
        Console.Error.WriteLine($"{input}:{exception.Line}: {exception.Problem}");
        return false;
    }
    catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"{input}: {exception.Message}");
        return false;
    }
}

async Task<int> Render(string[] arguments)
{
    var size = RenderOptions.DefaultSize;
    V3? light = null;
    var style = RenderStyle.Realistic;
    var paths = new List<string>();
    for (var i = 0; i < arguments.Length; i++)
    {
        if (arguments[i] == "--style" && i + 1 < arguments.Length)
        {
            if (!Enum.TryParse(arguments[++i], ignoreCase: true, out style) || !Enum.IsDefined(style))
                return Usage($"--style takes realistic or toon, not {arguments[i]}");
            continue;
        }
        if (arguments[i] == "--light" && i + 1 < arguments.Length)
        {
            var components = arguments[++i].Split(',');
            if (components.Length != 3 || !components.All(c => float.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                return Usage($"--light takes a direction like 0,1,-2, not {arguments[i]}");
            var xyz = components.Select(c => float.Parse(c, CultureInfo.InvariantCulture)).ToArray();
            light = new V3(xyz[0], xyz[1], xyz[2]);
            continue;
        }
        if (arguments[i] == "--size" && i + 1 < arguments.Length
            && int.TryParse(arguments[++i], NumberStyles.None, CultureInfo.InvariantCulture, out size) && size is >= 64 and <= 4096)
            continue;
        if (arguments[i].StartsWith("--"))
            return Usage($"Unknown or invalid option {arguments[i]}, --size takes 64 to 4096 pixels");
        paths.Add(arguments[i]);
    }

    if (LoadMaterials() is not { } materials)
        return 1;

    var options = new RenderOptions { Size = size, Materials = materials, LightDirection = light, Style = style };
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
