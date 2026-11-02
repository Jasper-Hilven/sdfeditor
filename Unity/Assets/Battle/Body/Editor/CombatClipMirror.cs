using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SdfBody.Editor
{
    /// <summary>
    /// Bakes the left-right mirror of a Generic clip on the Synty skeleton (ticket 06). The Animator's mirror flag works on Humanoid clips
    /// only and the soldiers are Generic (decision 27), so the mirrored flinch is baked as its own clip.
    /// <para>
    /// The <b>motion</b> is mirrored, not the pose: every clip starts in the right-handed rifle stance (the hips turned about 55 degrees),
    /// and a pose mirror would start in a left-handed stance turned the other way (a 110-degree snap out of the locomotion, and the rifle
    /// in the other hand during a flinch). So each frame is sampled on the bare skeleton, each bone's model-space turn since the first
    /// frame is reflected through the body's centre plane (x → −x) and given to its partner (<c>_L</c> ↔ <c>_R</c>; a centre bone to
    /// itself), applied on the source's own first pose; the bones that move (<c>Root</c>) move by their partner's move, reflected. The
    /// bake starts exactly where the source starts and falls (or flinches) to the other side. Written as local rotation curves (and
    /// position curves where the source keys positions).
    /// </para>
    /// <para>
    /// The bakes live in <c>Assets/Battle/Body/Animator/Mirrored/</c> and are committed (the controller points at them); the
    /// layer builder bakes a missing one, <see cref="RebakeAll"/> bakes them again in place (same GUIDs), and a test compares the
    /// committed bakes with a fresh one.
    /// </para>
    /// </summary>
    public static class CombatClipMirror
    {
        public const string Folder = "Assets/Battle/Body/Animator/Mirrored/";
        /// <summary>Samples per second of the bake.</summary>
        public const float Rate = 60f;

        /// <summary>The bakes: the mirrored clip's name and the clip (03's convention name) it mirrors.</summary>
        public static readonly (string Name, string Source)[] Bakes =
        {
            (CombatRules.MirroredHitClip, "soldier_hit"),
        };

        public static string PathOf(string name) => Folder + name + ".anim";

        /// <summary>The mirror of a model-space rotation through the x = 0 plane: M R M with M = diag(−1, 1, 1).</summary>
        public static Quaternion Reflect(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);

        public static Vector3 Reflect(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        /// <summary>A new clip: <paramref name="source"/> mirrored on the Synty skeleton (not saved).</summary>
        public static AnimationClip Bake(AnimationClip source, string name)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var skeleton = CombatSkeleton.Load();
            var bones = skeleton.Bones;
            int n = bones.Count;

            // Which bones the source keys (rotation or position); the bake keys their partners.
            var rotated = new bool[n];
            var moved = new bool[n];
            foreach (var b in AnimationUtility.GetCurveBindings(source))
            {
                var bone = skeleton.ByPath(b.path);
                if (bone == null || b.type != typeof(Transform)) continue;
                if (b.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal) || b.propertyName.StartsWith("localEulerAngles", StringComparison.Ordinal))
                    rotated[bone.Index] = true;
                else if (b.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal))
                    moved[bone.Index] = true;
            }

            var root = skeleton.Instantiate(out var t);
            try
            {
                int frames = Mathf.Max(2, Mathf.RoundToInt(source.length * Rate) + 1);
                var rot = new Quaternion[frames, n];
                var pos = new Vector3[frames, n];
                var times = new float[frames];
                var g = new Quaternion[n];
                var p = new Vector3[n];
                var g0 = new Quaternion[n];
                var p0 = new Vector3[n];
                for (var f = 0; f < frames; f++)
                {
                    float time = Mathf.Min(source.length, f / Rate);
                    times[f] = time;
                    source.SampleAnimation(root, time);
                    for (var i = 0; i < n; i++)
                    {
                        g[i] = t[i].rotation;
                        p[i] = t[i].position;
                        if (f == 0) { g0[i] = g[i]; p0[i] = p[i]; }
                    }
                    // The motion mirrored onto the first pose: each bone turns by its partner's model-space turn since the first frame,
                    // reflected; the bones that move (Root) move by their partner's move, reflected.
                    var gm = new Quaternion[n];
                    var pm = new Vector3[n];
                    for (var i = 0; i < n; i++)
                    {
                        var m = bones[i].Mirror;
                        gm[m] = Reflect(g[i] * Quaternion.Inverse(g0[i])) * g0[m];
                        pm[m] = p0[m] + Reflect(p[i] - p0[i]);
                    }
                    for (var i = 0; i < n; i++)
                    {
                        var parent = bones[i].Parent;
                        var pr = parent < 0 ? Quaternion.identity : gm[parent];
                        var pp = parent < 0 ? Vector3.zero : pm[parent];
                        var local = Quaternion.Inverse(pr) * gm[i];
                        // keep the sign continuous for the curves
                        if (f > 0 && Quaternion.Dot(rot[f - 1, i], local) < 0f) local = new Quaternion(-local.x, -local.y, -local.z, -local.w);
                        rot[f, i] = local;
                        pos[f, i] = Quaternion.Inverse(pr) * (pm[i] - pp);
                    }
                }

                var clip = new AnimationClip { name = name, frameRate = Rate };
                for (var i = 0; i < n; i++)
                {
                    var src = bones[i].Mirror; // the bone of the source this one plays
                    if (rotated[src])
                    {
                        Curve(clip, bones[i].Path, "m_LocalRotation.x", times, f => rot[f, i].x);
                        Curve(clip, bones[i].Path, "m_LocalRotation.y", times, f => rot[f, i].y);
                        Curve(clip, bones[i].Path, "m_LocalRotation.z", times, f => rot[f, i].z);
                        Curve(clip, bones[i].Path, "m_LocalRotation.w", times, f => rot[f, i].w);
                    }
                    if (moved[src])
                    {
                        Curve(clip, bones[i].Path, "m_LocalPosition.x", times, f => pos[f, i].x);
                        Curve(clip, bones[i].Path, "m_LocalPosition.y", times, f => pos[f, i].y);
                        Curve(clip, bones[i].Path, "m_LocalPosition.z", times, f => pos[f, i].z);
                    }
                }
                var settings = AnimationUtility.GetAnimationClipSettings(source);
                var mine = AnimationUtility.GetAnimationClipSettings(clip);
                mine.loopTime = settings.loopTime;
                AnimationUtility.SetAnimationClipSettings(clip, mine);
                return clip;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void Curve(AnimationClip clip, string path, string property, float[] times, Func<int, float> value)
        {
            var keys = new Keyframe[times.Length];
            for (var f = 0; f < times.Length; f++) keys[f] = new Keyframe(times[f], Round(value(f)));
            // linear tangents: the bake is dense (60 per second)
            for (var f = 0; f < keys.Length; f++)
            {
                float inT = f > 0 ? (keys[f].value - keys[f - 1].value) / Mathf.Max(1e-6f, keys[f].time - keys[f - 1].time) : 0f;
                float outT = f < keys.Length - 1 ? (keys[f + 1].value - keys[f].value) / Mathf.Max(1e-6f, keys[f + 1].time - keys[f].time) : 0f;
                if (f == 0) inT = outT;
                if (f == keys.Length - 1) outT = inT;
                keys[f].inTangent = inT;
                keys[f].outTangent = outT;
            }
            clip.SetCurve(path, typeof(Transform), property, new AnimationCurve(keys));
        }

        /// <summary>Six decimals: the bake is the same file on every machine.</summary>
        static float Round(float v) => (float)Math.Round(v, 6);

        /// <summary>The committed bake of a name, made when missing (from 03's clip map). Returns the asset.</summary>
        public static AnimationClip Ensure(string name, Func<string, AnimationClip> clipOf)
        {
            var path = PathOf(name);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) return existing;
            var source = SourceOf(name);
            var clip = Bake(clipOf(source), name);
            Directory.CreateDirectory(Path.GetFullPath(Folder));
            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();
            Debug.Log("[CombatClipMirror] baked " + path + " from " + source);
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        public static string SourceOf(string name) => Bakes.First(b => b.Name == name).Source;

        /// <summary>Bakes every mirror again over the committed files (their GUIDs kept); then rebuild the controller.</summary>
        [MenuItem("Sdf/Body/Rebake Mirrored Clips")]
        public static void RebakeAll()
        {
            var map = AssetDatabase.LoadAssetAtPath<SdfBody.SoldierClipMap>(SoldierControllerBuilder.ClipMapPath);
            foreach (var (name, source) in Bakes)
            {
                var fresh = Bake(map.Get(source), name);
                var path = PathOf(name);
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing == null)
                {
                    Directory.CreateDirectory(Path.GetFullPath(Folder));
                    AssetDatabase.CreateAsset(fresh, path);
                }
                else
                {
                    EditorUtility.CopySerialized(fresh, existing);
                    existing.name = name;
                    EditorUtility.SetDirty(existing);
                }
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Batch: bake the mirrors again, measure the deaths into the tuning, rebuild the controller; exit 0 or 1.
        /// <code>$U -batchmode -nographics -quit -logFile - -projectPath $P -executeMethod SdfBody.Editor.CombatClipMirror.RebakeFromCommandLine</code>
        /// </summary>
        public static void RebakeFromCommandLine()
        {
            try
            {
                RebakeAll();
                SoldierControllerBuilder.BuildController();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[CombatClipMirror] failed: " + e);
                EditorApplication.Exit(1);
            }
        }
    }
}
