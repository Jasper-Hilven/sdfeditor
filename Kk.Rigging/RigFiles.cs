using System.Numerics;
using SDFPipeline.Materials;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;

namespace Kk.Rigging;

/// <summary>
/// What <c>sdf mesh</c>, <c>sdf render</c> and <c>sdf unity</c> need to take a format 2 (rigged) model file like any other: telling it
/// apart, meshing it to a skinned .glb, and giving the rest pose as a format 1 program to render. Ported with the rigging from
/// BattleRoyale's <c>kk mesh</c> (ticket 43); the CLI keeps this pipeline's own commands.
/// </summary>
public static class RigFiles
{
    /// <summary>Whether the model file is format 2: its first line that is neither blank nor a comment is <c>format 2</c>.</summary>
    public static bool IsRigged(string path)
    {
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            return IsFormat2Line(line);
        }
        return false;
    }

    /// <summary>Whether <paramref name="line"/> (trimmed) is <c>format 2</c>.</summary>
    public static bool IsFormat2Line(string line)
    {
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0] == "format" && parts[1] == "2";
    }

    /// <summary>Reads a format 2 model file; errors the file has are thrown as <see cref="InvalidOperationException"/> listing them.</summary>
    public static RigModel Load(string path)
    {
        RigModel model;
        try
        {
            model = RigModel.Load(path);
        }
        catch (RigFormatException e)
        {
            throw new InvalidOperationException($"{path}:{e.Line}: {e.Problem}");
        }
        if (model.HasErrors)
            throw new InvalidOperationException($"{path} has errors:\n" + string.Join("\n", model.Issues.Where(i => i.IsError).Select(i => $"  {i}")));
        return model;
    }

    /// <summary>The model's rest pose as a format 1 program (what <c>sdf render</c> draws), parts read from <c>content/parts</c>.</summary>
    public static SDFProgram LoadRestProgram(string path)
    {
        var model = Load(path);
        var content = ModelFiles.FindContentFolder(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return SdfProgramFile.Read(model.RestProgram(), content is not null ? ModelFiles.PartsIn(content) : null);
    }

    /// <summary>
    /// Meshes a rigged model to a skinned .glb: one mesh with the skeleton, the sockets, the bone weights and a hit capsule per
    /// <c>collider bones</c> bone. <paramref name="options"/> carries the materials and the resolution; levels of detail do not apply
    /// (a rigged model is one mesh).
    /// </summary>
    /// <returns>The file's bytes, and a line saying what was made.</returns>
    public static (byte[] Glb, string Summary) MeshToGlb(string path, MeshingOptions options, TextWriter errors, bool keepJointDensity = true)
    {
        var model = Load(path);
        var content = ModelFiles.FindContentFolder(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var parts = content is not null ? ModelFiles.PartsIn(content) : null;
        var program = SdfProgramFile.Read(model.RestProgram(), parts);
        var missing = ModelFiles.MaterialNames(program).Where(n => !options.Materials.Contains(n)).ToArray();
        if (missing.Length > 0)
            errors.WriteLine($"{path}: no material file for {string.Join(", ", missing)}, meshed with the magenta stand-in");
        RigMesh mesh;
        try
        {
            mesh = RigMesher.Mesh(model, parts, options, keepJointDensity);
        }
        catch (SdfProgramFileException e)
        {
            throw new InvalidOperationException($"{path}: the model's program is not valid (line {e.Line} of the format 1 program the rigging wrote): {e.Problem}");
        }
        var name = Path.GetFileNameWithoutExtension(path);
        var glb = RigMesher.Glb(mesh, name);
        var summary = $"{mesh.Body.TriangleCount:N0} triangles, {mesh.Body.Positions.Length:N0} vertices, {model.Skeleton.Bones.Count} bones, " +
                      $"{model.Skeleton.Sockets.Count} sockets, {mesh.Hits.Count} hit capsules";
        return (glb, summary);
    }
}
