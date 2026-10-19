using System.Globalization;
using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;
using static SDFPipeline.ProgramFiles.FileSyntax;

namespace SDFPipeline.ProgramFiles;

/// <summary>
/// Reads the text <see cref="SdfProgramFileWriter"/> writes, and the hand- or AI-written variations of it: comments after '#',
/// blank lines, whole numbers where a number is needed. Every node gets a fresh pointer id.
/// </summary>
internal sealed class SdfProgramFileReader
{
    private sealed record Variable(string Id, ValueKind Kind);

    private sealed record FunctionHeader(string Name, SourceLine Line, string[] ArgumentNames, ValueKind?[] ArgumentKinds);

    private sealed class Scope(FunctionHeader? function, string description)
    {
        public FunctionHeader? Function { get; } = function;
        public string Description { get; } = description;
        public Dictionary<string, Variable> Variables { get; } = [];
    }

    /// <summary>What the file and the parts it uses share while being read: variable ids, and every part loaded so far.</summary>
    private sealed class PartLoading(PartSource? source)
    {
        public PartSource? Source { get; } = source;
        public long NextVariableId;
        public Dictionary<string, string> LoadedIds { get; } = [];
        public List<string> Loading { get; } = [];
        /// <summary>The variables of all loaded parts, each after the parts it uses, so they can go first in the scene.</summary>
        public List<VariableDeclaration> Declarations { get; } = [];
    }

    private readonly List<SourceLine> lines;
    private readonly Dictionary<string, FunctionHeader> functions = [];
    private readonly List<FunctionHeader> functionOrder = [];
    private readonly PartLoading parts;

    private SdfProgramFileReader(string text, PartLoading parts)
    {
        lines = SourceLine.Parse(text);
        this.parts = parts;
    }

    public static SDFProgram Read(string text, PartSource? parts) => new SdfProgramFileReader(text, new PartLoading(parts)).Read();

    private SDFProgram Read()
    {
        var (definitions, variables, scene) = ReadFile();
        var pointers = new SDFPointers(new(), new(), new(), new(), parts.NextVariableId);
        var sceneVariables = parts.Declarations.Concat(variables).ToArray();
        return SDFProgramNormalizePointers.Pointerify(new SDFProgram(definitions, pointers, sceneVariables, scene));
    }

    /// <param name="part">Whether the file is a part, which has no settings of its own: the model it is in has them.</param>
    private (SDFFunctionDefinition[] functions, VariableDeclaration[] variables, SDFEvaluable scene) ReadFile(bool part = false)
    {
        if (lines.Count == 0)
            throw new SdfProgramFileException(1, $"The file is empty, it must start with '{FormatKeyword} {SdfProgramFile.FormatVersion}'");
        ReadFormat(lines[0]);

        // The model's settings (collider ...) do not change its shape; ModelSettings reads them, checked here so a bad one is an error
        var rest = lines.Skip(1).ToList();
        ModelSettings.Read(rest);
        if (part && rest.FirstOrDefault(ModelSettings.IsSetting) is { } setting)
            throw new SdfProgramFileException(setting.Number, "A part has no settings of its own; give the model that uses it its collider");
        rest = rest.Where(line => !ModelSettings.IsSetting(line)).ToList();
        foreach (var line in rest.Where(IsFunction))
        {
            var header = ReadHeader(line);
            if (!functions.TryAdd(header.Name, header))
                throw new SdfProgramFileException(line.Number, $"Function '{header.Name}' is declared twice");
            functionOrder.Add(header);
        }

        var definitions = functionOrder.Select(ReadFunction).ToArray<SDFFunctionDefinition>();
        var (variables, scene) = ReadScope(rest.Where(l => !IsFunction(l)).ToList(), new Scope(null, "the file"), rest.LastOrDefault() ?? lines[0]);
        return (definitions, variables, scene);
    }

    private Evaluable ReadPart(SourceLine line, Cursor cursor, ValueKind? expected, Scope scope)
    {
        if (cursor.ReadName("'part='") != PartParameter)
            throw cursor.Error($"Write a part as {PartKeyword} {PartParameter}=eye");
        cursor.Expect('=');
        var name = cursor.ReadValue() is RawName { Name: var partName } ? partName : throw cursor.Error("Name the part, like part=eye");
        cursor.ExpectEnd();
        if (line.Children.Count > 0)
            throw new SdfProgramFileException(line.Children[0].Number, "Nothing can be indented below a part");
        if (scope.Function is not null)
            throw cursor.Error("Parts cannot be used inside functions yet");
        RequireKind(expected, ValueKind.Sdf, $"part '{name}'", cursor);
        return new VariableReferenceSDF(LoadPart(name, cursor));
    }

