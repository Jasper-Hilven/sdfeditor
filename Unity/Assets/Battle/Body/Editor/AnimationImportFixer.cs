using System.IO;
using UnityEditor;
using UnityEngine;

namespace SdfBody.Editor
{
    /// <summary>
    /// Ticket 03.1: puts the catalog clips' settings right (loop flags and names of the .anim files, the import of the FBX files) with one
    /// command, so a re-copy of the source clips is repeated with one command. Idempotent.
    /// </summary>
    public static class AnimationImportFixer
    {
        [MenuItem("Sdf/Body/Fix Animation Imports")]
        public static void Fix()
        {
            var changes = 0;
            foreach (var e in SoldierClips.All)
            {
                var path = SoldierClipAssets.PathOf(e);
                if (e.IsFbx)
                {
                    var metaFile = path + ".meta";
                    var before = File.Exists(metaFile) ? File.ReadAllText(metaFile) : "";
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    var after = File.Exists(metaFile) ? File.ReadAllText(metaFile) : "";
                    if (before != after)
                    {
                        changes++;
                        Debug.Log($"Fix: {path}: import settings applied");
                    }
                    continue;
                }
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    Debug.LogError($"Fix: {path}: no clip");
                    continue;
                }
                var dirty = false;
                if (clip.name != e.Name)
                {
                    Debug.Log($"Fix: {path}: name {clip.name} -> {e.Name}");
                    clip.name = e.Name;
                    dirty = true;
                }
                var s = AnimationUtility.GetAnimationClipSettings(clip);
                if (s.loopTime != e.Loop)
                {
                    Debug.Log($"Fix: {path}: loopTime {s.loopTime} -> {e.Loop}");
                    s.loopTime = e.Loop;
                    AnimationUtility.SetAnimationClipSettings(clip, s);
                    dirty = true;
                }
                if (dirty)
                {
                    EditorUtility.SetDirty(clip);
                    changes++;
                }
            }
            AssetDatabase.SaveAssets();
            ClipMapBuilder.Build();
            Debug.Log($"Fix: {changes} changes");
        }
    }
}
