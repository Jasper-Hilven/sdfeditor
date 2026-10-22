using System.Numerics;

namespace SDFPipeline.Levels;

/// <summary>
/// The footprints helicopters keep out of in a room (ticket 30.7): every placed model taller than <see cref="MinHeight"/>, as the box
/// round its bounds seen from above (turned and scaled as placed). Only a level with a room and a <c>machines</c> line has them, and
/// only when it places such a model; baked as <c>battle.helicopterKeepOuts</c>, read by Unity's HelicopterPilot.
/// </summary>
public static class HelicopterKeepOuts
{
    /// <summary>A model reaching higher than this (in metres, as placed) is a keep-out.</summary>
    public const float MinHeight = 6;

    /// <summary>One <c>[minX, minZ, maxX, maxZ]</c> per tall placed model, in the file's order; null where the level has none to give.</summary>
    public static float[][]? Of(LevelDefinition level, IReadOnlyList<PlacedModel> placed, Func<string, ModelFootprint> footprintOf)
    {
        if (level.Room is null || level.Machines is null)
            return null;
        var boxes = new List<float[]>();
        foreach (var model in placed)
        {
            var footprint = footprintOf(model.Model);
            if (footprint.Height * model.Scale <= MinHeight)
                continue;
            Vector2 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (var corner in new[] { footprint.Min, new(footprint.Min.X, footprint.Max.Y), footprint.Max, new(footprint.Max.X, footprint.Min.Y) })
            {
                var at = Placement2D.ToLevel(corner * model.Scale, model.Yaw) + new Vector2(model.Position.X, model.Position.Z);
                min = Vector2.Min(min, at);
                max = Vector2.Max(max, at);
            }
            boxes.Add([min.X, min.Y, max.X, max.Y]);
        }
        return boxes.Count == 0 ? null : boxes.ToArray();
    }
}
