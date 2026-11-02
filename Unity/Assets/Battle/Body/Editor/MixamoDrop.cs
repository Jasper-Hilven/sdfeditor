using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SdfBody.Editor
{
    /// <summary>Validates a Mixamo download dropped into <see cref="SoldierClips.MixamoFolder"/> (ticket 03).</summary>
    public static class MixamoDrop
    {
        /// <summary>Set by tests that copy temporary drops: the postprocessor then neither rebuilds the clip map nor logs.</summary>
        public static bool SuppressMapRebuild;

        /// <summary>Every .fbx in the Mixamo folder.</summary>
        public static IEnumerable<string> Drops()
        {
            var guids = AssetDatabase.FindAssets("", new[] { SoldierClips.MixamoFolder.TrimEnd('/') });
            return guids.Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        /// <summary>The problems of one FBX; empty means good.</summary>
        public static List<string> Validate(string assetPath)
        {
            var problems = new List<string>();
            var file = Path.GetFileNameWithoutExtension(assetPath);
            if (!(AssetImporter.GetAtPath(assetPath) is ModelImporter mi))
            {
                problems.Add("not imported as a model");
                return problems;
            }
            if (mi.animationType != ModelImporterAnimationType.Generic)
                problems.Add($"animation type is {mi.animationType}, must be Generic");

            var all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (all.Any(o => o is Mesh || o is SkinnedMeshRenderer))
                problems.Add("the file holds a mesh: re-download from Mixamo with Skin \"Without Skin\"; the Synty mesh must never be in this repo");

            if (!SoldierClips.IsConventionName(file))
                problems.Add($"the file name \"{file}\" is not a convention name (soldier_<group>_<name>, lowercase letters, digits, underscores)");

            var clips = all.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToList();
            if (clips.Count != 1)
            {
                problems.Add($"expected exactly one animation clip, found {clips.Count}");
                return problems;
            }
            var clip = clips[0];
            if (clip.name != file)
                problems.Add($"the clip is named \"{clip.name}\", must equal the file name \"{file}\"");
            if (clip.length <= 0f)
                problems.Add("the clip has no length");

            var travel = SoldierAnimationPostprocessor.RootTravel(clip);
            if (travel > SoldierAnimationPostprocessor.RootTravelTolerance)
                problems.Add($"the Root bone travels {travel:F2} m across the clip (root motion): the game moves the body, the import should have baked it out");

            var unknown = AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Distinct()
                .Where(p => !SyntySkeletonPaths.IsPath(p)).ToList();
            if (unknown.Count > 0)
            {
                var msg = $"{unknown.Count} curve path(s) are not Synty bones: {string.Join(", ", unknown.Take(5))}";
                if (unknown.Any(p => p.Contains("mixamorig:")))
                    msg += " (mixamorig: names: the upload to Mixamo must be the Synty FBX so Mixamo keeps the Synty bone names)";
                problems.Add(msg);
            }

            if (SoldierClips.IsConventionName(file))
            {
                var loops = AnimationUtility.GetAnimationClipSettings(clip).loopTime;
                var want = SoldierClips.LoopsByName(file);
                if (loops != want)
                    problems.Add($"loopTime is {loops}, {file} must be {want}");
            }
            return problems;
        }
    }
}
