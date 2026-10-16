using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Helpers;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class ReplaceNodeTests
{
    [Fact]
    public void ReplacesNodeWithDefaultsAndRemovesTheOldPointers()
    {
        var program = Program(new Union(Sphere(1), new Round(new Elongate(Sphere(2), new ConstValueV3(new V3(1, 0, 0))), new ConstValueFloat(0.1f))));
        var roundId = program.pointers.SDFEvaluablePointers.Single(e => e.Value is Round).Key;
        var oldIds = new PointerTables(program.pointers)
            .Subtree(new SDFEvaluablePointerable(roundId)).Where(id => id != roundId).ToList();

        var result = SdfProgramOperator.ApplyOperation(program, new ReplaceNode(roundId, SDFNodeType.Sphere));

        Assert.Equal("Union(Sphere(1), Sphere(0.5))", DescribeScene(result));
        Assert.True(result.pointers.SDFEvaluablePointers[roundId] is Sphere);
        Assert.NotEmpty(oldIds);
        Assert.All(oldIds, id => Assert.False(result.pointers.ContainsPointer(id)));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void NodeWithManyNestedChildren_RemovesEveryDescendant()
    {
        var tree = new Union(
            Sphere(1),
            new SmoothUnion(new ConstValueFloat(0.2f), Sphere(2), new Round(Sphere(3), new ConstValueFloat(0.1f))),
            new Revolution(new Union2D(new Circle(new ConstValueFloat(1)), new Translate2D(new Circle(new ConstValueFloat(2)), new ConstValueV2(new V2(1, 0)))), new ConstValueFloat(0.5f)));
        var program = Program(new Union(Sphere(9), tree));
        var tables = new PointerTables(program.pointers);
        var treeId = program.pointers.SDFEvaluablePointers.Single(e => e.Value is Union { primitives.Length: 3 }).Key;
        var descendants = tables.Subtree(new SDFEvaluablePointerable(treeId)).Where(id => id != treeId).ToList();

        var result = SdfProgramOperator.ApplyOperation(program, new ReplaceNode(treeId, SDFNodeType.Sphere));

        Assert.Equal("Union(Sphere(9), Sphere(0.5))", DescribeScene(result));
        Assert.Contains(descendants, id => program.pointers.SDF2DEvaluablePointers.ContainsKey(id));
        Assert.All(descendants, id => Assert.False(result.pointers.ContainsPointer(id)));
        AssertPointersConsistent(result);
        var fresh = Program(new Union(Sphere(9), Sphere(0.5f)));
        Assert.Equal(PointerCount(fresh), PointerCount(result));
    }

    [Fact]
    public void SceneRoot_RemovesTheWholeOldScene()
    {
        var program = Program(new Union(Sphere(1), new Subtraction(Sphere(2), new Union(Sphere(3), Sphere(4)))));
        var rootId = PointerTables.PointerId(program.scene)!;

        var result = SdfProgramOperator.ApplyOperation(program, new ReplaceNode(rootId, SDFNodeType.Torus));

        Assert.Equal("Torus(0.3, 0.5)", DescribeScene(result));
        Assert.Equal(rootId, PointerTables.PointerId(result.scene));
        Assert.Equal(1, PointerCount(result));
        AssertPointersConsistent(result);
    }

    private static int PointerCount(SDFProgram program) =>
        program.pointers.SDFEvaluablePointers.Count + program.pointers.SDF2DEvaluablePointers.Count +
        program.pointers.FloatEvaluablePointers.Count + program.pointers.IntegerEvaluablePointers.Count;

    [Fact]
    public void ReplacingAVariableReference_KeepsTheVariable()
    {
        var variable = new VariableDeclaration("v", "var", Sphere(1));
        var program = Program(new Empty(), Function(new Union(new Round(new VariableReferenceSDF("var"), new ConstValueFloat(0.1f)), Sphere(2)), variable));
        var roundId = program.pointers.SDFEvaluablePointers.Single(e => e.Value is Round).Key;

        var result = SdfProgramOperator.ApplyOperation(program, new ReplaceNode(roundId, SDFNodeType.Sphere));

        Assert.Equal("Union(Sphere(0.5), Sphere(2))", DescribeBody(result));
        Assert.Single(result.Functions[0].variables);
        AssertPointersConsistent(result);
    }

    [Fact]
    public void UnknownNode_Throws()
    {
        var program = Program(new Union(Sphere(1)));

        Assert.Throws<KeyNotFoundException>(() => SdfProgramOperator.ApplyOperation(program, new ReplaceNode("missing", SDFNodeType.Box)));
    }
}
