using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SdfBody.Editor
{
    /// <summary>Loads the clips of <see cref="SoldierClips"/> from their files (ticket 03).</summary>
    public static class SoldierClipAssets
    {
        /// <summary>The asset path of a catalog clip's file.</summary>
        public static string PathOf(ClipEntry entry) => SoldierClips.Folder + entry.File;

        /// <summary>
        /// The clip of <paramref name="entry"/>: a .anim's main asset; an FBX's take (the one named as the entry, else its first clip that
        /// is not a <c>__preview__</c>). Null when the file or the clip is missing.
        /// </summary>
        public static AnimationClip Load(ClipEntry entry) => LoadAt(PathOf(entry), entry.Name);

        /// <summary>The clip in the file at <paramref name="assetPath"/>, preferring the one called <paramref name="name"/>.</summary>
        public static AnimationClip LoadAt(string assetPath, string name)
        {
            if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                return AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToList();
            return clips.FirstOrDefault(c => c.name == name) ?? clips.FirstOrDefault();
        }

        /// <summary>A catalog clip by convention name.</summary>
        public static AnimationClip Load(string name) =>
            SoldierClips.Find(name) is { } e ? Load(e) : throw new ArgumentException($"{name} is not in SoldierClips.All");
    }
}
