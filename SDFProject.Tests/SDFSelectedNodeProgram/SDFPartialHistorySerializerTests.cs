using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.InteractiveCommands;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using SDFProject.SDFProgramOperator;
using ExtractVariable = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.ExtractVariable;

namespace SDFProject.Tests;

public class SDFPartialHistorySerializerTests
{
    private static SDFPartialCommandNodeProgram Initial() => new(
        SdfInteractiveNodeProgramConstants.GetInitialProgram(),
        new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home));

    private static string[] View(SDFPartialActionHistory history) => new YamlRender().GetRender(history.Current.interactiveProgram);

    private static SDFPartialActionHistory Session() => SDFPartialActionHistory.Start(Initial())
        .Do(new AddNode()).Do(new AddSphere())
        .Do(new EditNode()).Do(new TypeParameterValue()).Do(new InsertText("2")).Do(new ConfirmText())
        .Do(new SwitchToHomeMode())
        .Do(new ApplyInteractiveOperation(new InteractiveAdjustTransform(TransformKind.Translate, new V3(1, 0.5f, -2))))
        .Do(new Undo())
        .Do(new ApplyInteractiveOperation(new InteractiveSetNodeParameter("radius", [1.25f])))
        .Do(new ApplyInteractiveOperation(new InteractiveAddNodeNextTo(SDFNodeType.Torus)))
        .Do(new ExtractVariable()).Do(new InsertText("ring")).Do(new ConfirmText())
        .Do(new Undo()).Do(new Undo());

    [Fact]
    public void RoundTrip_KeepsEveryEntryBranchAndThePosition()
    {
        var history = Session();

        var json = SDFPartialHistorySerializer.Serialize(history);
        var loaded = SDFPartialHistorySerializer.Deserialize(json, Initial());

        Assert.Equal(history.Entries, loaded.Entries, (a, b) => a.Parent == b.Parent && SameAction(a.Action, b.Action));
        Assert.Equal(history.Position, loaded.Position);
        Assert.Equal(View(history), View(loaded));
        Assert.Equal(history.Current.state, loaded.Current.state);
        Assert.Equal(json, SDFPartialHistorySerializer.Serialize(loaded));
    }

    [Fact]
    public void RoundTrip_EveryBranchReplaysTheSame()
    {
        var history = Session();
        var loaded = SDFPartialHistorySerializer.Deserialize(SDFPartialHistorySerializer.Serialize(history), Initial());

        for (var entry = 0; entry < history.Entries.Count; entry++)
            Assert.Equal(View(history.GoTo(entry)), View(loaded.GoTo(entry)));
    }

    [Fact]
    public void Json_NamesTheActionsAndTheirArguments()
    {
        var json = SDFPartialHistorySerializer.Serialize(Session());

        Assert.Contains("\"$type\": \"InsertText\"", json);
        Assert.Contains("\"text\": \"ring\"", json);
        Assert.Contains("\"Type\": \"Torus\"", json);
        Assert.Contains("\"$type\": \"InteractiveAdjustTransform\"", json);
    }

    [Fact]
    public void EmptyHistory_RoundTrips()
    {
        var loaded = SDFPartialHistorySerializer.Deserialize(
            SDFPartialHistorySerializer.Serialize(SDFPartialActionHistory.Start(Initial())), Initial());

        Assert.Empty(loaded.Entries);
        Assert.Null(loaded.Position);
    }

    [Theory]
    [InlineData("""{ "version": 1, "position": 0, "entries": [ { "parent": null, "action": { "$type": "Fly" } } ] }""")]
    [InlineData("""{ "version": 1, "position": 0, "entries": [ { "parent": 0, "action": { "$type": "AddNode" } } ] }""")]
    [InlineData("""{ "version": 1, "position": 3, "entries": [ { "parent": null, "action": { "$type": "AddNode" } } ] }""")]
    [InlineData("""{ "version": 2, "position": null, "entries": [] }""")]
    [InlineData("""{ "version": 1, "position": 0, "entries": [ { "parent": null, "action": { "$type": "InsertText" } } ] }""")]
    public void InvalidFiles_AreRejected(string json)
    {
        Assert.ThrowsAny<Exception>(() => SDFPartialHistorySerializer.Deserialize(json, Initial()));
    }

    [Fact]
    public void ActionsThatCannotBeReplayed_AreRejected()
    {
        var json = """
            { "version": 1, "position": null, "entries": [
                { "parent": null, "action": { "$type": "EditNode" } },
                { "parent": 0, "action": { "$type": "TypeParameterValue" } },
                { "parent": 1, "action": { "$type": "InsertText", "text": "1" } },
                { "parent": 2, "action": { "$type": "ConfirmText" } }
            ] }
            """;

        var error = Assert.Throws<FormatException>(() => SDFPartialHistorySerializer.Deserialize(json, Initial()));
        Assert.Contains("entry 3", error.Message);
    }

    private static bool SameAction(ISDFPartialAction a, ISDFPartialAction b) =>
        a is ApplyInteractiveOperation { operation: InteractiveSetNodeParameter x } && b is ApplyInteractiveOperation { operation: InteractiveSetNodeParameter y }
            ? x.Parameter == y.Parameter && x.Components.SequenceEqual(y.Components)
            : a.Equals(b);
}
