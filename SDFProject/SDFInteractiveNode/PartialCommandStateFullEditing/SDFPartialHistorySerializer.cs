using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public static class SDFPartialHistorySerializer
{
    private const int Version = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new RecordInterfaceConverter<ISDFPartialAction>(),
            new RecordInterfaceConverter<ISdfInteractiveNodeProgramOperation>(),
        },
    };

    private sealed record HistoryFile(int version, int? position, EntryFile[] entries);

    private sealed record EntryFile(int? parent, ISDFPartialAction action);

    public static string Serialize(SDFPartialActionHistory history) =>
        JsonSerializer.Serialize(
            new HistoryFile(Version, history.Position, history.Entries.Select(e => new EntryFile(e.Parent, e.Action)).ToArray()),
            Options);

    public static SDFPartialActionHistory Deserialize(string json, SDFPartialCommandNodeProgram initial)
    {
        var file = JsonSerializer.Deserialize<HistoryFile>(json, Options)
                   ?? throw new FormatException("The file holds no history");
        if (file.version != Version)
            throw new FormatException($"Version {file.version} is not supported, only version {Version} is");

        var entries = (file.entries ?? []).Select(e => new SDFPartialHistoryEntry(e.parent, e.action)).ToImmutableList();
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Action is null)
                throw new FormatException($"Entry {i} has no action");
            if (entries[i].Parent is { } parent && (parent < 0 || parent >= i))
                throw new FormatException($"Entry {i} has parent {parent}, which does not come before it");
        }
        if (file.position is { } position && (position < 0 || position >= entries.Count))
            throw new FormatException($"Position {position} is not one of the {entries.Count} entries");

        var history = new SDFPartialActionHistory(initial, entries, null, initial);
        foreach (var leaf in Enumerable.Range(0, entries.Count).Where(i => history.Children(i).Count == 0))
        {
            try
            {
                history.GoTo(leaf);
            }
            catch (Exception e)
            {
                throw new FormatException($"The actions up to entry {leaf} cannot be replayed: {e.Message}", e);
            }
        }

        return history.GoTo(file.position);
    }

    private sealed class RecordInterfaceConverter<T> : JsonConverter<T> where T : class
    {
        private const string TypeProperty = "$type";

        private static readonly Dictionary<string, Type> Types = typeof(T).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t))
            .ToDictionary(t => t.Name);

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var node = JsonNode.Parse(ref reader)?.AsObject()
                       ?? throw new JsonException($"Expected a {typeof(T).Name}");
            var name = node[TypeProperty]?.GetValue<string>()
                       ?? throw new JsonException($"A {typeof(T).Name} needs a \"{TypeProperty}\"");
            if (!Types.TryGetValue(name, out var type))
                throw new JsonException($"There is no {typeof(T).Name} called '{name}'");

            var constructor = type.GetConstructors().Single();
            var arguments = constructor.GetParameters()
                .Select(p => node[p.Name!] is { } value
                    ? value.Deserialize(p.ParameterType, options)
                    : throw new JsonException($"{name} needs \"{p.Name}\""))
                .ToArray();
            return (T)constructor.Invoke(arguments);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var type = value.GetType();
            writer.WriteStartObject();
            writer.WriteString(TypeProperty, type.Name);
            foreach (var parameter in type.GetConstructors().Single().GetParameters())
            {
                writer.WritePropertyName(parameter.Name!);
                var property = type.GetProperty(parameter.Name!, BindingFlags.Public | BindingFlags.Instance)!;
                JsonSerializer.Serialize(writer, property.GetValue(value), property.PropertyType, options);
            }
            writer.WriteEndObject();
        }
    }
}
