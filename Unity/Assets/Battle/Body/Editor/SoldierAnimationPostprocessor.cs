using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SdfBody.Editor
{
    /// <summary>Imports every FBX under the soldier animation folder as a Generic, avatar-less, one-clip animation file (ticket 03).</summary>
    public sealed class SoldierAnimationPostprocessor : AssetPostprocessor
    {
        private static bool IsOurs(string path) =>
            path.StartsWith(SoldierClips.Folder, StringComparison.Ordinal) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        private static bool IsDrop(string path) =>
            path.StartsWith(SoldierClips.MixamoFolder, StringComparison.Ordinal) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        /// <summary>The clip name an FBX gets: the catalog name for a catalog file, else the file name.</summary>
        public static string ClipNameOf(string assetPath)
        {
            var rel = assetPath.Substring(SoldierClips.Folder.Length);
            var e = SoldierClips.All.FirstOrDefault(x => x.File == rel);
            return e != null ? e.Name : Path.GetFileNameWithoutExtension(assetPath);
        }

        private void OnPreprocessModel()
        {
            if (!IsOurs(assetPath) || !(assetImporter is ModelImporter m))
                return;
            m.animationType = ModelImporterAnimationType.Generic;
            m.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            m.importAnimation = true;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importCameras = false;
            m.importLights = false;
            m.importVisibility = false;
            m.importBlendShapes = false;
            m.importConstraints = false;
            m.motionNodeName = "";
        }

        private void OnPreprocessAnimation()
        {
            if (!IsOurs(assetPath) || !(assetImporter is ModelImporter m))
                return;
            var defaults = m.defaultClipAnimations;
            if (defaults == null || defaults.Length == 0)
                return;
            var name = ClipNameOf(assetPath);
            var want = defaults[0];
            want.name = name;
            want.loopTime = SoldierClips.LoopsByName(name);
            want.loopPose = false;

            var have = m.clipAnimations;
            if (have != null && have.Length == 1 && have[0].name == want.name && have[0].takeName == want.takeName
                && have[0].loopTime == want.loopTime && have[0].loopPose == want.loopPose
                && Mathf.Approximately(have[0].firstFrame, want.firstFrame) && Mathf.Approximately(have[0].lastFrame, want.lastFrame))
                return;
            m.clipAnimations = new[] { want };
        }

        /// <summary>
        /// A Mixamo pack download carries root motion (no "In Place" in a pack): the Root bone travels. The game moves the body with the
        /// CharacterController, so the net horizontal travel of the Root's position curves is taken out (a straight ramp from the first to
        /// the last key, tangents too); the rest of the curve (the bob, the sway, the jump's rise) stays, and the first and last pose
        /// still match for the loop. An In Place clip has no net travel and is left as it is.
        /// </summary>
        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (!IsDrop(assetPath))
                return;
            var n = StripRootTravel(clip, out var metres);
            if (n > 0)
                Debug.Log($"Mixamo drop {assetPath}: root motion removed ({metres:F2} m of horizontal travel baked out)");
        }

        /// <summary>Net horizontal travel (metres, x and z of the shallowest position-animated bone, first to last key) of a clip; 0 when it has none.</summary>
        public static float RootTravel(AnimationClip clip)
        {
            var path = RootPath(clip);
            if (path == null)
                return 0f;
            float x = Net(clip, path, "m_LocalPosition.x"), z = Net(clip, path, "m_LocalPosition.z");
            return Mathf.Sqrt(x * x + z * z);
        }

        public const float RootTravelTolerance = 0.05f;

        private static string RootPath(AnimationClip clip) =>
            AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform) && b.propertyName == "m_LocalPosition.x")
                .Select(b => b.path).OrderBy(p => p.Count(c => c == '/')).ThenBy(p => p, StringComparer.Ordinal).FirstOrDefault();

        private static float Net(AnimationClip clip, string path, string property)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property));
            return curve == null || curve.length < 2 ? 0f : curve[curve.length - 1].value - curve[0].value;
        }

        /// <summary>Removes the net x and z travel of the Root's position curves; returns how many curves changed.</summary>
        public static int StripRootTravel(AnimationClip clip, out float metres)
        {
            metres = RootTravel(clip);
            var path = RootPath(clip);
            if (path == null || metres < RootTravelTolerance)
                return 0;
            var changed = 0;
            foreach (var property in new[] { "m_LocalPosition.x", "m_LocalPosition.z" })
            {
                var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length < 2)
                    continue;
                var t0 = curve[0].time;
                var span = curve[curve.length - 1].time - t0;
                var slope = (curve[curve.length - 1].value - curve[0].value) / span;
                if (Mathf.Abs(slope * span) < 1e-4f)
                    continue;
                var keys = curve.keys;
                for (var i = 0; i < keys.Length; i++)
                {
                    keys[i].value -= slope * (keys[i].time - t0);
                    if (!float.IsInfinity(keys[i].inTangent)) keys[i].inTangent -= slope;
                    if (!float.IsInfinity(keys[i].outTangent)) keys[i].outTangent -= slope;
                }
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, binding, curve);
                changed++;
            }
            return changed;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (MixamoDrop.SuppressMapRebuild)
                return;
            var touched = imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(IsDrop);
            if (!touched)
                return;
            foreach (var p in imported.Concat(moved).Where(IsDrop).Distinct())
                foreach (var problem in MixamoDrop.Validate(p))
                    Debug.LogError($"Mixamo drop {p}: {problem}");
            EditorApplication.delayCall += () =>
            {
                if (!MixamoDrop.SuppressMapRebuild)
                    ClipMapBuilder.Build();
            };
        }
    }
}
