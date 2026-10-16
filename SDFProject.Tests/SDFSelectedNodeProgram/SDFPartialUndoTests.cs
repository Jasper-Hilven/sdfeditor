using SDFProject.Calculation;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

namespace SDFProject.Tests;

public class SDFPartialUndoTests
{
    private static SDFPartialActionHistory Start() => SDFPartialActionHistory.Start(new(
        SdfInteractiveNodeProgramConstants.GetInitialProgram(),
        new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home)));

    private static SDFPartialActionHistory Press(SDFPartialActionHistory history, string letter) =>
        history.Do(SDFPartialKeyBindings.Bindings[(history.Current.state.Mode, FingerKeyLayouts.AzertyBeFingerKeyToIndex[letter])]);

    private static string[] View(SDFPartialActionHistory history) => new YamlRender().GetRender(history.Current.interactiveProgram);

    [Fact]
    public void Undo_StepsBackAndRebuildsTheCurrentState()
    {
        var before = Start().Do(new AddNode()).Do(new AddSphere());
        var after = before.Do(new IntersectNode());

        var undone = after.Do(new Undo());

        Assert.Equal(before.Actions, undone.Actions);
        Assert.Equal(View(before), View(undone));
        Assert.Equal(before.Current.interactiveProgram.SelectedNodeId, undone.Current.interactiveProgram.SelectedNodeId);
        Assert.Equal(before.Current.state, undone.Current.state);
    }

    [Fact]
    public void Undo_WithoutActions_DoesNothing()
    {
        var start = Start();

        Assert.False(start.CanUndo);
        Assert.Same(start, start.Do(new Undo()));
    }

    [Fact]
    public void Undo_IsNotRecorded_SoReplayStillMatches()
    {
        var history = Start().Do(new AddNode()).Do(new AddSphere()).Do(new SubtractNode()).Do(new Undo()).Do(new IntersectNode());

        Assert.DoesNotContain(history.Actions, a => a is Undo);
        Assert.Equal(new YamlRender().GetRender(history.Replay().interactiveProgram), View(history));
    }

    [Theory]
    [InlineData(SDFPartialCommandNodeProgramMode.Home)]
    [InlineData(SDFPartialCommandNodeProgramMode.Scale)]
    [InlineData(SDFPartialCommandNodeProgramMode.Rotate)]
    [InlineData(SDFPartialCommandNodeProgramMode.Translate)]
    [InlineData(SDFPartialCommandNodeProgramMode.EditNode)]
    [InlineData(SDFPartialCommandNodeProgramMode.EditParameter)]
    [InlineData(SDFPartialCommandNodeProgramMode.AddNode)]
    [InlineData(SDFPartialCommandNodeProgramMode.AddNodeMore)]
    [InlineData(SDFPartialCommandNodeProgramMode.ReplaceNode)]
    [InlineData(SDFPartialCommandNodeProgramMode.ReplaceNodeMore)]
    public void WKey_UndoesInTheTransformAndEditModes(SDFPartialCommandNodeProgramMode mode)
    {
        Assert.IsType<Undo>(SDFPartialKeyBindings.Bindings[(mode, FingerKeyLayouts.AzertyBeFingerKeyToIndex["w"])]);
    }

    [Fact]
    public void WKey_InTheAddMenu_StepsBackAPageAndThenOutOfTheMenu()
    {
        var adding = Press(Start(), "f");
        var morePage = Press(adding, "=");
        Assert.Equal(SDFPartialCommandNodeProgramMode.AddNodeMore, morePage.Current.state.Mode);

        var backOnFirstPage = Press(morePage, "w");
        var backHome = Press(backOnFirstPage, "w");

        Assert.Equal(SDFPartialCommandNodeProgramMode.AddNode, backOnFirstPage.Current.state.Mode);
        Assert.Equal(SDFPartialCommandNodeProgramMode.Home, backHome.Current.state.Mode);
        Assert.Equal(SDFPartialCommandNodeProgramMode.AddNode, Press(morePage, "=").Current.state.Mode);
    }

    [Fact]
    public void WKey_InTranslateMode_UndoesTheLastMove()
    {
        var moving = Press(Start(), "=");
        Assert.Equal(SDFPartialCommandNodeProgramMode.Translate, moving.Current.state.Mode);
        var moved = Press(moving, "f");

        var undone = Press(moved, "w");

        Assert.Equal(View(moving), View(undone));
        Assert.Equal(SDFPartialCommandNodeProgramMode.Translate, undone.Current.state.Mode);
    }

    private static SDFPartialActionHistory Branched(out SDFPartialActionHistory sphereBranch, out SDFPartialActionHistory boxBranch)
    {
        var adding = Start().Do(new AddNode());
        sphereBranch = adding.Do(new AddSphere());
        boxBranch = sphereBranch.Do(new Undo()).Do(new AddBox());
        return boxBranch.Do(new Undo());
    }

    [Fact]
    public void Undo_KeepsTheUndoneEntries()
    {
        var done = Start().Do(new AddNode()).Do(new AddSphere());

        var undone = done.Do(new Undo());

        Assert.Equal(done.Entries, undone.Entries);
        Assert.Single(undone.Actions);
        Assert.True(undone.CanRedo);
    }

    [Fact]
    public void Redo_WithOneBranch_GoesForwardAgain()
    {
        var done = Start().Do(new AddNode()).Do(new AddSphere()).Do(new IntersectNode());

        var redone = done.Do(new Undo()).Do(new Undo()).Do(new Redo()).Do(new Redo());

        Assert.Equal(done.Position, redone.Position);
        Assert.Equal(View(done), View(redone));
        Assert.Equal(View(done), new YamlRender().GetRender(redone.Replay().interactiveProgram));
        Assert.False(redone.CanRedo);
    }

    [Fact]
    public void DoingSomethingAfterUndo_StartsANewBranchAndKeepsTheOldOne()
    {
        var atFork = Branched(out _, out _);

        Assert.Equal(3, atFork.Entries.Count);
        Assert.Equal(2, atFork.Children(atFork.Position).Count);
    }

    [Fact]
    public void Redo_WithSeveralBranches_ForcesAChoice()
    {
        var atFork = Branched(out var sphereBranch, out var boxBranch);

        var choosing = atFork.Do(new Redo());

        Assert.True(choosing.ChoosingRedo);
        Assert.Equal(atFork.Position, choosing.Position);
        Assert.Same(choosing, choosing.Do(new SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SelectNext()));
        Assert.False(choosing.CanUndo);

        Assert.Equal(View(boxBranch), View(choosing.Do(new ChooseRedoBranch(0))));
        Assert.Equal(View(sphereBranch), View(choosing.Do(new ChooseRedoBranch(1))));
        Assert.False(choosing.Do(new ChooseRedoBranch(1)).ChoosingRedo);

        var cancelled = choosing.Do(new CancelRedoChoice());
        Assert.False(cancelled.ChoosingRedo);
        Assert.Equal(atFork.Position, cancelled.Position);
    }

    [Fact]
    public void WhileChoosing_TheKeysPickABranchOrCancel()
    {
        var choosing = Branched(out var sphereBranch, out _).Do(new Redo());

        Assert.IsType<CancelRedoChoice>(choosing.ActionFor(InputKey.LPinkHome));
        Assert.Null(choosing.ActionFor(SDFPartialKeyBindings.RedoChoiceKeys[2]));

        var picked = choosing.Do(choosing.ActionFor(SDFPartialKeyBindings.RedoChoiceKeys[1])!);

        Assert.Equal(View(sphereBranch), View(picked));
    }

    [Fact]
    public void XKey_IsRedoInEveryModeThatDoesNotTakeText()
    {
        var x = FingerKeyLayouts.AzertyBeFingerKeyToIndex["x"];
        Assert.Equal(x, FingerKeyLayouts.QwertyFingerKeyToIndex["x"]);

        foreach (var mode in Enum.GetValues<SDFPartialCommandNodeProgramMode>())
        {
            if (SDFPartialCommandNodeProgramOperator.TakesText(mode))
                Assert.False(SDFPartialKeyBindings.Bindings.ContainsKey((mode, x)));
            else
                Assert.IsType<Redo>(SDFPartialKeyBindings.Bindings[(mode, x)]);
        }
    }

    [Fact]
    public void Graph_PutsABranchInItsOwnLane()
    {
        var atFork = Branched(out _, out _);

        var graph = SDFPartialHistoryGraph.Of(atFork);

        Assert.Equal([0, 0, 0, 1], graph.Lanes);
        Assert.Equal([(0, 1), (1, 2), (1, 3)], graph.Edges);
    }

    [Fact]
    public void GoingToAnEntry_JumpsThereOnAnyBranchAndKeepsTheHistory()
    {
        var atFork = Branched(out var sphereBranch, out var boxBranch);

        var onSphere = atFork.Do(new GoToHistoryEntry(sphereBranch.Position));
        var onBox = onSphere.Do(new GoToHistoryEntry(boxBranch.Position));
        var atStart = onBox.Do(new GoToHistoryEntry(null));

        Assert.Equal(View(sphereBranch), View(onSphere));
        Assert.Equal(View(boxBranch), View(onBox));
        Assert.Equal(View(Start()), View(atStart));
        Assert.Equal(atFork.Entries, atStart.Entries);
        Assert.Equal(View(onBox), new YamlRender().GetRender(onBox.Replay().interactiveProgram));
    }

    [Fact]
    public void GoingToAnEntry_EndsAChoiceAndIgnoresUnknownEntries()
    {
        var choosing = Branched(out var sphereBranch, out _).Do(new Redo());

        Assert.False(choosing.Do(new GoToHistoryEntry(sphereBranch.Position)).ChoosingRedo);
        Assert.Same(choosing, choosing.Do(new GoToHistoryEntry(99)));
    }
}
