using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

namespace SDFProject.Tests;

public class SDFPartialActionLabelsTests
{
    public static TheoryData<string> AllActions() => new(typeof(ISDFPartialAction).Assembly.GetTypes()
        .Where(t => t.IsClass && typeof(ISDFPartialAction).IsAssignableFrom(t))
        .Select(t => t.Name));

    private static ISDFPartialAction Create(string name)
    {
        var type = typeof(ISDFPartialAction).Assembly.GetTypes().Single(t => t.Name == name && typeof(ISDFPartialAction).IsAssignableFrom(t));
        return type == typeof(InsertText) ? new InsertText("a")
            : type == typeof(ChooseRedoBranch) ? new ChooseRedoBranch(0)
            : type == typeof(GoToHistoryEntry) ? new GoToHistoryEntry(null)
            : type == typeof(PaintColor) ? SDFPartialKeyBindings.Palette[0]
            : type == typeof(AssignMaterial) ? new AssignMaterial("chanterelle_cap")
            : type == typeof(PickMaterialParameter) ? new PickMaterialParameter(0)
            : type == typeof(ApplyInteractiveOperation) ? new ApplyInteractiveOperation(new SDFInteractiveNode.SelectNext())
            : (ISDFPartialAction)Activator.CreateInstance(type)!;
    }

    [Theory]
    [MemberData(nameof(AllActions))]
    public void EveryAction_HasAShortLabel(string name)
    {
        var label = SDFPartialActionLabels.Label(Create(name));

        Assert.False(string.IsNullOrEmpty(label.Symbol));
        Assert.InRange(label.Text.Length, 1, 20);
    }
}
