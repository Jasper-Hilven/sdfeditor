using System.Diagnostics;
using System.Globalization;
using SDFPipeline.Levels;
using SDFPipeline.Materials;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;
using SDFPipeline.Rendering;
using SDFPipeline.Skies;

const string usage = """
    Usage:
      sdf render [--size <pixels>]                            Renders every .sdf in the content folder, each next to its file
                                                              Materials come from the materials folder next to content
      sdf render <directory> [--size <pixels>]                Renders every .sdf in the directory and its subdirectories
      sdf render <model.sdf> [output.png] [--size <pixels>]   Renders side, top, front and perspective views to a PNG
      Options: --light <x,y,z>   direction towards the sun, e.g. --light 0,1,-2 to light the model from behind
               --style <look>    realistic (default) or toon, the cartoon look of the Unity toon shader
               --camera <yaw,pitch,distance[,x,y,z]>
                                 the perspective view's camera: radians round the target (yaw 0 looks from +z), metres
                                 away, and the target, e.g. --camera 0,-0.06,40,0,4,0 for eye height 40 m in front
               --sky <preset>    the toon look under a sky preset of content/skies (day, night), as a level shows it
      sdf mesh [--resolution <cells>]                         Meshes every .sdf in the content folder to a .glb next to its file
      sdf mesh <directory> [--resolution <cells>]             Meshes every .sdf in the directory and its subdirectories
      sdf mesh <model.sdf> [output.glb] [--resolution <cells>]
                                                              Meshes the model and each part it uses, with per-vertex material,
                                                              thickness and occlusion for the game engine's shaders
      Options: --resolution <cells>   grid cells along the longest side of a 2 m model, 16 to 1024 (default 128); smaller
                                      models and parts get fewer cells, so fewer triangles
               --reference-size <m>   the size that gets --resolution cells (default 2)
               --into <folder>        write the .glb files into this folder instead of next to each model, keeping the
                                      subfolders they are in below the directory, e.g. <folder>/weapons/pistol.glb
               --suffix <text>        add this to each .glb's name, e.g. --suffix _512 for chanterelle-scout_512.glb
               --lods <n>             levels of detail per mesh, LOD0 included, 1 to 4 (default 1); each further level
                                      has about a third of the triangles of the one before
      sdf level <level.level> --into <folder>                 Bakes the level's ground into <folder>: a .glb per 25 m chunk with
                                                              its levels of detail, the scattered models (instances.bin) and
                                                              level.json for Unity's level builder
      sdf level <directory> --into <folder>                   Bakes every .level in the directory into <folder>/<level name>/
      sdf level --into <folder>                               The same for content/levels
      sdf unity [--resolution <cells>] [--lods <n>]           Updates Unity: meshes content/models into
                                                              Unity/Assets/Mushrooms/Generated (as sdf mesh, with these options),
                                                              then bakes content/levels into Unity/Assets/Level/Generated
      sdf examples <directory>                                Writes the IDE's examples as .sdf files
    """;

// Where `sdf unity` puts what it makes, below the folder that holds content/
const string unityModelsFolder = "Unity/Assets/Mushrooms/Generated";
const string unityLevelsFolder = "Unity/Assets/Level/Generated";

// Each render runs its own headless Chrome; a few at a time keeps the machine responsive.
var parallelRenders = Math.Clamp(Environment.ProcessorCount / 3, 1, 4);

