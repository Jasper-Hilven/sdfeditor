using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfRig
{
    /// <summary>One bone of a rigged model. Axes and limits are already in Unity's frame (mirrored from the file, see <see cref="RigConvert"/>).</summary>
    [Serializable]
    public sealed class RigBoneInfo
    {
        public string name;
        /// <summary>The parent bone's name; empty for the root.</summary>
        public string parent;
        public float falloff;
        public bool hasSpin;
        /// <summary>Free rotation axis in the bone's local frame (Unity).</summary>
        public Vector3 spin;
        public bool hasHinge;
        /// <summary>Hinge axis in the bone's local frame (Unity).</summary>
        public Vector3 hinge;
        public bool hasLimit;
        /// <summary>With a hinge: (min, max) radians about it. Without: (cone, twist) radians.</summary>
        public Vector2 limit;
        /// <summary>"auto", "none" or a radius in metres as text.</summary>
        public string hit;
    }

    /// <summary>What a rigged model's file says about it: bones with their attributes, sockets. Found by name.</summary>
    [DisallowMultipleComponent]
    public sealed class RigInfo : MonoBehaviour
    {
        public RigBoneInfo[] bones = Array.Empty<RigBoneInfo>();
        public string[] sockets = Array.Empty<string>();
        /// <summary>The external skeleton the model is rigged onto (e.g. "synty"); empty otherwise.</summary>
        public string skeleton = "";
        /// <summary>The root motion node of the Generic avatar (external skeleton only); empty for none.</summary>
        public string rootMotionNode = "";

        private Dictionary<string, Transform> byName;

        /// <summary>The joint called <paramref name="name"/>, or null.</summary>
        public Transform Bone(string name) => Find(name);

        /// <summary>The socket <paramref name="name"/> ("mouth" finds the object "socket_mouth"), or null.</summary>
        public Transform Socket(string name) => Find("socket_" + name);

        public RigBoneInfo BoneInfo(string name)
        {
            foreach (var b in bones)
                if (b.name == name)
                    return b;
            return null;
        }

        /// <summary>The bone's hinge axis (Unity, bone frame), or null.</summary>
        public Vector3? Hinge(string bone)
        {
            var b = BoneInfo(bone);
            return b != null && b.hasHinge ? b.hinge : (Vector3?)null;
        }

        /// <summary>The bone's limit, or null: (min, max) radians with a hinge, (cone, twist) radians without.</summary>
        public Vector2? Limit(string bone)
        {
            var b = BoneInfo(bone);
            return b != null && b.hasLimit ? b.limit : (Vector2?)null;
        }

        public Vector3? SpinAxis(string bone)
        {
            var b = BoneInfo(bone);
            return b != null && b.hasSpin ? b.spin : (Vector3?)null;
        }

        /// <summary>Forget the cached lookups (after the hierarchy changed).</summary>
        public void Refresh() => byName = null;

        private Transform Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (byName != null && byName.TryGetValue(name, out var cached) && cached != null)
                return cached;
            byName = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t != transform && !byName.ContainsKey(t.name))
                    byName.Add(t.name, t);
            return byName.TryGetValue(name, out var found) ? found : null;
        }
    }
}