    /// <summary>Reads a part's file once, however often it is used, and returns the id of the variable that is the part.</summary>
    private string LoadPart(string name, Cursor cursor)
    {
        if (parts.LoadedIds.TryGetValue(name, out var loaded))
            return loaded;
        if (parts.Loading.Contains(name))
            throw cursor.Error($"Part '{name}' uses itself: {string.Join(" → ", parts.Loading.Append(name))}");
        if (parts.Source is null)
            throw cursor.Error($"Part '{name}' cannot be found, because this file is not read from a content folder");
        var text = parts.Source(name) ?? throw cursor.Error($"There is no part '{name}'; parts are the files in content/parts/, like {name}.sdf");

        parts.Loading.Add(name);
        try
        {
            var (functionsOfPart, variables, scene) = new SdfProgramFileReader(text, parts).ReadFile(part: true);
            if (functionsOfPart.Length > 0)
                throw new SdfProgramFileException(1, "Parts cannot declare functions yet");

            parts.Declarations.AddRange(variables.Select(v => v with { name = Parts.InnerVariableName(name, v.name) }));
            var id = (parts.NextVariableId++).ToString(CultureInfo.InvariantCulture);
            parts.Declarations.Add(new VariableDeclaration(Parts.VariableName(name), id, scene));
            parts.LoadedIds[name] = id;
            return id;
        }
        catch (SdfProgramFileException exception)
        {
            throw cursor.Error($"In part '{name}', line {exception.Line}: {exception.Problem}");
        }
        finally
        {
            parts.Loading.Remove(name);
        }
    }

    private static void ReadFormat(SourceLine line)
    {
        var cursor = new Cursor(line);
        if (cursor.TryReadName() != FormatKeyword || cursor.ReadValue() is not RawNumber { IsWhole: true } number)
            throw cursor.Error($"The file must start with '{FormatKeyword} {SdfProgramFile.FormatVersion}'");
        cursor.ExpectEnd();

        var version = int.Parse(number.Text, CultureInfo.InvariantCulture);
        if (version != SdfProgramFile.FormatVersion)
            throw cursor.Error($"This file has format {version}, but only format {SdfProgramFile.FormatVersion} can be read");
        if (line.Children.Count > 0)
            throw new SdfProgramFileException(line.Children[0].Number, "Nothing can be indented below the format line");
    }

    private static bool IsFunction(SourceLine line)
    {
        var cursor = new Cursor(line);
        return cursor.TryReadName() == FunctionKeyword && cursor.Peek() is not ('=' or ':' or null);
    }

    private static FunctionHeader ReadHeader(SourceLine line)
    {
        var cursor = new Cursor(line);
        cursor.ReadName("'function'");
        var name = cursor.ReadName("a function name");
        cursor.Expect('(');

        var names = new List<string>();
        var kinds = new List<ValueKind?>();
        if (!cursor.TryConsume(')'))
        {
            do
            {
                var argument = cursor.ReadName("an argument name");
                if (names.Contains(argument))
                    throw cursor.Error($"Function '{name}' has two arguments named '{argument}'");
                names.Add(argument);
                kinds.Add(cursor.TryConsume(':') ? ReadType(cursor) : null);
            }
            while (cursor.TryConsume(','));
            cursor.Expect(')');
        }
        cursor.ExpectEnd();

        return new FunctionHeader(name, line, names.ToArray(), kinds.ToArray());
    }

    private static ValueKind ReadType(Cursor cursor)
    {
        var keyword = cursor.ReadName("a type");
        return ValueKinds.FromKeyword(keyword) ?? throw cursor.Error($"Unknown type '{keyword}', use float, int, v2 or v3");
    }

    private SDFFunction ReadFunction(FunctionHeader header)
    {
        var (variables, body) = ReadScope(header.Line.Children, new Scope(header, $"function '{header.Name}'"), header.Line);
        return new SDFFunction(header.Name, header.Name, header.ArgumentNames, variables, body);
    }

