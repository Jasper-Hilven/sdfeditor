using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using SDFProject.SDFProgramOperator.Helpers;
using static SDFProject.Tests.SDFProgramTestHelpers;
using Mode = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SDFPartialCommandNodeProgramMode;

namespace SDFProject.Tests;

public class MaterialEditingTests
{
    private static readonly MaterialLibrary Library = MaterialLibrary.Empty
        .With("chanterelle_cap", MaterialDefinition.Default with { baseColor = new V3(0.9f, 0.5f, 0.1f), roughness = 0.5f })
        .With("eye_white", MaterialDefinition.Default with { roughness = 0.1f });

    private static SDFPartialCommandNodeProgram Initial(MaterialLibrary materials) =>
        new(SdfInteractiveNodeProgramConstants.GetInitialProgram(), new SDFPartialCommandNodeProgramState(Mode.Home)) { Materials = materials };

    private static SDFPartialActionHistory Start(MaterialLibrary? materials = null) => SDFPartialActionHistory.Start(Initial(materials ?? Library));

    private static SDFPartialActionHistory Press(SDFPartialActionHistory history, string letter) =>
        history.Do(history.ActionFor(FingerKeyLayouts.AzertyBeFingerKeyToIndex[letter])
                   ?? throw new InvalidOperationException($"'{letter}' does nothing in {history.Current.state.Mode} mode"));

    private static SDFPartialActionHistory Press(SDFPartialActionHistory history, params string[] letters) => letters.Aggregate(history, Press);

    private static bool IsAvailable(SDFPartialActionHistory history, string letter) =>
        history.IsAvailable(FingerKeyLayouts.AzertyBeFingerKeyToIndex[letter]);

    /// <summary>The key that gives a material on the materials page.</summary>
    private static string LetterOf(SDFPartialActionHistory history, string material) =>
        FingerKeyLayouts.AzertyBeFingerKeyToIndex
            .Single(pair => new AssignMaterial(material).Equals(SDFPartialKeyBindings.ActionFor(history.Current, pair.Value))).Key;

    private static SDFPartialActionHistory WithCap() => Press(Start(), "c", "g").Do(new AssignMaterial("chanterelle_cap"));

    private static MaterialDefinition Cap(SDFPartialActionHistory history) => history.Current.Materials.Resolve("chanterelle_cap");

    [Fact]
    public void GKey_InMaterialMode_ShowsEveryMaterialOnAKey()
    {
        var page = Press(Start(), "c", "g");

        Assert.Equal(Mode.Materials, page.Current.state.Mode);
        Assert.NotEqual(LetterOf(page, "chanterelle_cap"), LetterOf(page, "eye_white"));
    }

    [Fact]
    public void PressingAMaterial_WrapsTheSelectionAndSelectsTheMaterialNode()
    {
        var page = Press(Start(), "c", "g");

        var assigned = Press(page, LetterOf(page, "chanterelle_cap"));

        Assert.Equal(Mode.Material, assigned.Current.state.Mode);
        Assert.Equal("Material(Box(ConstValueV3))", DescribeScene(assigned.Current.interactiveProgram.Program));
        Assert.Equal("chanterelle_cap", SDFPartialCommandNodeProgramOperator.SelectedMaterialName(assigned.Current));
        AssertPointersConsistent(assigned.Current.interactiveProgram.Program);
    }

    [Fact]
    public void AssigningAgain_RenamesTheMaterialInsteadOfNesting()
    {
        var again = Press(WithCap(), "g").Do(new AssignMaterial("eye_white"));

        Assert.Equal("Material(Box(ConstValueV3))", DescribeScene(again.Current.interactiveProgram.Program));
        Assert.Equal("eye_white", SDFPartialCommandNodeProgramOperator.SelectedMaterialName(again.Current));
    }

    [Fact]
    public void WithoutMaterials_TheMaterialsPageIsUnavailable()
    {
        var material = Press(Start(MaterialLibrary.Empty), "c");

        Assert.False(IsAvailable(material, "g"));
    }

    [Fact]
    public void EditingNeedsANodeWithAMaterial()
    {
        Assert.False(IsAvailable(Press(Start(), "c"), "b"));
        Assert.True(IsAvailable(WithCap(), "b"));
    }

    [Fact]
    public void SteppingAParameter_UsesTheZoomLevel_AndUndoRestoresIt()
    {
        // b: edit material, d: roughness, v: finer step (0.1), f: up
        var stepped = Press(WithCap(), "b", "d", "v", "f");

        Assert.Equal(Mode.EditMaterialParameter, stepped.Current.state.Mode);
        Assert.Equal(0.6f, Cap(stepped).roughness, 4);
        Assert.Equal(0.5f, Cap(Press(stepped, "w")).roughness, 4);
    }

    [Fact]
    public void ColourComponents_StepAndStayBetweenZeroAndOne()
    {
        // b: edit material, s: baseColor, f: red up from 0.9 by 0.5
        var redder = Press(WithCap(), "b", "s", "f");

        Assert.Equal(new V3(1, 0.5f, 0.1f), Cap(redder).baseColor);
        Assert.True(IsAvailable(redder, "k"));
    }

    [Fact]
    public void TypingAValue_SetsTheParameter()
    {
        var typing = Press(WithCap(), "b", "s", "s");
        Assert.Equal(Mode.TypeMaterialValue, typing.Current.state.Mode);

        var typed = typing.Do(new InsertText("0.1, 0.2, 0.3")).Do(new ConfirmText());

        Assert.Equal(Mode.EditMaterialParameter, typed.Current.state.Mode);
        Assert.Equal(new V3(0.1f, 0.2f, 0.3f), Cap(typed).baseColor);
    }

    [Fact]
    public void BackKeys_ReturnStepByStep()
    {
        var editing = Press(WithCap(), "b", "d");

        Assert.Equal(Mode.EditMaterial, Press(editing, "q").Current.state.Mode);
        Assert.Equal(Mode.Material, Press(editing, "q", "q").Current.state.Mode);
        Assert.Equal(Mode.Material, Press(Start(), "c", "g", "q").Current.state.Mode);
    }

    [Fact]
    public void Unpaint_RemovesTheMaterialNode()
    {
        var removed = Press(WithCap(), "t");

        Assert.Equal("Box(ConstValueV3)", DescribeScene(removed.Current.interactiveProgram.Program));
    }

    [Fact]
    public void MaterialEdits_ReplayAndSurviveTheHistoryFile()
    {
        var history = Press(WithCap(), "b", "d", "v", "f", "f");

        var loaded = SDFPartialHistorySerializer.Deserialize(SDFPartialHistorySerializer.Serialize(history), Initial(Library));

        Assert.Equal(Cap(history), Cap(loaded));
        Assert.Equal(Cap(history), history.Replay().Materials.Resolve("chanterelle_cap"));
        Assert.Equal(0.7f, Cap(loaded).roughness, 4);
    }

    [Fact]
    public void TheNewMaterialKeys_LeaveThePaletteWhereItWas()
    {
        var paletteKeys = SDFPartialKeyBindings.Bindings
            .Where(b => b.Key.Mode == Mode.Material && b.Value is PaintColor)
            .Select(b => b.Key.Key);

        Assert.DoesNotContain(InputKey.LIndex2Home, paletteKeys);
        Assert.DoesNotContain(InputKey.LIndex2Down, paletteKeys);
        Assert.Equal(SDFPartialKeyBindings.Palette.Count, paletteKeys.Count());
    }
}
