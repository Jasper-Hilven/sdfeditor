using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SdfBody.Editor
{
    /// <summary>
    /// Natural ground speed of a locomotion clip: how fast a planted foot slides back relative to the body (from BattleRoyale's ticket 05),
    /// measured on the soldier prefab (the feet are the Ball or Ankle bone, in the body's frame).
    /// </summary>
    public static class LocomotionClipSpeeds
    {
        public const int Samples = 120;
        public const float PlantedBand = 0.025f;
        public const string RigPrefab = "Assets/Rig/Generated/Soldier/soldier_blue.prefab";

        public readonly struct Measure
        {
            public readonly string Clip;
            public readonly float MetresPerSecond;
            /// <summary>Unit direction the body travels (x right, y forward = world z), zero when the speed is tiny.</summary>
            public readonly Vector2 Direction;
            public readonly int PlantedSamples;
            /// <summary>Metres per cycle (speed times clip length).</summary>
            public readonly float StrideMetres;

            public Measure(string clip, float metresPerSecond, Vector2 direction, int plantedSamples, float strideMetres)
            {
                Clip = clip; MetresPerSecond = metresPerSecond; Direction = direction; PlantedSamples = plantedSamples; StrideMetres = strideMetres;
            }

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "{0} {1:F3} m/s dir=({2:F2}, {3:F2}) planted={4} cycle={5:F2} m", Clip, MetresPerSecond, Direction.x, Direction.y, PlantedSamples, StrideMetres);
        }

        /// <summary>The clips measured (the speeds list of <see cref="LocomotionTuning.DefaultClipSpeeds"/>, and the idle).</summary>
        public static readonly string[] LocomotionNames =
        {
            "soldier_loco_aim_idle", "soldier_loco_walk_f", "soldier_loco_walk_b", "soldier_loco_strafe_l", "soldier_loco_strafe_r",
            "soldier_loco_walk_fl", "soldier_loco_walk_fr", "soldier_loco_walk_bl", "soldier_loco_walk_br",
            "soldier_loco_run_f", "soldier_loco_run_b", "soldier_loco_run_l", "soldier_loco_run_r",
        };

        /// <summary>The soldier prefab, instantiated at the origin (hidden); null when the models were not imported. Destroy it with <see cref="Cleanup"/>.</summary>
        public static GameObject InstantiateRig()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            if (prefab == null) return null;
            var rig = (GameObject)UnityEngine.Object.Instantiate(prefab);
            rig.hideFlags = HideFlags.HideAndDontSave;
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = rig.GetComponent<Animator>();
            if (animator != null) animator.enabled = false;
            return rig;
        }

        public static void Cleanup(GameObject rig)
        {
            if (rig != null) UnityEngine.Object.DestroyImmediate(rig);
        }

        public static Measure Of(AnimationClip clip, GameObject rig)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            var length = clip.length;
            var feet = new List<Transform>();
            foreach (var names in new[] { new[] { "Ball_L", "Ankle_L" }, new[] { "Ball_R", "Ankle_R" } })
            {
                var bone = names.Select(n => Find(rig.transform, n)).FirstOrDefault(t => t != null);
                if (bone != null) feet.Add(bone);
            }
            var frames = new Vector3[feet.Count][];
            for (var f = 0; f < feet.Count; f++) frames[f] = new Vector3[Samples];
            for (var i = 0; i < Samples; i++)
            {
                clip.SampleAnimation(rig, length * i / Samples);
                for (var f = 0; f < feet.Count; f++) frames[f][i] = rig.transform.InverseTransformPoint(feet[f].position);
            }
            var dt = length / Samples;
            var speeds = new List<float>();
            var sum = Vector2.zero;
            for (var f = 0; f < feet.Count; f++)
            {
                var min = frames[f].Min(p => p.y);
                for (var i = 0; i < Samples; i++)
                {
                    if (frames[f][i].y > min + PlantedBand) continue;
                    var a = frames[f][(i + 1) % Samples];
                    var b = frames[f][(i + Samples - 1) % Samples];
                    var v = new Vector2(a.x - b.x, a.z - b.z) / (2f * dt);
                    speeds.Add(v.magnitude);
                    sum += v;
                }
            }
            if (speeds.Count == 0) return new Measure(clip.name, 0f, Vector2.zero, 0, 0f);
            speeds.Sort();
            var n = speeds.Count;
            var median = n % 2 == 1 ? speeds[n / 2] : 0.5f * (speeds[n / 2 - 1] + speeds[n / 2]);
            var dir = -sum / n;
            dir = dir.magnitude < 0.01f ? Vector2.zero : dir.normalized;
            return new Measure(clip.name, median, dir, n, median * length);
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var t = Find(root.GetChild(i), name);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>The measures of the named clips, each as the clip map plays it (an own clip, or the stand-in's).</summary>
        public static List<Measure> OfMap(SoldierClipMap map, IEnumerable<string> names)
        {
            var rig = InstantiateRig() ?? throw new InvalidOperationException($"{RigPrefab} is missing: run `sdf unity`, then SdfRig.Editor.SoldierSkins.ImportAll");
            try
            {
                var result = new List<Measure>();
                foreach (var name in names)
                {
                    var clip = LocomotionClips.TryFind(name, out var entry) ? LocomotionClips.Resolve(map, entry) : map.Get(name);
                    if (clip == null) throw new InvalidOperationException($"clip map has no clip for {name}");
                    var m = Of(clip, rig);
                    result.Add(new Measure(name, m.MetresPerSecond, m.Direction, m.PlantedSamples, m.StrideMetres));
                }
                return result;
            }
            finally { Cleanup(rig); }
        }

        /// <summary>
        /// Measures every clip of the tuning's speed list (the names of <see cref="LocomotionTuning.DefaultClipSpeeds"/>, in that order)
        /// and writes the numbers into <c>SoldierAnimation.asset</c>; then rebuild the controller (the children's time scales follow).
        /// Run it after a Mixamo clip (the pistol or heavy runs) replaces a stand-in. Copy the logged numbers into
        /// <c>DefaultClipSpeeds</c> so the code defaults stay equal.
        /// </summary>
        [MenuItem("Sdf/Body/Measure Locomotion Clip Speeds")]
        public static void UpdateTuning()
        {
            var map = AssetDatabase.LoadAssetAtPath<SoldierClipMap>(ClipMapBuilder.AssetPath);
            if (map == null) throw new InvalidOperationException("clip map not found at " + ClipMapBuilder.AssetPath);
            var tuning = SoldierControllerBuilder.EnsureTuning();
            var names = LocomotionTuning.DefaultClipSpeeds().Select(c => c.Clip).ToList();
            var measured = OfMap(map, names);
            tuning.ClipSpeeds = measured.Select(m => new LocomotionTuning.ClipSpeed(m.Clip, (float)Math.Round(m.MetresPerSecond, 3))).ToArray();
            EditorUtility.SetDirty(tuning);
            AssetDatabase.SaveAssetIfDirty(tuning);
            foreach (var m in measured) Debug.Log("[ClipSpeed] wrote " + m);
        }

        /// <summary>Batch entry: <see cref="UpdateTuning"/> followed by <see cref="SoldierControllerBuilder.BuildController"/>.</summary>
        public static void UpdateTuningFromCommandLine()
        {
            try
            {
                UpdateTuning();
                SoldierControllerBuilder.BuildController();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[ClipSpeed] failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Batch entry: logs the measure of every locomotion clip.</summary>
        public static void MeasureFromCommandLine()
        {
            try
            {
                var map = AssetDatabase.LoadAssetAtPath<SoldierClipMap>(ClipMapBuilder.AssetPath);
                if (map == null) throw new InvalidOperationException("clip map not found at " + ClipMapBuilder.AssetPath);
                foreach (var m in OfMap(map, LocomotionNames))
                    Debug.Log("[ClipSpeed] " + m);
                Debug.Log("[ClipSpeed] done");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[ClipSpeed] failed: " + e);
                EditorApplication.Exit(1);
            }
        }
    }
}