    private (VariableDeclaration[] variables, SDFEvaluable body) ReadScope(List<SourceLine> scopeLines, Scope scope, SourceLine owner)
    {
        var variables = new List<VariableDeclaration>();
        (SDFEvaluable node, SourceLine line)? body = null;
        foreach (var line in scopeLines)
        {
            if (IsFunction(line))
                throw new SdfProgramFileException(line.Number, "Functions can only be declared at the top level, without indentation");

            if (IsDeclaration(line))
            {
                if (body is { } shape)
                    throw new SdfProgramFileException(line.Number, $"Declare variables before the shape, which starts on line {shape.line.Number}");
                variables.Add(ReadDeclaration(line, scope));
            }
            else
            {
                if (body is { } shape)
                    throw new SdfProgramFileException(line.Number,
                        $"{Capitalized(scope.Description)} already has its shape on line {shape.line.Number}; combine shapes with a Union");
                body = ((SDFEvaluable)ReadNode(line, ValueKind.Sdf, scope, fileShape: scope.Function is null), line);
            }
        }

        return body is { } result
            ? (variables.ToArray(), result.node)
            : throw new SdfProgramFileException(owner.Number, $"{Capitalized(scope.Description)} has no shape");
    }

    private static bool IsDeclaration(SourceLine line)
    {
        var cursor = new Cursor(line);
        return cursor.TryReadName() is not null && cursor.Peek() is '=' or ':';
    }

    private VariableDeclaration ReadDeclaration(SourceLine line, Scope scope)
    {
        var cursor = new Cursor(line);
        var name = cursor.ReadName("a variable name");
        ValueKind? type = cursor.TryConsume(':') ? ReadType(cursor) : null;
        cursor.Expect('=');
        if (scope.Variables.ContainsKey(name))
            throw cursor.Error($"Variable '{name}' is declared twice");

        Evaluable value;
        if (cursor.AtEnd)
        {
            if (type is not null)
                throw cursor.Error($"Variable '{name}' has type {ValueKinds.Keyword(type.Value)}, so it needs a value after '='");
            if (line.Children.Count != 1)
                throw cursor.Error($"Variable '{name}' needs a value after '=' or exactly one shape indented below it");
            value = ReadNode(line.Children[0], null, scope);
        }
        else
        {
            if (line.Children.Count > 0)
                throw new SdfProgramFileException(line.Children[0].Number, $"Variable '{name}' already has a value, so nothing can be indented below it");
            value = Convert(cursor.ReadValue(), type, scope, cursor);
            cursor.ExpectEnd();
        }

        var id = (parts.NextVariableId++).ToString(CultureInfo.InvariantCulture);
        scope.Variables[name] = new Variable(id, ValueKinds.Of(value));
        return new VariableDeclaration(name, id, value);
    }

