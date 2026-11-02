using System.Numerics;

namespace Kk.Rigging;

/// <summary>
/// One bone's animation sampled at fixed times. <see cref="Rotations"/> are rotations in the bone's frame about its head, relative to
/// the rest pose (identity = rest), as in <see cref="BonePose"/>; <see cref="Translations"/> likewise in the bone's frame.
/// Either may be null when the clip does not move the bone that way.
/// </summary>
public sealed record BakedTrack(string Bone, float[] Times, Quaternion[]? Rotations, Vector3[]? Translations);

/// <summary>A clip ready for the GLB: the tracks sampled at the clip's frame rate, and the procedural layers as JSON for the node extras.</summary>
/// <param name="LayersJson">A JSON array of the clip's procedural layers (<c>sway</c>, <c>breathe</c>, <c>wobble</c>), or null for none; written to the animation's extras.</param>
public sealed record BakedClip(string Name, float Length, bool Loop, IReadOnlyList<BakedTrack> Tracks, string? LayersJson);
