using System.Globalization;
using System.Text;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Materials;

/// <summary>
/// The text format of one material, in the style of program files; the file name is the material's name. Parameters that are left out
/// keep their <see cref="MaterialDefinition.Default"/> value.
/// <code>
/// format 1
///
/// baseColor = (0.86, 0.42, 0.05)
/// roughness = 0.55
/// subsurfaceRadius = 0.02
/// </code>
/// </summary>
public static class MaterialFile
{
    public const int FormatVersion = 1;

    public const string Extension = ".sdfmat";

    public static string Write(MaterialDefinition material)
    {
        var text = new StringBuilder($"{FileSyntax.FormatKeyword} {FormatVersion}\n\n");
        foreach (var parameter in MaterialDefinition.Parameters)
        {
            var components = parameter.Get(material).Select(FileSyntax.Number).ToArray();
            var value = components.Length == 1 ? components[0] : $"({string.Join(", ", components)})";
            text.Append($"{parameter.Name} = {value}\n");
        }
        return text.ToString();
    }

    /// <exception cref="SdfProgramFileException">The text is not a valid material; the exception names the line.</exception>
    public static MaterialDefinition Read(string text)
    {
        var lines = SourceLine.Parse(text);
        if (lines.Count == 0)
            throw new SdfProgramFileException(1, $"The file is empty, it must start with '{FileSyntax.FormatKeyword} {FormatVersion}'");
        ReadFormat(lines[0]);

        var material = MaterialDefinition.Default;
        var seen = new HashSet<string>();
        foreach (var line in lines.Skip(1))
        {
            if (line.Children.Count > 0)
                throw new SdfProgramFileException(line.Children[0].Number, "Material parameters are not indented");

            var cursor = new Cursor(line);
            var name = cursor.ReadName("a parameter like roughness = 0.5");
            var parameter = MaterialDefinition.FindParameter(name)
                            ?? throw cursor.Error($"Materials have no parameter '{name}'; they have {string.Join(", ", MaterialDefinition.Parameters.Select(p => p.Name))}");
            if (!seen.Add(name))
                throw cursor.Error($"Parameter '{name}' is given twice");
            cursor.Expect('=');
            var components = Components(cursor.ReadValue(), parameter, cursor);
            cursor.ExpectEnd();
            material = parameter.With(material, components);
        }
        return material;
    }

    public static MaterialDefinition Load(string path) => Read(File.ReadAllText(path));

    public static void Save(string path, MaterialDefinition material) => File.WriteAllText(path, Write(material));

    /// <summary>"materials/chanterelle_cap.sdfmat" is the material chanterelle_cap.</summary>
    public static string NameOf(string path) => Path.GetFileNameWithoutExtension(path);

    private static float[] Components(RawValue value, MaterialParameter parameter, Cursor cursor)
    {
        float[] components = value switch
        {
            RawNumber number => [Parse(number)],
            RawVector vector => vector.Components.Select(Parse).ToArray(),
            _ => throw cursor.Error($"'{parameter.Name}' needs {Expected(parameter)}"),
        };
        return components.Length == parameter.ComponentCount ? components : throw cursor.Error($"'{parameter.Name}' needs {Expected(parameter)}");
    }

    private static float Parse(RawNumber number) => float.Parse(number.Text, CultureInfo.InvariantCulture);

    private static string Expected(MaterialParameter parameter) =>
        parameter.Kind switch
        {
            MaterialParameterKind.Color => "a colour like (0.9, 0.5, 0.1)",
            MaterialParameterKind.Direction => "a direction like (0, 1, 0)",
            _ => "a number",
        };

    private static void ReadFormat(SourceLine line)
    {
        var cursor = new Cursor(line);
        if (cursor.TryReadName() != FileSyntax.FormatKeyword || cursor.ReadValue() is not RawNumber { IsWhole: true } number)
            throw cursor.Error($"The file must start with '{FileSyntax.FormatKeyword} {FormatVersion}'");
        cursor.ExpectEnd();
        if (int.Parse(number.Text, CultureInfo.InvariantCulture) is var version && version != FormatVersion)
            throw cursor.Error($"This material has format {version}, but only format {FormatVersion} can be read");
    }
}

/// <summary>All materials in a folder and its subfolders, named after their files.</summary>
public static class MaterialFiles
{
    public static IReadOnlyList<string> All(string directory) =>
        Directory.EnumerateFiles(directory, "*" + MaterialFile.Extension, SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <exception cref="SdfProgramFileException">A material file is invalid; the message names the file.</exception>
    public static MaterialLibrary LoadLibrary(string directory)
    {
        var materials = new Dictionary<string, MaterialDefinition>();
        foreach (var path in All(directory))
        {
            var name = MaterialFile.NameOf(path);
            if (materials.ContainsKey(name))
                throw new InvalidOperationException($"Two materials are named '{name}'; material names must be unique across {directory}");
            try
            {
                materials[name] = MaterialFile.Load(path);
            }
            catch (SdfProgramFileException exception)
            {
                throw new InvalidOperationException($"{path}:{exception.Line}: {exception.Problem}", exception);
            }
        }
        return MaterialLibrary.Of(materials);
    }
}
