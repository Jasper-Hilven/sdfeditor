using System.Collections.Immutable;
using UndoAction = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.Undo;
using RedoAction = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.Redo;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public sealed record SDFPartialHistoryEntry(int? Parent, ISDFPartialAction Action);

public sealed record SDFPartialActionHistory(
    SDFPartialCommandNodeProgram Initial,
    ImmutableList<SDFPartialHistoryEntry> Entries,
    int? Position,
    SDFPartialCommandNodeProgram Current,
    bool ChoosingRedo = false)
{
    public static SDFPartialActionHistory Start(SDFPartialCommandNodeProgram initial) => new(initial, [], null, initial);

    public ImmutableList<ISDFPartialAction> Actions => Path(Position).Select(i => Entries[i].Action).ToImmutableList();

    public bool CanUndo => Position is not null && !ChoosingRedo;

    public bool CanRedo => Children(Position).Count > 0 && !ChoosingRedo;

    public SDFPartialActionHistory Do(ISDFPartialAction action) => action switch
    {
        UndoAction => Undo(),
        RedoAction => Redo(),
        ChooseRedoBranch choose => ChooseRedo(choose.index),
        CancelRedoChoice => this with { ChoosingRedo = false },
        GoToHistoryEntry goTo => GoTo(goTo.entry),
        _ when ChoosingRedo => this,
        _ => this with
        {
            Current = SDFPartialCommandNodeProgramOperator.Operate(Current, action),
            Entries = Entries.Add(new SDFPartialHistoryEntry(Position, action)),
            Position = Entries.Count,
        },
    };

    public SDFPartialActionHistory Undo()
    {
        if (!CanUndo)
            return this;

        var parent = Entries[Position!.Value].Parent;
        return this with { Position = parent, Current = Replay(Initial, Path(parent).Select(i => Entries[i].Action)) };
    }

    public SDFPartialActionHistory GoTo(int? entry)
    {
        if (entry is { } index && (index < 0 || index >= Entries.Count))
            return this;

        return this with { Position = entry, ChoosingRedo = false, Current = Replay(Initial, Path(entry).Select(i => Entries[i].Action)) };
    }

    public SDFPartialActionHistory Redo()
    {
        if (!CanRedo)
            return this;

        var children = Children(Position);
        return children.Count == 1 ? MoveTo(children[0]) : this with { ChoosingRedo = true };
    }

    public IReadOnlyList<int> RedoOptions => ChoosingRedo ? Children(Position).Reverse().ToArray() : [];

    public SDFPartialActionHistory ChooseRedo(int index)
    {
        var options = RedoOptions;
        return index >= 0 && index < options.Count ? MoveTo(options[index]) with { ChoosingRedo = false } : this;
    }

    public ISDFPartialAction? ActionFor(InputKey key)
    {
        if (!ChoosingRedo)
            return SDFPartialKeyBindings.Bindings.GetValueOrDefault((Current.state.Mode, key));
        if (key == InputKey.LPinkHome)
            return new CancelRedoChoice();

        var index = SDFPartialKeyBindings.RedoChoiceKeys.ToList().IndexOf(key);
        return index >= 0 && index < RedoOptions.Count ? new ChooseRedoBranch(index) : null;
    }

    public bool IsAvailable(InputKey key) => ActionFor(key) switch
    {
        null => false,
        UndoAction => CanUndo,
        RedoAction => CanRedo,
        ChooseRedoBranch or CancelRedoChoice => true,
        _ => SDFPartialCommandNodeProgramEvaluator.IsActionAvailable(Current, key),
    };

    public IReadOnlyList<int> Children(int? entry) =>
        Enumerable.Range(0, Entries.Count).Where(i => Entries[i].Parent == entry).ToArray();

    public IReadOnlyList<int> Path(int? entry)
    {
        var path = new List<int>();
        for (var current = entry; current is { } index; current = Entries[index].Parent)
            path.Add(index);
        path.Reverse();
        return path;
    }

    public SDFPartialActionHistory StepForward(int child) =>
        child >= 0 && child < Entries.Count && Entries[child].Parent == Position
            ? MoveTo(child) with { ChoosingRedo = false }
            : throw new ArgumentException($"Entry {child} does not follow entry {Position?.ToString() ?? "start"}", nameof(child));

    private SDFPartialActionHistory MoveTo(int child) => this with
    {
        Position = child,
        Current = SDFPartialCommandNodeProgramOperator.Operate(Current, Entries[child].Action),
    };

    public SDFPartialCommandNodeProgram Replay() => Replay(Initial, Actions);

    public static SDFPartialCommandNodeProgram Replay(SDFPartialCommandNodeProgram initial, IEnumerable<ISDFPartialAction> actions) =>
        actions.Aggregate(initial, SDFPartialCommandNodeProgramOperator.Operate);
}
