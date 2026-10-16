using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;

namespace SDFProject.SDFInteractiveNode;

public interface ISdfInteractiveNodeProgramOperation;

public record InteractiveAddNodeNextTo(SDFNodeType Type) : ISdfInteractiveNodeProgramOperation;

public record InteractiveReplaceNode(SDFNodeType Type) : ISdfInteractiveNodeProgramOperation;

public record InteractiveInlineVariable() : ISdfInteractiveNodeProgramOperation;

public record InteractiveExtractVariable(string VariableName) : ISdfInteractiveNodeProgramOperation;

public record InteractiveEmptyNode() : ISdfInteractiveNodeProgramOperation;

public record InteractiveCleanEmptyNodesFromUnionsAndIntersections() : ISdfInteractiveNodeProgramOperation;

public record InteractiveAdjustTransform(TransformKind Kind, V3 Delta) : ISdfInteractiveNodeProgramOperation;

public record IncreaseStrZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record DecreaseStrZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record ZoomFullyOutStrZoomLevel(): ISdfInteractiveNodeProgramOperation;

public record ScaleUpAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record ScaleDownAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record ScaleXUpAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record ScaleXDownAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record ScaleYUpAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record ScaleYDownAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record ScaleZUpAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record ScaleZDownAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;

public record TranslateXUpAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record TranslateXDownAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record TranslateYUpAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record TranslateYDownAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record TranslateZUpAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record TranslateZDownAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;

public record RotateXUpAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record RotateXDownAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record RotateYUpAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record RotateYDownAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record RotateZUpAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record RotateZDownAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;

public record SelectNext() : ISdfInteractiveNodeProgramOperation;

public record SelectPrevious() : ISdfInteractiveNodeProgramOperation;
public record SelectNextSibling(): ISdfInteractiveNodeProgramOperation;
public record SelectPreviousSibling(): ISdfInteractiveNodeProgramOperation;
public record SelectParent(): ISdfInteractiveNodeProgramOperation;
public record SelectFirstChild(): ISdfInteractiveNodeProgramOperation;

public record ToggleSmoothness(): ISdfInteractiveNodeProgramOperation;

public record InteractiveWrapInSubtraction() : ISdfInteractiveNodeProgramOperation;

public record InteractiveWrapInIntersection() : ISdfInteractiveNodeProgramOperation;

public record InteractiveDissolveNode() : ISdfInteractiveNodeProgramOperation;

public record InteractiveSetNodeParameter(string Parameter, float[] Components) : ISdfInteractiveNodeProgramOperation;

public record AdjustNodeParameterAtCurrentZoomLevel(string Parameter, int Component, float Sign) : ISdfInteractiveNodeProgramOperation;
