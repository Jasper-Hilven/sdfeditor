using SDFProject.Calculation;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.InteractiveCommands;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using SelectParent = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SelectParent;
using SelectPrevious = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SelectPrevious;
using ToggleSmoothness = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.ToggleSmoothness;

namespace SDFProject.Tests;

public class SDFPartialActionHistoryTests
{
    private static SDFPartialActionHistory Start() => SDFPartialActionHistory.Start(new(
        SdfInteractiveNodeProgramConstants.GetInitialProgram(),
        new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home)));

    private static SDFPartialActionHistory Do(SDFPartialActionHistory history, params ISDFPartialAction[] actions) =>
        Do(history, [], actions);

    private static SDFPartialActionHistory Do(SDFPartialActionHistory history, List<string> failures, params ISDFPartialAction[] actions)
    {
        foreach (var action in actions)
        {
            try
            {
                history = history.Do(action);
            }
            catch (Exception e)
            {
                failures.Add($"{action}: {e.Message}");
            }
        }
        return history;
    }

    private static string Fingerprint(SDFPartialCommandNodeProgram program)
    {
        var interactive = program.interactiveProgram;
        var pointers = interactive.Program.pointers;
        var ids = pointers.SDFEvaluablePointers.Keys
            .Concat(pointers.SDF2DEvaluablePointers.Keys)
            .Concat(pointers.FloatEvaluablePointers.Keys)
            .Concat(pointers.IntegerEvaluablePointers.Keys)
            .Order();
        return string.Join("\n",
        [
            program.state.ToString(),
            $"selected {interactive.SelectedNodeId} zoom {interactive.StrZoomLevel} next pointer {pointers.nextFreePointer}",
            string.Join(" ", ids),
            string.Join(" ", interactive.Program.sceneVariables.Select(v => $"{v.name}={v.id}")),
            .. new YamlRender().GetRender(interactive),
            new Webgl2CodeGenerator().GenerateShaderCode(interactive.Program),
        ]);
    }

    [Fact]
    public void ReplayingTheActions_RebuildsTheCurrentState()
    {
        var failures = new List<string>();
        var history = Do(Start(), failures,
            new AddNode(), new AddSphere(),
            new AddNode(), new ShowMoreNodeTypes(), new AddOctahedronBound(),
            new SelectPrevious(),
            new EditNode(), new TypeParameterValue(), new InsertText("2"), new ConfirmText(), new StopEditingParameter(),
            new EditNode(), new TypeParameterValue(), new InsertText("1, 2"), new ConfirmText(), new CancelText(),
            new SwitchToHomeMode(), new SwitchToTranslateMode(), new IncreaseZoomLevel(), new TranslateXUp(), new TranslateYDown(),
            new SwitchToHomeMode(), new IntersectNode(), new SelectParent(), new ToggleSmoothness(),
            new ExtractVariable(), new InsertText("ballx"), new DeleteLastCharacter(), new ConfirmText(),
            new SubtractNode(), new ApplyInteractiveOperation(new SDFInteractiveNode.SelectParent()),
            new DissolveNode(), new InlineVariable());

        Assert.Equal("ConfirmText { }", Assert.Single(failures).Split(':')[0]);
        Assert.Equal(32, history.Actions.Count);
        Assert.Empty(history.Current.interactiveProgram.Program.sceneVariables);
        var badValue = history.Actions.FindIndex(a => a is InsertText { text: "1, 2" });
        Assert.IsType<CancelText>(history.Actions[badValue + 1]);
        Assert.Equal(Fingerprint(history.Current), Fingerprint(history.Replay()));
    }

    [Fact]
    public void FailingActions_AreNotRecorded()
    {
        var typing = Do(Start(), new EditNode(), new TypeParameterValue(), new InsertText("1"));

        var result = Do(typing, new ConfirmText());

        Assert.Same(typing, result);
        Assert.Equal(typing.Actions, result.Actions);
    }

    [Fact]
    public void TheOperationList_GoesThroughTheHistory()
    {
        var history = Do(Start(), new ApplyInteractiveOperation(new InteractiveWrapInSubtraction()));

        Assert.IsType<ApplyInteractiveOperation>(Assert.Single(history.Actions));
        Assert.Equal("Subtraction", new YamlRender().GetRender(history.Current.interactiveProgram)[0]);
        Assert.Equal(Fingerprint(history.Current), Fingerprint(history.Replay()));
    }
}
