using System.Collections.Immutable;
using System.Globalization;
using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode.InteractiveCommands;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Helpers;
using SDFProject.SDFProgramOperator.Operations;
using Mode = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SDFPartialCommandNodeProgramMode;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public sealed class SDFExampleScript(SDFPartialCommandNodeProgram initial)
{
    private const float Tolerance = 1e-3f;

    private readonly List<ISDFPartialAction> actions = [];

    public SDFPartialCommandNodeProgram Program { get; private set; } = initial;

    public ImmutableList<SDFPartialHistoryEntry> Entries =>
        actions.Select((action, i) => new SDFPartialHistoryEntry(i == 0 ? null : i - 1, action)).ToImmutableList();

    public SDFExampleScript Do(params ISDFPartialAction[] steps)
    {
        foreach (var step in steps)
        {
            if (!SDFPartialCommandNodeProgramEvaluator.IsActionAvailable(Program, step))
                throw new InvalidOperationException($"{step} is not available in {Program.state.Mode} mode after {actions.Count} action(s)");
            Program = SDFPartialCommandNodeProgramOperator.Operate(Program, step);
            actions.Add(step);
        }
        return this;
    }

    public SDFExampleScript Add(ISDFPartialAction nodeType) =>
        Do(new AddNode()).Do(OnPage(Mode.AddNode, nodeType) ? [] : [new ShowMoreNodeTypes()]).Do(nodeType);

    public SDFExampleScript Replace(ISDFPartialAction nodeType) =>
        Do(new ReplaceNode()).Do(OnPage(Mode.ReplaceNode, nodeType) ? [] : [new ShowMoreReplaceNodeTypes()]).Do(nodeType);

    public SDFExampleScript Set(string parameter, params float[] values)
    {
        Do(new EditNode());
        if (Program.state.Mode == Mode.EditNode)
        {
            var index = SDFPartialCommandNodeProgramOperator.Parameters(Program).ToList().FindIndex(p => p.Name == parameter);
            Do(index switch
            {
                0 => new EditParameter1(),
                1 => new EditParameter2(),
                2 => new EditParameter3(),
                3 => new EditParameter4(),
                _ => throw new InvalidOperationException($"Parameter '{parameter}' cannot be reached from the edit menu"),
            });
        }
        if (Program.state.EditedParameter != parameter)
            throw new InvalidOperationException($"Editing '{Program.state.EditedParameter}' instead of '{parameter}'");

        Type(values);
        Do(new StopEditingParameter());
        return Program.state.Mode == Mode.EditNode ? Do(new SwitchToHomeMode()) : this;
    }

    public SDFExampleScript Paint(string colorName, params float[] exact)
    {
        Do(new SwitchToMaterialMode(), SDFPartialKeyBindings.Palette.Single(c => c.name == colorName));
        if (exact.Length == 0)
            return Do(new SwitchToHomeMode());

        Do(new EditColor());
        Type(exact);
        return Do(new StopEditingParameter());
    }

    public SDFExampleScript PaintRgb(V3 color)
    {
        var nearest = SDFPartialKeyBindings.Palette.MinBy(c => Distance(c.color, color))!;
        return Distance(nearest.color, color) < Tolerance
            ? Paint(nearest.name)
            : Paint(nearest.name, Round(color.x, 0.001f), Round(color.y, 0.001f), Round(color.z, 0.001f));
    }

    public SDFExampleScript Top()
    {
        var scene = Program.interactiveProgram.Program.scene;
        while (ParentId() is { } parent && !(parent == PointerTables.PointerId(scene) && ParentNode() is Union))
            Do(new SelectParent());
        return this;
    }

    public SDFExampleScript Move(float x, float y, float z) =>
        Transform(new SwitchToTranslateMode(), TransformKind.Translate, new V3(Round(x, 0.005f), Round(y, 0.005f), Round(z, 0.005f)),
            SdfInteractiveNodeProgramOperator.TranslateZoomScaleToTranslation, 1,
            [(new TranslateXDown(), new TranslateXUp()), (new TranslateYDown(), new TranslateYUp()), (new TranslateZDown(), new TranslateZUp())]);

    public SDFExampleScript Turn(float xDegrees, float yDegrees, float zDegrees) =>
        Transform(new SwitchToRotateMode(), TransformKind.Rotate, new V3(Round(xDegrees, 0.1f), Round(yDegrees, 0.1f), Round(zDegrees, 0.1f)),
            SdfInteractiveNodeProgramOperator.TranslateZoomScaleToDegree, 180 / MathF.PI,
            [(new RotateXDown(), new RotateXUp()), (new RotateYDown(), new RotateYUp()), (new RotateZDown(), new RotateZUp())]);

    public SDFExampleScript Resize(float x, float y, float z) =>
        Transform(new SwitchToScaleMode(), TransformKind.Scale, new V3(x, y, z),
            SdfInteractiveNodeProgramOperator.TranslateZoomScaleToScale, 1,
            [(new ScaleXDown(), new ScaleXUp()), (new ScaleYDown(), new ScaleYUp()), (new ScaleZDown(), new ScaleZUp())]);

    public SDFExampleScript Resize(float factor) =>
        Transform(new SwitchToScaleMode(), TransformKind.Scale, new V3(factor, factor, factor),
            SdfInteractiveNodeProgramOperator.TranslateZoomScaleToScale, 1, [(new ScaleDown(), new ScaleUp())]);

    public SDFExampleScript Smooth(float smoothness) => Do(new ToggleSmoothness()).Set("smoothness", smoothness);

    public SDFExampleScript Subtract() => Do(new SubtractNode());

    public SDFExampleScript Intersect() => Do(new IntersectNode());

    public SDFExampleScript Extract(string name) => Do(new ExtractVariable(), new InsertText(name), new ConfirmText());

    public SDFExampleScript Parent(int times = 1)
    {
        for (var i = 0; i < times; i++)
            Do(new SelectParent());
        return this;
    }

    public SDFExampleScript Child() => Do(new SelectFirstChild());

    public SDFExampleScript Next(int times = 1)
    {
        for (var i = 0; i < times; i++)
            Do(new SelectNext());
        return this;
    }

    public SDFExampleScript Outer()
    {
        while (ParentNode() is Translate or Rotate or Scale or Color)
            Do(new SelectParent());
        return this;
    }

    private Evaluable? ParentNode() =>
        ParentId() is { } parent ? new PointerTables(Program.interactiveProgram.Program.pointers).Get(parent) : null;

    private string? ParentId()
    {
        var interactive = Program.interactiveProgram;
        return interactive.SelectedNodeId is { } id ? new PointerTables(interactive.Program.pointers).ParentOf(id) : null;
    }

    private static float Round(float value, float step) => MathF.Round(value / step) * step;

    private static float Distance(V3 a, V3 b) => MathF.Abs(a.x - b.x) + MathF.Abs(a.y - b.y) + MathF.Abs(a.z - b.z);

    private void Type(float[] values) =>
        Do(new TypeParameterValue(),
            new InsertText(string.Join(", ", values.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)))),
            new ConfirmText());

    private SDFExampleScript Transform(ISDFPartialAction enter, TransformKind kind, V3 target, Func<int, float> stepAt, float unit,
        (ISDFPartialAction Down, ISDFPartialAction Up)[] axes)
    {
        Do(enter);
        var targets = new[] { target.x, target.y, target.z };
        for (var axis = 0; axis < axes.Length; axis++)
        {
            for (var zoom = 0; zoom <= SdfInteractiveNodeProgramOperator.MaxStrZoomLevel; zoom++)
            {
                for (var guard = 0; guard < 100; guard++)
                {
                    var difference = targets[axis] - Component(Current(kind), axis) * unit;
                    var step = stepAt(zoom);
                    if (MathF.Abs(difference) < step - Tolerance)
                        break;
                    ZoomTo(zoom);
                    Do(difference > 0 ? axes[axis].Up : axes[axis].Down);
                }
            }
            if (MathF.Abs(targets[axis] - Component(Current(kind), axis) * unit) > 0.01f)
                throw new InvalidOperationException($"{kind} could not reach {target}");
        }
        ZoomTo(0);
        return Do(new SwitchToHomeMode());
    }

    private V3 Current(TransformKind kind) =>
        ApplyAdjustTransform.Current(Program.interactiveProgram.Program, Program.interactiveProgram.SelectedNodeId!, kind);

    private static float Component(V3 v, int axis) => axis switch { 0 => v.x, 1 => v.y, _ => v.z };

    private void ZoomTo(int zoom)
    {
        while (Program.interactiveProgram.StrZoomLevel < zoom)
            Do(new IncreaseZoomLevel());
        while (Program.interactiveProgram.StrZoomLevel > zoom)
            Do(new DecreaseZoomLevel());
    }

    private static bool OnPage(Mode mode, ISDFPartialAction nodeType) =>
        SDFPartialKeyBindings.Bindings.Any(b => b.Key.Mode == mode && b.Value.Equals(nodeType));
}