    /// <param name="fileShape">Whether this is the shape of the file itself, the only place a <see cref="Detail"/> node is read.</param>
    private Evaluable ReadNode(SourceLine line, ValueKind? expected, Scope scope, bool fileShape = false)
    {
        var cursor = new Cursor(line);
        if (cursor.TryConsume('$'))
        {
            var name = cursor.ReadName("a variable name after '$'");
            cursor.ExpectEnd();
            if (line.Children.Count > 0)
                throw new SdfProgramFileException(line.Children[0].Number, "Nothing can be indented below a variable");

            var variable = Lookup(name, scope, cursor);
            if (variable.Kind is not (ValueKind.Sdf or ValueKind.Sdf2D))
                throw cursor.Error($"Variable '{name}' is {ValueKinds.Describe(variable.Kind)}, not a shape");
            RequireKind(expected, variable.Kind, $"variable '{name}'", cursor);
            return ValueKinds.VariableReference(variable.Kind, variable.Id);
        }

        var typeName = cursor.ReadName("a node type");
        if (typeName == PartKeyword)
            return ReadPart(line, cursor, expected, scope);
        var shape = NodeTypes.Find(typeName) ?? throw cursor.Error(UnknownNode(typeName));
        RequireKind(expected, shape.Kind, typeName, cursor);

        var values = new Dictionary<string, RawValue>();
        while (!cursor.AtEnd)
        {
            var key = cursor.ReadName("a parameter like name=value");
            cursor.Expect('=');
            if (!values.TryAdd(key, cursor.ReadValue()))
                throw cursor.Error($"Parameter '{key}' is given twice");
        }

        var arguments = new object?[shape.Slots.Length];
        var children = line.Children;
        var nextChild = 0;
        FunctionHeader? called = null;
        foreach (var (slot, i) in shape.Slots.Select((s, i) => (s, i)))
        {
            switch (slot.Category)
            {
                case SlotCategory.Value:
                    arguments[i] = Convert(Take(slot), ValueKinds.OfSite(slot.Type), scope, cursor);
                    break;
                case SlotCategory.FunctionName:
                    var function = Take(slot) is RawName { Name: var functionName }
                        ? functionName
                        : throw cursor.Error($"Parameter '{slot.Name}' needs a function name");
                    called = functions.GetValueOrDefault(function) ?? throw cursor.Error($"There is no function '{function}'");
                    arguments[i] = called.Name;
                    break;
                case SlotCategory.MaterialName:
                    arguments[i] = Take(slot) is RawName { Name: var materialName }
                        ? materialName
                        : throw cursor.Error($"Parameter '{slot.Name}' needs a material name, like {slot.Name}=chanterelle_cap");
                    break;
                case SlotCategory.Arguments:
                    arguments[i] = ReadArguments(values.Remove(slot.Name, out var list) ? list : new RawList([]), called, scope, cursor);
                    break;
                case SlotCategory.Node:
                    if (nextChild >= children.Count)
                        throw cursor.Error($"{typeName} needs a shape indented below it for '{slot.Name}'");
                    arguments[i] = ReadNode(children[nextChild++], ValueKinds.OfSite(slot.Type), scope);
                    break;
                case SlotCategory.Nodes:
                    var elementType = slot.Type.GetElementType()!;
                    var nodes = Array.CreateInstance(elementType, children.Count - nextChild);
                    for (var n = 0; nextChild < children.Count; n++)
                        nodes.SetValue(ReadNode(children[nextChild++], ValueKinds.OfSite(elementType), scope), n);
                    arguments[i] = nodes;
                    break;
            }
        }

        if (values.Keys.FirstOrDefault() is { } unknown)
            throw cursor.Error($"{typeName} has no parameter '{unknown}'; its parameters are {Parameters(shape)}");
        if (nextChild < children.Count)
            throw new SdfProgramFileException(children[nextChild].Number, $"{typeName} on line {line.Number} has no room for another shape");

        var node = (Evaluable)shape.Constructor.Invoke(arguments);
        if (node is Detail detail)
            CheckDetail(detail, fileShape, cursor);
        return node;

        RawValue Take(NodeSlot slot) =>
            values.Remove(slot.Name, out var value) ? value : throw cursor.Error($"{typeName} is missing parameter '{slot.Name}'");
    }

    /// <summary>
    /// The mesh builder reads a Detail node at the root of a model's or a part's file only. Anywhere else it would do nothing, which is
    /// rejected rather than allowed: wrapping one branch, like the trunk, in a Detail would look like it makes that branch coarser.
    /// The detail is a plain number, so it is known without evaluating the program and checked here.
    /// </summary>
    private static void CheckDetail(Detail detail, bool fileShape, Cursor cursor)
    {
        if (!fileShape)
            throw cursor.Error($"{nameof(Detail)} only works as the shape of the file itself, at the top without indentation; " +
                               "it sets the detail of the whole model or part");
        if (detail.detail is not ConstValueFloat { value: var value })
            throw cursor.Error($"The detail of {nameof(Detail)} must be a number, like detail=0.5");
        if (value is < Detail.MinDetail or > Detail.MaxDetail || float.IsNaN(value))
            throw cursor.Error(FormattableString.Invariant(
                $"The detail of {nameof(Detail)} must be between {Detail.MinDetail} and {Detail.MaxDetail}, not {value}; 1 keeps the vertices the size gives"));
    }

