using System.Collections.Immutable;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public sealed record SDFPartialActionHistory(
    SDFPartialCommandNodeProgram Initial,
    ImmutableList<ISDFPartialAction> Actions,
    SDFPartialCommandNodeProgram Current)
{
    public static SDFPartialActionHistory Start(SDFPartialCommandNodeProgram initial) => new(initial, [], initial);

    public SDFPartialActionHistory Do(ISDFPartialAction action) =>
        this with { Actions = Actions.Add(action), Current = SDFPartialCommandNodeProgramOperator.Operate(Current, action) };

    public SDFPartialCommandNodeProgram Replay() => Replay(Initial, Actions);

    public static SDFPartialCommandNodeProgram Replay(SDFPartialCommandNodeProgram initial, IEnumerable<ISDFPartialAction> actions) =>
        actions.Aggregate(initial, SDFPartialCommandNodeProgramOperator.Operate);
}
