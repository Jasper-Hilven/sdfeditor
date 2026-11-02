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