return args switch
{
    ["render", .. var rest] => await Render(rest),
    ["mesh", .. var rest] => Mesh(rest),
    ["level", .. var rest] => Level(rest),
    ["unity", .. var rest] => Unity(rest),
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
    var lods = MeshingOptions.DefaultLodCount;
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
        if (arguments[i] == "--resolution" && i + 1 < arguments.Length)
        {
            if (!int.TryParse(arguments[++i], NumberStyles.None, CultureInfo.InvariantCulture, out resolution) || resolution is < 16 or > 1024)
                return Usage($"--resolution takes 16 to 1024 cells, not {arguments[i]}");
            continue;
        }
        if (arguments[i] == "--lods" && i + 1 < arguments.Length)
        {
            if (!int.TryParse(arguments[++i], NumberStyles.None, CultureInfo.InvariantCulture, out lods) || lods is < 1 or > MeshingOptions.MaxLodCount)
                return Usage($"--lods takes 1 to {MeshingOptions.MaxLodCount} levels, not {arguments[i]}");
            continue;
        }
        if (arguments[i] == "--reference-size" && i + 1 < arguments.Length)
        {
            if (!float.TryParse(arguments[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out referenceSize) || referenceSize <= 0)
                return Usage($"--reference-size takes a size in metres, not {arguments[i]}");
            continue;
        }
        if (arguments[i].StartsWith("--"))
            return Usage($"Unknown or invalid option {arguments[i]}, --resolution takes 16 to 1024 cells, --reference-size a size in metres, --lods 1 to {MeshingOptions.MaxLodCount} levels");
        paths.Add(arguments[i]);
    }

    if (LoadMaterials() is not { } materials)
        return 1;
    var options = new MeshingOptions { Resolution = resolution, ReferenceSize = referenceSize, LodCount = lods, Materials = materials };
    // Where each model's mesh goes: next to it, or into the --into folder under the same subfolder it has below the meshed directory
    // (content/models/weapons/pistol.sdf meshed from content/models goes to <into>/weapons/), with the --suffix after its name
    Func<string, string> OutputsUnder(string? directory) => model =>
    {
        var folder = Path.GetDirectoryName(model) ?? "";
        var target = into is null ? folder
            : directory is null ? into
            : Path.GetRelativePath(directory, folder) is var below && below != "." ? Path.Combine(into, below) : into;
        return Path.Combine(target, Path.GetFileNameWithoutExtension(model) + suffix + GlbWriter.Extension);
    };
    switch (paths)
    {
        case []:
            var content = ModelFiles.FindContentFolder(Directory.GetCurrentDirectory());
            return content is null
                ? Usage($"No '{ModelFiles.ContentFolderName}' folder was found here or in a folder above; pass a model or directory")
                : MeshAll(content, options, OutputsUnder(content));
        case [var directory] when Directory.Exists(directory):
            return MeshAll(directory, options, OutputsUnder(directory));
        case [var input]:
            return MeshOne(input, OutputsUnder(null)(input), options) ? 0 : 1;
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
        var settings = SdfProgramFile.LoadSettings(input);
        var meshOptions = settings.SurfacePart is { } part ? options with { SurfaceOf = SdfProgramFile.LoadPart(input, part) } : options;
        var meshed = ModelMesher.Mesh(program, meshOptions);
        var model = meshed with
        {
            Settings = settings,
            PartSettings = SdfProgramFile.LoadPartSettings(input, meshed.Meshes.Select(m => m.Part).OfType<string>(), settings),
        };
        foreach (var warning in model.Warnings)
            Console.Error.WriteLine($"{input}: {warning}");
        if (model.Meshes.Count == 0)
        {
            Console.Error.WriteLine($"{input}: there is no finite surface to mesh");
            return false;
        }
        GlbWriter.Save(output, model);

        var cells = model.Grid is { } grid ? string.Create(CultureInfo.InvariantCulture, $", {grid.CellSize * 1000:0.#} mm cells") : "";
        var modelDetail = string.Create(CultureInfo.InvariantCulture, $", detail {model.Detail:0.###}");
        Console.WriteLine($"Meshed {output} in {stopwatch.Elapsed.TotalSeconds:0.0} s{cells}{modelDetail}");
        var nameWidth = model.Meshes.Max(m => GlbWriter.LodName(m.Name, m.Lods.Count).Length);
        foreach (var mesh in model.Meshes)
        {
            var statistics = mesh.Statistics;
            // A part's own Detail multiplies the model's, so the detail is shown where it differs from the one above
            var detail = mesh.Detail != model.Detail ? string.Create(CultureInfo.InvariantCulture, $", detail {mesh.Detail:0.###}") : "";
            // A part meshed once for several uses says how often it is drawn
            var instances = mesh.Instances.Count > 0 ? $", {mesh.Instances.Count} instances" : "";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {mesh.Name.PadRight(nameWidth)}  {statistics.ContouredTriangles,8:N0} → {statistics.SimplifiedTriangles,7:N0} triangles, {statistics.Vertices,7:N0} vertices, {statistics.Time.TotalSeconds:0.00} s{detail}{instances}"));
            // Each level of detail is simplified on from the one before, so its time is on top of that level's
            for (var level = 1; level <= mesh.Lods.Count; level++)
            {
                var lod = mesh.Lods[level - 1];
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {GlbWriter.LodName(mesh.Name, level).PadRight(nameWidth)}  {"",8} → {lod.TriangleCount,7:N0} triangles, {lod.Statistics.Vertices,7:N0} vertices, +{lod.Statistics.Time.TotalSeconds:0.00} s, within {lod.MaxError * 1000:0.#} mm"));
            }
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

int Level(string[] arguments)
{
    string? into = null;
    var paths = new List<string>();
    for (var i = 0; i < arguments.Length; i++)
    {
        if (arguments[i] == "--into" && i + 1 < arguments.Length)
        {
            into = arguments[++i];
            continue;
        }
        if (arguments[i].StartsWith("--"))
            return Usage($"Unknown or invalid option {arguments[i]}, sdf level takes --into <folder>");
        paths.Add(arguments[i]);
    }
    if (into is null)
        return Usage("sdf level needs --into <folder>, where the baked level goes");

    if (LoadMaterials() is not { } materials)
        return 1;
    switch (paths)
    {
        case []:
            var content = ModelFiles.FindContentFolder(Directory.GetCurrentDirectory());
            return content is null
                ? Usage($"No '{ModelFiles.ContentFolderName}' folder was found here or in a folder above; pass a level or directory")
                : BakeAll(Path.Combine(content, LevelFile.FolderName), into, materials);
        case [var directory] when Directory.Exists(directory):
            return BakeAll(directory, into, materials);
        case [var input]:
            return BakeOne(input, into, materials) ? 0 : 1;
        default:
            return Usage();
    }
}

int BakeAll(string directory, string into, MaterialLibrary materials)
{
    var levels = Directory.Exists(directory) ? LevelFile.All(directory) : [];
    if (levels.Count == 0)
    {
        Console.WriteLine($"There are no {LevelFile.Extension} files in {directory}");
        return 0;
    }
    var names = levels.GroupBy(LevelFile.NameOf).FirstOrDefault(g => g.Count() > 1);
    if (names is not null)
    {
        Console.Error.WriteLine($"Two levels are named '{names.Key}' ({string.Join(", ", names)}); level names must be unique");
        return 1;
    }

    var stopwatch = Stopwatch.StartNew();
    var failed = levels.Count(level => !BakeOne(level, Path.Combine(into, LevelFile.NameOf(level)), materials));
    Console.WriteLine(failed == 0
        ? $"Baked all {levels.Count} levels in {stopwatch.Elapsed.TotalSeconds:0.0} s"
        : $"{failed} of {levels.Count} levels failed, see above ({stopwatch.Elapsed.TotalSeconds:0.0} s)");
    return failed == 0 ? 0 : 1;
}

bool BakeOne(string input, string into, MaterialLibrary materials)
{
    try
    {
        var stopwatch = Stopwatch.StartNew();
        var level = LevelFile.Load(input);
        var content = ModelFiles.FindContentFolder(Path.GetDirectoryName(Path.GetFullPath(input))!);
        var models = content is null ? null : LevelModels.In(Path.Combine(content, "models"));
        var modelExists = content is null ? null : LevelModels.Exist(Path.Combine(content, "models"));
        // The sky's presets are content/skies/; without a content folder the level is baked without a sky
        var sky = content is null ? null : SkyFile.Resolve(level.Sky, SkyFile.PresetsIn(Path.Combine(content, SkyFile.FolderName)));
        var baked = LevelBaker.Bake(level, LevelFile.NameOf(input), materials, into, models, modelExists, sky);
        var layout = baked.Layout;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Baked {input} into {into} in {stopwatch.Elapsed.TotalSeconds:0.0} s: {baked.ChunkFiles.Count} chunks of {layout.ChunkSize:0.#} m on a {layout.CountX} × {layout.CountZ} grid"));
        for (var lod = 0; lod < baked.Triangles.Length; lod++)
        {
            var detail = LevelBaker.Details[lod];
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {GlbWriter.LodName(LevelBaker.GroundMeshName, lod),-12}  {layout.ChunkSize / detail.Cells:0.###} m grid  → {LevelBaker.Count(baked.Triangles[lod]),9} triangles over all chunks ({LevelBaker.Count(baked.SkirtTriangles[lod])} in skirts), within {baked.MaxErrors[lod] * 100:0.#} cm"));
        }
        if (baked.Regions.Count > 0)
            Console.WriteLine($"  regions       {string.Join(", ", baked.Regions.Select(r => $"{r.Name} ({r.Shapes.Count} shape{(r.Shapes.Count == 1 ? "" : "s")})"))} in {RegionMap.FileName}");
        if (sky is not null)
            Console.WriteLine($"  sky           {sky.Preset}{(level.Sky?.Overridden.Count > 0 ? $", overriding its {string.Join(", ", level.Sky.Overridden)}" : "")}");
        foreach (var placed in baked.Placements)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  placed        {placed.Model} at ({placed.Position.X:0.##}, {placed.Position.Y:0.##}, {placed.Position.Z:0.##}), yaw {placed.Yaw:0.#}°{(placed.Scale != 1 ? ", scale " + placed.Scale.ToString("0.##", CultureInfo.InvariantCulture) : "")}"));
        var scattered = baked.Scattered;
        for (var layer = 0; layer < scattered.LayerCounts.Count; layer++)
        {
            var scatter = level.Scatters[layer];
            var counts = scatter.Entries.Select(e => e.Model).Select(m => scattered.Prototypes.ToList().FindIndex(p => p.Model == m))
                .Select(p => $"{scattered.Prototypes[p].Model} {LevelBaker.Count(scattered.Instances.Count(i => i.Prototype == p))}");
            Console.WriteLine($"  scattered     {LevelBaker.Count(scattered.LayerCounts[layer])} in group {scatter.Group}: {string.Join(", ", counts)}");
        }
        if (scattered.Instances.Count > 0)
            Console.WriteLine($"  instances     {LevelBaker.Count(scattered.Instances.Count)} in {InstanceFile.FileName}");
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

int Unity(string[] arguments)
{
    var content = ModelFiles.FindContentFolder(Directory.GetCurrentDirectory());
    if (content is null)
        return Usage($"No '{ModelFiles.ContentFolderName}' folder was found here or in a folder above");
    var root = Path.GetDirectoryName(content)!;
    if (arguments.FirstOrDefault(a => a.StartsWith("--") && a is not ("--resolution" or "--lods")) is { } unknown)
        return Usage($"sdf unity takes --resolution and --lods, not {unknown}");

    // Models first: the levels place model prefabs, which Unity builds from the meshes before it builds the level scenes
    var models = Mesh([Path.Combine(content, "models"), "--into", Path.Combine(root, unityModelsFolder), .. arguments]);
    var levels = Level([Path.Combine(content, LevelFile.FolderName), "--into", Path.Combine(root, unityLevelsFolder)]);
    return models != 0 ? models : levels;
}

async Task<int> Render(string[] arguments)
{
    var size = RenderOptions.DefaultSize;
    V3? light = null;
    var style = RenderStyle.Realistic;
    RenderView? view = null;
    string? skyName = null;
    var paths = new List<string>();
    for (var i = 0; i < arguments.Length; i++)
    {
        if (arguments[i] == "--sky" && i + 1 < arguments.Length)
        {
            skyName = arguments[++i];
            style = RenderStyle.Toon;
            continue;
        }
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
        if (arguments[i] == "--camera" && i + 1 < arguments.Length)
        {
            var components = arguments[++i].Split(',');
            if (components.Length is not (3 or 6) || !components.All(c => float.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                return Usage($"--camera takes yaw,pitch,distance or yaw,pitch,distance,x,y,z, not {arguments[i]}");
            var values = components.Select(c => float.Parse(c, CultureInfo.InvariantCulture)).ToArray();
            view = new RenderView(values[0], values[1], values[2], values.Length == 6 ? values[3..] : null);
            continue;
        }
        if (arguments[i] == "--size" && i + 1 < arguments.Length)
        {
            if (!int.TryParse(arguments[++i], NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 64 or > 4096)
                return Usage($"--size takes 64 to 4096 pixels, not {arguments[i]}");
            continue;
        }
        if (arguments[i].StartsWith("--"))
            return Usage($"Unknown or invalid option {arguments[i]}, --size takes 64 to 4096 pixels");
        paths.Add(arguments[i]);
    }

    if (LoadMaterials() is not { } materials)
        return 1;

    SkyDefinition? sky = null;
    if (skyName is not null)
    {
        var skies = ModelFiles.FindContentFolder(Directory.GetCurrentDirectory()) is { } contentFolder ? Path.Combine(contentFolder, SkyFile.FolderName) : null;
        var skyPath = skies is null ? null : Path.Combine(skies, skyName + SkyFile.Extension);
        if (skyPath is null || !File.Exists(skyPath))
            return Usage($"--sky takes a preset of content/{SkyFile.FolderName}, like night; there is no {skyName}{SkyFile.Extension}");
        try
        {
            sky = SkyFile.Load(skyPath);
        }
        catch (SdfProgramFileException exception)
        {
            Console.Error.WriteLine($"{skyPath}:{exception.Line}: {exception.Problem}");
            return 1;
        }
    }

    var options = new RenderOptions { Size = size, Materials = materials, LightDirection = light, Style = style, View = view, Sky = sky };
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
