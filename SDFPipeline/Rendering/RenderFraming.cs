using System.Numerics;
using SDFProject.MeshExport;
using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;
using Plane = SDFProject.ProgramDefinition.Plane;

namespace SDFPipeline.Rendering;

/// <summary>The renderer's camera: it orbits <see cref="Target"/>, and the axis views show 2 × <see cref="OrthoScale"/> metres high.</summary>
public sealed record RenderCamera(float[] Target, float Distance, float Yaw, float Pitch, float OrthoScale)
{
    public const float DefaultYaw = 0.7f;
    public const float DefaultPitch = 0.35f;

    /// <summary>The camera the IDE starts with, for when there is nothing finite to frame.</summary>
    public static readonly RenderCamera Default = new([0, 0, 0], 8, DefaultYaw, DefaultPitch, 8 * 0.5f / 1.6f);
}

public sealed record RenderFraming(RenderCamera Camera, SdfBounds? Bounds);

public static class RenderFramer
{
    /// <summary>The perspective view's half field of view: its rays are uv + 1.6 × forward, with uv.y in [-0.5, 0.5].</summary>
    private static readonly float HalfFieldOfView = MathF.Atan(0.5f / 1.6f);

    private const float Margin = 1.15f;
    private const float MinimumSize = 0.01f;

    /// <summary>
    /// Frames the model's bounds. Ground planes and other infinite shapes are left out first, otherwise every model on a floor would be
    /// framed as 100 metres wide.
    /// </summary>
    public static RenderFraming Frame(SDFProgram program)
    {
        var bounds = ModelBounds(program);
        if (bounds is null)
            return new RenderFraming(RenderCamera.Default, null);

        var center = (bounds.Min + bounds.Max) * 0.5f;
        var longestSide = MathF.Max(bounds.LongestSide, MinimumSize);
        var radius = MathF.Max(bounds.Size.Length() * 0.5f, MinimumSize);
        var camera = new RenderCamera(
            [center.X, center.Y, center.Z],
            radius * Margin / MathF.Sin(HalfFieldOfView),
            RenderCamera.DefaultYaw,
            RenderCamera.DefaultPitch,
            longestSide * Margin * 0.5f);
        return new RenderFraming(camera, bounds);
    }

    /// <summary>The bounds of the model without its ground planes and other infinite shapes, or null when nothing finite is left.</summary>
    internal static SdfBounds? ModelBounds(SDFProgram program) => FiniteBounds(WithoutInfiniteShapes(program)) ?? FiniteBounds(program);

    private static SdfBounds? FiniteBounds(SDFProgram program)
    {
        var bounds = SdfMeshExporter.FindBounds(program);
        var limit = SdfBoundsFinder.DefaultHalfExtent * 0.99f;
        return bounds is null || Vector3.Max(Vector3.Abs(bounds.Min), Vector3.Abs(bounds.Max)) is var extent
            && MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z)) >= limit
                ? null
                : bounds;
    }

    private static SDFProgram WithoutInfiniteShapes(SDFProgram program)
    {
        var tables = new PointerTables(program.pointers);

        Evaluable Strip(Evaluable node) => node is Plane or InfiniteCylinder or InfiniteCone or Repetition
            ? new Empty()
            : EvaluableChildren.MapChildren(node, Strip);

        return program.MapRoots(root => Strip(tables.Expand(root)));
    }
}
