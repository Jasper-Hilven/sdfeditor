using System;
using UnityEditor;
using UnityEngine;

namespace SdfBody.Editor
{
    /// <summary>
    /// The body's animation assets in one go (ticket 44): the clips' import settings and the clip map (<see cref="AnimationImportFixer.Fix"/>),
    /// the mirrored flinch, the tuning and the controller (<see cref="SoldierControllerBuilder.BuildController"/>). Re-runnable. Run it after
    /// a Mixamo clip is dropped in (the import does the map by itself, but not the controller).
    /// <code>tools/unity-batch.sh body -executeMethod SdfBody.Editor.BodyAssets.BuildAllFromCommandLine</code>
    /// </summary>
    public static class BodyAssets
    {
        [MenuItem("Sdf/Body/Build All")]
        public static void BuildAll()
        {
            AnimationImportFixer.Fix();
            SoldierControllerBuilder.BuildController();
            AssetDatabase.SaveAssets();
        }

        public static void BuildAllFromCommandLine()
        {
            try
            {
                BuildAll();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[BodyAssets] failed: " + e);
                EditorApplication.Exit(1);
            }
        }
    }
}