    private Evaluable[] ReadArguments(RawValue raw, FunctionHeader? function, Scope scope, Cursor cursor)
    {
        if (raw is not RawList list)
            throw cursor.Error("Function arguments are written as a list like [0.5, (1, 2, 3)]");
        if (function is null)
            throw cursor.Error("Name the function before its arguments");
        if (list.Items.Length != function.ArgumentNames.Length)
            throw cursor.Error($"Function '{function.Name}' takes {function.ArgumentNames.Length} argument(s), but {list.Items.Length} are given");

        return list.Items.Select((item, i) => Convert(item, function.ArgumentKinds[i], scope, cursor)).ToArray();
    }

    /// <summary>Turns a raw value into the evaluable a parameter needs; without an expected kind, a plain number is a float.</summary>
    private Evaluable Convert(RawValue raw, ValueKind? expected, Scope scope, Cursor cursor)
    {
        switch (raw)
        {
            case RawNumber number when expected == ValueKind.Int:
                return number.IsWhole
                    ? new ConstValueInt(int.Parse(number.Text, CultureInfo.InvariantCulture))
                    : throw cursor.Error($"Expected a whole number, found {number.Text}");
            case RawNumber number:
                RequireKind(expected, ValueKind.Float, $"the number {number.Text}", cursor);
                return new ConstValueFloat(float.Parse(number.Text, CultureInfo.InvariantCulture));
            case RawVector vector:
                var components = vector.Components.Select(c => float.Parse(c.Text, CultureInfo.InvariantCulture)).ToArray();
                return components.Length switch
                {
                    2 => RequireKind(expected, ValueKind.V2, "a 2D vector", cursor, new ConstValueV2(new V2(components[0], components[1]))),
                    3 => RequireKind(expected, ValueKind.V3, "a 3D vector", cursor, new ConstValueV3(new V3(components[0], components[1], components[2]))),
                    _ => throw cursor.Error($"A vector has 2 or 3 numbers, not {components.Length}"),
                };
            case RawVariable { Name: var name }:
                var variable = Lookup(name, scope, cursor);
                if (variable.Kind is ValueKind.Sdf or ValueKind.Sdf2D)
                    throw cursor.Error($"Variable '{name}' is a shape, put it on its own line as ${Name(name)}");
                RequireKind(expected, variable.Kind, $"variable '{name}'", cursor);
                return ValueKinds.VariableReference(variable.Kind, variable.Id);
            case RawArgument { Name: var name }:
                var function = scope.Function ?? throw cursor.Error($"@{name} is a function argument, which only exists inside a function");
                var index = Array.IndexOf(function.ArgumentNames, name);
                if (index < 0)
                    throw cursor.Error($"Function '{function.Name}' has no argument '{name}'");
                var kind = function.ArgumentKinds[index] ?? expected
                    ?? throw cursor.Error($"Give argument '{name}' a type in the function header, like {name}: float");
                RequireKind(expected, kind, $"argument '{name}'", cursor);
                function.ArgumentKinds[index] = kind;
                return ValueKinds.ArgumentReference(kind, index);
            case RawName { Name: var name }:
                throw cursor.Error($"Expected a value, found '{name}'; variables are written as ${name}");
            default:
                throw cursor.Error("A list can only hold function arguments");
        }
    }

    private static Variable Lookup(string name, Scope scope, Cursor cursor) =>
        scope.Variables.GetValueOrDefault(name)
        ?? throw cursor.Error($"Variable '{name}' is not declared before this line in {scope.Description}");

    private static string Capitalized(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static void RequireKind(ValueKind? expected, ValueKind actual, string what, Cursor cursor)
    {
        if (expected is { } kind && kind != actual)
            throw cursor.Error($"Expected {ValueKinds.Describe(kind)}, but {what} is {ValueKinds.Describe(actual)}");
    }

    private static Evaluable RequireKind(ValueKind? expected, ValueKind actual, string what, Cursor cursor, Evaluable value)
    {
        RequireKind(expected, actual, what, cursor);
        return value;
    }

    private static string UnknownNode(string typeName)
    {
        var similar = NodeTypes.NamesLike(typeName).Take(5).ToArray();
        return similar.Length > 0
            ? $"Unknown node type '{typeName}', did you mean {string.Join(" or ", similar)}?"
            : $"Unknown node type '{typeName}'";
    }

    private static string Parameters(NodeShape shape) =>
        string.Join(", ", shape.Slots.Where(s => s.Category is SlotCategory.Value or SlotCategory.FunctionName or SlotCategory.MaterialName or SlotCategory.Arguments).Select(s => s.Name));
}
