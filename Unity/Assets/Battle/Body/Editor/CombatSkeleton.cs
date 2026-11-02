using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SdfBody.Editor
{
    /// <summary>
    /// The Synty skeleton of <c>content/skeletons/synty.skeleton</c> (02) as ticket 06 needs it: the bones in file order with their
    /// paths (the clips' binding paths), aliases, mirror partners and rest transforms, and a bare transform hierarchy to sample clips on
    /// (the upper-body mask, the mirrored bakes and the death measures need no model).
    /// </summary>
    public sealed class CombatSkeleton
    {
        public sealed class Bone
        {
            public string Name, Alias, Path;
            public int Index, Parent = -1, Mirror;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        public static string SkeletonFile => System.IO.Path.GetFullPath(Application.dataPath + "/../../content/skeletons/synty.skeleton");

        public readonly List<Bone> Bones = new List<Bone>();
        readonly Dictionary<string, Bone> byName = new Dictionary<string, Bone>();
        readonly Dictionary<string, Bone> byPath = new Dictionary<string, Bone>();

        static CombatSkeleton cached;

        /// <summary>The skeleton (read once).</summary>
        public static CombatSkeleton Load() => cached ??= Read(SkeletonFile);

        public Bone ByName(string name) => byName.TryGetValue(name, out var b) ? b : null;
        public Bone ByPath(string path) => byPath.TryGetValue(path, out var b) ? b : null;

        public static CombatSkeleton Read(string file)
        {
            var s = new CombatSkeleton();
            var byAlias = new Dictionary<string, Bone>();
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (!line.StartsWith("bone ", StringComparison.Ordinal)) continue;
                var t = line.Split(new[] { ' ' }, 5, StringSplitOptions.RemoveEmptyEntries);
                var bone = new Bone { Name = t[1], Alias = t[2], Index = s.Bones.Count };
                if (t[3] != "-")
                {
                    var parent = s.ByName(t[3]) ?? throw new InvalidDataException("bone " + bone.Name + ": parent " + t[3] + " not before it");
                    bone.Parent = parent.Index;
                    bone.Path = parent.Path + "/" + bone.Name;
                }
                else bone.Path = bone.Name;
                bone.Position = Vec(Field(t[4], "pos"));
                var r = Field(t[4], "rot");
                bone.Rotation = new Quaternion(r[0], r[1], r[2], r[3]);
                s.Bones.Add(bone);
                s.byName[bone.Name] = bone;
                s.byPath[bone.Path] = bone;
                byAlias[bone.Alias] = bone;
            }
            foreach (var b in s.Bones)
            {
                b.Mirror = b.Index;
                string other = null;
                if (b.Alias.EndsWith("_L", StringComparison.Ordinal)) other = b.Alias.Substring(0, b.Alias.Length - 2) + "_R";
                else if (b.Alias.EndsWith("_R", StringComparison.Ordinal)) other = b.Alias.Substring(0, b.Alias.Length - 2) + "_L";
                if (other != null)
                    b.Mirror = byAlias.TryGetValue(other, out var m) ? m.Index : throw new InvalidDataException("no mirror for " + b.Name);
            }
            return s;
        }

        static float[] Field(string rest, string key)
        {
            var at = rest.IndexOf(key + "=(", StringComparison.Ordinal);
            if (at < 0) throw new InvalidDataException("no " + key + " in " + rest);
            var start = at + key.Length + 2;
            var end = rest.IndexOf(')', start);
            var parts = rest.Substring(start, end - start).Split(',');
            var v = new float[parts.Length];
            for (var i = 0; i < parts.Length; i++) v[i] = float.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
            return v;
        }

        static Vector3 Vec(float[] v) => new Vector3(v[0], v[1], v[2]);

        /// <summary>
        /// A bare hierarchy at the rest pose (the T-pose): an empty parent at the origin (where the Animator sits) with the bones under
        /// it by name, so a clip's paths bind. Destroy it with <see cref="UnityEngine.Object.DestroyImmediate(UnityEngine.Object)"/>.
        /// </summary>
        public GameObject Instantiate(out Transform[] transforms)
        {
            var root = new GameObject("CombatSkeleton") { hideFlags = HideFlags.HideAndDontSave };
            transforms = new Transform[Bones.Count];
            foreach (var b in Bones)
            {
                var go = new GameObject(b.Name) { hideFlags = HideFlags.HideAndDontSave };
                var t = go.transform;
                t.SetParent(b.Parent < 0 ? root.transform : transforms[b.Parent], false);
                t.localPosition = b.Position;
                t.localRotation = b.Rotation;
                transforms[b.Index] = t;
            }
            return root;
        }
    }
}
