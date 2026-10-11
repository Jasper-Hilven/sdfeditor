using SDFProject.Definitions;

namespace SDFProject.SDFInteractiveNode;

public interface SDFInteractiveNodeProgramOperation;

public record InteractiveAddNodeNextTo(SDFNodeType type) : SDFInteractiveNodeProgramOperation;

public record InteractiveInlineVariable() : SDFInteractiveNodeProgramOperation;

public record InteractiveExtractVariable(string variableName) : SDFInteractiveNodeProgramOperation;

public record InteractiveEmptyNode() : SDFInteractiveNodeProgramOperation;

public record InteractiveCleanEmptyNodesFromUnionsAndIntersections() : SDFInteractiveNodeProgramOperation;

public record InteractiveAdjustScaleTranslateRotate(float scale, V3 translation, V3 rotation) : SDFInteractiveNodeProgramOperation;

public record IncreaseStrZoomLevel(): SDFInteractiveNodeProgramOperation;
public record DecreaseStrZoomLevel(): SDFInteractiveNodeProgramOperation;
public record ZoomFullyOutStrZoomLevel(): SDFInteractiveNodeProgramOperation;

public record ScaleUpAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;
public record ScaleDownAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;

public record TranslateXUpAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;
public record TranslateXDownAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;
public record TranslateYUpAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;
public record TranslateYDownAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;
public record TranslateZUpAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;
public record TranslateZDownAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;

public record RotateXUpAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;
public record RotateXDownAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;
public record RotateYUpAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;
public record RotateYDownAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;
public record RotateZUpAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;
public record RotateZDownAtCurrentZoomLevel(): SDFInteractiveNodeProgramOperation;
