using SDFProject.ProgramDefinition;

namespace SDFProject.SDFInteractiveNode;

public interface ISdfInteractiveNodeProgramOperation;

public record InteractiveAddNodeNextTo(SDFNodeType Type) : ISdfInteractiveNodeProgramOperation;

public record InteractiveInlineVariable() : ISdfInteractiveNodeProgramOperation;

public record InteractiveExtractVariable(string VariableName) : ISdfInteractiveNodeProgramOperation;

public record InteractiveEmptyNode() : ISdfInteractiveNodeProgramOperation;

public record InteractiveCleanEmptyNodesFromUnionsAndIntersections() : ISdfInteractiveNodeProgramOperation;

public record InteractiveAdjustScaleTranslateRotate(float Scale, V3 Translation, V3 Rotation) : ISdfInteractiveNodeProgramOperation;

public record IncreaseStrZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record DecreaseStrZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record ZoomFullyOutStrZoomLevel(): ISdfInteractiveNodeProgramOperation;

public record ScaleUpAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;
public record ScaleDownAtCurrentZoomLevel(): ISdfInteractiveNodeProgramOperation;

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

public record SelectNextSibling(): ISdfInteractiveNodeProgramOperation;
public record SelectPreviousSibling(): ISdfInteractiveNodeProgramOperation;
public record SelectParent(): ISdfInteractiveNodeProgramOperation;
public record SelectFirstChild(): ISdfInteractiveNodeProgramOperation;
