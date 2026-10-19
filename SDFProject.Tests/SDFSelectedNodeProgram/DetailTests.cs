using System.Numerics;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using SDFProject.SDFProgramOperator.Helpers;
using static SDFProject.Tests.SDFProgramTestHelpers;
using Detail = SDFProject.ProgramDefinition.Detail;
using DissolveNode = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.DissolveNode;
using EmptyNode = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.EmptyNode;
using ExtractVariable = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.ExtractVariable;
using ReplaceNode = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.ReplaceNode;
using SelectFirstChild = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SelectFirstChild;
using SelectParent = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SelectParent;
using ToggleSmoothness = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.ToggleSmoothness;

namespace SDFProject.Tests;

public class DetailTests
{
    private static SDFPartialActionHistory Start() => SDFPartialActionHistory.Start(new(
        SdfInteractiveNodeProgramConstants.GetInitialProgram(),
        new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home)));

    private static Evaluable Selected(SDFPartialActionHistory history)
    {
        var interactive = history.Current.interactiveProgram;
        return new PointerTables(interactive.Program.pointers).Get(interactive.SelectedNodeId!);
    }

    private static Evaluable Scene(SDFPartialActionHistory history)
    {
        var program = history.Current.interactiveProgram.Program;
        return new PointerTables(program.pointers).Get(PointerTables.PointerId(program.scene)!);
    }

    private static bool Available(SDFPartialActionHistory history, ISDFPartialAction action) =>
        SDFPartialCommandNodeProgramEvaluator.IsActionAvailable(history.Current, action);

    private static string[] View(SDFPartialActionHistory history) => new YamlRender().GetRender(history.Current.interactiveProgram);

    private static SDFEvaluable Painted(SDFEvaluable shape, float r, float g, float b) => new Color(shape, new ConstValueV3(new V3(r, g, b)));

    /// <summary>A shape with two colours and a material, so a node that lost the materials below it would show.</summary>
    private static SDFEvaluable ColouredShape() => new SmoothUnion(new ConstValueFloat(0.2f),
        Painted(Sphere(1), 0.9f, 0.2f, 0.1f),
        new Translate(new Material(Painted(Sphere(0.5f), 0.1f, 0.8f, 0.3f), "tree_bark"), new ConstValueV3(new V3(1, 0.4f, 0))));

    [Fact]
    public void N_InHome_GivesTheModelADetail()
    {
        var key = FingerKeyLayouts.AzertyBeFingerKeyToIndex["n"];

        Assert.Equal(key, FingerKeyLayouts.QwertyFingerKeyToIndex["n"]);
        Assert.Equal(new DetailNode(), SDFPartialKeyBindings.Bindings[(SDFPartialCommandNodeProgramMode.Home, key)]);
    }

    [Fact]
    public void DetailNode_WrapsTheRootAndSelectsTheDetail()
    {
        var history = Start().Do(new DetailNode());

        var detail = Assert.IsType<Detail>(Selected(history));
        Assert.Same(Selected(history), Scene(history));
        Assert.Equal(new ConstValueFloat(0.5f), detail.detail);
        Assert.Equal(SDFPartialCommandNodeProgramMode.Home, history.Current.state.Mode);
        Assert.Contains(View(history), line => line.Contains("Box"));
    }

    [Fact]
    public void DetailNode_IsOnlyAvailableAtTheRoot_AndOnlyOnce()
    {
        var withSphere = Start().Do(new AddSphere());
        Assert.IsType<Sphere>(Selected(withSphere));

        Assert.False(Available(withSphere, new DetailNode()));
        Assert.True(Available(withSphere.Do(new SelectParent()), new DetailNode()));
        Assert.False(Available(withSphere.Do(new SelectParent()).Do(new DetailNode()), new DetailNode()));
    }

    [Fact]
    public void OnADetail_OnlyItsDetailCanBeEditedAndItCanBeDissolved()
    {
        var history = Start().Do(new DetailNode());

        Assert.True(Available(history, new EditNode()));
        Assert.True(Available(history, new DissolveNode()));
        Assert.True(Available(history, new SelectFirstChild()));
        // Each of these would move the Detail away from the root, where it does nothing and a file cannot hold it
        ISDFPartialAction[] burying =
        [
            new AddNode(), new ReplaceNode(), new EmptyNode(), new SubtractNode(), new IntersectNode(), new DistortNode(),
            new ExtractVariable(), new ToggleSmoothness(), new TranslateXUp(), new ScaleUp(),
            new RotateXUp(), SDFPartialKeyBindings.Palette[0], new AssignMaterial("tree_bark"),
        ];
        Assert.All(burying, action => Assert.False(Available(history, action), $"{action.GetType().Name} is available on a Detail"));
        // The shape below it can still be edited as before
        Assert.True(Available(history.Do(new SelectFirstChild()), new AddNode()));
    }

    [Theory]
    [InlineData("0.25", 0.25f)]
    [InlineData("10", Detail.MaxDetail)]
    [InlineData("0", Detail.MinDetail)]
    public void Detail_IsEditable_WithinTheRangeFilesAccept(string typed, float expected)
    {
        var history = Start().Do(new DetailNode()).Do(new EditNode())
            .Do(new TypeParameterValue()).Do(new InsertText(typed)).Do(new ConfirmText());

        Assert.Equal(["detail"], SDFPartialCommandNodeProgramOperator.Parameters(history.Current).Select(p => p.Name));
        Assert.Equal(new ConstValueFloat(expected), Assert.IsType<Detail>(Selected(history)).detail);
    }

    [Fact]
    public void Dissolve_RemovesTheDetailAgain()
    {
        var plain = Start();

        var dissolved = plain.Do(new DetailNode()).Do(new DissolveNode());

        Assert.Equal(View(plain), View(dissolved));
    }

    [Fact]
    public void Detail_KeepsTheDistancesAndMaterialsOfItsShape_OnTheCpu()
    {
        var plain = new SdfCalculatorCpu().Prepare(Program(ColouredShape()));
        var detailed = new SdfCalculatorCpu().Prepare(Program(new Detail(ColouredShape(), new ConstValueFloat(0.5f))));

        for (var i = 0; i < 100; i++)
        {
            var p = new Vector3(MathF.Sin(i * 0.37f) * 1.6f, MathF.Cos(i * 0.53f) * 1.3f, MathF.Sin(i * 0.71f + 1));
            Assert.Equal(plain.Distance(p), detailed.Distance(p));
            Assert.Equal(plain.Surface(p).Materials, detailed.Surface(p).Materials);
        }
    }

    [Fact]
    public void Detail_KeepsTheShaderOfItsShape()
    {
        var generator = new Webgl2CodeGenerator(MaterialLibrary.Empty.With("tree_bark", MaterialDefinition.Default));

        Assert.Equal(generator.GenerateShaderCode(Program(ColouredShape())),
            generator.GenerateShaderCode(Program(new Detail(ColouredShape(), new ConstValueFloat(0.5f)))));
    }
}
